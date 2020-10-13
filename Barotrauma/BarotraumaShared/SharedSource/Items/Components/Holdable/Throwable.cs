using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Throwable : Holdable
    {
        private float throwPos;
        private bool throwing, throwDone;

        private bool midAir;

        public Character CurrentThrower
        {
            get;
            private set;
        }

        [Serialize(1.0f, false, description: "The impulse applied to the physics body of the item when thrown. Higher values make the item be thrown faster.")]
        public float ThrowForce { get; set; }

        public Throwable(Item item, XElement element)
            : base(item, element)
        {
            //throwForce = ToolBox.GetAttributeFloat(element, "throwforce", 1.0f);
            if (aimPos == Vector2.Zero)
            {
                aimPos = new Vector2(0.6f, 0.1f);
            }
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            return characterUsable || character == null; //We do the actual throwing in Aim because Use might be used by chems
        }

        public override bool SecondaryUse(float deltaTime, Character character = null)
        {
            if (!throwDone) return false; //This should only be triggered in update
            throwDone = false;
            return true;
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
            if (!item.body.Enabled) { return; }
            if (midAir)
            {
                if (item.body.LinearVelocity.LengthSquared() < 0.01f)
                {
                    CurrentThrower = null;
                    if (statusEffectLists.ContainsKey(ActionType.OnImpact))
                    {
                        foreach (var statusEffect in statusEffectLists[ActionType.OnImpact])
                        {
                            statusEffect.SetUser(null);
                        }
                    }
                    if (statusEffectLists.ContainsKey(ActionType.OnBroken))
                    {
                        foreach (var statusEffect in statusEffectLists[ActionType.OnBroken])
                        {
                            statusEffect.SetUser(null);
                        }
                    }
                    item.body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionPlatform;
                    midAir = false;
                }
                return;
            }

            if (picker == null || picker.Removed || !picker.HasSelectedItem(item))
            {
                IsActive = false;
                return;
            }

            if (picker.IsKeyDown(InputType.Aim) && picker.IsKeyHit(InputType.Shoot)) { throwing = true; }
            if (!picker.IsKeyDown(InputType.Aim) && !throwing) { throwPos = 0.0f; }
            bool aim = picker.IsKeyDown(InputType.Aim) && picker.CanAim;

            if (picker.IsDead || !picker.AllowInput)
            {
                throwing = false;
                aim = false;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);

            if (item.body.Dir != picker.AnimController.Dir) { item.FlipX(relativeToSub: false); }

            AnimController ac = picker.AnimController;

            item.Submarine = picker.Submarine;

            if (!throwing)
            {
                if (aim)
                {
                    throwPos = MathUtils.WrapAnglePi(System.Math.Min(throwPos + deltaTime * 5.0f, MathHelper.PiOver2));
                    ac.HoldItem(deltaTime, item, handlePos, aimPos, Vector2.Zero, false, throwPos);
                }
                else
                {
                    throwPos = 0;
                    ac.HoldItem(deltaTime, item, handlePos, holdPos, Vector2.Zero, false, holdAngle);
                }
            }
            else
            {
                throwPos = MathUtils.WrapAnglePi(throwPos - deltaTime * 15.0f);
                ac.HoldItem(deltaTime, item, handlePos, aimPos, Vector2.Zero, false, throwPos);

                if (throwPos < 0)
                {
                    Vector2 throwVector = Vector2.Normalize(picker.CursorWorldPosition - picker.WorldPosition);
                    //throw upwards if cursor is at the position of the character
                    if (!MathUtils.IsValid(throwVector)) { throwVector = Vector2.UnitY; }

#if SERVER
                    GameServer.Log(GameServer.CharacterLogName(picker) + " threw " + item.Name, ServerLog.MessageType.ItemInteraction);
#endif
                    CurrentThrower = picker;
                    if (statusEffectLists.ContainsKey(ActionType.OnImpact))
                    {
                        foreach (var statusEffect in statusEffectLists[ActionType.OnImpact])
                        {
                            statusEffect.SetUser(CurrentThrower);
                        }
                    }
                    if (statusEffectLists.ContainsKey(ActionType.OnBroken))
                    {
                        foreach (var statusEffect in statusEffectLists[ActionType.OnBroken])
                        {
                            statusEffect.SetUser(CurrentThrower);
                        }
                    }

                    item.Drop(CurrentThrower, createNetworkEvent: GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer);
                    item.body.ApplyLinearImpulse(throwVector * ThrowForce * item.body.Mass * 3.0f, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);

                    //disable platform collisions until the item comes back to rest again
                    item.body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel;
                    midAir = true;

                    ac.GetLimb(LimbType.Head).body.ApplyLinearImpulse(throwVector * 10.0f, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    ac.GetLimb(LimbType.Torso).body.ApplyLinearImpulse(throwVector * 10.0f, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);

                    Limb rightHand = ac.GetLimb(LimbType.RightHand);
                    item.body.AngularVelocity = rightHand.body.AngularVelocity;
                    throwPos = 0;
                    throwDone = true;

                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                    {
                        GameMain.NetworkMember.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnSecondaryUse, this, CurrentThrower.ID });
                    }
                    if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                    {
                        //Stun grenades, flares, etc. all have their throw-related things handled in "onSecondaryUse"
                        ApplyStatusEffects(ActionType.OnSecondaryUse, deltaTime, CurrentThrower, user: CurrentThrower);
                    }
                    throwing = false;
                }
            }
        }    
    }
}
