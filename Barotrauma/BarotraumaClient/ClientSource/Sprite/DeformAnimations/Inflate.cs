using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.SpriteDeformations
{
    class InflateParams : SpriteDeformationParams
    {
        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, DecimalCount = 2, ValueStep = 1)]
        public override float Frequency { get; set; } = 1;
        [Serialize(1.0f, true), Editable(MinValueFloat = 0.01f, MaxValueFloat = 10.0f, DecimalCount = 2, ValueStep = 0.1f)]
        public float Scale { get; set; }

        public InflateParams(XElement element) : base(element)
        {
        }
    }

    class Inflate : SpriteDeformation
    {
        public override float Phase
        {
            get { return phase; }
            set
            {
                phase = value;
                //phase %= MathHelper.TwoPi;
            }
        }
        private float phase;

        private Vector2[,] deformation;

        private InflateParams InflateParams => Params as InflateParams;

        public Inflate(XElement element) : base(element, new InflateParams(element))
        {
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

        protected override void GetDeformation(out Vector2[,] deformation, out float multiplier, bool inverse)
        {
            deformation = this.deformation;
            multiplier = InflateParams.Frequency <= 0.0f ? InflateParams.Scale : (float)(Math.Sin(phase) + 1.0f) / 2.0f * InflateParams.Scale;
            multiplier *= Params.Strength;
        }

        public override void Update(float deltaTime)
        {
            if (!Params.UseMovementSine)
            {
                phase += deltaTime * InflateParams.Frequency;
                phase %= MathHelper.TwoPi;
            }
        }
    }
}
