using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CharacterHealth
    {
        private static Sprite noiseOverlay, damageOverlay;

        private static GUIProgressBar oxygenBar, healthBar;

        private float damageOverlayTimer;

        static CharacterHealth()
        {
            noiseOverlay = new Sprite("Content/UI/noise.png", Vector2.Zero);            
            damageOverlay = new Sprite("Content/UI/damageOverlay.png", Vector2.Zero);            
        }

        partial void InitProjSpecific(XElement element, Character character)
        {
            healthBar = new GUIProgressBar(new Rectangle(5,GameMain.GraphicsHeight - 128, 20, 128), Color.White, 1.0f);
            healthBar.IsHorizontal = false;
        }

        partial void UpdateOxygenProjSpecific(float prevOxygen)
        {
            if (prevOxygen > 0.0f && oxygenAmount <= 0.0f && Character.Controlled == character)
            {
                SoundPlayer.PlaySound("drown");
            }
        }


        public void UpdateHUD(float deltaTime)
        {
            if (damageOverlayTimer > 0.0f) damageOverlayTimer -= deltaTime;
            
            healthBar.Color = (vitality > 0.0f) ? Color.Lerp(Color.Orange, Color.Green, vitality / MaxVitality) : healthBar.Color = Color.Red;
            healthBar.BarSize = (vitality > 0.0f) ? vitality / MaxVitality : 1.0f - vitality / minVitality;
            
            healthBar.Update(deltaTime);
        }

        public void DrawHUD(SpriteBatch spriteBatch)
        {
            float noiseAlpha = character.IsUnconscious ? 1.0f : MathHelper.Clamp(1.0f - oxygenAmount, 0.0f, 0.8f);

            if (noiseAlpha > 0.0f)
            {
                Vector2 noiseOffset = Rand.Vector(noiseOverlay.size.X);
                noiseOffset.X = Math.Abs(noiseOffset.X);
                noiseOffset.Y = Math.Abs(noiseOffset.Y);
                noiseOverlay.DrawTiled(spriteBatch, Vector2.Zero - noiseOffset, 
                    new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) + noiseOffset,
                    Vector2.Zero, Color.White * noiseAlpha);
            }

            if (damageOverlayTimer > 0.0f)
            {
                damageOverlay.Draw(spriteBatch, Vector2.Zero, Color.White * damageOverlayTimer, Vector2.Zero, 0.0f,
                    new Vector2(GameMain.GraphicsWidth / damageOverlay.size.X, GameMain.GraphicsHeight / damageOverlay.size.Y));
            }

            foreach (LimbHealth limbHealth in limbHealths)
            {
                if (limbHealth.IndicatorSprite == null) continue;
                limbHealth.IndicatorSprite.Draw(spriteBatch,
                    new Vector2(20, GameMain.GraphicsHeight - limbHealth.IndicatorSprite.size.Y),
                    Color.Lerp(Color.Green, Color.Red, limbHealth.BluntDamageAmount / 100.0f),limbHealth.IndicatorSprite.Origin,0, 
                    new Vector2(limbHealth.IndicatorSprite.size.X / limbHealth.IndicatorSprite.Texture.Width, limbHealth.IndicatorSprite.size.Y / limbHealth.IndicatorSprite.Texture.Height));
            }

            healthBar.Draw(spriteBatch);
        }

        partial void RemoveProjSpecific()
        {
            foreach (LimbHealth limbHealth in limbHealths)
            {
                if (limbHealth.IndicatorSprite != null)
                {
                    limbHealth.IndicatorSprite.Remove();
                    limbHealth.IndicatorSprite = null;
                }
            }
        }
    }
}
