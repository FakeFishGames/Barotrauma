using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.SpriteDeformations
{
    class JointBendDeformationParams : SpriteDeformationParams
    {
        public JointBendDeformationParams(XElement element) : base(element)
        {
        }
    }

    /// <summary>
    /// Does a rotational deformations around pivot points at the edges of the sprite.
    /// </summary>
    class JointBendDeformation : SpriteDeformation
    {
        //how much to bend at the right side of the sprite
        private float bendRight;
        public float BendRight
        {
            get { return bendRight; }
            set { bendRight = MathHelper.Clamp(value, -MaxRotationInRadians, MaxRotationInRadians); }
        }
        //the pivot point to rotate the right side around
        public Vector2 BendRightRefPos = new Vector2(1.0f, 0.5f);

        private float bendLeft;
        public float BendLeft
        {
            get { return bendLeft; }
            set { bendLeft = MathHelper.Clamp(value, -MaxRotationInRadians, MaxRotationInRadians); }
        }
        public Vector2 BendLeftRefPos = new Vector2(0.0f, 0.5f);

        private float bendUp;
        public float BendUp
        {
            get { return bendUp; }
            set { bendUp = MathHelper.Clamp(value, -MaxRotationInRadians, MaxRotationInRadians); }
        }
        public Vector2 BendUpRefPos = new Vector2(0.5f, 0.0f);

        private float bendDown;
        public float BendDown
        {
            get { return bendDown; }
            set { bendDown = MathHelper.Clamp(value, -MaxRotationInRadians, MaxRotationInRadians); }
        }
        public Vector2 BendDownRefPos = new Vector2(0.5f, 1.0f);

        public Vector2 Scale = Vector2.Zero;

        private float MaxRotationInRadians => MathHelper.ToRadians(Params.MaxRotation);

        public JointBendDeformation(XElement element) : base(element, new JointBendDeformationParams(element)) { }

        protected override void GetDeformation(out Vector2[,] deformation, out float multiplier)
        {
            deformation = Deformation;
            multiplier = 1.0f;// this.multiplier;
        }

        public override void Update(float deltaTime)
        {
            Vector2 normalizedPos = Vector2.Zero;
            for (int x = 0; x < Resolution.X; x++)
            {
                normalizedPos.X = x / (float)(Resolution.X - 1);
                for (int y = 0; y < Resolution.Y; y++)
                {
                    normalizedPos.Y = y / (float)(Resolution.Y - 1);
                    Deformation[x, y] = Vector2.Zero;

                    if (Math.Abs(BendLeft) > 0.001f)
                    {
                        float strength = 1.0f - normalizedPos.X;//(1.0f - Math.Max(normalizedPos.X - BendLeftRefPos.X, 0.0f) / (1.0f - BendLeftRefPos.X));
                        strength = Math.Max((strength - 0.5f) * 2.0f, 0.0f);
                        Vector2 rotatedP = RotatePointAroundTarget(normalizedPos, BendLeftRefPos, BendLeft * strength * Params.Strength);
                        Vector2 offset = rotatedP - normalizedPos;
                        offset.X *= Scale.Y / Scale.X;
                        Deformation[x, y] += offset;
                    }
                    if (Math.Abs(BendRight) > 0.001f)
                    {
                        float strength = normalizedPos.X;//(1.0f - Math.Max(BendRightRefPos.X - normalizedPos.X, 0.0f) / (BendRightRefPos.X));
                        strength = Math.Max((strength - 0.5f) * 2.0f, 0.0f);
                        Vector2 rotatedP = RotatePointAroundTarget(normalizedPos, BendRightRefPos, BendRight * strength * Params.Strength);
                        Vector2 offset = rotatedP - normalizedPos;
                        offset.X *= Scale.Y / Scale.X;
                        Deformation[x, y] += offset;
                    }

                    if (Math.Abs(BendUp) > 0.001f)
                    {
                        float strength = 1.0f - normalizedPos.Y;//(1.0f - Math.Max(normalizedPos.Y - BendUpRefPos.Y, 0.0f) / (1.0f - BendUpRefPos.Y));
                        strength = Math.Max((strength - 0.5f) * 2.0f, 0.0f);
                        Vector2 rotatedP = RotatePointAroundTarget(normalizedPos, BendUpRefPos, BendUp * strength * Params.Strength);
                        Vector2 offset = rotatedP - normalizedPos;
                        offset.Y *= Scale.X / Scale.Y;
                        Deformation[x, y] += offset;
                    }
                    if (Math.Abs(BendDown) > 0.001f)
                    {
                        float strength = normalizedPos.Y;//(1.0f - Math.Max(BendDownRefPos.Y - normalizedPos.Y, 0.0f) / (BendDownRefPos.Y));
                        strength = Math.Max((strength - 0.5f) * 2.0f, 0.0f);
                        Vector2 rotatedP = RotatePointAroundTarget(normalizedPos, BendDownRefPos, BendDown * strength * Params.Strength);
                        Vector2 offset = rotatedP - normalizedPos;
                        offset.Y *= Scale.X / Scale.Y;
                        Deformation[x, y] += offset;
                    }
                }
            }
        }

        public static Vector2 RotatePointAroundTarget(Vector2 point, Vector2 target, float angle)
        {
            return RotatePointAroundTarget(point, target, (float)Math.Sin(angle), (float)Math.Cos(angle));
        }

        public static Vector2 RotatePointAroundTarget(Vector2 point, Vector2 target, float sin, float cos)
        {
            Vector2 dir = point - target;
            var x = (cos * dir.X) - (sin * dir.Y) + target.X;
            var y = (sin * dir.X) + (cos * dir.Y) + target.Y;
            return new Vector2(x, y);
        }
    }
}
