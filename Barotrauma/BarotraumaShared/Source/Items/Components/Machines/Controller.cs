using FarseerPhysics;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    struct LimbPos
    {
        public LimbType limbType;
        public Vector2 position;

        public LimbPos(LimbType limbType, Vector2 position)
        {
            this.limbType = limbType;
            this.position = position;
        }
    }

    partial class Controller : ItemComponent, IServerSerializable
    {
        //where the limbs of the user should be positioned when using the controller
        private readonly List<LimbPos> limbPositions;

        private Direction dir;

        //the position where the user walks to when using the controller 
        //(relative to the position of the item)
        private Vector2 userPos;

        private Camera cam;

        private Character user;

        private Item focusTarget;
        private float targetRotation;

        private bool state;

        public Vector2 UserPos
        {
            get { return userPos; }
            set { userPos = value; }
        }

        public Character User
        {
            get { return user; }
        }

        public IEnumerable<LimbPos> LimbPositions { get { return limbPositions; } }

        [Editable, Serialize(false, false, description: "When enabled, the item will continuously send out a 0/1 signal and interacting with it will flip the signal (making the item behave like a switch). When disabled, the item will simply send out 1 when interacted with.")]
        public bool IsToggle
        {
            get;
            set;
        }

        public Controller(Item item, XElement element)
            : base(item, element)
        {
            limbPositions = new List<LimbPos>();

            userPos = element.GetAttributeVector2("UserPos", Vector2.Zero);

            Enum.TryParse(element.GetAttributeString("direction", "None"), out dir);
                
            foreach (XElement el in element.Elements())
            {
                if (el.Name != "limbposition") continue;

                LimbPos lp = new LimbPos();

                try
                {
                    lp.limbType = (LimbType)Enum.Parse(typeof(LimbType), el.Attribute("limb").Value, true);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + element + ": " + e.Message, e);
                }

                lp.position = el.GetAttributeVector2("position", Vector2.Zero);

                limbPositions.Add(lp);
            }

            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            this.cam = cam;

            if (IsToggle)
            {
                item.SendSignal(0, state ? "1" : "0", "signal_out", sender: null);
            }

            if (user == null 
                || user.Removed
                || user.SelectedConstruction != item
                || !user.CanInteractWith(item))
            {
                if (user != null)
                {
                    CancelUsing(user);
                    user = null;
                }
                if (!IsToggle) { IsActive = false; }
                return;
            }

            user.AnimController.Anim = AnimController.Animation.UsingConstruction;

            if (userPos != Vector2.Zero)
            {
                Vector2 diff = (item.WorldPosition + userPos) - user.WorldPosition;

                if (user.AnimController.InWater)
                {
                    if (diff.Length() > 30.0f)
                    {
                        user.AnimController.TargetMovement = Vector2.Clamp(diff*0.01f, -Vector2.One, Vector2.One);
                        user.AnimController.TargetDir = diff.X > 0.0f ? Direction.Right : Direction.Left;
                    }
                    else
                    {
                        user.AnimController.TargetMovement = Vector2.Zero;
                    }
                }
                else
                {
                    diff.Y = 0.0f;
                    if (diff != Vector2.Zero && diff.LengthSquared() > 10.0f * 10.0f)
                    {
                        user.AnimController.TargetMovement = Vector2.Normalize(diff);
                        user.AnimController.TargetDir = diff.X > 0.0f ? Direction.Right : Direction.Left;
                        return;
                    }
                    user.AnimController.TargetMovement = Vector2.Zero;                    
                }
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, user);

            if (limbPositions.Count == 0) { return; }

            user.AnimController.Anim = AnimController.Animation.UsingConstruction;

            user.AnimController.ResetPullJoints();

            if (dir != 0) user.AnimController.TargetDir = dir;

            foreach (LimbPos lb in limbPositions)
            {
                Limb limb = user.AnimController.GetLimb(lb.limbType);
                if (limb == null || !limb.body.Enabled) continue;

                limb.Disabled = true;
                
                Vector2 worldPosition = new Vector2(item.WorldRect.X, item.WorldRect.Y) + lb.position * item.Scale;
                Vector2 diff = worldPosition - limb.WorldPosition;

                limb.PullJointEnabled = true;
                limb.PullJointWorldAnchorB = limb.SimPosition + ConvertUnits.ToSimUnits(diff);
            }
        }

        public override bool Use(float deltaTime, Character activator = null)
        {
            if (activator != user)
            {
                return false;
            }

            if (user == null || user.Removed ||
                user.SelectedConstruction != item || !user.CanInteractWith(item))
            {
                user = null;
                return false;
            }

            item.SendSignal(0, "1", "trigger_out", user);

            ApplyStatusEffects(ActionType.OnUse, 1.0f, activator);
            
            return true;
        }
        
        public override bool SecondaryUse(float deltaTime, Character character = null)
        {
            if (this.user != character)
            {
                return false;
            }

            if (this.user == null || character.Removed ||
                this.user.SelectedConstruction != item || !character.CanInteractWith(item))
            {
                this.user = null;
                return false;
            }
            if (character == null) return false;

            focusTarget = GetFocusTarget();
            if (focusTarget == null)
            {
                Vector2 centerPos = new Vector2(item.WorldRect.Center.X, item.WorldRect.Center.Y);

                Vector2 offset = character.CursorWorldPosition - centerPos;
                offset.Y = -offset.Y;

                targetRotation = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(offset));
                return false;
            }

            character.ViewTarget = focusTarget;
