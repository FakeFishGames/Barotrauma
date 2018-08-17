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

        private Vector2[,] deformation;

        /// <summary>
        /// 0 = no falloff, the entire sprite is stretched
        /// 1 = stretching the center of the sprite has no effect at the edges
        /// </summary>
        private float falloff;

        /// <summary>
        /// Maximum stretch per vertex (1 = the size of the sprite)
        /// </summary>
        private float maxDeformation;

        /// <summary>
        /// How fast the sprite reacts to being stretched
        /// </summary>
        private float reactionSpeed;

        /// <summary>
        /// How fast the sprite returns back to normal after stretching ends
        /// </summary>
        private float recoverSpeed;

        public PositionalDeformation(XElement element) : base(element)
        {
            deformation = new Vector2[resolution.X, resolution.Y];
            maxDeformation = element.GetAttributeFloat("maxdeformation", 1.0f);
            falloff = element.GetAttributeFloat("falloff", 0.0f);

            reactionSpeed = element.GetAttributeFloat("reactionspeed", 0.1f);
            recoverSpeed = element.GetAttributeFloat("recoverspeed", 0.01f);
        }

        public override void Update(float deltaTime)
        {
            if (recoverSpeed <= 0.0f) return;

            for (int x = 0; x < resolution.X; x++)
            {
                for (int y = 0; y < resolution.Y; y++)
                {
                    if (deformation[x,y].LengthSquared() < 0.0001f)
                    {
                        deformation[x, y] = Vector2.Zero;
                        continue;
                    }

                    Vector2 reduction = deformation[x, y];
                    deformation[x, y] -= reduction.ClampLength(recoverSpeed) * deltaTime;
                }
            }
        }
        
        public void Deform(Vector2 worldPosition, Vector2 amount, float deltaTime, Matrix transformMatrix)
        {
            Vector2 pos = Vector2.Transform(worldPosition, transformMatrix);
            Point deformIndex = new Point((int)(pos.X * (resolution.X - 1)), (int)(pos.Y * (resolution.Y - 1)));
            
            if (deformIndex.X < 0 || deformIndex.Y < 0) return;
            if (deformIndex.X >= resolution.X || deformIndex.Y >= resolution.Y) return;

            amount = amount.ClampLength(maxDeformation);

            float invFalloff = 1.0f - falloff;

            for (int x = 0; x < resolution.X; x++)
            {
                float normalizedDiffX = Math.Abs(x - deformIndex.X) / (resolution.X * 0.5f);
                for (int y = 0; y < resolution.Y; y++)
                {
                    float normalizedDiffY = Math.Abs(y - deformIndex.Y) / (resolution.Y * 0.5f);
                    Vector2 targetDeformation = amount * MathHelper.Clamp(1.0f - new Vector2(normalizedDiffX, normalizedDiffY).Length() * falloff, 0.0f, 1.0f);

                    Vector2 diff = targetDeformation - deformation[x, y];
                    deformation[x, y] += diff.ClampLength(reactionSpeed) * deltaTime;
                }
            }            
        }

        protected override void GetDeformation(out Vector2[,] deformation, out float multiplier)
        {
            deformation = this.deformation;
            multiplier = 1.0f;
        }
    }
}
