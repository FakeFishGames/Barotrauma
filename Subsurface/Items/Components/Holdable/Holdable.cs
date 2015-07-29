using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;

namespace Subsurface.Items.Components
{
    class Holdable : Pickable
    {
        //the position(s) in the item that the character grabs
        protected Vector2[] handlePos;
        
        //protected Character picker;

        //the distance from the holding characters elbow to center of the physics body of the item
        protected Vector2 holdPos;

        protected Vector2 aimPos;

        protected bool aimable;

        private bool attachable;
        private bool attached;
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

            //holdAngle = ToolBox.GetAttributeFloat(element, "holdangle", 0.0f);
            //holdAngle = MathHelper.ToRadians(holdAngle);
        }

        //public override void Equip(Character picker)
        //{
        //    if (picker == null) return;
        //    if (picker.Inventory == null) return;

        //    this.picker = picker;

        //    for (int i = item.linkedTo.Count - 1; i >= 0; i--)
        //        item.linkedTo[i].RemoveLinked((MapEntity)item);
        //    item.linkedTo.Clear();

        //    System.Diagnostics.Debug.WriteLine("picked item");

        //    //this.picker = picker;
        //    picker.SelectedItem = item;

        //    isActive = true;
        //}

        public override void Drop(Character dropper)
        {
            if (picker == null)
            {
                if (dropper==null) return;
                picker = dropper;
            }
            if (picker.Inventory == null) return;

            item.body.Enabled = true;
            isActive = false;

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
                isActive = true;
            }
        }

        public override void Unequip(Character character)
        {
            if (picker == null) return;

            picker.DeselectItem(item);

            item.body.Enabled = false;
            isActive = false;
        }

        public override bool Pick(Character picker)
        {
            if (!base.Pick(picker)) return false;

            if (!attachable) return false;

            //if (item.body==null)
            //{
            //    DebugConsole.ThrowError("Item " + item + " must have a physics body component to be attachable!");
            //    return false;
            //}

            //if (attached) return false;

            item.body = body;
            item.body.Enabled = true;

            return true;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (!attachable || item.body==null) return true;

            item.Drop();
            item.body.Enabled = false;
            item.body = null;

            attached = true;

            return true;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            //if (picker == null)// || picker.animController.selectedItem != item)
            //{
            //    System.Diagnostics.Debug.WriteLine("drop");
            //    //picker = null;
            //    isActive = false;
            //    return;
            //}
                        
            if (!item.body.Enabled) return;
            if (!picker.HasSelectedItem(item)) isActive = false;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);
            
            if (item.body.Dir != picker.AnimController.Dir) Flip(item);

            AnimController ac = picker.AnimController;

            ac.HoldItem(deltaTime, cam, item, handlePos, holdPos, aimPos, holdAngle);
        }    
    
        protected void Flip(Item item)
        {
            handlePos[0].X = -handlePos[0].X;
            handlePos[1].X = -handlePos[1].X;
            item.body.Dir = -item.body.Dir;
        }

        public override void OnMapLoaded()
        {
            if (attached) Use(1.0f);
        }
    }
}
