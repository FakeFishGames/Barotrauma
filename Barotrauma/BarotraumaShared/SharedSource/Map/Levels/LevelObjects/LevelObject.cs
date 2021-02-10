using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class LevelObject : ISpatialEntity
    {
        public readonly LevelObjectPrefab Prefab;
        public Vector3 Position;

        public float NetworkUpdateTimer;

        public float Scale;

        public float Rotation;

        private int spriteIndex;

        public LevelObjectPrefab ActivePrefab;

        public PhysicsBody PhysicsBody
        {
            get;
            private set;
        }

        public List<LevelTrigger> Triggers
        {
            get;
            private set;
        }

        public bool NeedsNetworkSyncing
        {
            get { return Triggers != null && Triggers.Any(t => t.NeedsNetworkSyncing); }
            set 
            {
                if (Triggers == null) { return; }
                Triggers.ForEach(t => t.NeedsNetworkSyncing = false); 
            }
        }

        public bool NeedsUpdate
        {
            get; private set;
        }

        public Sprite Sprite
        {
            get 
            {
                var prefab = ActivePrefab?.Sprites.Count > 0 ? ActivePrefab : Prefab;
                return spriteIndex < 0 || prefab.Sprites.Count == 0 ? null : prefab.Sprites[spriteIndex % prefab.Sprites.Count]; 
            }
        }

        Vector2 ISpatialEntity.Position => new Vector2(Position.X, Position.Y);

        public Vector2 WorldPosition => new Vector2(Position.X, Position.Y);

        public Vector2 SimPosition => ConvertUnits.ToSimUnits(WorldPosition);

        public Submarine Submarine => null;

        public Level.Cave ParentCave;

        public LevelObject(LevelObjectPrefab prefab, Vector3 position, float scale, float rotation = 0.0f)
        {
            ActivePrefab = Prefab = prefab;
            Position = position;
            Scale = scale;
            Rotation = rotation;

            spriteIndex = ActivePrefab.Sprites.Any() ? Rand.Int(ActivePrefab.Sprites.Count, Rand.RandSync.Server) : -1;

            if (Sprite != null && prefab.SpriteSpecificPhysicsBodyElements.ContainsKey(Sprite))
            {
                PhysicsBody = new PhysicsBody(prefab.SpriteSpecificPhysicsBodyElements[Sprite], ConvertUnits.ToSimUnits(new Vector2(position.X, position.Y)), Scale);
            }
            else if (prefab.PhysicsBodyElement != null)
            {
                PhysicsBody = new PhysicsBody(prefab.PhysicsBodyElement, ConvertUnits.ToSimUnits(new Vector2(position.X, position.Y)), Scale);
            }

            if (PhysicsBody != null)
            {
                PhysicsBody.SetTransformIgnoreContacts(PhysicsBody.SimPosition, -Rotation);
                PhysicsBody.BodyType = BodyType.Static;
                PhysicsBody.CollisionCategories = Physics.CollisionLevel;
                PhysicsBody.CollidesWith = Physics.CollisionWall | Physics.CollisionCharacter;
            }

            foreach (XElement triggerElement in prefab.LevelTriggerElements)
            {
                Triggers ??= new List<LevelTrigger>();
                Vector2 triggerPosition = triggerElement.GetAttributeVector2("position", Vector2.Zero) * scale;

                if (rotation != 0.0f)
                {
                    var ca = (float)Math.Cos(rotation);
                    var sa = (float)Math.Sin(rotation);

                    triggerPosition = new Vector2(
                        ca * triggerPosition.X + sa * triggerPosition.Y,
                        -sa * triggerPosition.X + ca * triggerPosition.Y);
                }

                var newTrigger = new LevelTrigger(triggerElement, new Vector2(position.X, position.Y) + triggerPosition, -rotation, scale, prefab.Name);
                int parentTriggerIndex = prefab.LevelTriggerElements.IndexOf(triggerElement.Parent);
                if (parentTriggerIndex > -1) { newTrigger.ParentTrigger = Triggers[parentTriggerIndex]; }
                Triggers.Add(newTrigger);
            }

            if (spriteIndex == -1)
            {
                foreach (var overrideProperties in prefab.OverrideProperties)
                {
                    if (overrideProperties == null) { continue; }
                    if (overrideProperties.Sprites.Count > 0)
                    {
                        spriteIndex = Rand.Int(overrideProperties.Sprites.Count, Rand.RandSync.Server);
                        break;
                    }
                }
            }

            NeedsUpdate = NeedsNetworkSyncing || (Triggers != null && Triggers.Any()) || Prefab.PhysicsBodyTriggerIndex > -1;

            InitProjSpecific();
        }
        
        partial void InitProjSpecific();
        
        public Vector2 LocalToWorld(Vector2 localPosition, float swingState = 0.0f)
        {
            Vector2 emitterPos = localPosition * Scale;

            if (Rotation != 0.0f || Prefab.SwingAmountRad != 0.0f)
            {
                float rot = Rotation + swingState * Prefab.SwingAmountRad;

                var ca = (float)Math.Cos(rot);
                var sa = (float)Math.Sin(rot);

                emitterPos = new Vector2(
                    ca * emitterPos.X + sa * emitterPos.Y,
                    -sa * emitterPos.X + ca * emitterPos.Y);
            }
            return new Vector2(Position.X, Position.Y) + emitterPos;
        }

        public void Remove()
        {
            RemoveProjSpecific();
        }

        partial void RemoveProjSpecific();

        public override string ToString()
        {
            return "LevelObject (" + ActivePrefab.Name + ")";
        }

        public void ServerWrite(IWriteMessage msg, Client c)
        {
            if (Triggers == null) { return; }
            for (int j = 0; j < Triggers.Count; j++)
            {
                if (!Triggers[j].UseNetworkSyncing) { continue; }
                Triggers[j].ServerWrite(msg, c);
            }
        }
    }
}
