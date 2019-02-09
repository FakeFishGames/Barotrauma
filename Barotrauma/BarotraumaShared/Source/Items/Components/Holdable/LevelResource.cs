using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class LevelResource : ItemComponent, IServerSerializable
    {
        [Serialize(1.0f, false)]
        public float DeattachDuration
        {
            get;
            set;
        }
        
        [Serialize(0.0f, false)]
        public float DeattachTimer
        {
            get { return deattachTimer; }
            set
            {
                deattachTimer = Math.Max(0.0f, value);
                //clients don't deattach the item until the server says so (handled in ClientRead)
                if (GameMain.Client == null && deattachTimer >= DeattachDuration)
                {
                    holdable.DeattachFromWall();
                }
            }
        }

        private PhysicsBody trigger;

        private Holdable holdable;

        private float deattachTimer;
        
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
            holdable.PickingTime = float.MaxValue;

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

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(deattachTimer);
        }
    }
}
