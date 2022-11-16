using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class MotionSensor : ItemComponent
    {
        private float rangeX, rangeY;

        private Vector2 detectOffset;

        private float updateTimer;

        [Flags]
        public enum TargetType
        {
            Human = 1,
            Monster = 2,
            Wall = 4,
            Pet = 8,
            Any = Human | Monster | Wall | Pet,
        }

        [Serialize(false, IsPropertySaveable.No, description: "Has the item currently detected movement. Intended to be used by StatusEffect conditionals (setting this value in XML has no effect).")]
        public bool MotionDetected { get; set; }

        [InGameEditable, Serialize(TargetType.Any, IsPropertySaveable.Yes, description: "Which kind of targets can trigger the sensor?", alwaysUseInstanceValues: true)]
        public TargetType Target
        {
            get;
            set;
        }

        [InGameEditable, Serialize(false, IsPropertySaveable.Yes, description: "Should the sensor ignore the bodies of dead characters?", alwaysUseInstanceValues: true)]
        public bool IgnoreDead
        {
            get;
            set;
        }


        [InGameEditable, Serialize(0.0f, IsPropertySaveable.Yes, description: "Horizontal detection range.", alwaysUseInstanceValues: true)]
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
        [InGameEditable, Serialize(0.0f, IsPropertySaveable.Yes, description: "Vertical movement detection range.", alwaysUseInstanceValues: true)]
        public float RangeY
        {
            get { return rangeY; }
            set
            {
                rangeY = MathHelper.Clamp(value, 0.0f, 1000.0f);
            }
        }

        [InGameEditable, Serialize("0,0", IsPropertySaveable.Yes, description: "The position to detect the movement at relative to the item. For example, 0,100 would detect movement 100 units above the item.")]
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

        public Vector2 TransformedDetectOffset
        {
            get
            {
                Vector2 transformedDetectOffset = detectOffset;
                if (item.FlippedX) { transformedDetectOffset.X = -transformedDetectOffset.X; }
                if (item.FlippedY) { transformedDetectOffset.Y = -transformedDetectOffset.Y; }
                return transformedDetectOffset;
            }
        }

        [Editable(MinValueFloat = 0.1f, MaxValueFloat = 100.0f, DecimalCount = 2), Serialize(0.1f, IsPropertySaveable.Yes, description: "How often the sensor checks if there's something moving near it. Higher values are better for performance.", alwaysUseInstanceValues: true)]
        public float UpdateInterval
        {
            get;
            set;
        }

        private int maxOutputLength;
        [Editable, Serialize(200, IsPropertySaveable.No, description: "The maximum length of the output strings. Warning: Large values can lead to large memory usage or networking issues.")]
        public int MaxOutputLength
        {
            get { return maxOutputLength; }
            set
            {
                maxOutputLength = Math.Max(value, 0);
            }
        }

        private string output;
        [InGameEditable, Serialize("1", IsPropertySaveable.Yes, description: "The signal the item outputs when it has detected movement.", alwaysUseInstanceValues: true)]
        public string Output
        {
            get { return output; }
            set
            {
                if (value == null) { return; }
                output = value;
                if (output.Length > MaxOutputLength && (item.Submarine == null || !item.Submarine.Loading))
                {
                    output = output.Substring(0, MaxOutputLength);
                }
            }
        }

        private string falseOutput;
        [InGameEditable, Serialize("", IsPropertySaveable.Yes, description: "The signal the item outputs when it has not detected movement.", alwaysUseInstanceValues: true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set
            {
                if (value == null) { return; }
                falseOutput = value;
                if (falseOutput.Length > MaxOutputLength && (item.Submarine == null || !item.Submarine.Loading))
                {
                    falseOutput = falseOutput.Substring(0, MaxOutputLength);
                }
            }
        }

        [Editable(DecimalCount = 3), Serialize(0.01f, IsPropertySaveable.Yes, description: "How fast the objects within the detector's range have to be moving (in m/s).", alwaysUseInstanceValues: true)]
        public float MinimumVelocity
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the sensor trigger when the item itself moves.")]
        public bool DetectOwnMotion
        {
            get;
            set;
        }

        public MotionSensor(Item item, ContentXElement element)
            : base(item, element)
        {
            IsActive = true;

            //backwards compatibility
            if (element.GetAttribute("range") != null)
            {
                rangeX = rangeY = element.GetAttributeFloat("range", 0.0f);
            }

            //randomize update timer so all sensors aren't updated during the same frame
            updateTimer = Rand.Range(0.0f, UpdateInterval);
        }

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap)
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

            if (!string.IsNullOrEmpty(signalOut)) { item.SendSignal(new Signal(signalOut, 1), "state_out"); }

            if (MotionDetected)
            {
                ApplyStatusEffects(ActionType.OnUse, deltaTime);
            }

            updateTimer -= deltaTime;
            if (updateTimer > 0.0f) { return; }

            MotionDetected = false;
            updateTimer = UpdateInterval;

            if (item.body != null && item.body.Enabled && DetectOwnMotion)
            {
                if (Math.Abs(item.body.LinearVelocity.X) > MinimumVelocity || Math.Abs(item.body.LinearVelocity.Y) > MinimumVelocity)
                {
                    MotionDetected = true;
                    return;
                }
            }

            Vector2 detectPos = item.WorldPosition + TransformedDetectOffset;
            Rectangle detectRect = new Rectangle((int)(detectPos.X - rangeX), (int)(detectPos.Y - rangeY), (int)(rangeX * 2), (int)(rangeY * 2));
            float broadRangeX = Math.Max(rangeX * 2, 500);
            float broadRangeY = Math.Max(rangeY * 2, 500);

            if (item.CurrentHull == null && item.Submarine != null && Target.HasFlag(TargetType.Wall))
            {
                if (Level.Loaded != null && (Math.Abs(item.Submarine.Velocity.X) > MinimumVelocity || Math.Abs(item.Submarine.Velocity.Y) > MinimumVelocity))
                {
                    var cells = Level.Loaded.GetCells(item.WorldPosition, 1);
                    foreach (var cell in cells)
                    {
                        if (cell.IsPointInside(item.WorldPosition))
                        {
                            MotionDetected = true;
                            return;
                        }
                        foreach (var edge in cell.Edges)
                        {
                            Vector2 e1 = edge.Point1 + cell.Translation;
                            Vector2 e2 = edge.Point2 + cell.Translation;
                            if (MathUtils.LinesIntersect(e1, e2, new Vector2(detectRect.X, detectRect.Y), new Vector2(detectRect.Right, detectRect.Y)) ||
                                MathUtils.LinesIntersect(e1, e2, new Vector2(detectRect.X, detectRect.Bottom), new Vector2(detectRect.Right, detectRect.Bottom)) ||
                                MathUtils.LinesIntersect(e1, e2, new Vector2(detectRect.X, detectRect.Y), new Vector2(detectRect.X, detectRect.Bottom)) ||
                                MathUtils.LinesIntersect(e1, e2, new Vector2(detectRect.Right, detectRect.Y), new Vector2(detectRect.Right, detectRect.Bottom)))
                            {
                                MotionDetected = true;
                                return;
                            }
                        }
                    }
                }
                foreach (Submarine sub in Submarine.Loaded)
                {
                    if (sub == item.Submarine) { continue; }

                    Vector2 relativeVelocity = item.Submarine.Velocity - sub.Velocity;
                    if (Math.Abs(relativeVelocity.X) < MinimumVelocity && Math.Abs(relativeVelocity.Y) < MinimumVelocity) { continue; }

                    Rectangle worldBorders = new Rectangle(
                        sub.Borders.X + (int)sub.WorldPosition.X,
                        sub.Borders.Y + (int)sub.WorldPosition.Y - sub.Borders.Height,
                        sub.Borders.Width,
                        sub.Borders.Height);

                    if (worldBorders.Intersects(detectRect))
                    {
                        MotionDetected = true;
                        return;
                    }
                }
            }

            if (Target.HasFlag(TargetType.Human) || Target.HasFlag(TargetType.Pet) || Target.HasFlag(TargetType.Monster))
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (IgnoreDead && c.IsDead) { continue; }

                    //ignore characters that have spawned a second or less ago
                    //makes it possible to detect when a spawned character moves without triggering the detector immediately as the ragdoll spawns and drops to the ground
                    if (c.SpawnTime > Timing.TotalTime - 1.0) { continue; }

                    if (c.IsHuman)
                    {
                        if (!Target.HasFlag(TargetType.Human)) { continue; }
                    }
                    else if (c.IsPet)
                    {
                        if (!Target.HasFlag(TargetType.Pet)) { continue; }
                    }
                    else
                    {
                        if (!Target.HasFlag(TargetType.Monster)) { continue; }
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
                        if (limb.LinearVelocity.LengthSquared() < MinimumVelocity * MinimumVelocity) { continue; }
                        if (MathUtils.CircleIntersectsRectangle(limb.WorldPosition, ConvertUnits.ToDisplayUnits(limb.body.GetMaxExtent()), detectRect))
                        {
                            MotionDetected = true;
                            return;
                        }
                    }
                }
            }
        }

        public override XElement Save(XElement parentElement)
        {
            Vector2 prevDetectOffset = detectOffset;
            XElement element = base.Save(parentElement);
            detectOffset = prevDetectOffset;
            return element;
        }
    }
}
