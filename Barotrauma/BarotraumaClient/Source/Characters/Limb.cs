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
        //minimum duration between hit/attack sounds
        public const float SoundInterval = 0.4f;
        public float LastAttackSoundTime, LastImpactSoundTime;

        private float wetTimer;
        private float dripParticleTimer;
        
        public LightSource LightSource
        {
            get;
            private set;
        }

        private float damageOverlayStrength;
        public float DamageOverlayStrength
        {
            get { return damageOverlayStrength; }
            set { damageOverlayStrength = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        private float burnOverLayStrength;
        public float BurnOverlayStrength
        {
            get { return burnOverLayStrength; }
            set { burnOverLayStrength = MathHelper.Clamp(value, 0.0f, 100.0f); }
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

        partial void AddDamageProjSpecific(Vector2 position, List<Affliction> afflictions, bool playSound, List<DamageModifier> appliedDamageModifiers)
        {
            float bleedingDamage = afflictions.FindAll(a => a is AfflictionBleeding).Sum(a => a.GetVitalityDecrease(character.CharacterHealth));
            float damage = afflictions.FindAll(a => a.Prefab.AfflictionType == "damage").Sum(a => a.GetVitalityDecrease(character.CharacterHealth));

            if (playSound)
            {
                string damageSoundType = (bleedingDamage > damage) ? "LimbSlash" : "LimbBlunt";

                foreach (DamageModifier damageModifier in appliedDamageModifiers)
                {
                    if (!string.IsNullOrWhiteSpace(damageModifier.DamageSound))
                    {
                        damageSoundType = damageModifier.DamageSound;
                        break;
                    }
                }

                SoundPlayer.PlayDamageSound(damageSoundType, Math.Max(damage, bleedingDamage), position);
            }

            if (character.UseBloodParticles)
            {
                float bloodParticleAmount = (int)Math.Min(bleedingDamage * 5, 10);
                float bloodParticleSize = MathHelper.Clamp(bleedingDamage, 0.1f, 1.0f);

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
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            if (!body.Enabled) return;

            if (!character.IsDead)
            {
                DamageOverlayStrength -= deltaTime;
                BurnOverlayStrength -= deltaTime;
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
            float brightness = 1.0f - (burnOverLayStrength / 100.0f) * 0.5f;
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
            
            bool hideLimb = wearingItems.Any(w => w != null && w.HideLimb);
            body.UpdateDrawPosition();

            if (!hideLimb)
            {
                body.Draw(spriteBatch, sprite, color, null, Scale);
            }

            if (LightSource != null)
            {
                LightSource.Position = body.DrawPosition;
                LightSource.LightSpriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipVertically;
            }
            
            WearableSprite onlyDrawable = wearingItems.Find(w => w.HideOtherWearables);
            foreach (WearableSprite wearable in WearingItems)
            {
                if (onlyDrawable != null && onlyDrawable != wearable) continue;

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
                    new Color((color.R * wearableColor.R) / 255.0f, (color.G * wearableColor.G) / 255.0f, (color.B * wearableColor.B) / 255.0f, (color.A * wearableColor.A) / 255.0f),
                    origin, -body.DrawRotation,
                    Scale, spriteEffect, depth);
            }

            if (damageOverlayStrength > 0.0f && damagedSprite != null && !hideLimb)
            {
                SpriteEffects spriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

                float depth = sprite.Depth - 0.0000015f;

                damagedSprite.Draw(spriteBatch,
                    new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                    color * Math.Min(damageOverlayStrength / 50.0f, 1.0f), sprite.Origin,
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
