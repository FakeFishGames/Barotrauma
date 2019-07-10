using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
#if CLIENT
using Barotrauma.Lights;
#endif
using Barotrauma.Extensions;

namespace Barotrauma.Items.Components
{
    partial class Door : Pickable, IDrawableComponent, IServerSerializable
    {
        private Gap linkedGap;
        private bool isOpen;

        private float openState;
        private Sprite doorSprite, weldedSprite, brokenSprite;
        private bool scaleBrokenSprite, fadeBrokenSprite;
        private bool autoOrientGap;

        private bool isStuck;
        public bool IsStuck => isStuck;

        private float resetPredictionTimer;

        private Rectangle doorRect;

        private bool isBroken;
        
        public bool IsBroken
        {
            get { return isBroken; }
            set
            {
                if (isBroken == value) return;
                isBroken = value;
                if (isBroken)
                {
                    DisableBody();
                }
                else
                {
                    EnableBody();
                }
            }
        }

        public PhysicsBody Body { get; private set; }

        private float RepairThreshold
        {
            get { return item.GetComponent<Repairable>()?.ShowRepairUIThreshold ?? 0.0f; }
        }

        public bool CanBeWelded = true;

        private float stuck;
        [Serialize(0.0f, false)]
        public float Stuck
        {
            get { return stuck; }
            set 
            {
                if (isOpen || isBroken || !CanBeWelded) return;
                stuck = MathHelper.Clamp(value, 0.0f, 100.0f);
                if (stuck <= 0.0f) isStuck = false;
                if (stuck >= 100.0f) isStuck = true;
            }
        }

        [Serialize(3.0f, true), Editable]
        public float OpeningSpeed { get; private set; }

