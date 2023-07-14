﻿using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using FarseerPhysics.Dynamics;
#if CLIENT
using Barotrauma.Lights;
#endif
using Barotrauma.Extensions;

namespace Barotrauma.Items.Components
{
    partial class Door : Pickable, IDrawableComponent, IServerSerializable
    {
        private static readonly HashSet<Door> doorList = new HashSet<Door>();

        public static IReadOnlyCollection<Door> DoorList { get { return doorList; } }

        private Gap linkedGap;
        private bool isOpen;

        private float openState;
        private readonly Sprite doorSprite, weldedSprite, brokenSprite;
        private readonly bool scaleBrokenSprite, fadeBrokenSprite;
        private readonly bool autoOrientGap;
        
        private bool isJammed;
        public bool IsJammed
        {
            get { return isJammed; }
            set
            {
                if (isJammed == value) { return; }
                isJammed = value;
#if SERVER
                item.CreateServerEvent(this);
#endif
            }
        }

        private bool isStuck;
        public bool IsStuck
        {
            get { return isStuck; }
            private set
            {
                if (isStuck == value) { return; }
                isStuck = value;
#if SERVER
                item.CreateServerEvent(this);
#endif
            }
        }

        //how much "less stuck" partially doors get when opened
        const float StuckReductionOnOpen = 30.0f;

        private float resetPredictionTimer;
        private float toggleCooldownTimer;
        private Character lastUser;

        private float damageSoundCooldown;

        private double lastBrokenTime;

        private Rectangle doorRect;

        private bool isBroken;

        public bool CanBeTraversed => !Impassable && (IsBroken || IsOpen);
        
        public bool IsBroken
        {
            get { return isBroken; }
            set
            {
                if (isBroken == value) { return; }
                isBroken = value;
                if (isBroken)
                {
                    DisableBody();
                }
                else
                {
                    EnableBody();
                }
#if SERVER
                item.CreateServerEvent(this);
#endif
            }
        }

        public PhysicsBody Body { get; private set; }

        //the fixture that's part of the submarine's collider (= fixture that things outside the sub can collide with if the door is outside hulls)
        public Fixture OutsideSubmarineFixture;

        private float RepairThreshold
        {
            get { return item.GetComponent<Repairable>() == null ? 0.0f : item.MaxCondition; }
        }

        public bool CanBeWelded = true;

        private float stuck;
        [Serialize(0.0f, IsPropertySaveable.No, description: "How badly stuck the door is (in percentages). If the percentage reaches 100, the door needs to be cut open to make it usable again.")]
        public float Stuck
        {
            get { return stuck; }
            set
            {
                if (isOpen || isBroken || !CanBeWelded) { return; }
                stuck = MathHelper.Clamp(value, 0.0f, 100.0f);
                //don't allow clients to make the door stuck unless the server says so (handled in ClientRead)
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
                if (stuck <= 0.0f) { IsStuck = false; }
                if (stuck >= 99.0f) { IsStuck = true; }
            }
        }

        [Serialize(3.0f, IsPropertySaveable.Yes, description: "How quickly the door opens."), Editable]
        public float OpeningSpeed { get; private set; }

        [Serialize(3.0f, IsPropertySaveable.Yes, description: "How quickly the door closes."), Editable]
        public float ClosingSpeed { get; private set; }

        [Serialize(1.0f, IsPropertySaveable.Yes, description: "The door cannot be opened/closed during this time after it has been opened/closed by another character."), Editable]
        public float ToggleCoolDown { get; private set; }

        public bool? PredictedState { get; private set; }

        public Gap LinkedGap
        {
            get
            {
                if (linkedGap == null)
                {
                    GetLinkedGap();
                }
                return linkedGap;
            }
        }

        private void GetLinkedGap()
        {
            linkedGap = item.linkedTo.FirstOrDefault(e => e is Gap) as Gap;
            if (linkedGap == null)
            {
                Rectangle rect = item.Rect;
                linkedGap = new Gap(rect, !IsHorizontal, Item.Submarine)
                {
                    Submarine = item.Submarine
                };
                item.linkedTo.Add(linkedGap);
            }
            RefreshLinkedGap();
        }

        public bool IsHorizontal { get; private set; }

