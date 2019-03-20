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

namespace Barotrauma.Items.Components
{
    partial class Door : Pickable, IDrawableComponent, IServerSerializable
    {
        private Gap linkedGap;
        private bool isOpen;

        private float openState;
        private Sprite doorSprite, weldedSprite, brokenSprite;
        private bool scaleBrokenSprite, fadeBrokenSprite;
        private bool createdNewGap;
        private bool autoOrientGap;

        private bool isStuck;
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

        private float stuck;
        [Serialize(0.0f, false)]
        public float Stuck
        {
            get { return stuck; }
            set 
            {
                if (isOpen || isBroken) return;
                stuck = MathHelper.Clamp(value, 0.0f, 100.0f);
                if (stuck <= 0.0f) isStuck = false;
                if (stuck >= 100.0f) isStuck = true;
            }
        }

        public bool? PredictedState { get; private set; }

        public Gap LinkedGap
        {
            get
            {
                if (linkedGap != null) return linkedGap;

                foreach (MapEntity e in item.linkedTo)
                {
                    linkedGap = e as Gap;
                    if (linkedGap != null)
                    {
                        linkedGap.PassAmbientLight = Window != Rectangle.Empty;
                        return linkedGap;
                    }
                }
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
                    Submarine = item.Submarine,
                    PassAmbientLight = Window != Rectangle.Empty,
                    Open = openState
                };
                item.linkedTo.Add(linkedGap);
                createdNewGap = true;
                return linkedGap;
            }
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

        public override bool HasRequiredItems(Character character, bool addMessage)
        {
            if (item.Condition <= RepairThreshold) return true; //For repairing

            //this is a bit pointless atm because if canBePicked is false it won't allow you to do Pick() anyway, however it's still good for future-proofing.
            return requiredItems.Any() ? base.HasRequiredItems(character, addMessage) : canBePicked;
        }

        public override bool Pick(Character picker)
        {
            return item.Condition <= RepairThreshold ? true : base.Pick(picker);
        }

        public override bool OnPicked(Character picker)
        {
            if (item.Condition <= RepairThreshold) return true; //repairs

            SetState(PredictedState == null ? !isOpen : !PredictedState.Value, false, true); //crowbar function
#if CLIENT
            PlaySound(ActionType.OnPicked, item.WorldPosition, picker);
#endif
            return false;
        }

