using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class LevelResource : ItemComponent, IServerSerializable
    {
        private PhysicsBody trigger;

        private Holdable holdable;

        private float deattachTimer;

        [Serialize(1.0f, false, description: "How long it takes to deattach the item from the level walls (in seconds).")]
        public float DeattachDuration
        {
            get;
            set;
        }
        
        [Serialize(0.0f, false, description: "How far along the item is to being deattached. When the timer goes above DeattachDuration, the item is deattached.")]
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
                    holdable.DeattachFromWall();
                    trigger.Enabled = false;
                }
#endif
            }
        }

        public bool Attached
        {
            get { return holdable == null ? false : holdable.Attached; }
        }
                
        public LevelResource(Item item, XElement element) : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (!holdable.Attached)
            {
                trigger.Enabled = false;
                IsActive = false;
            }
            else
            {
                if (Vector2.DistanceSquared(item.SimPosition, trigger.SimPosition) > 0.01f)
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
                DebugConsole.ThrowError("Error while initializing item \"" + item.Name + "\". Level resources require a Holdable component.");
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
                trigger = new PhysicsBody(body.width, body.height, body.radius, body.Density)
                {
                    UserData = item
                };
                trigger.FarseerBody.IsSensor = true;
                trigger.FarseerBody.IsStatic = true;
                trigger.FarseerBody.CollisionCategories = Physics.CollisionWall;
                trigger.FarseerBody.CollidesWith = Physics.CollisionNone;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            if (trigger != null)
            {
                trigger.Remove();
                trigger = null;
            }
        }
    }
}
