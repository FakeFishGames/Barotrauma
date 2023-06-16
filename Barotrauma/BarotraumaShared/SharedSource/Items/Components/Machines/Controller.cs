using FarseerPhysics;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

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

        public Dictionary<Identifier, SerializableProperty> SerializableProperties => null;

        public LimbPos(LimbType limbType, Vector2 position, bool allowUsingLimb)
        {
            LimbType = limbType;
            Position = position;
            AllowUsingLimb = allowUsingLimb;
        }
    }

    partial class Controller : ItemComponent, IServerSerializable
    {
        private protected string output, falseOutput;

        //where the limbs of the user should be positioned when using the controller
        private readonly List<LimbPos> limbPositions = new List<LimbPos>();

        private Direction dir;
        public Direction Direction => dir;

        //the position where the user walks to when using the controller 
        //(relative to the position of the item)
        private Vector2 userPos;

        private Camera cam;

        private Character user;

        private Item focusTarget;
        private float targetRotation;

        [InGameEditable, Serialize("1", IsPropertySaveable.Yes, description: "The signal sent when the controller is being activated or is toggled on. If empty, no signal is sent.", alwaysUseInstanceValues: true)]
        public string Output
        {
            get { return output; }
            set
            {
                if (value == null || value == output) { return; }
                output = value;
                //reactivate if signal isn't empty (we may not have been previously sending a signal, but might now)
                if (value.IsNullOrEmpty()) { IsActive = true; }
            }
        }

        [InGameEditable, Serialize("0", IsPropertySaveable.Yes, description: "The signal sent when the controller is toggled off. If empty, no signal is sent. Only valid if IsToggle is true.", alwaysUseInstanceValues: true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set
            {
                if (value == null || value == falseOutput) { return; }
                falseOutput = value;
                //reactivate if signal isn't empty (we may not have been previously sending a signal, but might now)
                if (value.IsNullOrEmpty()) { IsActive = true; }
            }
        }

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

        [Editable, Serialize(false, IsPropertySaveable.No, description: "When enabled, the item will continuously send out a signal and interacting with it will flip the signal (making the item behave like a switch). When disabled, the item will simply send out a signal when interacted with.", alwaysUseInstanceValues: true)]
        public bool IsToggle
        {
            get;
            set;
        }

        [Editable, Serialize(false, IsPropertySaveable.No, description: "Whether the item is toggled on/off. Only valid if IsToggle is set to true.", alwaysUseInstanceValues: true)]
        public bool State
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.No, description: "Should the HUD (inventory, health bar, etc) be hidden when this item is selected.")]
        public bool HideHUD
        {
            get;
            set;
        }

        public enum UseEnvironment
        {
            Air, Water, Both
        };

        [Serialize(UseEnvironment.Both, IsPropertySaveable.No, description: "Can the item be selected in air, underwater or both.")]
        public UseEnvironment UsableIn { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "Should the character using the item be drawn behind the item.")]
        public bool DrawUserBehind
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.No, description: "Can another character select this controller when another character has already selected it?")]
        public bool AllowSelectingWhenSelectedByOther
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.No, description: "Can another character select this controller when a bot has already selected it?")]
        public bool AllowSelectingWhenSelectedByBot
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

        [Serialize(false, IsPropertySaveable.No)]
        public bool NonInteractableWhenFlippedX
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool NonInteractableWhenFlippedY
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "If true, other items can be used simultaneously.")]
        public bool IsSecondaryItem
        {
            get;
            private set;
        }

        public Controller(Item item, ContentXElement element)
            : base(item, element)
        {
            userPos = element.GetAttributeVector2("UserPos", Vector2.Zero);
            Enum.TryParse(element.GetAttributeString("direction", "None"), out dir);
            LoadLimbPositions(element);
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            this.cam = cam;
            UserInCorrectPosition = false;

            if (IsToggle)
            {
                string signal = State ? output : falseOutput;

                if (!string.IsNullOrEmpty(signal))
                {
                    item.SendSignal(signal, "signal_out");
                    item.SendSignal(signal, "trigger_out");
                }
            }

            if (user == null 
                || user.Removed
                || !user.IsAnySelectedItem(item)
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
                if (!IsToggle || item.Connections == null) { IsActive = false; }
                return;
            }

            user.AnimController.StartUsingItem();

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
                    // Secondary items (like ladders or chairs) will control the character position over primary items
                    // Only control the character position if the character doesn't have another secondary item already controlling it
                    if (!user.HasSelectedAnotherSecondaryItem(Item))
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
                        else if (Math.Abs(diff.X) > 10.0f)
                        {
                            user.AnimController.TargetMovement = Vector2.Normalize(diff);
                            user.AnimController.TargetDir = diff.X > 0.0f ? Direction.Right : Direction.Left;
                            return;
                        }
                        user.AnimController.TargetMovement = Vector2.Zero;
                    }
                    UserInCorrectPosition = true;
                }
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, user);

            if (limbPositions.Count == 0) { return; }

            user.AnimController.StartUsingItem();

            if (user.SelectedItem != null)
            {
                user.AnimController.ResetPullJoints(l => l.IsLowerBody);
            }
            else
            {
                user.AnimController.ResetPullJoints();
            }

            if (dir != 0) { user.AnimController.TargetDir = dir; }

            foreach (LimbPos lb in limbPositions)
            {
                Limb limb = user.AnimController.GetLimb(lb.LimbType);
                if (limb == null || !limb.body.Enabled) { continue; }
                // Don't move lower body limbs if there's another selected secondary item that should control them
                if (limb.IsLowerBody && user.HasSelectedAnotherSecondaryItem(Item)) { continue; }
                // Don't move hands if there's a selected primary item that should control them
                if (!limb.IsLowerBody && Item == user.SelectedSecondaryItem && user.SelectedItem != null) { continue; }
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
            if (user == null || user.Removed || !user.IsAnySelectedItem(item) || !user.CanInteractWith(item))
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
            else if (!string.IsNullOrEmpty(output))
            {
                item.SendSignal(new Signal(output, sender: user), "trigger_out");
            }

            lastUsed = Timing.TotalTime;
            ApplyStatusEffects(ActionType.OnUse, 1.0f, activator);          
            return true;
        }
                
        public override bool SecondaryUse(float deltaTime, Character character = null)
        {
            if (user != character)
            {
                return false;
            }
            if (user == null || character.Removed || !user.IsAnySelectedItem(item) || !character.CanInteractWith(item))
            {
                user = null;
                return false;
            }
            if (character == null)
            {
                return false;
            }

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
                if (focusTarget.GetComponent<Turret>() is { } turret)
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
            var positionOut = item.Connections?.Find(c => c.Name == "position_out");
            if (positionOut == null) { return null; }

            item.SendSignal(new Signal(MathHelper.ToDegrees(targetRotation).ToString("G", CultureInfo.InvariantCulture), sender: user), positionOut);

            for (int i = item.LastSentSignalRecipients.Count - 1; i >= 0; i--)
            {
                if (item.LastSentSignalRecipients[i].Item.Condition <= 0.0f || item.LastSentSignalRecipients[i].IsPower) { continue; }
                if (item.LastSentSignalRecipients[i].Item.Prefab.FocusOnSelected)
                {
                    return item.LastSentSignalRecipients[i].Item;
                }
            }

            foreach (var recipientPanel in item.GetConnectedComponentsRecursive<ConnectionPanel>(positionOut, allowTraversingBackwards: false))
            {
                if (recipientPanel.Item.Condition <= 0.0f) { continue; }
                if (recipientPanel.Item.Prefab.FocusOnSelected)
                {
                    return recipientPanel.Item;
                }
            }
                        
            return null;
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
            else if (!string.IsNullOrEmpty(output))
            {
                item.SendSignal(new Signal(output, sender: picker), "signal_out");
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
                humanoidAnim.LockFlipping(0.5f);
            }

            if (character.SelectedItem == item) { character.SelectedItem = null; }
            if (character.SelectedSecondaryItem == item) { character.SelectedSecondaryItem = null; }

            character.AnimController.StopUsingItem();
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
            if (Item.Condition <= 0.0f && !UpdateWhenInactive) { return false; }

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
                }
                else if (user.IsBot && !activator.IsBot)
                {
                    if (AllowSelectingWhenSelectedByBot)
                    {
                        CancelUsing(user);
                        user = activator;
                        IsActive = true;
                        return true;
                    }
                }
                return AllowSelectingWhenSelectedByOther;
            }
            else
            {
                user = activator;
                IsActive = true;
            }
