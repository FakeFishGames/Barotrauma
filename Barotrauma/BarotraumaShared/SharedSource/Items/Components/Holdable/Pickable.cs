using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
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
            get { return picker; }
        }
        
        public Pickable(Item item, XElement element)
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
            if (pickTimer > 0.0f) return false;
            if (picker == null || picker.Inventory == null) return false;

            if (PickingTime > 0.0f)
            {
                if (picker.PickingItem == null && PickingTime <= float.MaxValue)
                {
#if SERVER
                    item.CreateServerEvent(this);
#endif
                    pickingCoroutine = CoroutineManager.StartCoroutine(WaitForPick(picker, PickingTime));
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
            if (picker.Inventory.TryPutItemWithAutoEquipCheck(item, picker, allowedSlots, true, true))
            {
                if (!picker.HasSelectedItem(item) && item.body != null) item.body.Enabled = false;
                this.picker = picker;

                for (int i = item.linkedTo.Count - 1; i >= 0; i--)
                {
                    item.linkedTo[i].RemoveLinked(item);
                }
                item.linkedTo.Clear();

                DropConnectedWires(picker);

                ApplyStatusEffects(ActionType.OnPicked, 1.0f, picker);
#if CLIENT
                if (!GameMain.Instance.LoadingScreenOpen && picker == Character.Controlled) GUI.PlayUISound(GUISoundType.PickItem);
                PlaySound(ActionType.OnPicked,  picker);
#endif
                return true;
            }

#if CLIENT
            if (!GameMain.Instance.LoadingScreenOpen && picker == Character.Controlled) GUI.PlayUISound(GUISoundType.PickItemFail);
#endif

            return false;
        }

        private IEnumerable<object> WaitForPick(Character picker, float requiredTime)
        {
            activePicker = picker;
            picker.PickingItem = item;

            var leftHand = picker.AnimController.GetLimb(LimbType.LeftHand);
            var rightHand = picker.AnimController.GetLimb(LimbType.RightHand);

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
                    GUI.Style.Red, GUI.Style.Green);
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
                    foreach (Wire w in c.Wires)
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
                if (item.ParentInventory != null && item.ParentInventory.Owner != null)
                {
                    bodyDropPos = item.ParentInventory.Owner.SimPosition;

                    if (item.body != null) item.body.ResetDynamics();                    
                }
            }
            else
            {
                DropConnectedWires(picker);

                item.Submarine = picker.Submarine;
                bodyDropPos = picker.SimPosition;
                
                picker.Inventory.RemoveItem(item);
                picker = null;
            }

            if (item.body != null && !item.body.Enabled)
            {
                item.body.ResetDynamics();
                item.SetTransform(bodyDropPos, 0.0f);
                item.body.Enabled = true;
            }
        }

        public virtual void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(activePicker == null ? (ushort)0 : activePicker.ID);
        }

        public virtual void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
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
}
