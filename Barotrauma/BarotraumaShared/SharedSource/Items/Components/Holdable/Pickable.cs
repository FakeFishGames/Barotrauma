using Barotrauma.Abilities;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Pickable : ItemComponent, IServerSerializable
    {
        protected Character picker;

        protected List<InvSlotType> allowedSlots;

        private float pickTimer;

        private Character activePicker;

        private CoroutineHandle pickingCoroutine;

        public List<InvSlotType> AllowedSlots
        {
            get { return allowedSlots; }
        }

        public Character Picker
        {
            get 
            {
                if (picker != null && picker.Removed)
                {
                    picker = null;
                }
                return picker; 
            }
        }

        public Pickable(Item item, ContentXElement element)
            : base(item, element)
        {
            allowedSlots = new List<InvSlotType>();

            string slotString = element.GetAttributeString("slots", "Any");
            string[] slotCombinations = slotString.Split(',');
            foreach (string slotCombination in slotCombinations)
            {
                string[] slots = slotCombination.Split('+');
                InvSlotType allowedSlot = InvSlotType.None;
                foreach (string slot in slots)                
                {
                    switch (slot.ToLowerInvariant())
                    {
                        case "bothhands":
                            allowedSlot = InvSlotType.LeftHand | InvSlotType.RightHand;
                            break;
                        default:
                            allowedSlot = allowedSlot | (InvSlotType)Enum.Parse(typeof(InvSlotType), slot.Trim());
                            break;
                    }
                }
                allowedSlots.Add(allowedSlot);
            }

            canBePicked = true;
        }

        public override bool Pick(Character picker)
        {
            //return if someone is already trying to pick the item
            if (pickTimer > 0.0f) { return false; }
            if (picker == null || picker.Inventory == null) { return false; }
            if (!picker.Inventory.AccessibleWhenAlive && !picker.Inventory.AccessibleByOwner) { return false; }

            if (PickingTime > 0.0f)
            {
                var abilityPickingTime = new AbilityItemPickingTime(PickingTime, item.Prefab);
                picker.CheckTalents(AbilityEffectType.OnItemPicked, abilityPickingTime);

                if (requiredItems.ContainsKey(RelatedItem.RelationType.Equipped))
                {
                    foreach (RelatedItem ri in requiredItems[RelatedItem.RelationType.Equipped])
                    {
                        foreach (var heldItem in picker.HeldItems)
                        {
                            if (ri.MatchesItem(heldItem))
                            {
                                abilityPickingTime.Value /= 1 + heldItem.Prefab.AddedPickingSpeedMultiplier;
                            }
                        }
                    }
                }

                if ((picker.PickingItem == null || picker.PickingItem == item) && PickingTime <= float.MaxValue)
                {
#if SERVER
                    // Set active picker before creating the server event to make sure it's set correctly
                    activePicker = picker;
                    item.CreateServerEvent(this);
#endif
                    pickingCoroutine = CoroutineManager.StartCoroutine(WaitForPick(picker, abilityPickingTime.Value));
                }
                return false;
            }
            else
            {
                return OnPicked(picker);
            }
        }

        public virtual bool OnPicked(Character picker)
        {
            //if the item has multiple Pickable components (e.g. Holdable and Wearable, check that we don't equip it in hands when the item is worn or vice versa)
            if (item.GetComponents<Pickable>().Count() > 0)
            {
                bool alreadyEquipped = false;
                for (int i = 0; i < picker.Inventory.Capacity; i++)
                {
                    if (picker.Inventory.GetItemsAt(i).Contains(item))
                    {
                        if (picker.Inventory.SlotTypes[i] != InvSlotType.Any &&
                            !allowedSlots.Any(a => a.HasFlag(picker.Inventory.SlotTypes[i])))
                        {
                            alreadyEquipped = true;
                            break;
                        }
                    }
                }
                if (alreadyEquipped) { return false; }
            }
            if (picker.Inventory.TryPutItemWithAutoEquipCheck(item, picker, allowedSlots))
            {
                if (!picker.HeldItems.Contains(item) && item.body != null) { item.body.Enabled = false; }
                this.picker = picker;

                for (int i = item.linkedTo.Count - 1; i >= 0; i--)
                {
                    item.linkedTo[i].RemoveLinked(item);
                }
                item.linkedTo.Clear();

                DropConnectedWires(picker);

                ApplyStatusEffects(ActionType.OnPicked, 1.0f, picker);
#if CLIENT
                if (!GameMain.Instance.LoadingScreenOpen && picker == Character.Controlled) SoundPlayer.PlayUISound(GUISoundType.PickItem);
                PlaySound(ActionType.OnPicked,  picker);
#endif
                return true;
            }

#if CLIENT
            if (!GameMain.Instance.LoadingScreenOpen && picker == Character.Controlled) SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
#endif

            return false;
        }

        private IEnumerable<CoroutineStatus> WaitForPick(Character picker, float requiredTime)
        {
            activePicker = picker;
            picker.PickingItem = item;
            pickTimer = 0.0f;
            while (pickTimer < requiredTime && Screen.Selected != GameMain.SubEditorScreen)
            {
                //cancel if the item is currently selected
                //attempting to pick does not select the item, so if it is selected at this point, another ItemComponent
                //must have been selected and we should not keep deattaching (happens when for example interacting with
                //an electrical component while holding both a screwdriver and a wrench).
                if (picker.SelectedConstruction == item || 
                    picker.IsKeyDown(InputType.Aim) || 
                    !picker.CanInteractWith(item) ||
                    item.Removed || item.ParentInventory != null)
                {
                    StopPicking(picker);
                    yield return CoroutineStatus.Success;
                }

#if CLIENT
                Character.Controlled?.UpdateHUDProgressBar(
                    this,
                    item.WorldPosition,
                    pickTimer / requiredTime,
                    GUIStyle.Red, GUIStyle.Green,
                    !string.IsNullOrWhiteSpace(PickingMsg) ? PickingMsg : this is Door ? "progressbar.opening" : "progressbar.deattaching");
#endif
                
                picker.AnimController.UpdateUseItem(true, item.WorldPosition + new Vector2(0.0f, 100.0f) * ((pickTimer / 10.0f) % 0.1f));
                pickTimer += CoroutineManager.DeltaTime;

                yield return CoroutineStatus.Running;
            }

            StopPicking(picker);

            bool isNotRemote = true;
#if CLIENT
            isNotRemote = !picker.IsRemotePlayer;
#endif
            if (isNotRemote) OnPicked(picker);

            yield return CoroutineStatus.Success;
        }

        protected void StopPicking(Character picker)
        {
            if (picker != null)
            {
                picker.AnimController.Anim = AnimController.Animation.None;
                picker.PickingItem = null;
            }
            if (pickingCoroutine != null)
            {
                CoroutineManager.StopCoroutines(pickingCoroutine);
                pickingCoroutine = null;
            }
            activePicker = null;
            pickTimer = 0.0f;
        }

        protected void DropConnectedWires(Character character)
        {
            Vector2 pos = character == null ? item.SimPosition : character.SimPosition;
            
            foreach (ConnectionPanel connectionPanel in item.GetComponents<ConnectionPanel>())
            {
                foreach (Connection c in connectionPanel.Connections)
                {
                    foreach (Wire w in c.Wires.ToArray())
                    {
                        if (w == null) continue;
                        w.Item.Drop(character);
                        w.Item.SetTransform(pos, 0.0f);
                    }
                }
            }                       
        }
        
        public override void Drop(Character dropper)
        {            
            if (picker == null)
            {
                picker = dropper;
            }

            Vector2 bodyDropPos = Vector2.Zero;

            if (picker == null || picker.Inventory == null)
            {
                if (item.ParentInventory != null && item.ParentInventory.Owner != null && !item.ParentInventory.Owner.Removed)
                {
                    bodyDropPos = item.ParentInventory.Owner.SimPosition;

                    if (item.body != null) item.body.ResetDynamics();                    
                }
            }
            else if (!picker.Removed)
            {
                DropConnectedWires(picker);

                item.Submarine = picker.Submarine;
                bodyDropPos = picker.SimPosition;
                
                picker.Inventory.RemoveItem(item);
                picker = null;
            }

            if (item.body != null && !item.body.Enabled)
            {
                if (item.body.Removed)
                {
                    DebugConsole.ThrowError(
                        "Failed to drop the Pickable component of the item \"" + item.Name + "\" (body has been removed"
                        + (item.Removed ? ", item has been removed)" : ")"));
                }
                else
                {
                    item.body.ResetDynamics();
                    item.SetTransform(bodyDropPos, 0.0f);
                    item.body.Enabled = true;
                }
            }
        }

        public virtual void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.Write(activePicker?.ID ?? (ushort)0);
        }

        public virtual void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            ushort pickerID = msg.ReadUInt16();
            if (pickerID == 0)
            {
                StopPicking(activePicker);
            }
            else
            {
                Pick(Entity.FindEntityByID(pickerID) as Character);
            }
        }
    }

    class AbilityItemPickingTime : AbilityObject, IAbilityValue, IAbilityItemPrefab
    {
        public AbilityItemPickingTime(float pickingTime, ItemPrefab itemPrefab)
        {
            Value = pickingTime;
            ItemPrefab = itemPrefab;
        }
        public float Value { get; set; }
        public ItemPrefab ItemPrefab { get; set; }
    }
}
