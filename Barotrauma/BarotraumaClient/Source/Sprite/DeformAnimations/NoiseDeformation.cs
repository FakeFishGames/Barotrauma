using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.SpriteDeformations
{
    class NoiseDeformation : SpriteDeformation
    {
        private float frequency;
        private float amplitude;
        private float changeSpeed;

        private float phase;

        public NoiseDeformation(XElement element) : base(element)
        {
            frequency = element.GetAttributeFloat("frequency", 0.0f);
            amplitude = element.GetAttributeFloat("amplitude", 1.0f);
            changeSpeed = element.GetAttributeFloat("changespeed", 0.0f);

            phase = Rand.Range(0.0f, 255.0f);

            UpdateNoise();
        }

        private void UpdateNoise()
        {
            for (int x = 0; x < Resolution.X; x++)
            {
                float normalizedX = x / (float)(Resolution.X - 1);
                for (int y = 0; y < Resolution.Y; y++)
                {
                    float normalizedY = y / (float)(Resolution.X - 1);
                    
                    Deformation[x, y] = new Vector2(
                        (float)PerlinNoise.Perlin(normalizedX * frequency, normalizedY * frequency, phase) - 0.5f,
                        (float)PerlinNoise.Perlin(normalizedX * frequency, normalizedY * frequency, phase + 0.5f) - 0.5f);
                }
            }
        }

        protected override void GetDeformation(out Vector2[,] deformation, out float multiplier)
        {
            deformation = Deformation;
            multiplier = amplitude;
        }

        public override void Update(float deltaTime)
        {
            if (changeSpeed > 0.0f)
            {
                phase += deltaTime * changeSpeed;
                phase %= 255;
                UpdateNoise();
            }
        }
    }
}
