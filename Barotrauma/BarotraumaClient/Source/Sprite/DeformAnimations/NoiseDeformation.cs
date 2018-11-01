using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.SpriteDeformations
{
    class NoiseDeformation : SpriteDeformation
    {
        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f,
            ToolTip = "The frequency of the noise.")]
        public float Frequency { get; set; }

        [Serialize(1.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f, 
            ToolTip = "How much the noise distorts the sprite.")]
        public float Amplitude { get; set; }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f,
            ToolTip = "How fast the noise changes.")]
        public float ChangeSpeed { get; set; }
        
        private float phase;

        public NoiseDeformation(XElement element) : base(element)
        {
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
                        (float)PerlinNoise.Perlin(normalizedX * Frequency, normalizedY * Frequency, phase) - 0.5f,
                        (float)PerlinNoise.Perlin(normalizedX * Frequency, normalizedY * Frequency, phase + 0.5f) - 0.5f);
                }
            }
        }

        protected override void GetDeformation(out Vector2[,] deformation, out float multiplier)
        {
            deformation = Deformation;
            multiplier = Amplitude;
        }

        public override void Update(float deltaTime)
        {
            if (ChangeSpeed > 0.0f)
            {
                phase += deltaTime * ChangeSpeed;
                phase %= 255;
                UpdateNoise();
            }
        }
    }
}
