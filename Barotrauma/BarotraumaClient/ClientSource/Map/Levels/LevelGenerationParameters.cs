using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
namespace Barotrauma;

internal partial class LevelGenerationParams : PrefabWithUintIdentifier
{
    /// <remarks>Doesn't call SpriteBatch.Begin and SpriteBatch.End; They must be called manually.</remarks>
    public void DrawBackgrounds(SpriteBatch spriteBatch, Camera cam)
    {
        if (BackgroundTopSprite == null) { return; }

        Vector2 backgroundPos = cam.WorldViewCenter.FlipY() * 0.05f;
        int backgroundSize = (int)BackgroundTopSprite.size.Y;
        if (backgroundPos.Y >= backgroundSize) { return; }

        if (backgroundPos.Y < 0f)
        {
            BackgroundTopSprite.SourceRect = new Rectangle((int)backgroundPos.X, (int)backgroundPos.Y, backgroundSize, (int)Math.Min(-backgroundPos.Y, backgroundSize));
            BackgroundTopSprite.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(GameMain.GraphicsWidth, Math.Min(-backgroundPos.Y, GameMain.GraphicsHeight)),
                color: BackgroundTextureColor);
        }
        if (-backgroundPos.Y < GameMain.GraphicsHeight && BackgroundSprite != null)
        {
            BackgroundSprite.SourceRect = new Rectangle((int)backgroundPos.X, (int)Math.Max(backgroundPos.Y, 0), backgroundSize, backgroundSize);
            BackgroundSprite.DrawTiled(spriteBatch, (backgroundPos.Y < 0f) ? new Vector2(0f, (int)-backgroundPos.Y) : Vector2.Zero,
                new Vector2(GameMain.GraphicsWidth, (int)Math.Min(Math.Ceiling(backgroundSize - backgroundPos.Y), backgroundSize)),
                color: BackgroundTextureColor);
        }
    }

    /// <remarks>Doesn't call SpriteBatch.Begin and SpriteBatch.End; They must be called manually.</remarks>
    public void DrawWaterParticles(SpriteBatch spriteBatch, Camera cam, Vector2 offset)
    {
        if (WaterParticles == null || cam.Zoom <= 0.05f) { return; }

        float textureScale = WaterParticleScale;
        Vector2 textureSize = new Vector2(WaterParticles.Texture.Width, WaterParticles.Texture.Height);
        Vector2 origin = new Vector2(cam.WorldView.X, -cam.WorldView.Y);
        offset -= origin;

        // Draw 4 layers of particles.
        for (int i = 0; i < 4; i++)
        {
            float scale = 1f - i * 0.2f;
            float alpha = MathUtils.InverseLerp(0.05f, 0.1f, cam.Zoom * scale);
            if (alpha == 0f) { continue; }

            Vector2 newOffset = offset * scale;
            newOffset += cam.WorldView.Size.ToVector2() * (1f - scale) * 0.5f;
            newOffset -= new Vector2(256f * i);

            float newTextureScale = scale * textureScale;

            Vector2 newSize = textureSize * scale;
            while (newOffset.X <= -newSize.X) { newOffset.X += newSize.X; }
            while (newOffset.X > 0f) { newOffset.X -= newSize.X; }
            while (newOffset.Y <= -newSize.Y) { newOffset.Y += newSize.Y; }
            while (newOffset.Y > 0f) { newOffset.Y -= newSize.Y; }

            WaterParticles.DrawTiled(spriteBatch, origin + newOffset, cam.WorldView.Size.ToVector2() - newOffset,
                color: WaterParticleColor * alpha, textureScale: new Vector2(newTextureScale));
        }
    }

    public void UpdateWaterParticleOffset(ref Vector2 offset, Vector2 velocity, float deltaTime)
    {
        if (WaterParticles == null) { return; }
        Vector2 waterTextureSize = WaterParticles.size * WaterParticleScale;
        offset += velocity.FlipY() * WaterParticleScale * deltaTime;
        offset.X %= waterTextureSize.X;
        offset.Y %= waterTextureSize.Y;
    }
}
