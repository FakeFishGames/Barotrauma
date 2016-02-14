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

        protected List<LimbSlot> allowedSlots;

        private float pickTimer;


        public List<LimbSlot> AllowedSlots
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
            allowedSlots = new List<LimbSlot>();

            string slotString = ToolBox.GetAttributeString(element, "slots", "Any");
            string[] slotCombinations = slotString.Split(',');
            foreach (string slotCombination in slotCombinations)
            {
                string[] slots = slotCombination.Split('+');
                LimbSlot allowedSlot = LimbSlot.None;
                foreach (string slot in slots)                
                {
                    if (slot.ToLower()=="bothhands")
                    {
                        allowedSlot = LimbSlot.LeftHand | LimbSlot.RightHand;
                    }
                    else
                    {
                        allowedSlot = allowedSlot | (LimbSlot)Enum.Parse(typeof(LimbSlot), slot.Trim());
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

                //create a networkevent here, because the item doesn't count as picked yet and the character won't create one
                new NetworkEvent(NetworkEventType.PickItem, picker.ID, true,
                    new int[] 
                        { 
                            item.ID, 
                            picker.IsKeyHit(InputType.Select) ? 1 : 0, 
                            picker.IsKeyHit(InputType.Use) ? 1 : 0 
                        });
                

                return false;
            }
            else
            {
                return OnPicked(picker);
            }


        }

        private bool OnPicked(Character picker)
        {
            if (picker.Inventory.TryPutItem(item, allowedSlots, picker == Character.Controlled))
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

                //foreach (StatusEffect effect in item.Prefab.statusEffects)
                //{
                //    effect.OnPicked(picker, null);
                //}

                return true;
            }

            return false;
        }

        private IEnumerable<object> WaitForPick(Character picker, float requiredTime)
        {
            var leftHand = picker.AnimController.GetLimb(LimbType.LeftHand);
            var rightHand = picker.AnimController.GetLimb(LimbType.RightHand);


            pickTimer = 0.0f;
            while (pickTimer < requiredTime)
            {
                if (picker.IsKeyDown(InputType.Aim) || 
                    !item.IsInPickRange(picker.WorldPosition) ||
                    picker.Stun > 0.0f || picker.IsDead)
                {
                    StopPicking(picker);
                    yield return CoroutineStatus.Success;
                }

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

            if (!picker.IsNetworkPlayer) OnPicked(picker);

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

        
        public override void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            if (pickTimer <= 0.0f) return;

            float progressBarWidth = 100.0f;

            GUI.DrawProgressBar(spriteBatch, item.DrawPosition + new Vector2(-progressBarWidth/2.0f, 50.0f), new Vector2(progressBarWidth, 15.0f), 
                pickTimer / PickingTime,
                Color.Lerp(Color.Red, Color.Green, pickTimer / PickingTime));
        }

        public override void Drop(Character dropper)
        {            
            if (picker == null)
            {
                picker = dropper;

                //foreach (Character c in Character.characterList)
                //{
                //    if (c.Inventory == null) continue;
                //    if (c.Inventory.FindIndex(item) == -1) continue;
                    
                //    picker = c;
                //    break;                    
                //}
            }

            if (picker == null || picker.Inventory == null) return;

            DropConnectedWires(picker);

            item.Submarine = picker.Submarine;
            
            if (item.body!= null && !item.body.Enabled)
            {
                Limb rightHand = picker.AnimController.GetLimb(LimbType.RightHand);

                item.SetTransform(rightHand.SimPosition, 0.0f);
                item.body.Enabled = true;
                
            }
            picker.Inventory.RemoveItem(item);
            picker = null;
        }

    }
}
