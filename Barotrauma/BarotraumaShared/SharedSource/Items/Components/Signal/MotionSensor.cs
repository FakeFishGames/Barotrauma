using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class MotionSensor : ItemComponent
    {
        private const float UpdateInterval = 0.1f;
        private float rangeX, rangeY;

        private Vector2 detectOffset;

        private float updateTimer;

        public enum TargetType
        {
            Any,
            Human,
            Monster
        }

        [Serialize(false, false, description: "Has the item currently detected movement. Intended to be used by StatusEffect conditionals (setting this value in XML has no effect).")]
        public bool MotionDetected { get; set; }

        [InGameEditable, Serialize(TargetType.Any, true, description: "Which kind of targets can trigger the sensor?", alwaysUseInstanceValues: true)]
        public TargetType Target
        {
            get;
            set;
        }

        [InGameEditable, Serialize(false, true, description: "Should the sensor ignore the bodies of dead characters?", alwaysUseInstanceValues: true)]
        public bool IgnoreDead
        {
            get;
            set;
        }


        [InGameEditable, Serialize(0.0f, true, description: "Horizontal detection range.", alwaysUseInstanceValues: true)]
        public float RangeX
        {
            get { return rangeX; }
            set
            {
                rangeX = MathHelper.Clamp(value, 0.0f, 1000.0f);
#if CLIENT
                item.ResetCachedVisibleSize();
#endif
            }
        }
        [InGameEditable, Serialize(0.0f, true, description: "Vertical movement detection range.", alwaysUseInstanceValues: true)]
        public float RangeY
        {
            get { return rangeY; }
            set
            {
                rangeY = MathHelper.Clamp(value, 0.0f, 1000.0f);
            }
        }

        [InGameEditable, Serialize("0,0", true, description: "The position to detect the movement at relative to the item. For example, 0,100 would detect movement 100 units above the item.")]
        public Vector2 DetectOffset
        {
            get { return detectOffset; }
            set
            {
                detectOffset = value;
                detectOffset.X = MathHelper.Clamp(value.X, -rangeX, rangeX);
                detectOffset.Y = MathHelper.Clamp(value.Y, -rangeY, rangeY);
            }
        }

        [InGameEditable, Serialize("1", true, description: "The signal the item outputs when it has detected movement.", alwaysUseInstanceValues: true)]
        public string Output { get; set; }

        [InGameEditable, Serialize("", true, description: "The signal the item outputs when it has not detected movement.", alwaysUseInstanceValues: true)]
        public string FalseOutput { get; set; }

        [Editable(DecimalCount = 3), Serialize(0.01f, true, description: "How fast the objects within the detector's range have to be moving (in m/s).", alwaysUseInstanceValues: true)]
        public float MinimumVelocity
        {
            get;
            set;
        }

        public MotionSensor(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            //backwards compatibility
            if (element.Attribute("range") != null)
            {
                rangeX = rangeY = element.GetAttributeFloat("range", 0.0f);
            }
        }

        public override void Load(XElement componentElement, bool usePrefabValues, IdRemap idRemap)
        {
            base.Load(componentElement, usePrefabValues, idRemap);
            //backwards compatibility
            if (componentElement.GetAttributeBool("onlyhumans", false))
            {
                Target = TargetType.Human;
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            string signalOut = MotionDetected ? Output : FalseOutput;

            if (!string.IsNullOrEmpty(signalOut)) item.SendSignal(1, signalOut, "state_out", null);

            updateTimer -= deltaTime;
            if (updateTimer > 0.0f) return;

            MotionDetected = false;
            updateTimer = UpdateInterval;

            if (item.body != null && item.body.Enabled)
            {
                if (Math.Abs(item.body.LinearVelocity.X) > MinimumVelocity || Math.Abs(item.body.LinearVelocity.Y) > MinimumVelocity)
                {
                    MotionDetected = true;
                }
            }

            Vector2 detectPos = item.WorldPosition + detectOffset;
            Rectangle detectRect = new Rectangle((int)(detectPos.X - rangeX), (int)(detectPos.Y - rangeY), (int)(rangeX * 2), (int)(rangeY * 2));
            float broadRangeX = Math.Max(rangeX * 2, 500);
            float broadRangeY = Math.Max(rangeY * 2, 500);

            foreach (Character c in Character.CharacterList)
            {
                if (IgnoreDead && c.IsDead) { continue; }

                //ignore characters that have spawned a second or less ago
                //makes it possible to detect when a spawned character moves without triggering the detector immediately as the ragdoll spawns and drops to the ground
                if (c.SpawnTime > Timing.TotalTime - 1.0) { continue; }

                switch (Target)
                {
                    case TargetType.Human:
                        if (!c.IsHuman) { continue; }
                        break;
                    case TargetType.Monster:
                        if (c.IsHuman || c.IsPet) { continue; }
                        break;
                }

                //do a rough check based on the position of the character's collider first
                //before the more accurate limb-based check
                if (Math.Abs(c.WorldPosition.X - detectPos.X) > broadRangeX || Math.Abs(c.WorldPosition.Y - detectPos.Y) > broadRangeY)
                {
                    continue;
                }

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (limb.IsSevered) { continue; }
                    if (limb.LinearVelocity.LengthSquared() <= MinimumVelocity * MinimumVelocity) { continue; }
                    if (MathUtils.CircleIntersectsRectangle(limb.WorldPosition, ConvertUnits.ToDisplayUnits(limb.body.GetMaxExtent()), detectRect))
                    {
                        MotionDetected = true;
                        break;
                    }
                }
            }
        }

        public override void FlipX(bool relativeToSub)
        {
            detectOffset.X = -detectOffset.X;
        }
        public override void FlipY(bool relativeToSub)
        {
            detectOffset.Y = -detectOffset.Y;
        }
        public override XElement Save(XElement parentElement)
        {
            Vector2 prevDetectOffset = detectOffset;
            //undo flipping before saving
            if (item.FlippedX) { detectOffset.X = -detectOffset.X; }
            if (item.FlippedY) { detectOffset.Y = -detectOffset.Y; }
            XElement element = base.Save(parentElement);
            detectOffset = prevDetectOffset;
            return element;
        }
    }
}
