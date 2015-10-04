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
            if (!character.GetInputState(InputType.SecondaryHeld) || throwing) return false;

            //Vector2 diff = Vector2.Normalize(character.CursorPosition - character.AnimController.RefLimb.Position);

            //if (character.SelectedItems[1]==item)
            //{
            //    Limb leftHand = character.AnimController.GetLimb(LimbType.LeftHand);
            //    leftHand.body.ApplyLinearImpulse(diff * 20.0f);
            //    leftHand.Disabled = true;
            //}

            //if (character.SelectedItems[0] == item)
            //{
            //    Limb rightHand = character.AnimController.GetLimb(LimbType.RightHand);
            //    rightHand.body.ApplyLinearImpulse(diff * 20.0f);
            //    rightHand.Disabled = true;
            //}

            throwing = true;

            IsActive = true;
            return true;
        }

        public override void SecondaryUse(float deltaTime, Character character = null)
        {
            if (throwing) return;
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
            if (!picker.HasSelectedItem(item)) IsActive = false;

            if (!picker.GetInputState(InputType.SecondaryHeld) && !throwing) throwPos = 0.0f;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);

            if (item.body.Dir != picker.AnimController.Dir) Flip(item);

            AnimController ac = picker.AnimController;

            if (!throwing)
            {
                if (picker.GetInputState(InputType.SecondaryHeld))
                {
                    throwPos = (float)System.Math.Min(throwPos+deltaTime*5.0f, MathHelper.Pi*0.7f);

                    ac.HoldItem(deltaTime, item, handlePos, new Vector2(0.6f, -0.0f), new Vector2(-0.3f, 0.2f), false, throwPos);
                }
                else
                {
                    ac.HoldItem(deltaTime, item, handlePos, new Vector2(throwPos, 0.0f), aimPos, false, 0.0f);
                }


            }
            else
            {
                //Vector2 diff = Vector2.Normalize(picker.CursorPosition - ac.RefLimb.Position);
                //diff.X = diff.X * ac.Dir;

                throwPos -= deltaTime*15.0f;

                //angl = -hitPos * 2.0f;
                //    System.Diagnostics.Debug.WriteLine("<1.0f "+hitPos);



                ac.HoldItem(deltaTime, item, handlePos, new Vector2(0.6f, 0.0f), new Vector2(-0.3f, 0.2f), false, throwPos);
                //}
                //else
                //{
                //    System.Diagnostics.Debug.WriteLine(">1.0f " + hitPos);
                //    ac.HoldItem(deltaTime, item, handlePos, new Vector2(0.5f, 0.2f), new Vector2(1.0f, 0.2f), false, 0.0f);
                //}


                if (throwPos < -0.0)
                {

                    Vector2 throwVector = picker.CursorPosition - picker.AnimController.RefLimb.Position;
                    throwVector = Vector2.Normalize(throwVector);

                    item.Drop();
                    item.body.ApplyLinearImpulse(throwVector * throwForce * item.body.Mass * 3.0f);

                    ac.GetLimb(LimbType.Head).body.ApplyLinearImpulse(throwVector*10.0f);
                    ac.GetLimb(LimbType.Torso).body.ApplyLinearImpulse(throwVector * 10.0f);

                    Limb rightHand = ac.GetLimb(LimbType.RightHand);
                    item.body.AngularVelocity = rightHand.body.AngularVelocity;

                    throwing = false;
                }
            }
        }    
    }
}
