using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Throwable : Holdable
    {
        float throwForce;

        float throwPos;

        bool throwing;

        [SerializableProperty(1.0f, false)]
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
            if (!character.IsKeyDown(InputType.Aim) || throwing) return false;
            
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

            if (!picker.IsKeyDown(InputType.Aim) && !throwing) throwPos = 0.0f;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);

            if (item.body.Dir != picker.AnimController.Dir) Flip(item);

            AnimController ac = picker.AnimController;

            item.Submarine = picker.Submarine;

            if (!throwing)
            {
                if (picker.IsKeyDown(InputType.Aim))
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
                throwPos -= deltaTime * 15.0f;

                ac.HoldItem(deltaTime, item, handlePos, new Vector2(0.6f, 0.0f), new Vector2(-0.3f, 0.2f), false, throwPos);

                if (throwPos < -0.0)
                {
                    Vector2 throwVector = picker.CursorWorldPosition - picker.WorldPosition;
                    throwVector = Vector2.Normalize(throwVector);

                    GameServer.Log(picker.Name + " threw " + item.Name, ServerLog.MessageType.ItemInteraction);

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
