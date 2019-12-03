using Barotrauma.Lights;
using Barotrauma.Particles;
using Barotrauma.SpriteDeformations;
using Barotrauma.Extensions;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SpriteParams = Barotrauma.RagdollParams.SpriteParams;

namespace Barotrauma
{
    partial class LimbJoint : RevoluteJoint
    {
        public void UpdateDeformations(float deltaTime)
        {
            float diff = Math.Abs(UpperLimit - LowerLimit);
            float strength = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(0, MathHelper.Pi, diff));
            float jointAngle = this.JointAngle * strength;

            JointBendDeformation limbADeformation = LimbA.Deformations.Find(d => d is JointBendDeformation) as JointBendDeformation;
            JointBendDeformation limbBDeformation = LimbB.Deformations.Find(d => d is JointBendDeformation) as JointBendDeformation;

            if (limbADeformation != null && limbBDeformation != null)
            {
                UpdateBend(LimbA, limbADeformation, this.LocalAnchorA, -jointAngle);
                UpdateBend(LimbB, limbBDeformation, this.LocalAnchorB, jointAngle);
            }
            
            void UpdateBend(Limb limb, JointBendDeformation deformation, Vector2 localAnchor, float angle)
            {
                deformation.Scale = limb.DeformSprite.Size;

                Vector2 displayAnchor = ConvertUnits.ToDisplayUnits(localAnchor);
                displayAnchor.Y = -displayAnchor.Y;
                Vector2 refPos = displayAnchor + limb.DeformSprite.Origin;

                refPos.X /= limb.DeformSprite.Size.X;
                refPos.Y /= limb.DeformSprite.Size.Y;

                if (Math.Abs(localAnchor.X) > Math.Abs(localAnchor.Y))
                {
                    if (localAnchor.X > 0.0f)
                    {
                        deformation.BendRightRefPos = refPos;
                        deformation.BendRight = angle;
                    }
                    else
                    {
                        deformation.BendLeftRefPos = refPos;
                        deformation.BendLeft = angle;
                    }
                }
                else
                {
                    if (localAnchor.Y > 0.0f)
                    {
                        deformation.BendUpRefPos = refPos;
                        deformation.BendUp = angle;
                    }
                    else
                    {
                        deformation.BendDownRefPos = refPos;
                        deformation.BendDown = angle;
                    }
                }
            }

        }

