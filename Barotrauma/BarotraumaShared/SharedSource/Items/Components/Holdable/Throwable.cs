using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma.Items.Components
{
    class Throwable : Holdable
    {
        enum ThrowState
        {
            None,
            Initiated,
            Throwing
        }

        private const float ThrowAngleStart = -MathHelper.PiOver2, ThrowAngleEnd = MathHelper.PiOver2;
        private float throwAngle = ThrowAngleStart;

        private bool midAir;

        private ThrowState throwState;


        //continuous collision detection is used while the item is moving faster than this
        const float ContinuousCollisionThreshold = 5.0f;

        public Character CurrentThrower
        {
            get;
            private set;
        }

        [Serialize(1.0f, IsPropertySaveable.No, description: "The impulse applied to the physics body of the item when thrown. Higher values make the item be thrown faster.")]
        public float ThrowForce { get; set; }

        public Throwable(Item item, ContentXElement element)
            : base(item, element)
        {
            if (aimPos == Vector2.Zero)
            {
                aimPos = new Vector2(0.6f, 0.1f);
            }
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            //actual throwing logic is handled in Update
            return characterUsable || character == null;
        }

        public override bool SecondaryUse(float deltaTime, Character character = null)
        {
            //actual throwing logic is handled in Update - SecondaryUse only triggers when the item is thrown
            return false;
        }

        public override void Drop(Character dropper)
        {
            base.Drop(dropper);
            throwState = ThrowState.None;
            throwAngle = ThrowAngleStart;
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
                if (item.body.FarseerBody.IsBullet)
                {
                    if (item.body.LinearVelocity.LengthSquared() < ContinuousCollisionThreshold * ContinuousCollisionThreshold)
                    {
                        item.body.FarseerBody.IsBullet = false;
                    }
                }
                if (item.body.LinearVelocity.LengthSquared() < 0.01f)
                {
                    CurrentThrower = null;
                    if (statusEffectLists?.ContainsKey(ActionType.OnImpact) ?? false)
                    {
                        foreach (var statusEffect in statusEffectLists[ActionType.OnImpact])
                        {
                            statusEffect.SetUser(null);
                        }
                    }
                    if (statusEffectLists?.ContainsKey(ActionType.OnBroken) ?? false)
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

            if (picker == null || picker.Removed || !picker.HeldItems.Contains(item))
            {
                IsActive = false;
                return;
            }

            if (throwState != ThrowState.Throwing)
            {
                if (picker.IsKeyDown(InputType.Aim)) 
                {
                    if (picker.IsKeyDown(InputType.Shoot)) { throwState = ThrowState.Initiated; }
                }
                else if (throwState != ThrowState.Initiated)
                { 
                    throwAngle = ThrowAngleStart; 
                }
            }

            bool aim = picker.IsKeyDown(InputType.Aim) && picker.CanAim;
            if (picker.IsDead || !picker.AllowInput)
            {
                throwState = ThrowState.None;
                aim = false;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);
            //return if the status effect got rid of the picker somehow
            if (picker == null || picker.Removed || !picker.HeldItems.Contains(item))
            {
                IsActive = false;
                return;
            }

            if (item.body.Dir != picker.AnimController.Dir) { item.FlipX(relativeToSub: false); }

            AnimController ac = picker.AnimController;

            item.Submarine = picker.Submarine;

            if (throwState != ThrowState.Throwing)
            {
                if (aim || throwState == ThrowState.Initiated)
                {
                    throwAngle = System.Math.Min(throwAngle + deltaTime * 8.0f, ThrowAngleEnd);
                    ac.HoldItem(deltaTime, item, handlePos, aimPos, Vector2.Zero, aim: false, throwAngle);
                    if (throwAngle >= ThrowAngleEnd && throwState == ThrowState.Initiated)
                    {
                        throwState = ThrowState.Throwing;
                    }
                }
                else
                {
                    throwAngle = ThrowAngleStart;
                    ac.HoldItem(deltaTime, item, handlePos, holdPos, Vector2.Zero, aim: false, holdAngle);
                }
            }
            else
            {
                throwAngle = MathUtils.WrapAnglePi(throwAngle - deltaTime * 15.0f);
                ac.HoldItem(deltaTime, item, handlePos, aimPos, Vector2.Zero, aim: false, throwAngle);

                if (throwAngle < 0)
                {
                    Vector2 throwVector = Vector2.Normalize(picker.CursorWorldPosition - picker.WorldPosition);
                    //throw upwards if cursor is at the position of the character
                    if (!MathUtils.IsValid(throwVector)) { throwVector = Vector2.UnitY; }

#if SERVER
                    GameServer.Log(GameServer.CharacterLogName(picker) + " threw " + item.Name, ServerLog.MessageType.ItemInteraction);
#endif
                    CurrentThrower = picker;
                    if (statusEffectLists?.ContainsKey(ActionType.OnImpact) ?? false)
                    {
                        foreach (var statusEffect in statusEffectLists[ActionType.OnImpact])
                        {
                            statusEffect.SetUser(CurrentThrower);
                        }
                    }
                    if (statusEffectLists?.ContainsKey(ActionType.OnBroken) ?? false)
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
                    item.body.FarseerBody.IsBullet = true;
                    midAir = true;

                    ac.GetLimb(LimbType.Head)?.body.ApplyLinearImpulse(throwVector * 10.0f, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    ac.GetLimb(LimbType.Torso)?.body.ApplyLinearImpulse(throwVector * 10.0f, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);

                    Limb rightHand = ac.GetLimb(LimbType.RightHand);
                    item.body.AngularVelocity = rightHand.body.AngularVelocity;
                    throwAngle = ThrowAngleStart;
                    IsActive = true;

                    if (GameMain.NetworkMember is { IsServer: true })
                    {
                        GameMain.NetworkMember.CreateEntityEvent(item, new Item.ApplyStatusEffectEventData(ActionType.OnSecondaryUse, this, CurrentThrower));
                    }
                    if (!(GameMain.NetworkMember is { IsClient: true }))
                    {
                        //Stun grenades, flares, etc. all have their throw-related things handled in "onSecondaryUse"
                        ApplyStatusEffects(ActionType.OnSecondaryUse, deltaTime, CurrentThrower, useTarget: CurrentThrower, user: CurrentThrower);
                    }
                    throwState = ThrowState.None;
                }
            }
        }    
    }
}
