﻿using FarseerPhysics;
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

        private string output;
        [ConditionallyEditable(ConditionallyEditable.ConditionType.HasConnectionPanel, onlyInEditors: false), 
            Serialize("1", IsPropertySaveable.Yes, description: "The signal sent when the controller is being activated or is toggled on. If empty, no signal is sent.", alwaysUseInstanceValues: true)]
        public string Output
        {
            get { return output; }
            set
            {
                if (value == null || value == output) { return; }
                output = value;
                //reactivate if signal isn't empty (we may not have been previously sending a signal, but might now)
                if (!value.IsNullOrEmpty()) { IsActive = true; }
            }
        }

        private string falseOutput;
        [ConditionallyEditable(ConditionallyEditable.ConditionType.IsToggleableController, onlyInEditors: false), 
            Serialize("0", IsPropertySaveable.Yes, description: "The signal sent when the controller is toggled off. If empty, no signal is sent. Only valid if IsToggle is true.", alwaysUseInstanceValues: true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set
            {
                if (value == null || value == falseOutput) { return; }
                falseOutput = value;
                //reactivate if signal isn't empty (we may not have been previously sending a signal, but might now)
                if (!value.IsNullOrEmpty()) { IsActive = true; }
            }
        }

        private bool state;
        [ConditionallyEditable(ConditionallyEditable.ConditionType.IsToggleableController, onlyInEditors: true), 
            Serialize(false, IsPropertySaveable.No, description: "Whether the item is toggled on/off. Only valid if IsToggle is set to true.", alwaysUseInstanceValues: true)]
        public bool State
        {
            get { return state; }
            set
            {
                if (state != value)
                {
                    state = value;
                    string newOutput = state ? output : falseOutput;
                    IsActive = !string.IsNullOrEmpty(newOutput);
                }
            }
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

        [Serialize(false, IsPropertySaveable.No, description: "Does the Controller require power to function (= to send signals and move the camera focus to a connected item)?")]
        public bool RequirePower
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

        [Serialize(false, IsPropertySaveable.No, description: "If enabled, the user sticks to the position of this item even if the item moves.")]
        public bool ForceUserToStayAttached
        {
            get;
            set;
        }

        public Controller(Item item, ContentXElement element)
            : base(item, element)
        {
            userPos = element.GetAttributeVector2("UserPos", Vector2.Zero);
            Enum.TryParse(element.GetAttributeString("direction", "None"), out dir);
            LoadLimbPositions(element);
            IsActive = true;
        }

        /// <summary>
        /// Hack for allowing characters to interact with a loader to get inside a boarding pod. 
        /// Doing that simply by autointeracting with the contained pod is difficult, because interacting with the loader selects it 
        /// _after_ the Select method of the pod is called by the autointeract logic, and the character only goes inside the pod if it's the selected item.
        /// </summary>
        private bool forceSelectNextFrame;

        private float userCanInteractCheckTimer;

        private const float UserCanInteractCheckInterval = 1.0f;

        public override void Update(float deltaTime, Camera cam) 
        {
            this.cam = cam;
            if (!ForceUserToStayAttached) { UserInCorrectPosition = false; }

            string signal = IsToggle && State ? output : falseOutput;
            if (item.Connections != null && IsToggle && !string.IsNullOrEmpty(signal) && !IsOutOfPower())
            {
                item.SendSignal(signal, "signal_out");
                item.SendSignal(signal, "trigger_out");
            }

            if (forceSelectNextFrame && user != null)
            {
                user.SelectedItem = item;
            }
            forceSelectNextFrame = false;

            userCanInteractCheckTimer -= deltaTime;

            if (user == null 
                || user.Removed
                || !user.IsAnySelectedItem(item)
                || (item.ParentInventory != null && !IsAttachedUser(user))
                || (UsableIn == UseEnvironment.Water && !user.AnimController.InWater)
                || (UsableIn == UseEnvironment.Air && user.AnimController.InWater)
                || !CheckUserCanInteract())
            {
                if (user != null)
                {
                    CancelUsing(user);
                    user = null;
                }
                if (item.Connections == null || !IsToggle || string.IsNullOrEmpty(signal)) { IsActive = false; }
                return;
            }

            if (ForceUserToStayAttached && Vector2.DistanceSquared(item.WorldPosition, user.WorldPosition) > 0.1f)
            {
                user.TeleportTo(item.WorldPosition);
                user.AnimController.Collider.ResetDynamics();
                foreach (var limb in user.AnimController.Limbs)
                {
                    if (limb.Removed || limb.IsSevered) { continue; }
                    limb.body?.ResetDynamics();
                }
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
                if (limb.IsArm && Item == user.SelectedSecondaryItem && user.SelectedItem != null) { continue; }
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

        private bool CheckUserCanInteract()
        {
            //optimization: CanInteractWith is relatively heavy (can involve visibility checks for example), let's not do it every frame
            if (user != null)
            {
                if (userCanInteractCheckTimer <= 0.0f)
                {
                    userCanInteractCheckTimer = UserCanInteractCheckInterval;
                    return user.CanInteractWith(item);
                }
            }
            //we only do the actual check every UserCanInteractCheckInterval seconds
            //can mean the component can stay selected for <1s after the user no longer has access to it
            return true;
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

            if (IsOutOfPower()) { return false; }

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

            if (IsOutOfPower()) { return false; }

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

        public bool IsOutOfPower()
        {
            if (!RequirePower) { return false; }
            var powered = item.GetComponent<Powered>();
            return powered == null || powered.Voltage < powered.MinVoltage;
        }

        public Item GetFocusTarget()
        {
            var positionOut = item.Connections?.Find(c => c.Name == "position_out");
            if (positionOut == null) { return null; }

            if (IsOutOfPower()) { return null; }

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
            if (IsOutOfPower()) { return false; }
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
                    return false;
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
                if (ForceUserToStayAttached && item.Container != null)
                {
                    forceSelectNextFrame = true;
                    return false;
                }
            }

            //allow the selection logic above to run when out of power, but allow sending signals
            if (IsOutOfPower()) { return false; }

#if SERVER
            item.CreateServerEvent(this);
#endif            
            if (!string.IsNullOrEmpty(output))
            {
                item.SendSignal(new Signal(output, sender: user), "signal_out");
            }
            return true;
        }

        /// <summary>
        /// "Attached user" sticks to this item. Can be used for things such as clown crates and boarding pods.
        /// </summary>
        public bool IsAttachedUser(Character character)
        {
            return character != null && character == user && ForceUserToStayAttached;
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

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap, bool isItemSwap)
        {
            base.Load(componentElement, usePrefabValues, idRemap, isItemSwap);
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

        private void LoadLimbPositions(ContentXElement element)
        {
            limbPositions.Clear();
            foreach (var subElement in element.Elements())
            {
                if (subElement.Name != "limbposition") { continue; }
                string limbStr = subElement.GetAttributeString("limb", "");
                if (!Enum.TryParse(subElement.GetAttribute("limb").Value, out LimbType limbType))
                {
                    DebugConsole.ThrowError($"Error in item \"{item.Name}\" - {limbStr} is not a valid limb type.",
                        contentPackage: element.ContentPackage);
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