        public void Draw(SpriteBatch spriteBatch)
        {
            // TODO: move this into the character editor
            //var mouthPos = ragdoll.GetMouthPosition();
            //if (mouthPos != null)
            //{
            //    var pos = ConvertUnits.ToDisplayUnits(mouthPos.Value);
            //    pos.Y = -pos.Y;
            //    ShapeExtensions.DrawPoint(spriteBatch, pos, Color.Red, size: 5);
            //}

            // A debug visualisation on the bezier curve between limbs.
            /*var start = LimbA.WorldPosition;
            var end = LimbB.WorldPosition;
            var jointAPos = ConvertUnits.ToDisplayUnits(LocalAnchorA);
            var control = start + Vector2.Transform(jointAPos, Matrix.CreateRotationZ(LimbA.Rotation));
            start.Y = -start.Y;
            end.Y = -end.Y;
            control.Y = -control.Y;
            //GUI.DrawRectangle(spriteBatch, start, Vector2.One * 5, Color.White, true);
            //GUI.DrawRectangle(spriteBatch, end, Vector2.One * 5, Color.Black, true);
            //GUI.DrawRectangle(spriteBatch, control, Vector2.One * 5, Color.Black, true);
            //GUI.DrawLine(spriteBatch, start, end, Color.White);
            //GUI.DrawLine(spriteBatch, start, control, Color.Black);
            //GUI.DrawLine(spriteBatch, control, end, Color.Black);
            GUI.DrawBezierWithDots(spriteBatch, start, end, control, 1000, Color.Red);*/
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

        public Sprite Sprite { get; protected set; }

        protected DeformableSprite _deformSprite;

        public DeformableSprite DeformSprite
        {
            get
            {
                var conditionalSprite = ConditionalSprites.FirstOrDefault(c => c.IsActive && c.DeformableSprite != null);
                if (conditionalSprite != null)
                {
                    return conditionalSprite.DeformableSprite;
                }
                else
                {
                    return _deformSprite;
                }
            }
        }

        public List<DecorativeSprite> DecorativeSprites { get; private set; } = new List<DecorativeSprite>();

        public Sprite ActiveSprite
        {
            get
            {
                var conditionalSprite = ConditionalSprites.FirstOrDefault(c => c.IsActive && c.ActiveSprite != null);
                if (conditionalSprite != null)
                {
                    return conditionalSprite.ActiveSprite;
                }
                else
                {
                    return _deformSprite != null ? _deformSprite.Sprite : Sprite;
                }
            }
        }

        public WearableSprite HuskSprite { get; private set; }
        public WearableSprite HerpesSprite { get; private set; }

        public void LoadHuskSprite() => HuskSprite = GetWearableSprite(WearableType.Husk);
        public void LoadHerpesSprite() => HerpesSprite = GetWearableSprite(WearableType.Herpes);

        public float TextureScale => Params.Ragdoll.TextureScale;

        public Sprite DamagedSprite { get; private set; }

        public List<ConditionalSprite> ConditionalSprites { get; private set; } = new List<ConditionalSprite>();
        private Dictionary<DecorativeSprite, SpriteState> spriteAnimState = new Dictionary<DecorativeSprite, SpriteState>();
        private Dictionary<int, List<DecorativeSprite>> DecorativeSpriteGroups = new Dictionary<int, List<DecorativeSprite>>();

        class SpriteState
        {
            public float RotationState;
            public float OffsetState;
            public bool IsActive = true;
        }

        public Color InitialLightSourceColor
        {
            get;
            private set;
        }

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

        public string HitSoundTag => Params?.Sound?.Tag;

        private List<WearableSprite> wearableTypeHidingSprites = new List<WearableSprite>();
        private List<WearableType> wearableTypesToHide = new List<WearableType>();
        private bool enableHuskSprite;
        public bool EnableHuskSprite
        {
            get
            {
                return enableHuskSprite;
            }
            set
            {
                if (HuskSprite != null && value != enableHuskSprite)
                {
                    if (value)
                    {
                        List<WearableSprite> otherWearablesWithHusk = new List<WearableSprite>() { HuskSprite };
                        otherWearablesWithHusk.AddRange(OtherWearables);
                        OtherWearables = otherWearablesWithHusk;
                        UpdateWearableTypesToHide();
                    }
                    else
                    {
                        OtherWearables.Remove(HuskSprite);
                        UpdateWearableTypesToHide();
                    }
                }
                enableHuskSprite = value;
            }
        }

        partial void InitProjSpecific(XElement element)
        {
            for (int i = 0; i < Params.decorativeSpriteParams.Count; i++)
            {
                var param = Params.decorativeSpriteParams[i];
                var decorativeSprite = new DecorativeSprite(param.Element, file: GetSpritePath(param.Element, param));
                DecorativeSprites.Add(decorativeSprite);
                int groupID = decorativeSprite.RandomGroupID;
                if (!DecorativeSpriteGroups.ContainsKey(groupID))
                {
                    DecorativeSpriteGroups.Add(groupID, new List<DecorativeSprite>());
                }
                DecorativeSpriteGroups[groupID].Add(decorativeSprite);
                spriteAnimState.Add(decorativeSprite, new SpriteState());
            }
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        Sprite = new Sprite(subElement, file: GetSpritePath(subElement, Params.normalSpriteParams));
                        break;
                    case "damagedsprite":
                        DamagedSprite = new Sprite(subElement, file: GetSpritePath(subElement, Params.damagedSpriteParams));
                        break;
                    case "conditionalsprite":
                        var conditionalSprite = new ConditionalSprite(subElement, character, file: GetSpritePath(subElement, null));
                        ConditionalSprites.Add(conditionalSprite);
                        if (conditionalSprite.DeformableSprite != null)
                        {
                            CreateDeformations(subElement.GetChildElement("deformablesprite"));
                        }
                        break;
                    case "deformablesprite":
                        _deformSprite = new DeformableSprite(subElement, filePath: GetSpritePath(subElement, Params.deformSpriteParams));
                        CreateDeformations(subElement);
                        break;
                    case "lightsource":
                        LightSource = new LightSource(subElement);
                        InitialLightSourceColor = LightSource.Color;
                        break;
                }

                void CreateDeformations(XElement e)
                {
                    foreach (XElement animationElement in e.GetChildElements("spritedeformation"))
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
                                .Where(d => d.TypeName == typeName && d.Sync == sync)
                                .FirstOrDefault();
                        }
                        if (deformation == null)
                        {
                            deformation = SpriteDeformation.Load(animationElement, character.SpeciesName);
                            if (deformation != null)
                            {
                                ragdoll.SpriteDeformations.Add(deformation);
                            }
                        }
                        if (deformation != null)
                        {
                            Deformations.Add(deformation);
                        }
                    }
                }
            }
        }

        public void RecreateSprites()
        {
            if (Sprite != null)
            {
                Sprite.Remove();
                var source = Sprite.SourceElement;
                Sprite = new Sprite(source, file: GetSpritePath(source, Params.normalSpriteParams));
            }
            if (_deformSprite != null)
            {
                _deformSprite.Remove();
                var source = _deformSprite.Sprite.SourceElement;
                _deformSprite = new DeformableSprite(source, filePath: GetSpritePath(source, Params.deformSpriteParams));
            }
            if (DamagedSprite != null)
            {
                DamagedSprite.Remove();
                var source = DamagedSprite.SourceElement;
                DamagedSprite = new Sprite(source, file: GetSpritePath(source, Params.damagedSpriteParams));
            }
            for (int i = 0; i < ConditionalSprites.Count; i++)
            {
                var conditionalSprite = ConditionalSprites[i];
                var source = conditionalSprite.ActiveSprite.SourceElement;
                conditionalSprite.Remove();
                ConditionalSprites[i] = new ConditionalSprite(source, character, file: GetSpritePath(source, null));
            }
            for (int i = 0; i < DecorativeSprites.Count; i++)
            {
                var decorativeSprite = DecorativeSprites[i];
                decorativeSprite.Remove();
                var source = decorativeSprite.Sprite.SourceElement;
                DecorativeSprites[i] = new DecorativeSprite(source, file: GetSpritePath(source, Params.decorativeSpriteParams[i]));
            }
        }

        private void CalculateHeadPosition(Sprite sprite)
        {
            if (type != LimbType.Head) { return; }
            character.Info?.CalculateHeadPosition(sprite);
        }

        private string GetSpritePath(XElement element, SpriteParams spriteParams)
        {
            string texturePath = element.GetAttributeString("texture", null);
            if (string.IsNullOrWhiteSpace(texturePath) && spriteParams != null)
            {
                texturePath = spriteParams.Ragdoll.Texture;
            }
            return GetSpritePath(texturePath);
        }

        /// <summary>
        /// Get the full path of a limb sprite, taking into account tags, gender and head id
        /// </summary>
        private string GetSpritePath(string texturePath)
        {
            string spritePath = texturePath;
            string spritePathWithTags = spritePath;
            if (character.Info != null && character.IsHumanoid)
            {
                spritePath = spritePath.Replace("[GENDER]", (character.Info.Gender == Gender.Female) ? "female" : "male");
                spritePath = spritePath.Replace("[RACE]", character.Info.Race.ToString().ToLowerInvariant());
                spritePath = spritePath.Replace("[HEADID]", character.Info.HeadSpriteId.ToString());

                if (character.Info.HeadSprite != null && character.Info.SpriteTags.Any())
                {
                    string tags = "";
                    character.Info.SpriteTags.ForEach(tag => tags += "[" + tag + "]");

                    spritePathWithTags = Path.Combine(
                        Path.GetDirectoryName(spritePath),
                        Path.GetFileNameWithoutExtension(spritePath) + tags + Path.GetExtension(spritePath));
                }
            }
            return File.Exists(spritePathWithTags) ? spritePathWithTags : spritePath;
        }

        partial void LoadParamsProjSpecific()
        {
            bool isFlipped = dir == Direction.Left;
            Sprite?.LoadParams(Params.normalSpriteParams, isFlipped);
            DamagedSprite?.LoadParams(Params.damagedSpriteParams, isFlipped);
            _deformSprite?.Sprite.LoadParams(Params.deformSpriteParams, isFlipped);
            for (int i = 0; i < DecorativeSprites.Count; i++)
            {
                DecorativeSprites[i].Sprite?.LoadParams(Params.decorativeSpriteParams[i], isFlipped);
            }
        }

        partial void AddDamageProjSpecific(Vector2 simPosition, List<Affliction> afflictions, bool playSound, List<DamageModifier> appliedDamageModifiers)
        {
            float bleedingDamage = character.CharacterHealth.DoesBleed ? afflictions.FindAll(a => a is AfflictionBleeding).Sum(a => a.GetVitalityDecrease(character.CharacterHealth)) : 0;
            float damage = afflictions.FindAll(a => a.Prefab.AfflictionType == "damage").Sum(a => a.GetVitalityDecrease(character.CharacterHealth));
            float damageMultiplier = 1;
            foreach (DamageModifier damageModifier in appliedDamageModifiers)
            {
                damageMultiplier *= damageModifier.DamageMultiplier;
            }
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
                SoundPlayer.PlayDamageSound(damageSoundType, Math.Max(damage, bleedingDamage), WorldPosition);
            }

            // Always spawn damage particles
            float damageParticleAmount = Math.Min(damage / 10, 1.0f) * damageMultiplier;
            foreach (ParticleEmitter emitter in character.DamageEmitters)
            {
                if (inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Air) continue;
                if (!inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Water) continue;

                emitter.Emit(1.0f, WorldPosition, character.CurrentHull, amountMultiplier: damageParticleAmount);
            }

            if (bleedingDamage > 0)
            {
                float bloodParticleAmount = Math.Min(bleedingDamage / 5, 1.0f) * damageMultiplier;
                float bloodParticleSize = MathHelper.Clamp(bleedingDamage / 5, 0.5f, 1.5f);

                foreach (ParticleEmitter emitter in character.BloodEmitters)
                {
                    if (inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Air) continue;
                    if (!inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Water) continue;

                    emitter.Emit(1.0f, WorldPosition, character.CurrentHull, sizeMultiplier: bloodParticleSize, amountMultiplier: bloodParticleAmount);
                }

                if (bloodParticleAmount > 0 && character.CurrentHull != null && !string.IsNullOrEmpty(character.BloodDecalName))
                {
                    character.CurrentHull.AddDecal(character.BloodDecalName, WorldPosition, MathHelper.Clamp(bloodParticleSize, 0.5f, 1.0f));
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

            UpdateSpriteStates(deltaTime);
        }

        public void Draw(SpriteBatch spriteBatch, Camera cam, Color? overrideColor = null)
        {
            float brightness = 1.0f - (burnOverLayStrength / 100.0f) * 0.5f;
            Color color = new Color(brightness, brightness, brightness);

            color = overrideColor ?? color;

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

            float herpesStrength = character.CharacterHealth.GetAfflictionStrength("spaceherpes");

            bool hideLimb = Params.Hide || 
                OtherWearables.Any(w => w.HideLimb) || 
                wearingItems.Any(w => w != null && w.HideLimb);

            var activeSprite = ActiveSprite;
            if (type == LimbType.Head)
            {
                CalculateHeadPosition(activeSprite);
            }
            
            body.UpdateDrawPosition();

            if (!hideLimb)
            {
                var deformSprite = DeformSprite;
                if (deformSprite != null)
                {
                    if (Deformations != null && Deformations.Any())
                    {
                        var deformation = SpriteDeformation.GetDeformation(Deformations, deformSprite.Size);
                        deformSprite.Deform(deformation);
                    }
                    else
                    {
                        deformSprite.Reset();
                    }
                    body.Draw(deformSprite, cam, Vector2.One * Scale * TextureScale, color, Params.MirrorHorizontally);
                }
                else
                {
                    body.Draw(spriteBatch, activeSprite, color, null, Scale * TextureScale, Params.MirrorHorizontally, Params.MirrorVertically);
                }
            }
            SpriteEffects spriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            if (LightSource != null)
            {
                LightSource.Position = body.DrawPosition;
                if (LightSource.ParentSub != null) { LightSource.Position -= LightSource.ParentSub.DrawPosition; }
                LightSource.LightSpriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipVertically;
            }
            if (damageOverlayStrength > 0.0f && DamagedSprite != null && !hideLimb)
            {
                DamagedSprite.Draw(spriteBatch,
                    new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                    color * Math.Min(damageOverlayStrength, 1.0f), activeSprite.Origin,
                    -body.DrawRotation,
                    Scale, spriteEffect, activeSprite.Depth - 0.0000015f);
            }
            foreach (var decorativeSprite in DecorativeSprites)
            {
                if (!spriteAnimState[decorativeSprite].IsActive) { continue; }
                float rotation = decorativeSprite.GetRotation(ref spriteAnimState[decorativeSprite].RotationState);
                Vector2 offset = decorativeSprite.GetOffset(ref spriteAnimState[decorativeSprite].OffsetState) * Scale;
                var ca = (float)Math.Cos(-body.Rotation);
                var sa = (float)Math.Sin(-body.Rotation);
                Vector2 transformedOffset = new Vector2(ca * offset.X + sa * offset.Y, -sa * offset.X + ca * offset.Y);
                decorativeSprite.Sprite.Draw(spriteBatch, new Vector2(body.DrawPosition.X + transformedOffset.X, -(body.DrawPosition.Y + transformedOffset.Y)), color,
                    -body.Rotation + rotation, Scale, spriteEffect,
                    depth: decorativeSprite.Sprite.Depth);
            }
            float depthStep = 0.000001f;
            float step = depthStep;
            WearableSprite onlyDrawable = wearingItems.Find(w => w.HideOtherWearables);
            if (Params.MirrorHorizontally)
            {
                spriteEffect = spriteEffect == SpriteEffects.None ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            }
            if (Params.MirrorVertically)
            {
                spriteEffect |= SpriteEffects.FlipVertically;
            }
            if (onlyDrawable == null)
            {
                if (HerpesSprite != null && !wearableTypesToHide.Contains(WearableType.Herpes))
                {
                    DrawWearable(HerpesSprite, depthStep, spriteBatch, color * Math.Min(herpesStrength / 10.0f, 1.0f), spriteEffect);
                    depthStep += step;
                }
                foreach (WearableSprite wearable in OtherWearables)
                {
                    if (wearableTypesToHide.Contains(wearable.Type)) { continue; }
                    DrawWearable(wearable, depthStep, spriteBatch, color, spriteEffect);
                    //if there are multiple sprites on this limb, make the successive ones be drawn in front
                    depthStep += step;
                }
            }
            foreach (WearableSprite wearable in WearingItems)
            {
                if (onlyDrawable != null && onlyDrawable != wearable) continue;
                DrawWearable(wearable, depthStep, spriteBatch, color, spriteEffect);
                //if there are multiple sprites on this limb, make the successive ones be drawn in front
                depthStep += step;
            }

            if (GameMain.DebugDraw)
            {
                if (pullJoint != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(pullJoint.WorldAnchorB);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.Red, true);
                }
                var bodyDrawPos = body.DrawPosition;
                bodyDrawPos.Y = -bodyDrawPos.Y;
                if (IsStuck)
                {
                    Vector2 from = ConvertUnits.ToDisplayUnits(attachJoint.WorldAnchorA);
                    from.Y = -from.Y;
                    Vector2 to = ConvertUnits.ToDisplayUnits(attachJoint.WorldAnchorB);
                    to.Y = -to.Y;
                    var localFront = body.GetLocalFront(Params.GetSpriteOrientation());
                    var front = ConvertUnits.ToDisplayUnits(body.FarseerBody.GetWorldPoint(localFront));
                    front.Y = -front.Y;
                    GUI.DrawLine(spriteBatch, bodyDrawPos, front, Color.Yellow, width: 2);
                    GUI.DrawLine(spriteBatch, from, to, Color.Red, width: 1);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)from.X, (int)from.Y, 12, 12), Color.White, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)to.X, (int)to.Y, 12, 12), Color.White, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)from.X, (int)from.Y, 10, 10), Color.Blue, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)to.X, (int)to.Y, 10, 10), Color.Red, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)front.X, (int)front.Y, 10, 10), Color.Yellow, true);

                    //Vector2 mainLimbFront = ConvertUnits.ToDisplayUnits(ragdoll.MainLimb.body.FarseerBody.GetWorldPoint(ragdoll.MainLimb.body.GetFrontLocal(MathHelper.ToRadians(limbParams.Orientation))));
                    //mainLimbFront.Y = -mainLimbFront.Y;
                    //var mainLimbDrawPos = ragdoll.MainLimb.body.DrawPosition;
                    //mainLimbDrawPos.Y = -mainLimbDrawPos.Y;
                    //GUI.DrawLine(spriteBatch, mainLimbDrawPos, mainLimbFront, Color.White, width: 5);
                    //GUI.DrawRectangle(spriteBatch, new Rectangle((int)mainLimbFront.X, (int)mainLimbFront.Y, 10, 10), Color.Yellow, true);
                }
                DrawDamageModifiers(spriteBatch, cam, bodyDrawPos, isScreenSpace: false);
            }
        }

        public void UpdateWearableTypesToHide()
        {
            wearableTypeHidingSprites.Clear();
            if (WearingItems != null && WearingItems.Count > 0)
            {
                wearableTypeHidingSprites.AddRange(
                    WearingItems.FindAll(w => w.HideWearablesOfType != null && w.HideWearablesOfType.Count > 0));
            }
            if (OtherWearables != null && OtherWearables.Count > 0)
            {
                wearableTypeHidingSprites.AddRange(
                    OtherWearables.FindAll(w => w.HideWearablesOfType != null && w.HideWearablesOfType.Count > 0));
            }

            wearableTypesToHide.Clear();
            if (wearableTypeHidingSprites.Count > 0)
            {
                foreach (WearableSprite sprite in wearableTypeHidingSprites)
                {
                    foreach (WearableType type in sprite.HideWearablesOfType)
                    {
                        if (!wearableTypesToHide.Contains(type))
                        {
                            wearableTypesToHide.Add(type);
                        }
                    }
                }
            }
        }

        private void UpdateSpriteStates(float deltaTime)
        {
            foreach (int spriteGroup in DecorativeSpriteGroups.Keys)
            {
                for (int i = 0; i < DecorativeSpriteGroups[spriteGroup].Count; i++)
                {
                    var decorativeSprite = DecorativeSpriteGroups[spriteGroup][i];
                    if (decorativeSprite == null) { continue; }
                    if (spriteGroup > 0)
                    {
                        // TODO
                        //int activeSpriteIndex = ID % DecorativeSpriteGroups[spriteGroup].Count;
                        //if (i != activeSpriteIndex)
                        //{
                        //    spriteAnimState[decorativeSprite].IsActive = false;
                        //    continue;
                        //}
                    }

                    //check if the sprite is active (whether it should be drawn or not)
                    var spriteState = spriteAnimState[decorativeSprite];
                    spriteState.IsActive = true;
                    foreach (PropertyConditional conditional in decorativeSprite.IsActiveConditionals)
                    {
                        if (!conditional.Matches(this))
                        {
                            spriteState.IsActive = false;
                            break;
                        }
                    }
                    if (!spriteState.IsActive) { continue; }

                    //check if the sprite should be animated
                    bool animate = true;
                    foreach (PropertyConditional conditional in decorativeSprite.AnimationConditionals)
                    {
                        if (!conditional.Matches(this)) { animate = false; break; }
                    }
                    if (!animate) { continue; }
                    spriteState.OffsetState += deltaTime;
                    spriteState.RotationState += deltaTime;
                }
            }
        }

        public void DrawDamageModifiers(SpriteBatch spriteBatch, Camera cam, Vector2 startPos, bool isScreenSpace)
        {
            foreach (var modifier in DamageModifiers)
            {
                //Vector2 up = VectorExtensions.Backward(-body.TransformedRotation + Params.GetSpriteOrientation() * Dir);
                //int width = 4;
                //if (!isScreenSpace)
                //{
                //    width = (int)Math.Round(width / cam.Zoom);
                //}
                //GUI.DrawLine(spriteBatch, startPos, startPos + Vector2.Normalize(up) * size, Color.Red, width: width);
                Color color = modifier.DamageMultiplier > 1 ? Color.Red : Color.GreenYellow;
                float size = ConvertUnits.ToDisplayUnits(body.GetSize().Length() / 2);
                if (isScreenSpace)
                {
                    size *= cam.Zoom;
                }
                int thickness = 2;
                if (!isScreenSpace)
                {
                    thickness = (int)Math.Round(thickness / cam.Zoom);
                }
                float bodyRotation = -body.Rotation;
                float constantOffset = -MathHelper.PiOver2;
                Vector2 armorSector = modifier.ArmorSectorInRadians;
                float armorSectorSize = Math.Abs(armorSector.X - armorSector.Y);
                float radians = armorSectorSize * Dir;
                float armorSectorOffset = armorSector.X * Dir;
                float finalOffset = bodyRotation + constantOffset + armorSectorOffset;
                ShapeExtensions.DrawSector(spriteBatch, startPos, size, radians, 40, color, finalOffset, thickness);
            }
        }

        private void DrawWearable(WearableSprite wearable, float depthStep, SpriteBatch spriteBatch, Color color, SpriteEffects spriteEffect)
        {
            var sprite = ActiveSprite;
            if (wearable.InheritSourceRect)
            {
                if (wearable.SheetIndex.HasValue)
                {
                    wearable.Sprite.SourceRect = new Rectangle(CharacterInfo.CalculateOffset(sprite, wearable.SheetIndex.Value), sprite.SourceRect.Size);
                }
                else if (type == LimbType.Head && character.Info != null && character.Info.Head.SheetIndex.HasValue)
                {
                    wearable.Sprite.SourceRect = new Rectangle(CharacterInfo.CalculateOffset(sprite, character.Info.Head.SheetIndex.Value.ToPoint()), sprite.SourceRect.Size);
                }
                else
                {
                    wearable.Sprite.SourceRect = sprite.SourceRect;
                }
            }

            Vector2 origin = wearable.Sprite.Origin;
            if (wearable.InheritOrigin)
            {
                origin = sprite.Origin;
                wearable.Sprite.Origin = origin;
            }
            else
            {
                origin = wearable.Sprite.Origin;
                // If the wearable inherits the origin, flipping is already handled.
                if (body.Dir == -1.0f)
                {
                    origin.X = wearable.Sprite.SourceRect.Width - origin.X;
                }
            }

            float depth = wearable.Sprite.Depth;

            if (wearable.InheritLimbDepth)
            {
                depth = sprite.Depth - depthStep;
                Limb depthLimb = (wearable.DepthLimb == LimbType.None) ? this : character.AnimController.GetLimb(wearable.DepthLimb);
                if (depthLimb != null)
                {
                    depth = depthLimb.ActiveSprite.Depth - depthStep;
                }
            }
            var wearableItemComponent = wearable.WearableComponent;
            Color wearableColor = Color.White;
            if (wearableItemComponent != null)
            {
                // Draw outer cloths on top of inner cloths.
                if (wearableItemComponent.AllowedSlots.Contains(InvSlotType.OuterClothes))
                {
                    depth -= depthStep;
                }
                wearableColor = wearableItemComponent.Item.GetSpriteColor();
            }
            float textureScale = wearable.InheritTextureScale ? TextureScale : 1;

            wearable.Sprite.Draw(spriteBatch,
                new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                new Color((color.R * wearableColor.R) / (255.0f * 255.0f), (color.G * wearableColor.G) / (255.0f * 255.0f), (color.B * wearableColor.B) / (255.0f * 255.0f)) * ((color.A * wearableColor.A) / (255.0f * 255.0f)),
                origin, -body.DrawRotation,
                Scale * textureScale, spriteEffect, depth);
        }

        private WearableSprite GetWearableSprite(WearableType type, bool random = false)
        {
            var info = character.Info;
            if (info == null) { return null; }
            XElement element;
            if (random)
            {
                element = info.FilterByTypeAndHeadID(character.Info.FilterElementsByGenderAndRace(character.Info.Wearables), type)?.GetRandom(Rand.RandSync.ClientOnly);
            }
            else
            {
                element = info.FilterByTypeAndHeadID(character.Info.FilterElementsByGenderAndRace(character.Info.Wearables), type)?.FirstOrDefault();
            }
            if (element != null)
            {
                return new WearableSprite(element.Element("sprite"), type);
            }
            return null;
        }

        partial void RemoveProjSpecific()
        {
            Sprite?.Remove();
            Sprite = null;            

            DamagedSprite?.Remove();
            DamagedSprite = null;            

            _deformSprite?.Sprite?.Remove();
            _deformSprite = null;

            DecorativeSprites.ForEach(s => s.Remove());
            ConditionalSprites.Clear();

            ConditionalSprites.ForEach(s => s.Remove());
            ConditionalSprites.Clear();

            LightSource?.Remove();
            LightSource = null;

            OtherWearables?.ForEach(w => w.Sprite.Remove());
            OtherWearables = null;

            HuskSprite?.Sprite.Remove();
            HuskSprite = null;

            HerpesSprite?.Sprite.Remove();
            HerpesSprite = null;
        }
    }
}
