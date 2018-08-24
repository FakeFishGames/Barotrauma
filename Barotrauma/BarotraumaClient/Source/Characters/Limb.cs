using Barotrauma.Items.Components;
using Barotrauma.Lights;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.SpriteDeformations;

namespace Barotrauma
{
    partial class LimbJoint : RevoluteJoint
    {
        public void UpdateDeformations()
        {
            //var start = LimbA.WorldPosition;
            //var end = LimbB.WorldPosition;
            //var forward = end - start;
            //var jointAPos = ConvertUnits.ToDisplayUnits(LocalAnchorA);
            //var control = start + Vector2.Transform(jointAPos, Matrix.CreateRotationZ(LimbA.Rotation));
            void UpdateBezier(Limb limb)
            {
                foreach (var deformation in limb.Deformations)
                {
                    if (deformation is BezierDeformation bezierDeformation)
                    {
                        bezierDeformation.flipX = limb.character.AnimController.IsFlipped;
                        if ((limb.character.AnimController is FishAnimController fishController))
                        {
                            var waveLength = fishController.CurrentSwimParams.WaveLength;
                            var waveAmplitude = fishController.CurrentSwimParams.WaveAmplitude;
                            var lag = 0;
                            if (waveLength > 0 && waveAmplitude > 0)
                            {
                                float waveRotation = (float)Math.Sin((fishController.WalkPos - lag) / waveLength);
                                float v = waveRotation * waveAmplitude;
                                bezierDeformation.start.X = v;
                                bezierDeformation.start.Y = v;
                                bezierDeformation.end.X = v;
                                bezierDeformation.end.Y = v;
                                bezierDeformation.control.X = v;
                                bezierDeformation.control.Y = v;
                            }
                        }
                    }
                }
            }
            UpdateBezier(LimbA);
            UpdateBezier(LimbB);
            //start.Y = -start.Y;
            //end.Y = -end.Y;
            //control.Y = -control.Y;
            //GUI.DrawRectangle(spriteBatch, start, Vector2.One * 5, Color.Blue, true);
            //GUI.DrawRectangle(spriteBatch, end, Vector2.One * 5, Color.Green, true);
            //GUI.DrawRectangle(spriteBatch, control, Vector2.One * 5, Color.Black, true);
            //GUI.DrawLine(spriteBatch, start, end, Color.Black);
            //GUI.DrawLine(spriteBatch, start, control, Color.Gray);
            //GUI.DrawLine(spriteBatch, control, end, Color.Gray);
            //GUI.DrawBezierWithDots(spriteBatch, start, end, control, 1000, Color.Red);
        }
    }

    partial class Limb
    {
        //minimum duration between hit/attack sounds
        public const float SoundInterval = 0.4f;
        public float LastAttackSoundTime, LastImpactSoundTime;

        private float wetTimer;
        private float dripParticleTimer;

        /// <summary>
        /// Note that different limbs can share the same deformations.
        /// Use ragdoll.SpriteDeformations for a collection that cannot have duplicates.
        /// </summary>
        public List<SpriteDeformation> Deformations { get; private set; } = new List<SpriteDeformation>();
        
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
                    case "deformablesprite":
                        DeformSprite = new DeformableSprite(subElement);
                        foreach (XElement animationElement in subElement.Elements())
                        {
                            int sync = animationElement.GetAttributeInt("sync", -1);
                            SpriteDeformation deformation = null;
                            if (sync > -1)
                            {
                                // if the element is marked with the sync attribute, use a deformation of the same type with the same sync value, if there is one already.
                                string typeName = animationElement.GetAttributeString("type", "").ToLowerInvariant();
                                deformation = ragdoll.Limbs
                                    .Where(l => l != null)
                                    .SelectMany(l => l.Deformations)
                                    .Where(d => d.typeName == typeName && d.sync == sync)
                                    .FirstOrDefault();
                            }
                            if (deformation == null)
                            {
                                deformation = SpriteDeformation.Load(animationElement);
                                ragdoll.SpriteDeformations.Add(deformation);
                            }
                            if (deformation != null) Deformations.Add(deformation);
                        }
                        break;
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

        public void Draw(SpriteBatch spriteBatch, Camera cam)
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
                if (DeformSprite != null)
                {
                    if (Deformations != null && Deformations.Any())
                    {
                        var deformation = SpriteDeformation.GetDeformation(Deformations, DeformSprite.Size);
                        DeformSprite.Deform(deformation);
                    }
                    else
                    {
                        DeformSprite.Reset();
                    }
                    body.Draw(DeformSprite, cam, Vector2.One * Scale);
                }
                else
                {
                    body.Draw(spriteBatch, Sprite, color, null, Scale);
                }
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
                    depth = ActiveSprite.Depth - 0.000001f;
                    if (wearable.DepthLimb != LimbType.None)
                    {
                        Limb depthLimb = character.AnimController.GetLimb(wearable.DepthLimb);
                        if (depthLimb != null)
                        {
                            depth = depthLimb.ActiveSprite.Depth - 0.000001f;
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

            if (damageOverlayStrength > 0.0f && DamagedSprite != null && !hideLimb)
            {
                SpriteEffects spriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

                float depth = ActiveSprite.Depth - 0.0000015f;

                DamagedSprite.Draw(spriteBatch,
                    new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                    color * Math.Min(damageOverlayStrength / 50.0f, 1.0f), ActiveSprite.Origin,
                    -body.DrawRotation,
                    1.0f, spriteEffect, depth);
            }

            if (GameMain.DebugDraw)
            {
                if (pullJoint != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(pullJoint.WorldAnchorB);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.Red, true);
                }
            }
        }
    }
}
