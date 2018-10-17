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
        /*public Vector2 start;
        public Vector2 end;
        public Vector2 control;
        public float multiplier = 1;
        public bool flipX;
        public bool manipulateStart;
        public bool manipulateEnd;
        public bool manipulateControl = true;*/

        public enum Axis { X, Y, XY }
        public Axis axis;

        public float BendRight;
        public Vector2 BendRightRefPos = new Vector2(1.0f, 0.5f);
        public float BendLeft;
        public Vector2 BendLeftRefPos = new Vector2(0.0f, 0.5f);
        public float BendUp;
        public Vector2 BendUpRefPos = new Vector2(0.5f, 0.0f);
        public float BendDown;
        public Vector2 BendDownRefPos = new Vector2(0.5f, 1.0f);

        public Vector2 Scale;

        public BezierDeformation(XElement element) : base(element)
        {
            /*manipulateStart = element.GetAttributeBool("start", false);
            manipulateEnd = element.GetAttributeBool("end", false);
            manipulateControl = element.GetAttributeBool("control", true);
            multiplier = element.GetAttributeFloat("multiplier", 1f);*/
            axis = (Axis)Enum.Parse(typeof(Axis), element.GetAttributeString("axis", "xy"), true);
        }

        protected override void GetDeformation(out Vector2[,] deformation, out float multiplier)
        {
            deformation = Deformation;
            multiplier = 1.0f;// this.multiplier;
        }

        public override void Update(float deltaTime)
        {
            Vector2 sinCosRight = new Vector2((float)Math.Sin(BendRight), (float)Math.Cos(BendRight));
            Vector2 sinCosLeft = new Vector2((float)Math.Sin(BendLeft), (float)Math.Cos(BendLeft));
            Vector2 sinCosUp = new Vector2((float)Math.Sin(BendUp), (float)Math.Cos(BendUp));
            Vector2 sinCosDown = new Vector2((float)Math.Sin(BendDown), (float)Math.Cos(BendDown));

            Vector2 normalizedPos = Vector2.Zero;
            for (int x = 0; x < Resolution.X; x++)
            {
                normalizedPos.X = x / (float)(Resolution.X - 1);
                for (int y = 0; y < Resolution.Y; y++)
                {
                    normalizedPos.Y = y / (float)(Resolution.Y - 1);
                    Deformation[x, y] = Vector2.Zero;

                    if (Math.Abs(BendLeft) > 0.01f)
                    {
                        float aaa = 1.0f - normalizedPos.X;//(1.0f - Math.Max(normalizedPos.X - BendLeftRefPos.X, 0.0f) / (1.0f - BendLeftRefPos.X));
                        aaa = Math.Max((aaa - 0.5f) * 2.0f, 0.0f);
                        Vector2 rotatedP = RotatePointAroundTarget(normalizedPos, BendLeftRefPos, BendLeft * aaa);
                        Vector2 offset = rotatedP - normalizedPos;
                        offset.X *= Scale.Y / Scale.X;
                        Deformation[x, y] += offset;
                    }
                    if (Math.Abs(BendRight) > 0.01f)
                    {
                        float aaa = normalizedPos.X;//(1.0f - Math.Max(BendRightRefPos.X - normalizedPos.X, 0.0f) / (BendRightRefPos.X));
                        aaa = Math.Max((aaa - 0.5f) * 2.0f, 0.0f);
                        Vector2 rotatedP = RotatePointAroundTarget(normalizedPos, BendRightRefPos, BendRight * aaa);
                        Vector2 offset = rotatedP - normalizedPos;
                        offset.X *= Scale.Y / Scale.X;
                        Deformation[x, y] += offset;
                    }

                    if (Math.Abs(BendUp) > 0.01f)
                    {
                        float aaa = 1.0f - normalizedPos.Y;//(1.0f - Math.Max(normalizedPos.Y - BendUpRefPos.Y, 0.0f) / (1.0f - BendUpRefPos.Y));
                        aaa = Math.Max((aaa - 0.5f) * 2.0f, 0.0f);
                        Vector2 rotatedP = RotatePointAroundTarget(normalizedPos, BendUpRefPos, BendUp * aaa);
                        Vector2 offset = rotatedP - normalizedPos;
                        offset.Y *= Scale.X / Scale.Y;
                        Deformation[x, y] += offset;
                    }
                    if (Math.Abs(BendDown) > 0.01f)
                    {
                        float aaa = normalizedPos.Y;//(1.0f - Math.Max(BendDownRefPos.Y - normalizedPos.Y, 0.0f) / (BendDownRefPos.Y));
                        aaa = Math.Max((aaa - 0.5f) * 2.0f, 0.0f);
                        Vector2 rotatedP = RotatePointAroundTarget(normalizedPos, BendDownRefPos, BendDown * aaa);
                        Vector2 offset = rotatedP - normalizedPos;
                        offset.Y *= Scale.X / Scale.Y;
                        Deformation[x, y] += offset;
                    }
                }
            }

            /*var start = this.start;
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
            Vector2 normalizedPos = Vector2.Zero;
            for (int x = 0; x < Resolution.X; x++)
            {
                normalizedPos.X = x / (float)(Resolution.X - 1);
                for (int y = 0; y < Resolution.Y; y++)
                {
                    normalizedPos.Y = y / (float)(Resolution.Y - 1);
                    switch (axis)
                    {
                        case Axis.X:
                            Deformation[x, y] = MathUtils.Bezier(start, control, end, normalizedPos.X);
                            break;
                        case Axis.Y:
                            float t = normalizedPos.Y - start.Y / (end.Y - start.Y);
                            Deformation[x, y] = MathUtils.Bezier(start, control, end, t);
                            break;
                        case Axis.XY:
                            Deformation[x, y] = MathUtils.Bezier(start, control, end, normalizedPos.X);
                            Deformation[x, y] += MathUtils.Bezier(start, control, end, normalizedPos.Y);
                            break;
                    }
                }
            }*/
        }

        public static Vector2 RotatePointAroundTarget(Vector2 point, Vector2 target, float angle, bool clockWise = true)
        {
            return RotatePointAroundTarget(point, target, (float)Math.Sin(angle), (float)Math.Cos(angle), clockWise);
        }

        public static Vector2 RotatePointAroundTarget(Vector2 point, Vector2 target, float sin, float cos, bool clockWise = true)
        {
            if (!clockWise)
            {
                sin = -sin;
            }
            Vector2 dir = point - target;
            var x = (cos * dir.X) - (sin * dir.Y) + target.X;
            var y = (sin * dir.X) + (cos * dir.Y) + target.Y;
            return new Vector2((float)x, (float)y);
        }
    }
}
