using Barotrauma.Items.Components;
using Barotrauma.Lights;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Limb
    {
        public LightSource LightSource
        {
            get;
            private set;
        }

        private float damage, burnt, wetTimer;
        private float dripParticleTimer;

        public float Burnt
        {
            get { return burnt; }
            protected set { burnt = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public string HitSoundTag { get; private set; }

        partial void InitProjSpecific(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "lightsource":
                        LightSource = new LightSource(subElement);
                        break;
                    case "sound":
                        HitSoundTag = subElement.GetAttributeString("tag", "");
                        if (string.IsNullOrWhiteSpace(HitSoundTag))
                        {
                            //legacy support
                            HitSoundTag = subElement.GetAttributeString("file", "");
                        }
                        break;
                }
            }
        }

        partial void AddDamageProjSpecific(Vector2 position, DamageType damageType, float amount, float bleedingAmount, bool playSound, List<DamageModifier> appliedDamageModifiers)
        {
            if (playSound)
            {
                string damageSoundType = (damageType == DamageType.Blunt) ? "LimbBlunt" : "LimbSlash";

                foreach (DamageModifier damageModifier in appliedDamageModifiers)
                {
                    if (!string.IsNullOrWhiteSpace(damageModifier.DamageSound))
                    {
                        damageSoundType = damageModifier.DamageSound;
                        break;
                    }
                }

                SoundPlayer.PlayDamageSound(damageSoundType, amount, position);
            }

            if (character.UseBloodParticles)
            {
                float bloodParticleAmount = bleedingAmount <= 0.0f ? 0 : (int)Math.Min(amount / 5, 10);
                float bloodParticleSize = MathHelper.Clamp(amount / 50.0f, 0.1f, 1.0f);

                for (int i = 0; i < bloodParticleAmount; i++)
                {
                    var blood = GameMain.ParticleManager.CreateParticle(inWater ? "waterblood" : "blood", WorldPosition, Vector2.Zero, 0.0f, character.AnimController.CurrentHull);
                    if (blood != null)
                    {
                        blood.Size *= bloodParticleSize;
                    }
                }

                if (bloodParticleAmount > 0 && character.CurrentHull != null)
                {
                    character.CurrentHull.AddDecal("blood", WorldPosition, MathHelper.Clamp(bloodParticleSize, 0.5f, 1.0f));
                }
            }

            if (damageType == DamageType.Burn)
            {
                Burnt += amount * 10.0f;
            }

            damage += Math.Max(amount, bleedingAmount) / character.MaxHealth * 100.0f;

        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            if (!body.Enabled) return;

            if (!character.IsDead)
            {
                damage = Math.Max(0.0f, damage - deltaTime * 0.1f);
                Burnt -= deltaTime;
            }

            if (inWater)
            {
                wetTimer = 1.0f;
            }
            else
            {
                wetTimer -= deltaTime * 0.1f;
                if (wetTimer > 0.0f)
                {
                    dripParticleTimer += wetTimer * deltaTime * Mass * (wetTimer > 0.9f ? 50.0f : 5.0f);
                    if (dripParticleTimer > 1.0f)
                    {
                        float dropRadius = body.BodyShape == PhysicsBody.Shape.Rectangle ? Math.Min(body.width, body.height) : body.radius;
                        GameMain.ParticleManager.CreateParticle(
                            "waterdrop", 
                            WorldPosition + Rand.Vector(Rand.Range(0.0f, ConvertUnits.ToDisplayUnits(dropRadius))), 
                            ConvertUnits.ToDisplayUnits(body.LinearVelocity), 
                            0, character.CurrentHull);
                        dripParticleTimer = 0.0f;
                    }
                }
            }

            if (LightSource != null)
            {
                LightSource.ParentSub = body.Submarine;
                LightSource.Rotation = (dir == Direction.Right) ? body.Rotation : body.Rotation - MathHelper.Pi;
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            float brightness = 1.0f - (burnt / 100.0f) * 0.5f;
            Color color = new Color(brightness, brightness, brightness);

            if (isSevered)
            {
                if (severedFadeOutTimer > SeveredFadeOutTime)
                {
                    return;
                }
                else if (severedFadeOutTimer > SeveredFadeOutTime - 1.0f)
                {
                    color *= SeveredFadeOutTime - severedFadeOutTimer;
                }
            }

            body.Dir = Dir;

            bool hideLimb = WearingItems.Any(w => w != null && w.HideLimb);
            if (!hideLimb)
            {
                body.Draw(spriteBatch, sprite, color, null, Scale);
            }
            else
            {
                body.UpdateDrawPosition();
            }

            if (LightSource != null)
            {
                LightSource.Position = body.DrawPosition;
                LightSource.LightSpriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipVertically;
            }

            foreach (WearableSprite wearable in WearingItems)
            {
                SpriteEffects spriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

                Vector2 origin = wearable.Sprite.Origin;
                if (body.Dir == -1.0f) origin.X = wearable.Sprite.SourceRect.Width - origin.X;
                
                float depth = wearable.Sprite.Depth;

                if (wearable.InheritLimbDepth)
                {
                    depth = sprite.Depth - 0.000001f;
                    if (wearable.DepthLimb != LimbType.None)
                    {
                        Limb depthLimb = character.AnimController.GetLimb(wearable.DepthLimb);
                        if (depthLimb != null)
                        {
                            depth = depthLimb.sprite.Depth - 0.000001f;
                        }
                    }
                }

                Color wearableColor = wearable.WearableComponent.Item.GetSpriteColor();
                wearable.Sprite.Draw(spriteBatch,
                    new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                    new Color((color.R * wearableColor.R) / (255.0f * 255.0f), (color.G * wearableColor.G) / (255.0f * 255.0f), (color.B * wearableColor.B) / (255.0f * 255.0f)) * ((color.A * wearableColor.A) / (255.0f * 255.0f)),
                    origin, -body.DrawRotation,
                    Scale, spriteEffect, depth);
            }

            if (damage > 0.0f && damagedSprite != null && !hideLimb)
            {
                SpriteEffects spriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

                float depth = sprite.Depth - 0.0000015f;

                damagedSprite.Draw(spriteBatch,
                    new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                    color * Math.Min(damage / 50.0f, 1.0f), sprite.Origin,
                    -body.DrawRotation,
                    1.0f, spriteEffect, depth);
            }

            if (!GameMain.DebugDraw) return;

            if (pullJoint != null)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(pullJoint.WorldAnchorB);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.Red, true);
            }
        }
    }
}
