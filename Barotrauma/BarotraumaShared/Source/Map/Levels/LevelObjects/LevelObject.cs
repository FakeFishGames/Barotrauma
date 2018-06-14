using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
#if CLIENT
using Barotrauma.Particles;
#endif

namespace Barotrauma
{
    partial class LevelObject
    {
        public readonly LevelObjectPrefab Prefab;
        public Vector3 Position;

        public float Scale;

        public float Rotation;

        public LevelObjectPrefab ActivePrefab;

        public List<LevelTrigger> Triggers
        {
            get;
            private set;
        }

        public LevelObject(LevelObjectPrefab prefab, Vector3 position, float scale, float rotation = 0.0f)
        {
            Triggers = new List<LevelTrigger>();

            ActivePrefab = Prefab = prefab;
            Position = position;
            Scale = scale;
            Rotation = rotation;

            foreach (XElement triggerElement in prefab.LevelTriggerElements)
            {
                Vector2 triggerPosition = triggerElement.GetAttributeVector2("position", Vector2.Zero) * scale;

                if (rotation != 0.0f)
                {
                    var ca = (float)Math.Cos(rotation);
                    var sa = (float)Math.Sin(rotation);

                    triggerPosition = new Vector2(
                        ca * triggerPosition.X + sa * triggerPosition.Y,
                        -sa * triggerPosition.X + ca * triggerPosition.Y);
                }

                var newTrigger = new LevelTrigger(triggerElement, new Vector2(position.X, position.Y) + triggerPosition, -rotation, scale);
                Triggers.Add(newTrigger);
            }

            InitProjSpecific();
        }
        
        partial void InitProjSpecific();
        
        public Vector2 LocalToWorld(Vector2 localPosition, float swingState = 0.0f)
        {
            Vector2 emitterPos = localPosition * Scale;

            if (Rotation != 0.0f || Prefab.SwingAmount != 0.0f)
            {
                float rot = Rotation + swingState * Prefab.SwingAmount;

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
    }
}
