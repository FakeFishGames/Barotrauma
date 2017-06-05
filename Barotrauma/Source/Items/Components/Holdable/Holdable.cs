using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    class Holdable : Pickable
    {
        //the position(s) in the item that the Character grabs
        protected Vector2[] handlePos;

        private List<RelatedItem> prevRequiredItems;

        string prevMsg;

        //the distance from the holding characters elbow to center of the physics body of the item
        protected Vector2 holdPos;

        protected Vector2 aimPos;

        //protected bool aimable;

        private bool attachable, attached, attachedByDefault;
        private PhysicsBody body;

        //the angle in which the Character holds the item
        protected float holdAngle;

        [HasDefaultValue(false, true)]
        public bool Attached
        {
            get { return attached && item.ParentInventory == null; }
            set { attached = value; }
        }

        [HasDefaultValue(false, false)]
        public bool ControlPose
        {
            get;
            set;
        }

        [HasDefaultValue(false, false)]
        public bool Attachable
        {
            get { return attachable; }
            set { attachable = value; }
        }

        [HasDefaultValue(false, false)]
        public bool AttachedByDefault
        {
            get { return attachedByDefault; }
            set { attachedByDefault = value; }
        }

        [HasDefaultValue("0.0,0.0", false)]
        public string HoldPos
        {
            get { return ToolBox.Vector2ToString(ConvertUnits.ToDisplayUnits(holdPos)); }
            set { holdPos = ConvertUnits.ToSimUnits(ToolBox.ParseToVector2(value)); }
        }

        [HasDefaultValue("0.0,0.0", false)]
        public string AimPos
        {
            get { return ToolBox.Vector2ToString(ConvertUnits.ToDisplayUnits(aimPos)); }
            set { aimPos = ConvertUnits.ToSimUnits(ToolBox.ParseToVector2(value)); }
        }

        [HasDefaultValue(0.0f, false)]
        public float HoldAngle
        {
            get { return MathHelper.ToDegrees(holdAngle); }
            set { holdAngle = MathHelper.ToRadians(value); }
        }

        public Holdable(Item item, XElement element)
            : base(item, element)
        {
            body = item.body;

            handlePos = new Vector2[2];

            for (int i = 1; i < 3; i++)
            {
                handlePos[i - 1] = ToolBox.GetAttributeVector2(element, "handle" + i, Vector2.Zero);

                handlePos[i - 1] = ConvertUnits.ToSimUnits(handlePos[i - 1]);
            }

            canBePicked = true;

            if (attachable)
            {
                prevRequiredItems = new List<RelatedItem>(requiredItems);
                prevMsg = Msg;

                requiredItems.Clear();
                Msg = "";
            }

            if (attachedByDefault || (Screen.Selected == GameMain.EditMapScreen)) Use(1.0f);


            //holdAngle = ToolBox.GetAttributeFloat(element, "holdangle", 0.0f);
            //holdAngle = MathHelper.ToRadians(holdAngle);
        }

        public override void Drop(Character dropper)
        {
            DropConnectedWires(dropper);

            if (body != null) item.body = body;

            if (item.body != null) item.body.Enabled = true;
            IsActive = false;

            if (picker == null)
            {
                if (dropper == null) return;
                picker = dropper;

            }
            if (picker.Inventory == null) return;
            
            item.Submarine = picker.Submarine;
            if (item.body != null)
            {
                item.body.ResetDynamics();
                item.SetTransform(picker.SimPosition, 0.0f);
            }

            picker.DeselectItem(item);
            picker.Inventory.RemoveItem(item);
            picker = null;
        }

        public override void Equip(Character character)
        {
            picker = character;

            if (character != null) item.Submarine = character.Submarine;

            if (item.body == null)
            {
                if (body != null)
                {
                    item.body = body;
                }
                else
                {
                    return;
                }
            }

            if (!item.body.Enabled)
            {
                Limb rightHand = picker.AnimController.GetLimb(LimbType.RightHand);
                item.SetTransform(rightHand.SimPosition, 0.0f);
            }

            bool alreadySelected = character.HasSelectedItem(item);
            if (picker.TrySelectItem(item))
            {
                item.body.Enabled = true;
                IsActive = true;

                if (!alreadySelected) Networking.GameServer.Log(character.Name + " equipped " + item.Name, Networking.ServerLog.MessageType.ItemInteraction);
            }
        }

        public override void Unequip(Character character)
        {
            if (picker == null) return;

            picker.DeselectItem(item);

            Networking.GameServer.Log(character.Name + " unequipped " + item.Name, Networking.ServerLog.MessageType.ItemInteraction);

            item.body.Enabled = false;
            IsActive = false;
        }

        public override bool Pick(Character picker)
        {
            if (!attachable)
            {
                return base.Pick(picker);
            }

            if (!base.Pick(picker))
            {
                return false;
            }
            else
            {
                requiredItems.Clear();
                Msg = "";
            }

            attached = false;
            if (body != null) item.body = body;
            //item.body.Enabled = true;

            return true;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (!attachable || item.body == null) return true;
            if (character != null && !character.IsKeyDown(InputType.Aim)) return false;

            item.Drop();

            var containedItems = item.ContainedItems;
            if (containedItems != null)
            {
                foreach (Item contained in containedItems)
                {
                    if (contained.body == null) continue;
                    contained.SetTransform(item.SimPosition, contained.body.Rotation);
                }
            }

            item.body.Enabled = false;
            item.body = null;

            requiredItems = new List<RelatedItem>(prevRequiredItems);
            Msg = prevMsg;

            attached = true;
            
            return true;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.body == null || !item.body.Enabled) return;
            if (picker == null || !picker.HasSelectedItem(item))
            {
                IsActive = false;
                return;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);

            if (item.body.Dir != picker.AnimController.Dir) Flip(item);

            item.Submarine = picker.Submarine;

            picker.AnimController.HoldItem(deltaTime, item, handlePos, holdPos, aimPos, picker.IsKeyDown(InputType.Aim), holdAngle);
        }

        protected void Flip(Item item)
        {
            handlePos[0].X = -handlePos[0].X;
            handlePos[1].X = -handlePos[1].X;
            item.body.Dir = -item.body.Dir;
        }

        public override void OnMapLoaded()
        {
            //prevRequiredItems = new List<RelatedItem>(requiredItems);

            if (!attachable) return;

            if (Attached)
            {
                Use(1.0f);
            }
            else
            {
                if (item.ParentInventory != null)
                {
                    if (body != null)
                    {
                        item.body = body;
                        body.Enabled = false;
                    }
                    attached = false;
                }

                requiredItems.Clear();
                Msg = "";
            }
        }
        
    }
}
