using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    class Holdable : Pickable
    {
        //the position(s) in the item that the character grabs
        protected Vector2[] handlePos;

        private List<RelatedItem> prevRequiredItems;

        string prevMsg;
        
        //the distance from the holding characters elbow to center of the physics body of the item
        protected Vector2 holdPos;

        protected Vector2 aimPos;

        protected bool aimable;

        private bool attachable, attached, attachedByDefault;
        private PhysicsBody body;

        //the angle in which the character holds the item
        protected float holdAngle;

        [HasDefaultValue(false, true)]
        public bool Attached
        {
            get { return attached; }
            set { attached = value; }
        }

        [HasDefaultValue(false, false)]
        public bool Aimable
        {
            get { return aimable; }
            set { aimable = value; }
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
            get { return MathHelper.ToDegrees(holdAngle);  }
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

            if (attachedByDefault || (Screen.Selected == GameMain.EditMapScreen && Submarine.Loaded!=null)) Use(1.0f);


            //holdAngle = ToolBox.GetAttributeFloat(element, "holdangle", 0.0f);
            //holdAngle = MathHelper.ToRadians(holdAngle);
        }

        public override void Drop(Character dropper)
        {
            if (picker == null)
            {
                if (dropper==null) return;
                picker = dropper;
            }
            if (picker.Inventory == null) return;

            item.body.Enabled = true;
            IsActive = false;

            //item.Unequip();

            picker.DeselectItem(item);
            picker.Inventory.RemoveItem(item);
            picker = null;
        }

        public override void Equip(Character character)
        {
            picker = character;

            if (!item.body.Enabled)
            {
                Limb rightHand = picker.AnimController.GetLimb(LimbType.RightHand);
                item.SetTransform(rightHand.SimPosition, 0.0f);                
            }

            if (picker.TrySelectItem(item))
            {
                item.body.Enabled = true;
                IsActive = true;
            }
        }

        public override void Unequip(Character character)
        {
            if (picker == null) return;

            picker.DeselectItem(item);
            
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
            if (body!=null) item.body = body;
            //item.body.Enabled = true;

            return true;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (!attachable || item.body==null) return true;
            if (character != null && !character.GetInputState(InputType.SecondaryHeld)) return false;

            item.Drop();

            var containedItems = item.ContainedItems;
            if (containedItems!=null)
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

            item.NewComponentEvent(this, true);

            return true;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }

        public override void Update(float deltaTime, Camera cam)
        {         
            if (!item.body.Enabled) return;
            if (!picker.HasSelectedItem(item)) IsActive = false;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);
            
            if (item.body.Dir != picker.AnimController.Dir) Flip(item);

            AnimController ac = picker.AnimController;

            //item.sprite.Depth = picker.AnimController.GetLimb(LimbType.RightHand).sprite.Depth + 0.01f;            

            ac.HoldItem(deltaTime, item, handlePos, holdPos, aimPos, picker.GetInputState(InputType.SecondaryHeld), holdAngle);
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

            if (attached)
            {
                Use(1.0f);
            }
            else
            {                
                requiredItems.Clear();
                Msg = "";
            }
        }

        public override void FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetOutgoingMessage message)
        {
            message.Write(item.SimPosition.X);
            message.Write(item.SimPosition.Y);
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message)
        {
            Vector2 newPos = Vector2.Zero;

            try
            {
                newPos = new Vector2(message.ReadFloat(), message.ReadFloat());
            }

            catch
            { 
                return;
            }

            item.SetTransform(newPos, 0.0f);
            if (!attached) Use(1.0f);
        }
    }
}