#if CLIENT
            if (character == Character.Controlled && cam != null)
            {
                Lights.LightManager.ViewTarget = focusTarget;
                cam.TargetPos = focusTarget.WorldPosition;

                cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, (focusTarget as Item).Prefab.OffsetOnSelected, deltaTime * 10.0f);
                HideHUDs(true);
            }
#endif

            if (!character.IsRemotePlayer || character.ViewTarget == focusTarget)
            {
                Vector2 centerPos = new Vector2(item.WorldRect.Center.X, item.WorldRect.Center.Y);

                Item targetItem = focusTarget as Item;
                if (targetItem != null)
                {
                    Turret turret = targetItem.GetComponent<Turret>();
                    if (turret != null)
                    {
                        centerPos = new Vector2(targetItem.WorldRect.X + turret.TransformedBarrelPos.X, targetItem.WorldRect.Y - turret.TransformedBarrelPos.Y);
                    }
                }

                Vector2 offset = character.CursorWorldPosition - centerPos;
                offset.Y = -offset.Y;

                targetRotation = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(offset));
            }
            return true;
        }

        private Item GetFocusTarget()
        {
            item.SendSignal(0, MathHelper.ToDegrees(targetRotation).ToString("G", CultureInfo.InvariantCulture), "position_out", user);

            for (int i = item.LastSentSignalRecipients.Count - 1; i >= 0; i--)
            {
                if (item.LastSentSignalRecipients[i].Condition <= 0.0f) continue;
                if (item.LastSentSignalRecipients[i].Prefab.FocusOnSelected)
                {
                    return item.LastSentSignalRecipients[i];
                }
            }
            
            return null;
        }

        public override bool Pick(Character picker)
        {
            if (IsToggle)
            {
                if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                {
                    state = !state;
#if SERVER
                    item.CreateServerEvent(this);
#endif
                }
            }
            else
            {
                item.SendSignal(0, "1", "signal_out", picker);
            }
#if CLIENT
            PlaySound(ActionType.OnUse, item.WorldPosition, picker);
#endif
            return true;
        }

        private void CancelUsing(Character character)
        {
            if (character == null || character.Removed) { return; }

            foreach (LimbPos lb in limbPositions)
            {
                Limb limb = character.AnimController.GetLimb(lb.limbType);
                if (limb == null) continue;

                limb.Disabled = false;
                limb.PullJointEnabled = false;
            }

            if (character.SelectedConstruction == this.item) { character.SelectedConstruction = null; }

            character.AnimController.Anim = AnimController.Animation.None;
            if (character == Character.Controlled)
            {
                HideHUDs(false);
            }
#if SERVER
            item.CreateServerEvent(this);
#endif
        }

        public override bool Select(Character activator)
        {
            if (activator == null || activator.Removed) { return false; }

            //someone already using the item
            if (user != null && !user.Removed)
            {
                if (user == activator)
                {
                    IsActive = false;
                    CancelUsing(user);
                    user = null;
                    return false;
                }
            }
            else
            {
                user = activator;
                IsActive = true;
            }
#if SERVER
            item.CreateServerEvent(this);
#endif
            item.SendSignal(0, "1", "signal_out", user);
            return true;
        }

        public override void FlipX(bool relativeToSub)
        {
            if (dir != Direction.None)
            {
                dir = dir == Direction.Left ? Direction.Right : Direction.Left;
            }

            userPos.X = -UserPos.X;            

            for (int i = 0; i < limbPositions.Count; i++)
            {
                float diff = (item.Rect.X + limbPositions[i].position.X * item.Scale) - item.Rect.Center.X;

                Vector2 flippedPos =
                    new Vector2(
                        (item.Rect.Center.X - diff - item.Rect.X) / item.Scale,
                        limbPositions[i].position.Y);

                limbPositions[i] = new LimbPos(limbPositions[i].limbType, flippedPos);
            }
        }

        public override void FlipY(bool relativeToSub)
        {
            userPos.Y = -UserPos.Y;

            for (int i = 0; i < limbPositions.Count; i++)
            {
                float diff = (item.Rect.Y + limbPositions[i].position.Y) - item.Rect.Center.Y;

                Vector2 flippedPos =
                    new Vector2(
                        limbPositions[i].position.X,
                        item.Rect.Center.Y - diff - item.Rect.Y);

                limbPositions[i] = new LimbPos(limbPositions[i].limbType, flippedPos);
            }
        }

        partial void HideHUDs(bool value);
    }
}
