using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.SpriteDeformations
{
    /// <summary>
    /// Deforms the sprite following a bezier curve.
    /// Manipulating start or end value stretches the sprite from the edges, changing it's length/width.
    /// Manipulating the control point stretches the sprite from the middle without changing the end points.
    /// </summary>
    class BezierDeformation : SpriteDeformation
    {
        public Vector2 start;
        public Vector2 end;
        public Vector2 control;
        public float multiplier = 1;
        public bool flipX;
        public bool manipulateStart;
        public bool manipulateEnd;
        public bool manipulateControl = true;

        public enum Axis { X, Y, XY }
        public Axis axis;

        public BezierDeformation(XElement element) : base(element)
        {
            manipulateStart = element.GetAttributeBool("start", false);
            manipulateEnd = element.GetAttributeBool("end", false);
            manipulateControl = element.GetAttributeBool("control", true);
            axis = (Axis)Enum.Parse(typeof(Axis), element.GetAttributeString("axis", "xy"), true);
            multiplier = element.GetAttributeFloat("multiplier", 1f);
        }

        protected override void GetDeformation(out Vector2[,] deformation, out float multiplier)
        {
            deformation = Deformation;
            multiplier = this.multiplier;
        }

        public override void Update(float deltaTime)
        {
            var start = this.start;
            var end = this.end;
            var control = this.control;
            if (!manipulateStart)
            {
                start = Vector2.Zero;
            }
            if (!manipulateEnd)
            {
                end = Vector2.Zero;
            }
            if (!manipulateControl)
            {
                control = Vector2.Zero;
            }
            if (flipX)
            {
                start = new Vector2(-start.X, start.Y);
                end = new Vector2(-end.X, end.Y);
                control = new Vector2(-control.X, control.Y);
            }
            for (int x = 0; x < Resolution.X; x++)
            {
                float normalizedX = x / (float)(Resolution.X - 1);
                for (int y = 0; y < Resolution.Y; y++)
                {
                    float normalizedY = y / (float)(Resolution.Y - 1);
                    switch (axis)
                    {
                        case Axis.X:
                            Deformation[x, y] = MathUtils.Bezier(start, control, end, normalizedX);
                            break;
                        case Axis.Y:
                            Deformation[x, y] = MathUtils.Bezier(start, control, end, normalizedY);
                            break;
                        case Axis.XY:
                            Deformation[x, y] = MathUtils.Bezier(start, control, end, normalizedX);
                            Deformation[x, y] += MathUtils.Bezier(start, control, end, normalizedY);
                            break;
                    }
                }
            }
        }
    }
}
