using FarseerPhysics;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Items.Components
{
    class LimbPos : ISerializableEntity
    {
        [Editable]
        public LimbType LimbType { get; set; }
        [Editable]
        public Vector2 Position { get; set; }

        public bool AllowUsingLimb;

        public string Name => LimbType.ToString();

        public Dictionary<string, SerializableProperty> SerializableProperties => null;

        public LimbPos(LimbType limbType, Vector2 position, bool allowUsingLimb)
        {
            LimbType = limbType;
            Position = position;
            AllowUsingLimb = allowUsingLimb;
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

        [Editable, Serialize(false, false, description: "When enabled, the item will continuously send out a 0/1 signal and interacting with it will flip the signal (making the item behave like a switch). When disabled, the item will simply send out 1 when interacted with.", alwaysUseInstanceValues: true)]
        public bool IsToggle
        {
            get;
            set;
        }

        [Editable, Serialize(false, false, description: "Whether the item is toggled on/off. Only valid if IsToggle is set to true.", alwaysUseInstanceValues: true)]
        public bool State
        {
            get;
            set;
        }

        [Serialize(true, false, description: "Should the HUD (inventory, health bar, etc) be hidden when this item is selected.")]
        public bool HideHUD
        {
            get;
            set;
        }

        public enum UseEnvironment
        {
            Air, Water, Both
        };

        [Serialize(UseEnvironment.Both, false, description: "Can the item be selected in air, underwater or both.")]
        public UseEnvironment UsableIn { get; set; }

        [Serialize(false, false, description: "Should the character using the item be drawn behind the item.")]
        public bool DrawUserBehind
        {
            get;
            set;
        }

        public bool ControlCharacterPose
        {
            get { return limbPositions.Count > 0; }
        }

        public bool UserInCorrectPosition
        {
            get;
            private set;
        }

        public bool AllowAiming
        {
            get;
            private set;
        } = true;

        public Controller(Item item, XElement element)
            : base(item, element)
        {
            limbPositions = new List<LimbPos>();

            userPos = element.GetAttributeVector2("UserPos", Vector2.Zero);

            Enum.TryParse(element.GetAttributeString("direction", "None"), out dir);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name != "limbposition") { continue; }
                string limbStr = subElement.GetAttributeString("limb", "");
                if (!Enum.TryParse(subElement.Attribute("limb").Value, out LimbType limbType))
                {
                    DebugConsole.ThrowError($"Error in item \"{item.Name}\" - {limbStr} is not a valid limb type.");
                }
                else
                {
                    LimbPos limbPos = new LimbPos(limbType,
                        subElement.GetAttributeVector2("position", Vector2.Zero),
                        subElement.GetAttributeBool("allowusinglimb", false));
                    limbPositions.Add(limbPos);
                    if (!limbPos.AllowUsingLimb)
                    {
                        if (limbType == LimbType.RightHand || limbType == LimbType.RightForearm || limbType == LimbType.RightArm ||
                            limbType == LimbType.LeftHand || limbType == LimbType.LeftForearm || limbType == LimbType.LeftArm)
                        {
                            AllowAiming = false;
                        }
                    }
                }

            }

            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            this.cam = cam;
            UserInCorrectPosition = false;

            if (IsToggle)
            {
                item.SendSignal(State ? "1" : "0", "signal_out");
                item.SendSignal(State ? "1" : "0", "trigger_out");
            }

            if (user == null 
                || user.Removed
                || user.SelectedConstruction != item
                || item.ParentInventory != null
                || !user.CanInteractWith(item) 
                || (UsableIn == UseEnvironment.Water && !user.AnimController.InWater)
                || (UsableIn == UseEnvironment.Air && user.AnimController.InWater))
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
                    if (diff.LengthSquared() > 30.0f * 30.0f)
                    {
                        user.AnimController.TargetMovement = Vector2.Clamp(diff * 0.01f, -Vector2.One, Vector2.One);
                        user.AnimController.TargetDir = diff.X > 0.0f ? Direction.Right : Direction.Left;
                    }
                    else
                    {
                        user.AnimController.TargetMovement = Vector2.Zero;
                        UserInCorrectPosition = true;
                    }
                }
                else
                {
                    diff.Y = 0.0f;
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && user != Character.Controlled)
                    {
                        if (Math.Abs(diff.X) > 20.0f)
                        {
                            //wait for the character to walk to the correct position
                            return;
                        }
                        else if (Math.Abs(diff.X) > 0.1f)
                        {
                            //aim to keep the collider at the correct position once close enough
                            user.AnimController.Collider.LinearVelocity = new Vector2(
                                diff.X * 0.1f, 
                                user.AnimController.Collider.LinearVelocity.Y);
                        }
                    }
                    else
                    {
                        if (Math.Abs(diff.X) > 10.0f)
                        {
                            user.AnimController.TargetMovement = Vector2.Normalize(diff);
                            user.AnimController.TargetDir = diff.X > 0.0f ? Direction.Right : Direction.Left;
                            return;
                        }
                    }
                    user.AnimController.TargetMovement = Vector2.Zero;
                    UserInCorrectPosition = true;
                }
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, user);

            if (limbPositions.Count == 0) { return; }

            user.AnimController.Anim = AnimController.Animation.UsingConstruction;

            user.AnimController.ResetPullJoints();

            if (dir != 0) { user.AnimController.TargetDir = dir; }

            foreach (LimbPos lb in limbPositions)
            {
                Limb limb = user.AnimController.GetLimb(lb.LimbType);
                if (limb == null || !limb.body.Enabled) { continue; }

                if (lb.AllowUsingLimb)
                {
                    switch (lb.LimbType)
                    {
                        case LimbType.RightHand:
                        case LimbType.RightForearm:
                        case LimbType.RightArm:
                            if (user.Inventory.GetItemInLimbSlot(InvSlotType.RightHand) != null) { continue; }
                            break;
                        case LimbType.LeftHand:
                        case LimbType.LeftForearm:
                        case LimbType.LeftArm:
                            if (user.Inventory.GetItemInLimbSlot(InvSlotType.LeftHand) != null) { continue; }
                            break;
                    }
                }

                limb.Disabled = true;

                Vector2 worldPosition = new Vector2(item.WorldRect.X, item.WorldRect.Y) + lb.Position * item.Scale;
                Vector2 diff = worldPosition - limb.WorldPosition;

                limb.PullJointEnabled = true;
                limb.PullJointWorldAnchorB = limb.SimPosition + ConvertUnits.ToSimUnits(diff);
            }
        }

        private double lastUsed;

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

            if (IsToggle && (activator == null || lastUsed < Timing.TotalTime - 0.1))
            {
                if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                {
                    State = !State;
#if SERVER
                    item.CreateServerEvent(this);
#endif
                }
            }
            else
            {
                item.SendSignal(new Signal("1", sender: user), "trigger_out");
            }

            lastUsed = Timing.TotalTime;

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

                cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, (focusTarget as Item).Prefab.OffsetOnSelected * focusTarget.OffsetOnSelectedMultiplier, deltaTime * 10.0f);
                HideHUDs(true);
            }
