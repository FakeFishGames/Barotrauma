using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.SpriteDeformations
{
    class Inflate : SpriteDeformation
    {
        private float frequency;
        private float scale;

        private float phase;

        private Vector2[,] deformation;

        public Inflate(XElement element) : base(element)
        {
            frequency = element.GetAttributeFloat("frequency", 0.0f);
            scale = element.GetAttributeFloat("scale", 1.0f);

            deformation = new Vector2[Resolution.X, Resolution.Y];
            for (int x = 0; x < Resolution.X; x++)
            {
                float normalizedX = x / (float)(Resolution.X - 1);
                for (int y = 0; y < Resolution.Y; y++)
                {
                    float normalizedY = y / (float)(Resolution.X - 1);

                    Vector2 centerDiff = new Vector2(normalizedX - 0.5f, normalizedY - 0.5f);
                    float centerDist = centerDiff.Length() * 2.0f;
                    if (centerDist == 0.0f) continue;

                    deformation[x, y] = (centerDiff / centerDist) * Math.Min(1.0f, centerDist);
                }
            }

            phase = Rand.Range(0.0f, MathHelper.TwoPi);
        }

        protected override void GetDeformation(out Vector2[,] deformation, out float multiplier)
        {
            deformation = this.deformation;
            multiplier = frequency <= 0.0f ? scale : (float)(Math.Sin(phase)+1.0f) / 2.0f * scale;
        }

        public override void Update(float deltaTime)
        {
            phase += deltaTime * frequency;
            phase %= MathHelper.TwoPi;
        }
    }
}
