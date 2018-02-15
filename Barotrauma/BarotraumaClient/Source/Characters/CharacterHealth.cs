using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CharacterHealth
    {
        private static Sprite noiseOverlay, damageOverlay;
        
        private static Sprite statusIconOxygen;
        private static Sprite statusIconPressure;
        private static Sprite statusIconBloodloss;

        private static GUIProgressBar oxygenBar, healthBar;

        private float damageOverlayTimer;

        static CharacterHealth()
        {
            noiseOverlay = new Sprite("Content/UI/noise.png", Vector2.Zero);            
            damageOverlay = new Sprite("Content/UI/damageOverlay.png", Vector2.Zero);
            
            statusIconOxygen = new Sprite("Content/UI/Health/statusIcons.png", new Rectangle(96, 48, 48, 48), null);
            statusIconPressure = new Sprite("Content/UI/Health/statusIcons.png", new Rectangle(0, 48, 48, 48), null);
            statusIconBloodloss = new Sprite("Content/UI/Health/statusIcons.png", new Rectangle(48, 0, 48, 48), null);
        }

        partial void InitProjSpecific(XElement element, Character character)
        {
            healthBar = new GUIProgressBar(new Rectangle(5, GameMain.GraphicsHeight - 138, 20, 128), Color.White, null, 1.0f, Alignment.TopLeft);
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
            float noiseAlpha = character.IsUnconscious ? 1.0f : MathHelper.Clamp(1.0f - oxygenAmount / 100.0f, 0.0f, 0.8f);

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

                float damageLerp = limbHealth.TotalDamage > 0.0f ? MathHelper.Lerp(0.2f, 1.0f, limbHealth.TotalDamage / 100.0f) : 0.0f;

                Color color = damageLerp < 0.5f ?
                    Color.Lerp(Color.Green, Color.Orange, damageLerp * 2.0f) : Color.Lerp(Color.Orange, Color.Red, (damageLerp - 0.5f) * 2.0f);

                limbHealth.IndicatorSprite.Draw(spriteBatch,
                    new Vector2(20, GameMain.GraphicsHeight - limbHealth.IndicatorSprite.size.Y),
                    color, limbHealth.IndicatorSprite.Origin, 0,
                    new Vector2(limbHealth.IndicatorSprite.size.X / limbHealth.IndicatorSprite.Texture.Width, limbHealth.IndicatorSprite.size.Y / limbHealth.IndicatorSprite.Texture.Height));
            }

            List<Pair<Sprite, string>> statusIcons = new List<Pair<Sprite, string>>();
            if (oxygenAmount < 98.0f) statusIcons.Add(new Pair<Sprite, string>(statusIconOxygen, "Oxygen low"));
            if (character.CurrentHull == null || character.CurrentHull.LethalPressure > 5.0f) statusIcons.Add(new Pair<Sprite, string>(statusIconPressure, "High pressure"));
            if (bloodlossAmount > 10.0f) statusIcons.Add(new Pair<Sprite, string>(statusIconBloodloss, "Bloodloss"));

            List<Affliction> afflictions = new List<Affliction>();
            foreach (LimbHealth limbHealth in limbHealths)
            {
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    if (afflictions.Any(a => a.Prefab.AfflictionType == affliction.Prefab.AfflictionType)) continue;
                    statusIcons.Add(new Pair<Sprite, string>(affliction.Prefab.Icon, affliction.Prefab.Description));
                    afflictions.Add(affliction);
                }
            }

            Vector2 pos = healthBar.Rect.Location.ToVector2() + new Vector2(0.0f, -55);
            foreach (Pair<Sprite, string> statusIcon in statusIcons)
            {
                if (statusIcon.First != null) statusIcon.First.Draw(spriteBatch, pos);
                GUI.DrawString(spriteBatch, pos + new Vector2(55, 10), statusIcon.Second, Color.White * 0.8f, Color.Black * 0.5f);
                pos.Y -= 50.0f;
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