#endif

            if (!character.IsRemotePlayer || character.ViewTarget == focusTarget)
            {
                Vector2 centerPos = new Vector2(focusTarget.WorldRect.Center.X, focusTarget.WorldRect.Center.Y);

                Turret turret = focusTarget.GetComponent<Turret>();
                if (turret != null)
                {
                    centerPos = new Vector2(focusTarget.WorldRect.X + turret.TransformedBarrelPos.X, focusTarget.WorldRect.Y - turret.TransformedBarrelPos.Y);
                }

                Vector2 offset = character.CursorWorldPosition - centerPos;
                offset.Y = -offset.Y;

                targetRotation = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(offset));
            }
            return true;
        }

        public Item GetFocusTarget()
        {
            Item focusTarget = null;
            for (int c = 0; c < 2; c++)
            {
                //try finding the item to focus on using trigger_out, and if that fails, using position_out
                string connectionName = c == 0 ? "trigger_out" : "position_out";
                string signal = c == 0 ? "0" : MathHelper.ToDegrees(targetRotation).ToString("G", CultureInfo.InvariantCulture);
                if (!item.SendSignal(new Signal(signal, sender: user), connectionName) || focusTarget != null)
                {
                    continue;
                }

                for (int i = item.LastSentSignalRecipients.Count - 1; i >= 0; i--)
                {
                    if (item.LastSentSignalRecipients[i].Item.Condition <= 0.0f || item.LastSentSignalRecipients[i].IsPower) { continue; }
                    if (item.LastSentSignalRecipients[i].Item.Prefab.FocusOnSelected)
                    {
                        focusTarget = item.LastSentSignalRecipients[i].Item;
                        break;
                    }
                }
            }

            return focusTarget;
        }

        public override bool Pick(Character picker)
        {
#if CLIENT
            if (Screen.Selected == GameMain.SubEditorScreen) { return false; }
#endif
            if (IsToggle)
            {
                if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                {
                    State = !State;
#if SERVER
                    item.CreateServerEvent(this);
#endif
                }
            }
            else
            {
                item.SendSignal(new Signal("1", sender: picker), "signal_out");
            }
#if CLIENT
            PlaySound(ActionType.OnUse, picker);
#endif
            return true;
        }

        private void CancelUsing(Character character)
        {
            if (character == null || character.Removed) { return; }

            foreach (LimbPos lb in limbPositions)
            {
                Limb limb = character.AnimController.GetLimb(lb.LimbType);
                if (limb == null) { continue; }

                limb.Disabled = false;
                limb.PullJointEnabled = false;
            }

            //disable flipping for 0.5 seconds, because flipping the character when it's in a weird pose (e.g. lying in bed) can mess up the ragdoll
            if (character.AnimController is HumanoidAnimController humanoidAnim)
            {
                humanoidAnim.LockFlippingUntil = (float)Timing.TotalTime + 0.5f;
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

            if (UsableIn == UseEnvironment.Water && !activator.AnimController.InWater ||
                UsableIn == UseEnvironment.Air && activator.AnimController.InWater)
            {
                return false;
            }

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
            item.SendSignal(new Signal("1", sender: user), "signal_out");
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
                float diff = (item.Rect.X + limbPositions[i].Position.X * item.Scale) - item.Rect.Center.X;

                Vector2 flippedPos =
                    new Vector2(
                        (item.Rect.Center.X - diff - item.Rect.X) / item.Scale,
                        limbPositions[i].Position.Y);
                limbPositions[i] = new LimbPos(limbPositions[i].LimbType, flippedPos, limbPositions[i].AllowUsingLimb);
            }
        }

        public override void FlipY(bool relativeToSub)
        {
            userPos.Y = -UserPos.Y;

            for (int i = 0; i < limbPositions.Count; i++)
            {
                float diff = (item.Rect.Y + limbPositions[i].Position.Y) - item.Rect.Center.Y;

                Vector2 flippedPos =
                    new Vector2(
                        limbPositions[i].Position.X,
                        item.Rect.Center.Y - diff - item.Rect.Y);
                limbPositions[i] = new LimbPos(limbPositions[i].LimbType, flippedPos, limbPositions[i].AllowUsingLimb);
            }
        }

        public override bool HasAccess(Character character)
        {
            if (!item.IsInteractable(character)) { return false; }
            return base.HasAccess(character);
        }

        partial void HideHUDs(bool value);
    }
}
