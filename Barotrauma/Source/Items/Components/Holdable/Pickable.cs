using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Pickable : ItemComponent
    {
        protected Character picker;

        protected List<InvSlotType> allowedSlots;

        private float pickTimer;


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

            string slotString = ToolBox.GetAttributeString(element, "slots", "Any");
            string[] slotCombinations = slotString.Split(',');
            foreach (string slotCombination in slotCombinations)
            {
                string[] slots = slotCombination.Split('+');
                InvSlotType allowedSlot = InvSlotType.None;
                foreach (string slot in slots)                
                {
                    if (slot.ToLowerInvariant() == "bothhands")
                    {
                        allowedSlot = InvSlotType.LeftHand | InvSlotType.RightHand;
                    }
                    else
                    {
                        allowedSlot = allowedSlot | (InvSlotType)Enum.Parse(typeof(InvSlotType), slot.Trim());
                    }
                }
                allowedSlots.Add(allowedSlot);
            }

            canBePicked = true;            
        }

        public override bool Pick(Character picker)
        {
            //return if someone is already trying to pick the item
            if (pickTimer>0.0f) return false;
            if (picker == null || picker.Inventory == null) return false;

            if (PickingTime>0.0f)
            {
                CoroutineManager.StartCoroutine(WaitForPick(picker, PickingTime));
                
                return false;
            }
            else
            {
                return OnPicked(picker);
            }
        }

        protected virtual bool OnPicked(Character picker)
        {
            if (picker.Inventory.TryPutItem(item, allowedSlots))
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
                
                return true;
            }

            return false;
        }

        private IEnumerable<object> WaitForPick(Character picker, float requiredTime)
        {
            var leftHand = picker.AnimController.GetLimb(LimbType.LeftHand);
            var rightHand = picker.AnimController.GetLimb(LimbType.RightHand);

            pickTimer = 0.0f;
            while (pickTimer < requiredTime && Screen.Selected != GameMain.EditMapScreen)
            {
                if (picker.IsKeyDown(InputType.Aim) || 
                    !item.IsInPickRange(picker.WorldPosition) ||
                    picker.Stun > 0.0f || picker.IsDead)
                {
                    StopPicking(picker);
                    yield return CoroutineStatus.Success;
                }

                picker.UpdateHUDProgressBar(
                    this,
                    item.WorldPosition,
                    pickTimer / requiredTime,
                    Color.Red, Color.Green);

                picker.AnimController.Anim = AnimController.Animation.UsingConstruction;

                picker.AnimController.TargetMovement = Vector2.Zero;

                leftHand.Disabled = true;
                leftHand.pullJoint.Enabled = true;
                leftHand.pullJoint.WorldAnchorB = item.SimPosition + Vector2.UnitY * ((pickTimer / 10.0f) % 0.1f);

                rightHand.Disabled = true;
                rightHand.pullJoint.Enabled = true;
                rightHand.pullJoint.WorldAnchorB = item.SimPosition + Vector2.UnitY * ((pickTimer / 10.0f) % 0.1f);

                pickTimer += CoroutineManager.DeltaTime;

                yield return CoroutineStatus.Running;
            }

            StopPicking(picker);

            if (!picker.IsRemotePlayer) OnPicked(picker);

            yield return CoroutineStatus.Success;
        }

        private void StopPicking(Character picker)
        {
            picker.AnimController.Anim = AnimController.Animation.None;
            pickTimer = 0.0f;         
        }

        protected void DropConnectedWires(Character character)
        {
            Vector2 pos = character == null ? item.SimPosition : character.SimPosition;

            var connectionPanel = item.GetComponent<ConnectionPanel>();
            if (connectionPanel == null) return;
            
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

    }
}