        [Serialize(3.0f, true), Editable]
        public float ClosingSpeed { get; private set; }

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
                if (IsHorizontal)
                {
                    rect.Y += 5;
                    rect.Height += 10;
                }
                else
                {
                    rect.X -= 5;
                    rect.Width += 10;
                }
                linkedGap = new Gap(rect, !IsHorizontal, Item.Submarine)
                {
                    Submarine = item.Submarine
                };
                item.linkedTo.Add(linkedGap);
            }
            RefreshLinkedGap();
        }

        public bool IsHorizontal { get; private set; }

        [Serialize("0.0,0.0,0.0,0.0", false)]
        public Rectangle Window { get; set; }

        [Editable, Serialize(false, true)]
        public bool IsOpen
        {
            get { return isOpen; }
            set 
            {
                isOpen = value;
                OpenState = (isOpen) ? 1.0f : 0.0f;
            }
        }

        [Serialize(false, false)]
        public bool HasIntegratedButtons { get; private set; }
                
        public float OpenState
        {
            get { return openState; }
            set 
            {                
                openState = MathHelper.Clamp(value, 0.0f, 1.0f);
#if CLIENT
                float size = IsHorizontal ? item.Rect.Width : item.Rect.Height;
                if (Math.Abs(lastConvexHullState - openState) * size < 5.0f) { return; }
                UpdateConvexHulls();
                lastConvexHullState = openState;
#endif
            }
        }

        [Serialize(false, false)]
        public bool Impassable
        {
            get;
            set;
        }
        
        public Door(Item item, XElement element)
            : base(item, element)
        {
            IsHorizontal = element.GetAttributeBool("horizontal", false);
            canBePicked = element.GetAttributeBool("canbepicked", false);
            autoOrientGap = element.GetAttributeBool("autoorientgap", false);
            
            foreach (XElement subElement in element.Elements())
            {
                string texturePath = subElement.GetAttributeString("texture", "");
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        doorSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                    case "weldedsprite":
                        weldedSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                    case "brokensprite":
                        brokenSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        scaleBrokenSprite = subElement.GetAttributeBool("scale", false);
                        fadeBrokenSprite = subElement.GetAttributeBool("fade", false);
                        break;
                }
            }

            doorRect = new Rectangle(
                item.Rect.Center.X - (int)(doorSprite.size.X / 2 * item.Scale),
                item.Rect.Y - item.Rect.Height/2 + (int)(doorSprite.size.Y / 2.0f * item.Scale),
                (int)(doorSprite.size.X * item.Scale),
                (int)(doorSprite.size.Y * item.Scale));

            Body = new PhysicsBody(
                ConvertUnits.ToSimUnits(Math.Max(doorRect.Width, 1)),
                ConvertUnits.ToSimUnits(Math.Max(doorRect.Height, 1)),
                0.0f,
                1.5f)
            {
                UserData = item,
                CollisionCategories = Physics.CollisionWall,
                BodyType = BodyType.Static,
                Friction = 0.5f
            };
            Body.SetTransform(
                ConvertUnits.ToSimUnits(new Vector2(doorRect.Center.X, doorRect.Y - doorRect.Height / 2)),
                0.0f);
            
            IsActive = true;
        }

        public override void Move(Vector2 amount)
        {
            base.Move(amount);
            
            Body?.SetTransform(Body.SimPosition + ConvertUnits.ToSimUnits(amount), 0.0f);

#if CLIENT
            UpdateConvexHulls();
#endif
        }

        private string accessDeniedTxt = TextManager.Get("AccessDenied");
        private string cannotOpenText = TextManager.Get("DoorMsgCannotOpen");
        private bool hasValidIdCard;
        public override bool HasRequiredItems(Character character, bool addMessage, string msg = null)
        {
            var idCard = character.Inventory.FindItemByIdentifier("idcard");
            hasValidIdCard = requiredItems.Any(ri => ri.Value.Any(r => r.MatchesItem(idCard)));
            Msg = requiredItems.None() || hasValidIdCard ? "ItemMsgOpen" : "ItemMsgForceOpenCrowbar";
            ParseMsg();
            if (addMessage)
            {
                msg = msg ?? (HasIntegratedButtons ? accessDeniedTxt : cannotOpenText);
            }
            return isBroken || base.HasRequiredItems(character, addMessage, msg);
        }

        public override bool Pick(Character picker)
        {
            if (item.Condition <= RepairThreshold) { return true; }
            if (requiredItems.None()) { return false; }
            if (HasRequiredItems(picker, false) && hasValidIdCard) { return false; }
            return base.Pick(picker);
        }

        public override bool OnPicked(Character picker)
        {
            if (item.Condition <= RepairThreshold) { return true; }
            if (requiredItems.Any() && !hasValidIdCard)
            {
                ToggleState(ActionType.OnPicked);
            }
            return false;
        }

        private void ToggleState(ActionType actionType)
        {
            SetState(PredictedState == null ? !isOpen : !PredictedState.Value, false, true, forcedOpen: actionType == ActionType.OnPicked);
        }

        public override bool Select(Character character)
        {
            if (!isBroken)
            {
                bool hasRequiredItems = HasRequiredItems(character, false);
                if (requiredItems.None() || hasRequiredItems && hasValidIdCard)
                {
                    float originalPickingTime = PickingTime;
                    PickingTime = 0;
                    ToggleState(ActionType.OnUse);
                    PickingTime = originalPickingTime;
                }
                else if (hasRequiredItems)
                {
#if CLIENT
                    GUI.AddMessage(accessDeniedTxt, Color.Red);
#endif
                }
            }
            return item.Condition <= RepairThreshold;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (isBroken)
            {
                //the door has to be restored to 50% health before collision detection on the body is re-enabled
                if (item.ConditionPercentage > 50.0f)
                {
                    IsBroken = false;
                }
                return;
            }

            bool isClosing = false;
            if (!isStuck)
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

                LinkedGap.Open = openState;
            }
            
            if (isClosing)
            {
                if (OpenState < 0.9f) { PushCharactersAway(); }
            }
            else
            {
                Body.Enabled = Impassable || openState < 1.0f;                
            }

            //don't use the predicted state here, because it might set
            //other items to an incorrect state if the prediction is wrong
            item.SendSignal(0, (isOpen) ? "1" : "0", "state_out", null);
        }
        
        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            IsBroken = true;
        }

        private void EnableBody()
        {
            if (!Impassable)
            {
                Body.FarseerBody.IsSensor = false;
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
                Body.FarseerBody.IsSensor = true;
            }
            linkedGap.Open = 1.0f;
            IsOpen = false;
#if CLIENT
            if (convexHull != null) convexHull.Enabled = false;
            if (convexHull2 != null) convexHull2.Enabled = false;
#endif
        }

        public void RefreshLinkedGap()
        {
            LinkedGap.ConnectedDoor = this;
            if (autoOrientGap)
            {
                LinkedGap.AutoOrient();
            }
            LinkedGap.Open = openState;
            LinkedGap.PassAmbientLight = Window != Rectangle.Empty;
        }

        public override void OnMapLoaded()
        {
            RefreshLinkedGap();
#if CLIENT
            Vector2[] corners = GetConvexHullCorners(Rectangle.Empty);

            convexHull = new ConvexHull(corners, Color.Black, item);
            if (Window != Rectangle.Empty) convexHull2 = new ConvexHull(corners, Color.Black, item);

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
            
            //no need to remove the gap if we're unloading the whole submarine
            //otherwise the gap will be removed twice and cause console warnings
            if (!Submarine.Unloading)
            {
                if (linkedGap != null) linkedGap.Remove();
            }

            doorSprite.Remove();
            if (weldedSprite != null) weldedSprite.Remove();

#if CLIENT
            if (convexHull != null) convexHull.Remove();
            if (convexHull2 != null) convexHull2.Remove();
#endif
        }

        private void PushCharactersAway()
        {
            if (!MathUtils.IsValid(item.SimPosition))
            {
                DebugConsole.ThrowError("Failed to push a character out of a doorway - position of the door is not valid (" + item.SimPosition + ")");
                GameAnalyticsManager.AddErrorEventOnce("PushCharactersAway:DoorPosInvalid", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                      "Failed to push a character out of a doorway - position of the door is not valid (" + item.SimPosition + ").");
                return;
            }
            
            Vector2 simPos = ConvertUnits.ToSimUnits(new Vector2(item.Rect.X, item.Rect.Y));

            Vector2 currSize = IsHorizontal ?
                new Vector2(item.Rect.Width * (1.0f - openState), doorSprite.size.Y * item.Scale) :
                new Vector2(doorSprite.size.X * item.Scale, item.Rect.Height * (1.0f - openState));

            Vector2 simSize = ConvertUnits.ToSimUnits(currSize);

            foreach (Character c in Character.CharacterList)
            {
                if (!c.Enabled) continue;
                if (!MathUtils.IsValid(c.SimPosition))
                {
                    if (GameSettings.VerboseLogging) { DebugConsole.ThrowError("Failed to push a character out of a doorway - position of the character \"" + c.Name + "\" is not valid (" + c.SimPosition + ")"); }
                    GameAnalyticsManager.AddErrorEventOnce("PushCharactersAway:CharacterPosInvalid", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Failed to push a character out of a doorway - position of the character \"" + c.Name + "\" is not valid (" + c.SimPosition + ")." +
                        " Removed: " + c.Removed +
                        " Remoteplayer: " + c.IsRemotePlayer);
                    continue;
                }
                int dir = IsHorizontal ? Math.Sign(c.SimPosition.Y - item.SimPosition.Y) : Math.Sign(c.SimPosition.X - item.SimPosition.X);

                bool soundPlayed = false;
                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (PushBodyOutOfDoorway(c, limb.body, dir, simPos, simSize) && !soundPlayed)
                    {
#if CLIENT
                        SoundPlayer.PlayDamageSound("LimbBlunt", 1.0f, limb.body);
#endif
                        soundPlayed = true;
                    }
                }
                PushBodyOutOfDoorway(c, c.AnimController.Collider, dir, simPos, simSize);
            }
        }

        private bool PushBodyOutOfDoorway(Character c, PhysicsBody body, int dir, Vector2 doorRectSimPos, Vector2 doorRectSimSize)
        {
            float diff = 0.0f;
            if (!MathUtils.IsValid(body.SimPosition))
            {
                DebugConsole.ThrowError("Failed to push a limb out of a doorway - position of the body (character \"" + c.Name + "\") is not valid (" + body.SimPosition + ")");
                GameAnalyticsManager.AddErrorEventOnce("PushCharactersAway:LimbPosInvalid", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Failed to push a character out of a doorway - position of the character \"" + c.Name + "\" is not valid (" + body.SimPosition + ")." +
                    " Removed: " + c.Removed +
                    " Remoteplayer: " + c.IsRemotePlayer);
                return false;
            }
            
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

            c.SetStun(0.2f);
            return true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            if (isStuck) return;

            bool wasOpen = PredictedState == null ? isOpen : PredictedState.Value;

            if (connection.Name == "toggle")
            {
                SetState(!wasOpen, false, true, forcedOpen: false);
            }
            else if (connection.Name == "set_state")
            {
                SetState(signal != "0", false, true, forcedOpen: false);
            }

#if SERVER
            if (sender != null && wasOpen != isOpen)
            {
                GameServer.Log(sender.LogName + (isOpen ? " opened " : " closed ") + item.Name, ServerLog.MessageType.ItemInteraction);
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