        public override bool Select(Character character)
        {
            //can only be selected if the item is broken
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
                    OpenState += deltaTime * (isOpen ? 2.0f : -2.0f);
                    isClosing = openState > 0.0f && openState < 1.0f && !isOpen;
                }
                else
                {
                    OpenState += deltaTime * ((bool)PredictedState ? 2.0f : -2.0f);
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
                PushCharactersAway();
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

        public override void OnMapLoaded()
        {
            LinkedGap.ConnectedDoor = this;
            LinkedGap.Open = openState;
            if (createdNewGap && autoOrientGap) linkedGap.AutoOrient();

#if CLIENT
            Vector2[] corners = GetConvexHullCorners(Rectangle.Empty);

            convexHull = new ConvexHull(corners, Color.Black, item);
            if (Window != Rectangle.Empty) convexHull2 = new ConvexHull(corners, Color.Black, item);

            UpdateConvexHulls();
#endif
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

            //push characters out of the doorway when the door is closing/opening
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
                    DebugConsole.ThrowError("Failed to push a character out of a doorway - position of the character \"" + c.Name + "\" is not valid (" + c.SimPosition + ")");
                    GameAnalyticsManager.AddErrorEventOnce("PushCharactersAway:CharacterPosInvalid", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Failed to push a character out of a doorway - position of the character \"" + c.Name + "\" is not valid (" + c.SimPosition + ")." +
                        " Removed: " + c.Removed +
                        " Remoteplayer: " + c.IsRemotePlayer);
                    continue;
                }
                int dir = IsHorizontal ? Math.Sign(c.SimPosition.Y - item.SimPosition.Y) : Math.Sign(c.SimPosition.X - item.SimPosition.X);

                List<PhysicsBody> bodies = c.AnimController.Limbs.Select(l => l.body).ToList();
                bodies.Add(c.AnimController.Collider);

                foreach (PhysicsBody body in bodies)
                {
                    float diff = 0.0f;
                    if (!MathUtils.IsValid(body.SimPosition))
                    {
                        DebugConsole.ThrowError("Failed to push a limb out of a doorway - position of the body (character \"" + c.Name + "\") is not valid (" + body.SimPosition + ")");
                        GameAnalyticsManager.AddErrorEventOnce("PushCharactersAway:LimbPosInvalid", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                            "Failed to push a character out of a doorway - position of the character \"" + c.Name + "\" is not valid (" + body.SimPosition + ")." +
                            " Removed: " + c.Removed +
                            " Remoteplayer: " + c.IsRemotePlayer);
                        continue;
                    }

                    if (IsHorizontal)
                    {
                        if (body.SimPosition.X < simPos.X || body.SimPosition.X > simPos.X + simSize.X) continue;
                        diff = body.SimPosition.Y - item.SimPosition.Y;
                    }
                    else
                    {
                        if (body.SimPosition.Y > simPos.Y || body.SimPosition.Y < simPos.Y - simSize.Y) continue;
                        diff = body.SimPosition.X - item.SimPosition.X;
                    }
                   
                    if (Math.Sign(diff) != dir)
                    {
#if CLIENT
                        SoundPlayer.PlayDamageSound("LimbBlunt", 1.0f, body);
#endif

                        if (IsHorizontal)
                        {
                            body.SetTransform(new Vector2(body.SimPosition.X, item.SimPosition.Y + dir * simSize.Y * 2.0f), body.Rotation);
                            body.ApplyLinearImpulse(new Vector2(isOpen ? 0.0f : 1.0f, dir * 2.0f));
                        }
                        else
                        {
                            body.SetTransform(new Vector2(item.SimPosition.X + dir * simSize.X * 1.2f, body.SimPosition.Y), body.Rotation);
                            body.ApplyLinearImpulse(new Vector2(dir * 0.5f, isOpen ? 0.0f : -1.0f));
                        }
                    }

                    if (IsHorizontal)
                    {
                        if (Math.Abs(body.SimPosition.Y - item.SimPosition.Y) > simSize.Y * 0.5f) continue;

                        body.ApplyLinearImpulse(new Vector2(isOpen ? 0.0f : 1.0f, dir * 0.5f));
                    }
                    else
                    {
                        if (Math.Abs(body.SimPosition.X - item.SimPosition.X) > simSize.X * 0.5f) continue;

                        body.ApplyLinearImpulse(new Vector2(dir * 0.5f, isOpen ? 0.0f : -1.0f));
                    }

                    c.SetStun(0.2f);
                }
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            if (componentElement == null) return;
            base.Load(componentElement);
            var prevRequiredItems = new Dictionary<RelatedItem.RelationType, List<RelatedItem>>(requiredItems);
            bool overrideRequiredItems = false;

            bool wasOpen = PredictedState == null ? isOpen : PredictedState.Value;

            if (connection.Name == "toggle")
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "requireditem":
                        if (!overrideRequiredItems) requiredItems.Clear();
                        overrideRequiredItems = true;

#if SERVER
            if (sender != null && wasOpen != isOpen)
            {
                GameServer.Log(sender.LogName + (isOpen ? " opened " : " closed ") + item.Name, ServerLog.MessageType.ItemInteraction);
            }
#endif
        }

        public void TrySetState(bool open, bool isNetworkMessage, bool sendNetworkMessage = false)
        {
            SetState(open, isNetworkMessage, sendNetworkMessage);
        }

        partial void SetState(bool open, bool isNetworkMessage, bool sendNetworkMessage);
    }
}