        [Serialize("0.0,0.0,0.0,0.0", IsPropertySaveable.No, description: "Position and size of the window on the door. The upper left corner is 0,0. Set the width and height to 0 if you don't want the door to have a window.")]
        public Rectangle Window { get; set; }

        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "Is the door currently open.")]
        public bool IsOpen
        {
            get { return isOpen; }
            set 
            {
                isOpen = value;
                OpenState = isOpen ? 1.0f : 0.0f;
            }
        }
        public bool IsClosed => !IsOpen;

        public bool IsFullyOpen => IsOpen && OpenState >= 1.0f;

        public bool IsFullyClosed => IsClosed && OpenState <= 0f;

        [Serialize(false, IsPropertySaveable.No, description: "If the door has integrated buttons, it can be opened by interacting with it directly (instead of using buttons wired to it).")]
        public bool HasIntegratedButtons { get; private set; }
                
        public float OpenState
        {
            get { return openState; }
            set 
            {
                openState = MathHelper.Clamp(value, 0.0f, 1.0f);
#if CLIENT
                float size = IsHorizontal ? item.Rect.Width : item.Rect.Height;
                //refresh convex hulls if the body of the door has moved by 5 pixels,
                //or if it becomes fully closed or fully open
                if (Math.Abs(lastConvexHullState - openState) * size > 5.0f ||
                    (openState <= 0.0f && lastConvexHullState > 0.0f) ||
                    (openState >= 1.0f && lastConvexHullState < 1.0f)) 
                {
                    UpdateConvexHulls();
                    lastConvexHullState = openState; 
                }
#endif
            }
        }

        [Serialize(false, IsPropertySaveable.No, description: "Characters and items cannot pass through impassable doors. Useful for things such as ducts that should only let water and air through.")]
        public bool Impassable
        {
            get;
            set;
        }

        [Editable, Serialize(true, IsPropertySaveable.Yes, description: "", alwaysUseInstanceValues: true)]
        public bool UseBetweenOutpostModules { get; private set; }

        [Editable, Serialize(false, IsPropertySaveable.No, description: "If true, bots won't try to close this door behind them.", alwaysUseInstanceValues: true)]
        public bool BotsShouldKeepOpen { get; private set; }

        public Door(Item item, ContentXElement element)
            : base(item, element)
        {
            IsHorizontal = element.GetAttributeBool("horizontal", false);
            canBePicked = element.GetAttributeBool("canbepicked", false);
            autoOrientGap = element.GetAttributeBool("autoorientgap", false);

            allowedSlots.Clear();
            
            foreach (var subElement in element.Elements())
            {
                string textureDir = GetTextureDirectory(subElement);
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        doorSprite = new Sprite(subElement, path: textureDir);
                        break;
                    case "weldedsprite":
                        weldedSprite = new Sprite(subElement, path: textureDir);
                        break;
                    case "brokensprite":
                        brokenSprite = new Sprite(subElement, path: textureDir);
                        scaleBrokenSprite = subElement.GetAttributeBool("scale", false);
                        fadeBrokenSprite = subElement.GetAttributeBool("fade", false);
                        break;
                }
            }
                        
            IsActive = true;
            doorList.Add(this);
        }

        public override void OnItemLoaded()
        {
            //do this here because the scale of the item might not be set to the final value yet in the constructor
            doorRect = new Rectangle(
                item.Rect.Center.X - (int)(doorSprite.size.X / 2 * item.Scale),
                item.Rect.Y - item.Rect.Height / 2 + (int)(doorSprite.size.Y / 2.0f * item.Scale),
                (int)(doorSprite.size.X * item.Scale),
                (int)(doorSprite.size.Y * item.Scale));

            Body = new PhysicsBody(
                ConvertUnits.ToSimUnits(Math.Max(doorRect.Width, 1)),
                ConvertUnits.ToSimUnits(Math.Max(doorRect.Height, 1)),
                radius: 0.0f,
                density: 1.5f,
                BodyType.Static,
                Physics.CollisionWall,
                Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionCharacter | Physics.CollisionItemBlocking | Physics.CollisionProjectile,
                findNewContacts: false)
            {
                UserData = item,
                Friction = 0.5f
            };
            Body.SetTransformIgnoreContacts(
                ConvertUnits.ToSimUnits(new Vector2(doorRect.Center.X, doorRect.Y - doorRect.Height / 2)),
                0.0f);
            if (isBroken)
            {
                DisableBody();
            }
        }

        public override void Move(Vector2 amount, bool ignoreContacts = false)
        {
            if (ignoreContacts)
            {
                Body?.SetTransformIgnoreContacts(Body.SimPosition + ConvertUnits.ToSimUnits(amount), 0.0f);
            }
            else
            {
                Body?.SetTransform(Body.SimPosition + ConvertUnits.ToSimUnits(amount), 0.0f);
            }
#if CLIENT
            UpdateConvexHulls();
#endif
        }

        private readonly LocalizedString accessDeniedTxt = TextManager.Get("AccessDenied");
        private readonly LocalizedString cannotOpenText = TextManager.Get("DoorMsgCannotOpen");
        public override bool HasRequiredItems(Character character, bool addMessage, LocalizedString msg = null)
        {
            Msg = HasAccess(character) ? "ItemMsgOpen" : "ItemMsgForceOpenCrowbar";
            ParseMsg();
            if (addMessage)
            {
                msg = msg ?? (HasIntegratedButtons ? accessDeniedTxt : cannotOpenText).Value;
            }
            return isBroken || base.HasRequiredItems(character, addMessage, msg);
        }

        public override bool Pick(Character picker)
        {
            if (item.Condition < RepairThreshold && item.GetComponent<Repairable>().HasRequiredItems(picker, addMessage: false)) { return true; }
            if (requiredItems.None()) { return false; }
            if (HasAccess(picker) && HasRequiredItems(picker, false)) { return false; }
            return base.Pick(picker);
        }

        public override bool OnPicked(Character picker)
        {
            if (item.Condition < RepairThreshold && item.GetComponent<Repairable>().HasRequiredItems(picker, addMessage: false)) { return true; }
            if (!HasAccess(picker))
            {
                ToggleState(ActionType.OnPicked, picker);
            }
            return false;
        }

        private void ToggleState(ActionType actionType, Character user)
        {
            if (toggleCooldownTimer > 0.0f && user != lastUser)
            {
                OnFailedToOpen();
                return;
            }
            toggleCooldownTimer = ToggleCoolDown;
            if (IsStuck || IsJammed)
            {
#if CLIENT
                if (IsStuck) { HintManager.OnTryOpenStuckDoor(user); }
#endif
                toggleCooldownTimer = 1.0f;
                OnFailedToOpen();
                return;
            }
            lastUser = user;
            SetState(PredictedState == null ? !isOpen : !PredictedState.Value, false, true, forcedOpen: actionType == ActionType.OnPicked);
        }

        public override bool Select(Character character)
        {
            if (isBroken) { return true; }
            bool hasRequiredItems = HasRequiredItems(character, false);
            if (HasAccess(character))
            {
                float originalPickingTime = PickingTime;
                PickingTime = 0;
                ToggleState(ActionType.OnUse, character);
                PickingTime = originalPickingTime;
                StopPicking(picker);
                return true;
            }
#if CLIENT
            else if (hasRequiredItems && character != null && character == Character.Controlled)
            {
                GUI.AddMessage(accessDeniedTxt, GUIStyle.Red);
            }
#endif
            return false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateProjSpecific(deltaTime);
            toggleCooldownTimer -= deltaTime;
            damageSoundCooldown -= deltaTime;

            if (isBroken)
            {
                lastBrokenTime = Timing.TotalTime;
                //the door has to be restored to 50% health before collision detection on the body is re-enabled

                //multiply by MaxRepairConditionMultiplier so the item gets repaired at 50% of the _default max condition_
                //otherwise increasing the max condition is arguably harmful, as the door needs to be repaired further to re-enable the collider
                if (item.ConditionPercentage * Math.Max(item.MaxRepairConditionMultiplier, 1.0f) > 50.0f && 
                    (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer))
                {
                    IsBroken = false;
                }
                return;
            }



            bool isClosing = false;
            if ((!IsStuck && !IsJammed) || !isOpen)
            {
                if (PredictedState == null)
                {
                    OpenState += deltaTime * (isOpen ? OpeningSpeed : -ClosingSpeed);
                    isClosing = openState > 0.0f && openState < 1.0f && !isOpen;
                }
                else
                {
                    OpenState += deltaTime * ((bool)PredictedState ? OpeningSpeed : -ClosingSpeed);
                    isClosing = openState > 0.0f && openState < 1.0f && !(bool)PredictedState;

                    resetPredictionTimer -= deltaTime;
                    if (resetPredictionTimer <= 0.0f)
                    {
                        PredictedState = null;
                    }
                }
                LinkedGap.Open = isBroken ? 1.0f : openState;
            }
            
            if (isClosing)
            {
                if (OpenState < 0.9f) { PushCharactersAway(); }
                if (CheckSubmarinesInDoorWay())
                {
                    PredictedState = null;
                    isOpen = true;
                }
            }
            else
            {
                bool wasEnabled = Body.Enabled;
                Body.Enabled = Impassable || openState < 1.0f;
                if (OutsideSubmarineFixture != null)
                {
                    OutsideSubmarineFixture.CollidesWith = Body.Enabled ? SubmarineBody.CollidesWith : Category.None;
                }
                if (wasEnabled && !Body.Enabled && IsHorizontal)
                {
                    //when opening a hatch, force characters above it to refresh the floor position
                    //(otherwise the character won't fall through the hatch until it moves)
                    foreach (Character c in Character.CharacterList)
                    {
                        if (c.WorldPosition.Y < item.WorldPosition.Y) { continue; }
                        if (c.WorldPosition.X < item.WorldRect.X || c.WorldPosition.X > item.WorldRect.Right) { continue; }
                        c.AnimController?.ForceRefreshFloorY();
                    }
                }
            }

            //don't use the predicted state here, because it might set
            //other items to an incorrect state if the prediction is wrong
            item.SendSignal(isOpen ? "1" : "0", "state_out");
        }

        partial void UpdateProjSpecific(float deltaTime);


        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);
            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                IsBroken = true;
            }
        }

        private void EnableBody()
        {
            if (!Impassable)
            {
                Body.FarseerBody.SetIsSensor(false);
                var ce = Body.FarseerBody.ContactList;
                while (ce != null && ce.Contact != null)
                {
                    ce.Contact.Enabled = false;
                    ce = ce.Next;
                }
                PushCharactersAway();
            }
            if (OutsideSubmarineFixture != null && Body.Enabled)
            {
                OutsideSubmarineFixture.CollidesWith = SubmarineBody.CollidesWith;
            }
#if CLIENT
            UpdateConvexHulls();
#endif
            isBroken = false;
        }

        private void DisableBody()
        {
            //change the body to a sensor instead of disabling it completely, 
            //because otherwise repairtool raycasts won't hit it
            if (!Impassable)
            {
                Body.FarseerBody.SetIsSensor(true);
                var ce = Body.FarseerBody.ContactList;
                while (ce != null && ce.Contact != null)
                {
                    ce.Contact.Enabled = false;
                    ce = ce.Next;
                }
            }

            if (OutsideSubmarineFixture != null)
            {
                OutsideSubmarineFixture.CollidesWith = Category.None;
            }
            if (linkedGap != null)
            {
                linkedGap.Open = 1.0f;
            }

            IsOpen = false;
#if CLIENT
            if (convexHull != null) { convexHull.Enabled = false; }
            if (convexHull2 != null) { convexHull2.Enabled = false; }
#endif
        }

        public void RefreshLinkedGap()
        {
            LinkedGap.ConnectedDoor = this;
            if (autoOrientGap)
            {
                LinkedGap.AutoOrient();
            }
            LinkedGap.Open = isBroken ? 1.0f : openState;
            LinkedGap.PassAmbientLight = Window != Rectangle.Empty;
        }

        public override void OnMapLoaded()
        {
            RefreshLinkedGap();
#if CLIENT
            convexHull = new ConvexHull(doorRect, IsHorizontal, item);
            if (Window != Rectangle.Empty)
            {
                convexHull2 = new ConvexHull(doorRect, IsHorizontal, item);
            }
            UpdateConvexHulls();
#endif
        }

        public override void OnScaleChanged()
        {
#if CLIENT
            UpdateConvexHulls();
#endif
            if (linkedGap != null)
            {
                RefreshLinkedGap();
                linkedGap.Rect = item.Rect;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            if (Body != null)
            {
                Body.Remove();
                Body = null;
            }

            foreach (Gap gap in Gap.GapList)
            {
                if (gap.ConnectedDoor == this)
                {
                    gap.ConnectedDoor = null;
                }
            }

            if (OutsideSubmarineFixture != null)
            {
                OutsideSubmarineFixture.Body.Remove(OutsideSubmarineFixture);
                OutsideSubmarineFixture = null;
            }
            
            //no need to remove the gap if we're unloading the whole submarine
            //otherwise the gap will be removed twice and cause console warnings
            if (!Submarine.Unloading)
            {
                linkedGap?.Remove();
            }
            doorSprite?.Remove();
            weldedSprite?.Remove();

#if CLIENT
            convexHull?.Remove();
            convexHull2?.Remove();
#endif

            doorList.Remove(this);
        }

        private bool CheckSubmarinesInDoorWay()
        {
            if (linkedGap != null && linkedGap.IsRoomToRoom) { return false; }

            Rectangle doorRect = item.WorldRect;
            if (IsHorizontal)
            {
                doorRect.Width = (int)(item.Rect.Width * (1.0f - openState));
            }
            else
            {
                doorRect.Height = (int)(item.Rect.Height * (1.0f - openState));
            }

            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub == item.Submarine || sub.DockedTo.Contains(item.Submarine)) { continue; }
                Rectangle worldBorders = sub.Borders;
                worldBorders.Location += sub.WorldPosition.ToPoint();
                if (!Submarine.RectsOverlap(worldBorders, doorRect)) { continue; }                
                foreach (Hull hull in sub.GetHulls(alsoFromConnectedSubs: false))
                {
                    if (Submarine.RectsOverlap(hull.WorldRect, doorRect)) { return true; }
                }
            }
            return false;
        }

        bool itemPosErrorShown;
        private readonly HashSet<Character> characterPosErrorShown = new HashSet<Character>();
        private void PushCharactersAway()
        {
            if (!MathUtils.IsValid(item.SimPosition))
            {
                if (!itemPosErrorShown)
                {
                    DebugConsole.ThrowError("Failed to push a character out of a doorway - position of the door is not valid (" + item.SimPosition + ")");
                    GameAnalyticsManager.AddErrorEventOnce("PushCharactersAway:DoorPosInvalid", GameAnalyticsManager.ErrorSeverity.Error,
                          "Failed to push a character out of a doorway - position of the door is not valid (" + item.SimPosition + ").");
                    itemPosErrorShown = true;
                }
                return;
            }
            
            Vector2 simPos = ConvertUnits.ToSimUnits(new Vector2(item.Rect.X, item.Rect.Y));

            Vector2 currSize = IsHorizontal ?
                new Vector2(item.Rect.Width * (1.0f - openState), doorSprite.size.Y * item.Scale) :
                new Vector2(doorSprite.size.X * item.Scale, item.Rect.Height * (1.0f - openState));
            Vector2 simSize = ConvertUnits.ToSimUnits(currSize);

            foreach (Character c in Character.CharacterList)
            {
                if (!c.Enabled) { continue; }
                if (!MathUtils.IsValid(c.SimPosition))
                {
                    if (!characterPosErrorShown.Contains(c))
                    {
                        if (GameSettings.CurrentConfig.VerboseLogging) { DebugConsole.ThrowError("Failed to push a character out of a doorway - position of the character \"" + c.Name + "\" is not valid (" + c.SimPosition + ")"); }
                        GameAnalyticsManager.AddErrorEventOnce("PushCharactersAway:CharacterPosInvalid", GameAnalyticsManager.ErrorSeverity.Error,
                            "Failed to push a character out of a doorway - position of the character \"" + c.SpeciesName + "\" is not valid (" + c.SimPosition + ")." +
                            " Removed: " + c.Removed +
                            " Remoteplayer: " + c.IsRemotePlayer);
                        characterPosErrorShown.Add(c);
                    }
                    continue;
                }
                int dir = IsHorizontal ? Math.Sign(c.SimPosition.Y - item.SimPosition.Y) : Math.Sign(c.SimPosition.X - item.SimPosition.X);

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (limb.IsSevered) { continue; }
                    if (PushBodyOutOfDoorway(c, limb.body, dir, simPos, simSize) && damageSoundCooldown <= 0.0f)
                    {
#if CLIENT
                        SoundPlayer.PlayDamageSound("LimbBlunt", 1.0f, limb.body);
#endif
                        damageSoundCooldown = 0.5f;
                    }
                }
                PushBodyOutOfDoorway(c, c.AnimController.Collider, dir, simPos, simSize);
            }
        }

        private bool PushBodyOutOfDoorway(Character c, PhysicsBody body, int dir, Vector2 doorRectSimPos, Vector2 doorRectSimSize)
        {
            if (!MathUtils.IsValid(body.SimPosition))
            {
                DebugConsole.ThrowError("Failed to push a limb out of a doorway - position of the body (character \"" + c.Name + "\") is not valid (" + body.SimPosition + ")");
                GameAnalyticsManager.AddErrorEventOnce("PushCharactersAway:LimbPosInvalid", GameAnalyticsManager.ErrorSeverity.Error,
                    "Failed to push a character out of a doorway - position of the character \"" + c.SpeciesName + "\" is not valid (" + body.SimPosition + ")." +
                    " Removed: " + c.Removed +
                    " Remoteplayer: " + c.IsRemotePlayer);
                return false;
            }

            float diff;
            if (IsHorizontal)
            {
                if (body.SimPosition.X < doorRectSimPos.X || body.SimPosition.X > doorRectSimPos.X + doorRectSimSize.X) { return false; }
                diff = body.SimPosition.Y - item.SimPosition.Y;
            }
            else
            {
                if (body.SimPosition.Y > doorRectSimPos.Y || body.SimPosition.Y < doorRectSimPos.Y - doorRectSimSize.Y) { return false; }
                diff = body.SimPosition.X - item.SimPosition.X;
            }

            //if the limb is at a different side of the door than the character (collider), 
            //immediately teleport it to the correct side
            if (Math.Sign(diff) != dir)
            {
                if (IsHorizontal)
                {
                    body.SetTransform(new Vector2(body.SimPosition.X, item.SimPosition.Y + dir * doorRectSimSize.Y * 2.0f), body.Rotation);
                }
                else
                {
                    body.SetTransform(new Vector2(item.SimPosition.X + dir * doorRectSimSize.X * 1.2f, body.SimPosition.Y), body.Rotation);
                }
            }

            //apply an impulse to push the limb further from the door
            if (IsHorizontal)
            {
                if (Math.Abs(body.SimPosition.Y - item.SimPosition.Y) > doorRectSimSize.Y * 0.5f) { return false; }
                body.ApplyLinearImpulse(new Vector2(isOpen ? 0.0f : 1.0f, dir * 2.0f), maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
            }
            else
            {
                if (Math.Abs(body.SimPosition.X - item.SimPosition.X) > doorRectSimSize.X * 0.5f) { return false; }
                body.ApplyLinearImpulse(new Vector2(dir * 2.0f, isOpen ? 0.0f : -1.0f), maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
            }

            //don't stun if the door was broken a moment ago
            //otherwise enabling the door's collider and pushing the character away will interrupt repairing
            if (lastBrokenTime < Timing.TotalTime - 1.0f)
            {
                c.SetStun(0.2f);
            }
            return true;
        }

        partial void OnFailedToOpen();

        public override bool HasAccess(Character character)
        {
            if (!item.IsInteractable(character)) { return false; }
            if (HasIntegratedButtons)
            {
                return base.HasAccess(character);
            }
            else
            {
                return base.HasAccess(character) && Item.GetConnectedComponents<Controller>(true).Any(b => b.HasAccess(character));
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            if (IsStuck || IsJammed) { return; }

            bool wasOpen = PredictedState == null ? isOpen : PredictedState.Value;
            
            if (connection.Name == "toggle")
            {
                if (signal.value == "0") { return; }
                if (toggleCooldownTimer > 0.0f && signal.sender != lastUser) { OnFailedToOpen(); return; }
                if (IsStuck) { toggleCooldownTimer = 1.0f; OnFailedToOpen(); return; }
                toggleCooldownTimer = ToggleCoolDown;
                lastUser = signal.sender;
                SetState(!wasOpen, false, true, forcedOpen: false);
            }
            else if (connection.Name == "set_state")
            {
                bool signalOpen = signal.value != "0";
                if (IsStuck && signalOpen != wasOpen) { toggleCooldownTimer = 1.0f; OnFailedToOpen(); return; }
                SetState(signalOpen, false, true, forcedOpen: false);
            }

#if SERVER
            if (signal.sender != null && wasOpen != isOpen)
            {
                GameServer.Log(GameServer.CharacterLogName(signal.sender) + (isOpen ? " opened " : " closed ") + item.Name, ServerLog.MessageType.ItemInteraction);
            }
#endif
        }

        public void TrySetState(bool open, bool isNetworkMessage, bool sendNetworkMessage = false)
        {
            SetState(open, isNetworkMessage, sendNetworkMessage, forcedOpen: false);
        }

        partial void SetState(bool open, bool isNetworkMessage, bool sendNetworkMessage, bool forcedOpen);
    }
}
