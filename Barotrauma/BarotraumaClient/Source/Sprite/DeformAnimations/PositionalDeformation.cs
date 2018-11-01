using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.SpriteDeformations
{
    /// <summary>
    /// Stretch a position in the deformable sprite to some direction
    /// </summary>
    class PositionalDeformation : SpriteDeformation
    {
        public enum ReactionType
        {
            ReactToTriggerers
        }

        public ReactionType Type;

        /// <summary>
        /// 0 = no falloff, the entire sprite is stretched
        /// 1 = stretching the center of the sprite has no effect at the edges
        /// </summary>
        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f, 
            ToolTip = "0 = no falloff, the entire sprite is stretched, 1 = stretching the center of the sprite has no effect at the edges.")]
        public float Falloff { get; set; }

        /// <summary>
        /// Maximum stretch per vertex (1 = the size of the sprite)
        /// </summary>
        [Serialize(1.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f, 
            ToolTip = "Maximum stretch per vertex (1 = the size of the sprite)")]
        public float MaxDeformation { get; set; }


        /// <summary>
        /// How fast the sprite reacts to being stretched
        /// </summary>
        [Serialize(1.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f, 
            ToolTip = "How fast the sprite reacts to being stretched")]
        public float ReactionSpeed { get; set; }

        /// <summary>
        /// How fast the sprite returns back to normal after stretching ends
        /// </summary>
        [Serialize(0.1f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f, 
            ToolTip = "How fast the sprite returns back to normal after stretching ends")]
        public float RecoverSpeed { get; set; }

        public PositionalDeformation(XElement element) : base(element)
        {
        }

        public override void Update(float deltaTime)
        {
            if (RecoverSpeed <= 0.0f) return;

            for (int x = 0; x < Resolution.X; x++)
            {
                for (int y = 0; y < Resolution.Y; y++)
                {
                    if (Deformation[x,y].LengthSquared() < 0.0001f)
                    {
                        Deformation[x, y] = Vector2.Zero;
                        continue;
                    }

                    Vector2 reduction = Deformation[x, y];
                    Deformation[x, y] -= reduction.ClampLength(RecoverSpeed) * deltaTime;
                }
            }
        }
        
        public void Deform(Vector2 worldPosition, Vector2 amount, float deltaTime, Matrix transformMatrix)
        {
            Vector2 pos = Vector2.Transform(worldPosition, transformMatrix);
            Point deformIndex = new Point((int)(pos.X * (Resolution.X - 1)), (int)(pos.Y * (Resolution.Y - 1)));
            
            if (deformIndex.X < 0 || deformIndex.Y < 0) return;
            if (deformIndex.X >= Resolution.X || deformIndex.Y >= Resolution.Y) return;

            amount = amount.ClampLength(MaxDeformation);

            float invFalloff = 1.0f - Falloff;

            for (int x = 0; x < Resolution.X; x++)
            {
                float normalizedDiffX = Math.Abs(x - deformIndex.X) / (Resolution.X * 0.5f);
                for (int y = 0; y < Resolution.Y; y++)
                {
                    float normalizedDiffY = Math.Abs(y - deformIndex.Y) / (Resolution.Y * 0.5f);
                    Vector2 targetDeformation = amount * MathHelper.Clamp(1.0f - new Vector2(normalizedDiffX, normalizedDiffY).Length() * Falloff, 0.0f, 1.0f);

                    Vector2 diff = targetDeformation - Deformation[x, y];
                    Deformation[x, y] += diff.ClampLength(ReactionSpeed) * deltaTime;
                }
            }            
        }

        protected override void GetDeformation(out Vector2[,] deformation, out float multiplier)
        {
            deformation = Deformation;
            multiplier = 1.0f;
        }
    }
}
