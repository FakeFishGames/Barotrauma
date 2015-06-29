using FarseerPhysics;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class Throwable : Holdable
    {
        float throwForce;

        float throwPos;

        bool throwing;

        [HasDefaultValue(1.0f, false)]
        public float ThrowForce
        {
            get { return throwForce; }
            set { throwForce = value; }
        }

        public Throwable(Item item, XElement element)
            : base(item, element)
        {
            //throwForce = ToolBox.GetAttributeFloat(element, "throwforce", 1.0f);
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null) return false;
            if (!character.SecondaryKeyDown.State || throwing) return false;

            throwing = true;

            isActive = true;
            return true;
        }

        public override void SecondaryUse(float deltaTime, Character character = null)
        {
            if (throwing) return;
            throwPos = 0.25f;
        }

        public override void Drop(Character dropper)
        {
            base.Drop(dropper);

            throwing = false;
            throwPos = 0.0f;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            if (!item.body.Enabled) return;
            if (!picker.HasSelectedItem(item)) isActive = false;

            if (!picker.SecondaryKeyDown.State && !throwing) throwPos = 0.0f;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);

            if (item.body.Dir != picker.animController.Dir) Flip(item);

            AnimController ac = picker.animController;

            ac.HoldItem(deltaTime, cam, item, handlePos, new Vector2(throwPos, 0.0f), aimPos, holdAngle);

            if (!throwing) return;

            throwPos += deltaTime*5.0f;

            Vector2 throwVector = ConvertUnits.ToSimUnits(picker.CursorPosition) - item.body.Position;
            throwVector = Vector2.Normalize(throwVector);

            if (handlePos[0]!=Vector2.Zero)
            {
                Limb leftHand = ac.GetLimb(LimbType.LeftHand);
                leftHand.body.ApplyForce(throwVector*10.0f);
            }

            if (handlePos[1] != Vector2.Zero)
            {
                Limb rightHand = ac.GetLimb(LimbType.RightHand);
                rightHand.body.ApplyForce(throwVector * 10.0f);
            }
                        
            if (throwPos>1.0f)
            {
                item.Drop();
                item.body.ApplyLinearImpulse(throwVector * throwForce * item.body.Mass * 3.0f);
            }
        }    
    }
}
