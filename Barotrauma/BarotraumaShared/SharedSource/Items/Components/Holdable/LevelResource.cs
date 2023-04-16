using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class LevelResource : ItemComponent, IServerSerializable
    {
        private PhysicsBody trigger;

        private Holdable holdable;

        private float deattachTimer;

        [Serialize(1.0f, IsPropertySaveable.No, description: "How long it takes to deattach the item from the level walls (in seconds).")]
        public float DeattachDuration
        {
            get;
            set;
        }
        
        [Serialize(0.0f, IsPropertySaveable.No, description: "How far along the item is to being deattached. When the timer goes above DeattachDuration, the item is deattached.")]
        public float DeattachTimer
        {
            get { return deattachTimer; }
            set
            {
                //clients don't deattach the item until the server says so (handled in ClientRead)
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
                {
                    return;
                }

                if (holdable == null) { return; }

                deattachTimer = Math.Max(0.0f, value);
#if SERVER
                if (deattachTimer >= DeattachDuration)
                {
                    if (holdable.Attached) { item.CreateServerEvent(this); }
                    holdable.DeattachFromWall();
                }
                else if (Math.Abs(lastSentDeattachTimer - deattachTimer) > 0.1f)
                {
                    item.CreateServerEvent(this);
                    lastSentDeattachTimer = deattachTimer;
                }
#else
                if (deattachTimer >= DeattachDuration)
                {
                    if (holdable.Attached)
                    {
                        GameAnalyticsManager.AddDesignEvent("ResourceCollected:" + (GameMain.GameSession?.GameMode?.Preset.Identifier.Value ?? "none") + ":" + item.Prefab.Identifier);
                        holdable.DeattachFromWall();
                    }
                    trigger.Enabled = false;
                }
#endif
            }
        }

        [Serialize(1.0f, IsPropertySaveable.No, description: "How much the position of the item can vary from the wall the item spawns on.")]
        public float RandomOffsetFromWall
        {
            get;
            set;
        }

        public bool Attached
        {
            get { return holdable != null && holdable.Attached; }
        }
                
        public LevelResource(Item item, ContentXElement element) : base(item, element)
        {
            IsActive = true;
        }

        public override void Move(Vector2 amount, bool ignoreContacts = false)
        {
            if (trigger != null && amount.LengthSquared() > 0.00001f)
            {
                if (ignoreContacts)
                {
                    trigger.SetTransformIgnoreContacts(item.SimPosition, 0.0f);
                }
                else
                {
                    trigger.SetTransform(item.SimPosition, 0.0f);
                }
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (holdable != null && !holdable.Attached)
            {
                trigger.Enabled = false;
                IsActive = false;
            }
            else
            {
                if (trigger != null && Vector2.DistanceSquared(item.SimPosition, trigger.SimPosition) > 0.01f)
                {
                    trigger.SetTransform(item.SimPosition, 0.0f);
                }
                IsActive = false;
            }
        }

        public override void OnItemLoaded()
        {
            holdable = item.GetComponent<Holdable>();
            if (holdable == null)
            {
                IsActive = false;
                return;
            }
            holdable.Reattachable = false;
            if (requiredItems.Any())
            {
                holdable.PickingTime = float.MaxValue;
            }

            var body = item.body ?? holdable.Body;

            if (body != null)
            {
                trigger = new PhysicsBody(body.Width, body.Height, body.Radius, 
                    body.Density,
                    BodyType.Static,
                    Physics.CollisionWall,
                    Physics.CollisionNone,
                    findNewContacts: false)
                {
                    UserData = item
                };
                trigger.FarseerBody.SetIsSensor(true);
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            if (trigger != null)
            {
                trigger.Remove();
                trigger = null;
            }
        }
    }
}