#if SERVER
            item.CreateServerEvent(this);
#endif
            if (!string.IsNullOrEmpty(output))
            {
                item.SendSignal(new Signal(output, sender: user), "signal_out");
            }
            return true;
        }

        public override void FlipX(bool relativeToSub)
        {
            if (dir != Direction.None)
            {
                dir = dir == Direction.Left ? Direction.Right : Direction.Left;
            }
            userPos.X = -UserPos.X;
            FlipLimbPositions();
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

        public override XElement Save(XElement parentElement)
        {
            return SaveLimbPositions(base.Save(parentElement));
        }

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap)
        {
            base.Load(componentElement, usePrefabValues, idRemap);
            if (GameMain.GameSession?.GameMode?.Preset == GameModePreset.TestMode)
            {
                LoadLimbPositions(componentElement);
            }
        }

        private XElement SaveLimbPositions(XElement element)
        {
            if (Screen.Selected == GameMain.SubEditorScreen)
            {
                if (item.FlippedX)
                {
                    FlipLimbPositions();
                }
                // Don't save flipped positions.
                foreach (var limbPos in limbPositions)
                {
                    element.Add(new XElement("limbposition",
                        new XAttribute("limb", limbPos.LimbType),
                        new XAttribute("position", XMLExtensions.Vector2ToString(limbPos.Position)),
                        new XAttribute("allowusinglimb", limbPos.AllowUsingLimb)));
                }
                if (item.FlippedX)
                {
                    FlipLimbPositions();
                }
            }
            return element;
        }

        private void LoadLimbPositions(XElement element)
        {
            limbPositions.Clear();
            foreach (var subElement in element.Elements())
            {
                if (subElement.Name != "limbposition") { continue; }
                string limbStr = subElement.GetAttributeString("limb", "");
                if (!Enum.TryParse(subElement.GetAttribute("limb").Value, out LimbType limbType))
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
        }

        private void FlipLimbPositions()
        {
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

        public override void OnItemLoaded()
        {
            if (item.FlippedX && NonInteractableWhenFlippedX)
            {
                item.NonInteractable = true;
            }
            else if (item.FlippedY && NonInteractableWhenFlippedY)
            {
                item.NonInteractable = true;
            }
        }

        public override void Reset()
        {
            base.Reset();
            LoadLimbPositions(originalElement);
            if (item.FlippedX)
            {
                FlipLimbPositions();
            }
        }
    }
}
