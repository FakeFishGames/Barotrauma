﻿using Barotrauma.Lights;
using Barotrauma.Particles;
using Barotrauma.SpriteDeformations;
using Barotrauma.Extensions;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using Barotrauma.Utils;
using System.Linq;
using System.Xml.Linq;
using SpriteParams = Barotrauma.RagdollParams.SpriteParams;

namespace Barotrauma
{
    partial class LimbJoint
    {
        public void UpdateDeformations(float deltaTime)
        {
            float diff = Math.Abs(UpperLimit - LowerLimit);
            float strength = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(0, MathHelper.Pi, diff));
            float jointAngle = JointAngle * strength;

            JointBendDeformation limbADeformation = LimbA.ActiveDeformations.Find(d => d is JointBendDeformation) as JointBendDeformation;
            JointBendDeformation limbBDeformation = LimbB.ActiveDeformations.Find(d => d is JointBendDeformation) as JointBendDeformation;

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
            //var mouthPos = ragdoll.GetMouthPosition();
            //if (mouthPos != null)
            //{
            //    var pos = ConvertUnits.ToDisplayUnits(mouthPos.Value);
            //    pos.Y = -pos.Y;
            //    ShapeExtensions.DrawPoint(spriteBatch, pos, GUIStyle.Red, size: 5);
            //}
            
            // A debug visualisation on the bezier curve between limbs.
            /*var start = LimbA.WorldPosition;
            var end = LimbB.WorldPosition;
            var jointAPos = ConvertUnits.ToDisplayUnits(LocalAnchorA);
            var control = start + Vector2.Transform(jointAPos, Matrix.CreateRotationZ(LimbA.Rotation));
            start.Y = -start.Y;
            end.Y = -end.Y;
            control.Y = -control.Y;
            GUI.DrawRectangle(spriteBatch, start, Vector2.One * 5, Color.White, true);
            GUI.DrawRectangle(spriteBatch, end, Vector2.One * 5, Color.Black, true);
            GUI.DrawRectangle(spriteBatch, control, Vector2.One * 5, Color.Black, true);
            GUI.DrawLine(spriteBatch, start, end, Color.White);
            GUI.DrawLine(spriteBatch, start, control, Color.Black);
            GUI.DrawLine(spriteBatch, control, end, Color.Black);
            GUI.DrawBezierWithDots(spriteBatch, start, end, control, 1000, GUIStyle.Red);*/
        }
    }

    partial class Limb
    {
        //minimum duration between hit/attack sounds
        public const float SoundInterval = 0.4f;
        public float LastAttackSoundTime, LastImpactSoundTime;

        private float wetTimer;
        private float dripParticleTimer;
        private float deadTimer;
        private Color? randomColor;

        /// <summary>
        /// Note that different limbs can share the same deformations.
        /// Use ragdoll.SpriteDeformations for a collection that cannot have duplicates.
        /// </summary>
        private List<SpriteDeformation> Deformations { get; set; } = new List<SpriteDeformation>();
        private List<SpriteDeformation> NonConditionalDeformations { get; set; } = new List<SpriteDeformation>();
        private List<(ConditionalSprite, IEnumerable<SpriteDeformation>)> ConditionalDeformations { get; set; } = new List<(ConditionalSprite, IEnumerable<SpriteDeformation>)>();
        public List<SpriteDeformation> ActiveDeformations { get; set; } = new List<SpriteDeformation>();

        public Sprite Sprite { get; protected set; }
        public Sprite TintMask { get; protected set; }
        
        public Sprite HuskMask { get; protected set; }
        public float TintHighlightThreshold { get; protected set; }
        public float TintHighlightMultiplier { get; protected set; }
        
        private SpriteBatch.EffectWithParams tintEffectParams;
        private SpriteBatch.EffectWithParams huskSpriteParams;
        

        protected DeformableSprite _deformSprite;

        public DeformableSprite DeformSprite
        {
            get
            {
                // Performance-sensitive, hence implemented without Linq.
                ConditionalSprite conditionalSprite = null;
                foreach (ConditionalSprite cs in ConditionalSprites)
                {
                    if (cs.Exclusive && cs.IsActive && cs.DeformableSprite != null)
                    {
                        conditionalSprite = cs;
                        break;
                    }
                }
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
                // Performance-sensitive, hence implemented without Linq.
                ConditionalSprite conditionalSprite = null;
                foreach (ConditionalSprite cs in ConditionalSprites)
                {
                    if (cs.Exclusive && cs.IsActive && cs.ActiveSprite != null)
                    {
                        conditionalSprite = cs;
                        break;
                    }
                }
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

        public Sprite GetActiveSprite(bool excludeConditionalSprites = true)
            => excludeConditionalSprites ? (_deformSprite != null ? _deformSprite.Sprite : Sprite) 
            : ActiveSprite;

        public float DefaultSpriteDepth { get; private set; }

        public WearableSprite HairWithHatSprite { get; set; }
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
            public float ScaleState;
            public Vector2 RandomOffsetMultiplier = new Vector2(Rand.Range(-1.0f, 1.0f), Rand.Range(-1.0f, 1.0f));
            public float RandomRotationFactor = Rand.Range(0.0f, 1.0f);
            public float RandomScaleFactor = Rand.Range(0.0f, 1.0f);
            public bool IsActive = true;
        }

        public Color InitialLightSourceColor
        {
            get;
            private set;
        }
        public float? InitialLightSpriteAlpha
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
            set { damageOverlayStrength = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        private float burnOverLayStrength;
        public float BurnOverlayStrength
        {
            get { return burnOverLayStrength; }
            set { burnOverLayStrength = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        public string HitSoundTag => Params?.Sound?.Tag;

        private readonly List<WearableSprite> wearableTypeHidingSprites = new List<WearableSprite>();
        private readonly HashSet<WearableType> wearableTypesToHide = new HashSet<WearableType>();
        private bool enableHuskSprite;
        public bool EnableHuskSprite
        {
            get
            {
                return enableHuskSprite;
            }
            set
            {
                if (enableHuskSprite == value) { return; }
                enableHuskSprite = value;
                if (enableHuskSprite)
                {
                    if (HuskSprite == null)
                    {
                        LoadHuskSprite();
                    }
                }
                if (HuskSprite != null)
                {
                    if (enableHuskSprite)
                    {
                        OtherWearables.Insert(0, HuskSprite);
                        UpdateWearableTypesToHide();
                    }
                    else
                    {
                        OtherWearables.Remove(HuskSprite);
                        UpdateWearableTypesToHide();
                    }
                }
            }
        }

        partial void InitProjSpecific(ContentXElement element)
        {
            for (int i = 0; i < Params.decorativeSpriteParams.Count; i++)
            {
                var param = Params.decorativeSpriteParams[i];
                var decorativeSprite = new DecorativeSprite(param.Element, file: GetSpritePath(param.Element, param, ref _texturePath));
                DecorativeSprites.Add(decorativeSprite);
                int groupID = decorativeSprite.RandomGroupID;
                if (!DecorativeSpriteGroups.ContainsKey(groupID))
                {
                    DecorativeSpriteGroups.Add(groupID, new List<DecorativeSprite>());
                }
                DecorativeSpriteGroups[groupID].Add(decorativeSprite);
                spriteAnimState.Add(decorativeSprite, new SpriteState());
            }
            TintMask = null;
            float sourceRectScale = ragdoll.RagdollParams.SourceRectScale;
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        Sprite = new Sprite(subElement, file: GetSpritePath(subElement, Params.normalSpriteParams, ref _texturePath), sourceRectScale: sourceRectScale);
                        break;
                    case "damagedsprite": 
                        DamagedSprite = new Sprite(subElement, file: GetSpritePath(subElement, Params.damagedSpriteParams, ref _damagedTexturePath), sourceRectScale: sourceRectScale);
                        break;
                    case "conditionalsprite":
                        string conditionalSpritePath = string.Empty;
                        GetSpritePath(subElement.GetChildElement("sprite") ?? subElement.GetChildElement("deformablesprite") ?? subElement, null, ref conditionalSpritePath);
                        if  (conditionalSpritePath.IsNullOrEmpty())
                        {
                            DebugConsole.ThrowError($"Failed to find a sprite path in the conditional sprite defined in {character.SpeciesName}, limb {type}.", 
                                contentPackage: subElement.ContentPackage);
                        }
                        var conditionalSprite = new ConditionalSprite(subElement, GetConditionalTarget(), 
                            file: conditionalSpritePath, 
                            sourceRectScale: sourceRectScale);
                        ConditionalSprites.Add(conditionalSprite);
                        if (conditionalSprite.DeformableSprite != null)
                        {
                            var conditionalDeformations = CreateDeformations(subElement.GetChildElement("deformablesprite"));
                            Deformations.AddRange(conditionalDeformations);
                            ConditionalDeformations.Add((conditionalSprite, conditionalDeformations));
                        }
                        break;
                    case "deformablesprite":
                        _deformSprite = new DeformableSprite(subElement, filePath: GetSpritePath(subElement, Params.deformSpriteParams, ref _texturePath), sourceRectScale: sourceRectScale);
                        var deformations = CreateDeformations(subElement);
                        Deformations.AddRange(deformations);
                        NonConditionalDeformations.AddRange(deformations);
                        break;
                    case "randomcolor":
                        randomColor = subElement.GetAttributeColorArray("colors", null)?.GetRandomUnsynced();
                        if (randomColor.HasValue)
                        {
                            Params.GetSprite().Color = randomColor.Value;
                        }
                        break;
                    case "lightsource":
                        LightSource = new LightSource(subElement, GetConditionalTarget())
                        {
                            ParentBody = body,
                            SpriteScale = Vector2.One * Scale * TextureScale
                        };
                        if (randomColor.HasValue)
                        {
                            LightSource.Color = new Color(randomColor.Value.R, randomColor.Value.G, randomColor.Value.B, LightSource.Color.A);
                        }
                        InitialLightSourceColor = LightSource.Color;
                        InitialLightSpriteAlpha = LightSource.OverrideLightSpriteAlpha;
                        break;
                    case "tintmask":
                        ContentPath tintMaskPath = subElement.GetAttributeContentPath("texture");
                        if (!tintMaskPath.IsNullOrWhiteSpace())
                        {
                            TintMask = new Sprite(subElement, file: GetSpritePath(tintMaskPath), sourceRectScale: sourceRectScale);
                            TintHighlightThreshold = subElement.GetAttributeFloat("highlightthreshold", 0.6f);
                            TintHighlightMultiplier = subElement.GetAttributeFloat("highlightmultiplier", 0.8f);
                        }
                        break;
                    case "huskmask":
                        ContentPath huskMaskPath = subElement.GetAttributeContentPath("texture");
                        if (!huskMaskPath.IsNullOrWhiteSpace())
                        {
                            HuskMask = new Sprite(subElement, file: GetSpritePath(huskMaskPath), sourceRectScale: sourceRectScale);
                        }
                        break;
                }

                ISerializableEntity GetConditionalTarget()
                {
                    ISerializableEntity targetEntity;
                    string target = subElement.GetAttributeString("target", null);
                    if (string.Equals(target, "character", StringComparison.OrdinalIgnoreCase))
                    {
                        targetEntity = character;
                    }
                    else
                    {
                        targetEntity = this;
                    }
                    return targetEntity;
                }

                IEnumerable<SpriteDeformation> CreateDeformations(XElement e)
                {
                    List<SpriteDeformation> deformations = new List<SpriteDeformation>();
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
                                .FirstOrDefault(d => d.TypeName == typeName && d.Sync == sync);
                        }
                        if (deformation == null)
                        {
                            deformation = SpriteDeformation.Load(animationElement, character.SpeciesName.Value);
                            if (deformation != null)
                            {
                                ragdoll.SpriteDeformations.Add(deformation);
                            }
                        }
                        if (deformation != null)
                        {
                            deformations.Add(deformation);
                        }
                    }
                    return deformations;
                }
            }
            DefaultSpriteDepth = GetActiveSprite()?.Depth ?? 0.0f;
            LightSource?.CheckConditionals();
        }

        private void RefreshDeformations()
        {
            if (_deformSprite == null) { return; }
            if (ConditionalSprites.None())
            {
                ActiveDeformations = Deformations;
            }
            else
            {
                ActiveDeformations.Clear();
                if (_deformSprite == DeformSprite)
                {
                    ActiveDeformations.AddRange(NonConditionalDeformations);
                }
                foreach (var conditionalDeformation in ConditionalDeformations)
                {
                    if (conditionalDeformation.Item1.IsActive)
                    {
                        ActiveDeformations.AddRange(conditionalDeformation.Item2);
                    }
                }
            }
        }

        public void RecreateSprites()
        {
            if (Sprite != null)
            {
                var source = Sprite.SourceElement;
                Sprite.Remove();
                Sprite = new Sprite(source, file: GetSpritePath(source, Params.normalSpriteParams, ref _texturePath));
            }
            if (_deformSprite != null)
            {
                var source = _deformSprite.Sprite.SourceElement;
                _deformSprite.Remove();
                _deformSprite = new DeformableSprite(source, filePath: GetSpritePath(source, Params.deformSpriteParams, ref _texturePath));
            }
            if (DamagedSprite != null)
            {
                var source = DamagedSprite.SourceElement;
                DamagedSprite.Remove();
                DamagedSprite = new Sprite(source, file: GetSpritePath(source, Params.damagedSpriteParams, ref _damagedTexturePath));
            }
            for (int i = 0; i < ConditionalSprites.Count; i++)
            {
                var conditionalSprite = ConditionalSprites[i];
                var source = conditionalSprite.ActiveSprite.SourceElement;
                conditionalSprite.Remove();
                ConditionalSprites[i] = new ConditionalSprite(source, character, file: GetSpritePath(source, null, ref _texturePath));
            }
            for (int i = 0; i < DecorativeSprites.Count; i++)
            {
                var decorativeSprite = DecorativeSprites[i];
                var source = decorativeSprite.Sprite.SourceElement;
                decorativeSprite.Remove();
                DecorativeSprites[i] = new DecorativeSprite(source, file: GetSpritePath(source, Params.decorativeSpriteParams[i], ref _texturePath));
            }
        }

        private void CalculateHeadPosition(Sprite sprite)
        {
            if (type != LimbType.Head) { return; }
            character.Info?.CalculateHeadPosition(sprite);
        }

        private string _texturePath;
        private string _damagedTexturePath;
        private string GetSpritePath(ContentXElement element, SpriteParams spriteParams, ref string path)
        {
            if (path.IsNullOrEmpty())
            {
                if (spriteParams != null)
                {
                    ContentPath texturePath;
                    //1. check if the limb defines the texture directly
                    var definedTexturePath = element?.GetAttributeContentPath("texture");
                    if (!definedTexturePath.IsNullOrEmpty())
                    {
                        texturePath = definedTexturePath;
                    }
                    else
                    {
                        //2. check if the character file defines the texture directly
                        texturePath = character.Params.VariantFile?.GetRootExcludingOverride()?.GetAttributeContentPath("texture", character.Prefab.ContentPackage);
                    }
                    
                    //3. check if the base prefab defines the texture
                    if (texturePath.IsNullOrEmpty() && !character.Prefab.VariantOf.IsEmpty)
                    {
                        Identifier speciesName = character.GetBaseCharacterSpeciesName();
                        RagdollParams parentRagdollParams = character.IsHumanoid ?
                                RagdollParams.GetDefaultRagdollParams<HumanRagdollParams>(speciesName, character.Params, character.Prefab.ContentPackage) :
                                RagdollParams.GetDefaultRagdollParams<FishRagdollParams>(speciesName, character.Params, character.Prefab.ContentPackage);
 
                        texturePath = parentRagdollParams.OriginalElement?.GetAttributeContentPath("texture");
                    }
                    //4. "default case", get the texture from this character's XML
                    texturePath ??= ContentPath.FromRaw(spriteParams.Element.ContentPackage ?? character.Prefab.ContentPackage, spriteParams.GetTexturePath());
                    path = GetSpritePath(texturePath);
                }
                else
                {
                    ContentPath texturePath = element.GetAttributeContentPath("texture");
                    texturePath = texturePath.IsNullOrWhiteSpace()
                        ? ContentPath.FromRaw(character.Prefab.ContentPackage, ragdoll.RagdollParams.Texture)
                        : texturePath;
                    path = GetSpritePath(texturePath);
                }
            }
            return path;
        }

        /// <summary>
        /// Get the full path of a limb sprite, taking into account tags, gender and head id
        /// </summary>
        public static string GetSpritePath(ContentPath texturePath, CharacterInfo characterInfo)
        {
            string spritePath = texturePath.Value;
            string spritePathWithTags = spritePath;
            if (characterInfo != null)
            {
                spritePath = characterInfo.ReplaceVars(spritePath);
                characterInfo.VerifySpriteTagsLoaded();
                if (characterInfo.SpriteTags.Any())
                {
                    string tags = "";
                    characterInfo.SpriteTags.ForEach(tag => tags += $"[{tag}]");

                    spritePathWithTags = Path.Combine(
                        Path.GetDirectoryName(spritePath),
                        Path.GetFileNameWithoutExtension(spritePath) + tags + Path.GetExtension(spritePath));
                }
            }
            return File.Exists(spritePathWithTags) ? spritePathWithTags : spritePath;
        }


        private string GetSpritePath(ContentPath texturePath)
        {
            if (!character.IsHumanoid) { return texturePath.Value; }
            return GetSpritePath(texturePath, character.Info);
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

        partial void AddDamageProjSpecific(bool playSound, AttackResult result)
        {
            float bleedingDamage = 0;
            if (character.CharacterHealth.DoesBleed)
            {
                foreach (var affliction in result.Afflictions)
                {
                    if (affliction is AfflictionBleeding bleeding && bleeding.Prefab.DamageParticles)
                    {
                        bleedingDamage += affliction.GetVitalityDecrease(null);
                    }
                }
            }
            float damage = 0;
            foreach (var affliction in result.Afflictions)
            {
                if (affliction.Prefab.DamageParticles && affliction.Prefab.AfflictionType == AfflictionPrefab.DamageType)
                {
                    damage += affliction.GetVitalityDecrease(null);
                }
            }
            float damageMultiplier = 1;
            float bleedingDamageMultiplier = 1;
            foreach (DamageModifier damageModifier in result.AppliedDamageModifiers)
            {
                if (damageModifier.MatchesAfflictionType(AfflictionPrefab.DamageType))
                {
                    damageMultiplier *= damageModifier.DamageMultiplier;
                }
                else if (damageModifier.MatchesAfflictionType(AfflictionPrefab.BleedingType))
                {
                    bleedingDamageMultiplier *= damageModifier.DamageMultiplier;
                }
            }
            if (playSound)
            {
                string damageSoundType = (bleedingDamage > damage) ? "LimbSlash" : "LimbBlunt";
                foreach (DamageModifier damageModifier in result.AppliedDamageModifiers)
                {
                    if (!string.IsNullOrWhiteSpace(damageModifier.DamageSound))
                    {
                        damageSoundType = damageModifier.DamageSound;
                        break;
                    }
                }
                SoundPlayer.PlayDamageSound(damageSoundType, Math.Max(damage, bleedingDamage), WorldPosition);
            }

            // spawn damage particles
            float damageParticleAmount = damage < 1 ? 0 : Math.Min(damage / 5, 1.0f) * damageMultiplier;
            if (damageParticleAmount > 0.001f)
            {
                foreach (ParticleEmitter emitter in character.DamageEmitters)
                {
                    if (emitter?.Prefab == null) { continue; }
                    if (InWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Air) { continue; }
                    if (!InWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Water) { continue; }
                    ParticlePrefab overrideParticle = null;
                    foreach (DamageModifier damageModifier in result.AppliedDamageModifiers)
                    {
                        if (damageModifier.DamageMultiplier > 0 && !string.IsNullOrWhiteSpace(damageModifier.DamageParticle))
                        {
                            overrideParticle = ParticleManager.FindPrefab(damageModifier.DamageParticle);
                            break;
                        }
                    }
                    emitter.Emit(1.0f, WorldPosition, character.CurrentHull, amountMultiplier: damageParticleAmount, overrideParticle: overrideParticle);
                }
            }

            if (bleedingDamage > 0)
            {
                float bloodParticleAmount = Math.Min(bleedingDamage / 5, 1.0f) * bleedingDamageMultiplier;
                float bloodParticleSize = MathHelper.Clamp(bleedingDamage / 5, 0.1f, 1.0f);

                foreach (ParticleEmitter emitter in character.BloodEmitters)
                {
                    if (emitter?.Prefab == null) { continue; }
                    if (InWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Air) { continue; }
                    if (!InWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Water) { continue; }
                    emitter.Emit(1.0f, WorldPosition, character.CurrentHull, sizeMultiplier: bloodParticleSize, amountMultiplier: bloodParticleAmount);
                }
            }   
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            if (!body.Enabled) { return; }

            if (IsDead)
            {
                var spriteParams = Params.GetSprite();
                if (spriteParams != null && spriteParams.DeadColorTime > 0 && deadTimer < spriteParams.DeadColorTime)
                {
                    deadTimer += deltaTime;
                }
            }

            if (InWater)
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
                        float dropRadius = body.BodyShape == PhysicsBody.Shape.Rectangle ? Math.Min(body.Width, body.Height) : body.Radius;
                        GameMain.ParticleManager.CreateParticle(
                            "waterdrop", 
                            WorldPosition + Rand.Vector(Rand.Range(0.0f, ConvertUnits.ToDisplayUnits(dropRadius))), 
                            ConvertUnits.ToDisplayUnits(body.LinearVelocity), 
                            0, character.CurrentHull);
                        dripParticleTimer = 0.0f;
                    }
                }
            }

            foreach (var conditionalSprite in ConditionalSprites)
            {
                conditionalSprite.CheckConditionals();
            }

            if (LightSource != null)
            {
                LightSource.ParentSub = body.Submarine;
                LightSource.Rotation = (dir == Direction.Right) ? body.Rotation : body.Rotation - MathHelper.Pi;
                if (LightSource.LightSprite != null)
                {
                    LightSource.LightSprite.Depth = ActiveSprite.Depth;
                }
                if (LightSource.DeformableLightSprite != null)
                {
                    LightSource.DeformableLightSprite.Sprite.Depth = ActiveSprite.Depth;
                }
                LightSource.CheckConditionals();
            }

            UpdateSpriteStates(deltaTime);
            RefreshDeformations();
        }

        public void Draw(SpriteBatch spriteBatch, Camera cam, Color? overrideColor = null, bool disableDeformations = false)
        {
            var spriteParams = Params.GetSprite();
            if (spriteParams == null || Alpha <= 0) { return; }
            float burn = spriteParams.IgnoreTint ? 0 : burnOverLayStrength;
            float brightness = Math.Max(1.0f - burn, 0.2f);
            Color tintedColor = spriteParams.Color;
            if (!spriteParams.IgnoreTint)
            {
                tintedColor = tintedColor.Multiply(ragdoll.RagdollParams.Color);
                if (character.Info != null)
                {
                    tintedColor = tintedColor.Multiply(character.Info.Head.SkinColor);
                }
                if (character.CharacterHealth.FaceTint.A > 0 && type == LimbType.Head)
                {
                    tintedColor = Color.Lerp(tintedColor, character.CharacterHealth.FaceTint.Opaque(), character.CharacterHealth.FaceTint.A / 255.0f);
                }
                if (character.CharacterHealth.BodyTint.A > 0)
                {
                    tintedColor = Color.Lerp(tintedColor, character.CharacterHealth.BodyTint.Opaque(), character.CharacterHealth.BodyTint.A / 255.0f);
                }
            }
            Color color = new Color(tintedColor.Multiply(brightness), tintedColor.A);
            Color colorWithoutTint = new Color(spriteParams.Color.Multiply(brightness), spriteParams.Color.A);
            Color blankColor = new Color(brightness, brightness, brightness, 1);
            if (deadTimer > 0)
            {
                color = Color.Lerp(color, spriteParams.DeadColor, MathUtils.InverseLerp(0, spriteParams.DeadColorTime, deadTimer));
                colorWithoutTint = Color.Lerp(colorWithoutTint, spriteParams.DeadColor, MathUtils.InverseLerp(0, spriteParams.DeadColorTime, deadTimer));
            }

            color = overrideColor ?? color;
            colorWithoutTint = overrideColor ?? colorWithoutTint;
            blankColor = overrideColor ?? blankColor;
            color *= Alpha;
            blankColor *= Alpha;

            if (isSevered)
            {
                if (severedFadeOutTimer > SeveredFadeOutTime)
                {
                    if (LightSource != null) { LightSource.Enabled = false; }
                    return;
                }
                else if (severedFadeOutTimer > SeveredFadeOutTime - 1.0f)
                {
                    color *= SeveredFadeOutTime - severedFadeOutTimer;
                    colorWithoutTint *= SeveredFadeOutTime - severedFadeOutTimer;
                }
            }
            
            float herpesStrength = character.CharacterHealth.GetAfflictionStrengthByType(AfflictionPrefab.SpaceHerpesType);

            bool hideLimb = ShouldHideLimb(this);
            if (!hideLimb && Params.InheritHiding != LimbType.None)
            {
                if (character.AnimController.GetLimb(Params.InheritHiding) is Limb otherLimb)
                {
                    hideLimb = ShouldHideLimb(otherLimb);
                }
            }

            static bool ShouldHideLimb(Limb limb)
            {
                if (limb.Hide) { return true; }
                // Performance-sensitive code -> implemented without Linq
                foreach (var wearable in limb.OtherWearables)
                {
                    if (wearable.HideLimb) { return true; }
                }
                foreach (var wearable in limb.WearingItems)
                {
                    if (wearable.HideLimb) { return true; }
                }
                return false;
            }

            bool drawHuskSprite = HuskSprite != null && !wearableTypesToHide.Contains(WearableType.Husk);

            var activeSprite = ActiveSprite;
            if (type == LimbType.Head)
            {
                CalculateHeadPosition(activeSprite);
            }
            
            body.UpdateDrawPosition();
            float depthStep = 0.000001f;

            if (!hideLimb)
            {
                var deformSprite = DeformSprite;
                if (deformSprite != null && !disableDeformations)
                {
                    if (ActiveDeformations.Any())
                    {
                        var deformation = SpriteDeformation.GetDeformation(ActiveDeformations, deformSprite.Size, flippedHorizontally: IsFlipped, false);
                        deformSprite.Deform(deformation);
                        if (LightSource != null && LightSource.DeformableLightSprite != null)
                        {
                            //apparently inversing on the y-axis is only necessary for light sprites (see 345a65ca6)
                            //it's a mystery why this is the case, something to do with sprite flipping being handled differently in light rendering?
                            deformation = SpriteDeformation.GetDeformation(ActiveDeformations, deformSprite.Size, flippedHorizontally: IsFlipped, inverseY: dir == Direction.Left);
                            LightSource.DeformableLightSprite.Deform(deformation);
                        }
                    }
                    else
                    {
                        deformSprite.Reset();
                    }
                    body.Draw(deformSprite, cam, Vector2.One * Scale * TextureScale, color, Params.MirrorHorizontally);
                }
                else
                {
                    bool useTintMask = TintMask != null && spriteBatch.GetCurrentEffect() is null;
                    if (useTintMask && Sprite?.Texture != null && TintMask?.Texture != null)
                    {
                        tintEffectParams.Effect ??= GameMain.GameScreen.ThresholdTintEffect;
                        tintEffectParams.Params ??= new Dictionary<string, object>();
                        var parameters = tintEffectParams.Params;
                        parameters["xBaseTexture"] = Sprite.Texture;
                        parameters["xTintMaskTexture"] = TintMask.Texture;
                        if (drawHuskSprite && HuskMask != null)
                        {
                            parameters["xCutoffTexture"] = HuskMask.Texture;
                            parameters["baseToCutoffSizeRatio"] = (float)Sprite.Texture.Width / (float)HuskMask.Texture.Width;
                        }
                        else
                        {
                            parameters["xCutoffTexture"] = GUI.WhiteTexture;
                            parameters["baseToCutoffSizeRatio"] = 1.0f;
                        }
                        parameters["highlightThreshold"] = TintHighlightThreshold;
                        parameters["highlightMultiplier"] = TintHighlightMultiplier;
                        spriteBatch.SwapEffect(tintEffectParams);
                    }
                    body.Draw(spriteBatch, activeSprite, color, null, Scale * TextureScale, Params.MirrorHorizontally, Params.MirrorVertically);
                    if (useTintMask)
                    {
                        spriteBatch.SwapEffect(null);
                    }
                }
                // Handle non-exlusive, i.e. additional conditional sprites
                foreach (var conditionalSprite in ConditionalSprites)
                {
                    // Exclusive conditional sprites are handled in the Properties
                    if (conditionalSprite.Exclusive) { continue; }
                    if (!conditionalSprite.IsActive) { continue; }
                    if (conditionalSprite.DeformableSprite != null)
                    {
                        var defSprite = conditionalSprite.DeformableSprite;
                        if (ActiveDeformations.Any())
                        {
                            var deformation = SpriteDeformation.GetDeformation(ActiveDeformations, defSprite.Size, flippedHorizontally: IsFlipped);
                            defSprite.Deform(deformation);
                        }
                        else
                        {
                            defSprite.Reset();
                        }
                        body.Draw(defSprite, cam, Vector2.One * Scale * TextureScale, color, Params.MirrorHorizontally);
                    }
                    else
                    {
                        body.Draw(spriteBatch, conditionalSprite.Sprite, color, depth: activeSprite.Depth - (depthStep * 50), Scale * TextureScale, Params.MirrorHorizontally, Params.MirrorVertically);
                    }
                }
            }
            SpriteEffects spriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            if (LightSource != null)
            {
                LightSource.LightSpriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipVertically;
            }
            float step = depthStep;
            WearableSprite onlyDrawable = WearingItems.Find(w => w.HideOtherWearables);
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
                if (HerpesSprite != null && !wearableTypesToHide.Contains(WearableType.Herpes) && herpesStrength > 0)
                {
                    float alpha = Math.Min(herpesStrength * 2 / 100.0f, 1.0f);
                    DrawWearable(HerpesSprite, depthStep, spriteBatch, blankColor, alpha: alpha, spriteEffect);
                    depthStep += step;
                }
                if (drawHuskSprite)
                {
                    bool useTintEffect = HuskMask != null && spriteBatch.GetCurrentEffect() is null;
                    if (useTintEffect)
                    {
                        huskSpriteParams.Effect ??= GameMain.GameScreen.ThresholdTintEffect;
                        huskSpriteParams.Params ??= new Dictionary<string, object>();
                        var parameters = huskSpriteParams.Params;
                        parameters["xCutoffTexture"] = GUI.WhiteTexture;
                        parameters["baseToCutoffSizeRatio"] = 1.0f;
                        spriteBatch.SwapEffect(huskSpriteParams);
                    }
                    DrawWearable(HuskSprite, depthStep, spriteBatch, color, alpha: color.A / 255f, spriteEffect);
                    if (useTintEffect)
                    {
                        spriteBatch.SwapEffect(null);
                    }
                    depthStep += step;
                }
                if (!hideLimb)
                {
                    foreach (WearableSprite wearable in OtherWearables)
                    {
                        if (wearable.Type == WearableType.Husk) { continue; }
                        if (wearableTypesToHide.Contains(wearable.Type)) 
                        {
                            // Draws the short hair
                            if (wearable.Type == WearableType.Hair)
                            {
                                if (HairWithHatSprite != null)
                                {
                                    DrawWearable(HairWithHatSprite, depthStep, spriteBatch, blankColor, alpha: color.A / 255f, spriteEffect);
                                    depthStep += step;
                                }
                            }
                            continue;
                        }
                        DrawWearable(wearable, depthStep, spriteBatch, blankColor, alpha: color.A / 255f, spriteEffect);
                        //if there are multiple sprites on this limb, make the successive ones be drawn in front
                        depthStep += step;
                    }
                }
            }
            foreach (WearableSprite wearable in WearingItems)
            {
                if (onlyDrawable != null && onlyDrawable != wearable && wearable.CanBeHiddenByOtherWearables) { continue; }
                if (wearable.CanBeHiddenByItem.Any())
                {
                    bool hiddenByOtherItem = false;
                    foreach (var otherWearable in WearingItems)
                    {
                        if (otherWearable == wearable) { continue; }
                        if (wearable.CanBeHiddenByItem.Contains(otherWearable.WearableComponent.Item.Prefab.Identifier))
                        {
                            hiddenByOtherItem = true;
                            break;
                        }
                        foreach (Identifier tag in wearable.CanBeHiddenByItem)
                        {
                            if (otherWearable.WearableComponent.Item.HasTag(tag))
                            {
                                hiddenByOtherItem = true;
                                break;
                            }
                        }
                    }
                    if (hiddenByOtherItem) { continue; }
                }
                DrawWearable(wearable, depthStep, spriteBatch, blankColor, alpha: color.A / 255f, spriteEffect);
                //if there are multiple sprites on this limb, make the successive ones be drawn in front
                depthStep += step;
            }
            if (!Hide && onlyDrawable == null)
            {
                foreach (var decorativeSprite in DecorativeSprites)
                {
                    if (!spriteAnimState[decorativeSprite].IsActive) { continue; }
                    Color c = new Color(decorativeSprite.Color.R / 255f * brightness, decorativeSprite.Color.G / 255f * brightness, decorativeSprite.Color.B / 255f * brightness, decorativeSprite.Color.A / 255f);
                    if (deadTimer > 0)
                    {
                        c = Color.Lerp(c, spriteParams.DeadColor, MathUtils.InverseLerp(0, Params.GetSprite().DeadColorTime, deadTimer));
                    }
                    c = overrideColor ?? c;
                    float rotation = decorativeSprite.GetRotation(ref spriteAnimState[decorativeSprite].RotationState, spriteAnimState[decorativeSprite].RandomRotationFactor);
                    Vector2 offset = decorativeSprite.GetOffset(ref spriteAnimState[decorativeSprite].OffsetState, spriteAnimState[decorativeSprite].RandomOffsetMultiplier) * Scale;
                    var ca = (float)Math.Cos(-body.Rotation);
                    var sa = (float)Math.Sin(-body.Rotation);
                    Vector2 transformedOffset = new Vector2(ca * offset.X + sa * offset.Y, -sa * offset.X + ca * offset.Y);
                    decorativeSprite.Sprite.Draw(spriteBatch, new Vector2(body.DrawPosition.X + transformedOffset.X, -(body.DrawPosition.Y + transformedOffset.Y)), c, decorativeSprite.Sprite.Origin,
                        -body.Rotation + rotation, decorativeSprite.GetScale(ref spriteAnimState[decorativeSprite].ScaleState, spriteAnimState[decorativeSprite].RandomScaleFactor) * Scale, spriteEffect,
                        depth: activeSprite.Depth - depthStep);
                    depthStep += step;
                }
                if (damageOverlayStrength > 0.0f && DamagedSprite != null)
                {
                    DamagedSprite.Draw(spriteBatch,
                        new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                        colorWithoutTint * damageOverlayStrength, activeSprite.Origin,
                        -body.DrawRotation,
                        Scale * TextureScale, spriteEffect, activeSprite.Depth - depthStep * Math.Max(1, WearingItems.Count * 2)); // Multiply by 2 to get rid of z-fighting with some clothing combos
                }
            }

            if (GameMain.DebugDraw)
            {
                if (pullJoint != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(pullJoint.WorldAnchorB);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), GUIStyle.Red, true);
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
                    GUI.DrawLine(spriteBatch, from, to, GUIStyle.Red, width: 1);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)from.X, (int)from.Y, 12, 12), Color.White, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)to.X, (int)to.Y, 12, 12), Color.White, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)from.X, (int)from.Y, 10, 10), Color.Blue, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)to.X, (int)to.Y, 10, 10), GUIStyle.Red, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)front.X, (int)front.Y, 10, 10), Color.Yellow, true);

                    //Vector2 mainLimbFront = ConvertUnits.ToDisplayUnits(ragdoll.MainLimb.body.FarseerBody.GetWorldPoint(ragdoll.MainLimb.body.GetFrontLocal(MathHelper.ToRadians(limbParams.Orientation))));
                    //mainLimbFront.Y = -mainLimbFront.Y;
                    //var mainLimbDrawPos = ragdoll.MainLimb.body.DrawPosition;
                    //mainLimbDrawPos.Y = -mainLimbDrawPos.Y;
                    //GUI.DrawLine(spriteBatch, mainLimbDrawPos, mainLimbFront, Color.White, width: 5);
                    //GUI.DrawRectangle(spriteBatch, new Rectangle((int)mainLimbFront.X, (int)mainLimbFront.Y, 10, 10), Color.Yellow, true);
                }
                //DrawDamageModifiers(spriteBatch, cam, bodyDrawPos, isScreenSpace: false);
            }
        }

        public void UpdateWearableTypesToHide()
        {
            alphaClipEffectParams?.Clear();

            wearableTypeHidingSprites.Clear();

            void addWearablesFrom(IReadOnlyList<WearableSprite> wearableSprites)
            {
                if (wearableSprites.Count <= 0) { return; }

                wearableTypeHidingSprites.AddRange(
                    wearableSprites.Where(w => w.HideWearablesOfType.Count > 0));
            }

            addWearablesFrom(WearingItems);
            addWearablesFrom(OtherWearables);

            wearableTypesToHide.Clear();

            if (wearableTypeHidingSprites.Count <= 0) { return; }

            foreach (WearableSprite sprite in wearableTypeHidingSprites)
            {
                wearableTypesToHide.UnionWith(sprite.HideWearablesOfType);
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
                //GUI.DrawLine(spriteBatch, startPos, startPos + Vector2.Normalize(up) * size, GUIStyle.Red, width: width);
                Color color = modifier.DamageMultiplier > 1 ? GUIStyle.Red : GUIStyle.Green;
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

        private (
            Color FinalColor,
            Vector2 Origin,
            float Rotation,
            float Scale,
            float Depth)
            CalculateDrawParameters(WearableSprite wearable, float depthStep, Color color, float alpha)
        {
            var sprite = ActiveSprite;
            if (wearable.InheritSourceRect)
            {
                if (wearable.SheetIndex.HasValue)
                {
                    wearable.Sprite.SourceRect = new Rectangle(CharacterInfo.CalculateOffset(sprite, wearable.SheetIndex.Value), sprite.SourceRect.Size);
                }
                else if (type == LimbType.Head && character.Info != null)
                {
                    wearable.Sprite.SourceRect = new Rectangle(CharacterInfo.CalculateOffset(sprite, character.Info.Head.SheetIndex.ToPoint()), sprite.SourceRect.Size);
                }
                else
                {
                    wearable.Sprite.SourceRect = sprite.SourceRect;
                }
            }

            Vector2 origin;
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
                // Draw outer clothes on top of inner clothes.
                if (wearableItemComponent.AllowedSlots.Contains(InvSlotType.OuterClothes))
                {
                    depth -= depthStep;
                }
                if (wearableItemComponent.AllowedSlots.Contains(InvSlotType.Bag))
                {
                    depth -= depthStep * 4;
                }
                wearableColor = wearableItemComponent.Item.GetSpriteColor();
            }
            else if (character.Info != null)
            {
                if (wearable.Type == WearableType.Hair)
                {
                    wearableColor = character.Info.Head.HairColor;
                }
                else if (wearable.Type == WearableType.Beard || wearable.Type == WearableType.Moustache)
                {
                    wearableColor = character.Info.Head.FacialHairColor;
                }
            }
            float scale = wearable.Scale;
            if (wearable.InheritScale)
            {
                if (!wearable.IgnoreTextureScale)
                {
                    scale *= TextureScale;
                }
                if (!wearable.IgnoreLimbScale)
                {
                    scale *= Params.Scale;
                }
                if (!wearable.IgnoreRagdollScale)
                {
                    scale *= ragdoll.RagdollParams.LimbScale;
                }
            }
            float rotation = -body.DrawRotation - wearable.Rotation * Dir;
            float finalAlpha = alpha * wearableColor.A;
            Color finalColor = color.Multiply(wearableColor);
            finalColor = new Color(finalColor.R, finalColor.G, finalColor.B, (byte)finalAlpha);

            return (finalColor, origin, rotation, scale, depth);
        }

        private static Effect alphaClipEffect;
        private Dictionary<WearableSprite, Dictionary<string, object>> alphaClipEffectParams;
        private void ApplyAlphaClip(SpriteBatch spriteBatch, WearableSprite wearable, WearableSprite alphaClipper, SpriteEffects spriteEffect)
        {
            SpriteRecorder.Command makeCommand(WearableSprite w)
            {
                var (_, origin, rotation, scale, _)
                    = CalculateDrawParameters(w, 0f, Color.White, 0f);

                var command = SpriteRecorder.Command.FromTransform(
                    texture: w.Sprite.Texture,
                    pos: new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                    srcRect: w.Sprite.SourceRect,
                    color: Color.White,
                    rotationRad: rotation,
                    origin: origin,
                    scale: new Vector2(scale, scale),
                    effects: spriteEffect,
                    depth: 0f,
                    index: 0);

                return command;
            }

            void spacesFromCommand(WearableSprite w, SpriteRecorder.Command command, out CoordinateSpace2D textureSpace, out CoordinateSpace2D worldSpace)
            {
                var (topLeft, bottomLeft, topRight) = spriteEffect switch
                {
                    SpriteEffects.None
                        => (command.VertexTL, command.VertexBL, command.VertexTR),
                    SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically
                        => (command.VertexBR, command.VertexTR, command.VertexBL),
                    SpriteEffects.FlipHorizontally
                        => (command.VertexTR, command.VertexBR, command.VertexTL),
                    SpriteEffects.FlipVertically
                        => (command.VertexBL, command.VertexTL, command.VertexBR)
                };

                textureSpace = new CoordinateSpace2D
                {
                    Origin = topLeft.TextureCoordinate,
                    I = topRight.TextureCoordinate - topLeft.TextureCoordinate,
                    J = bottomLeft.TextureCoordinate - topLeft.TextureCoordinate
                };

                worldSpace = new CoordinateSpace2D
                {
                    Origin = topLeft.Position.DiscardZ(),
                    I = topRight.Position.DiscardZ() - topLeft.Position.DiscardZ(),
                    J = bottomLeft.Position.DiscardZ() - topLeft.Position.DiscardZ()
                };
            }

            var wearableCommand = makeCommand(wearable);
            var clipperCommand = makeCommand(alphaClipper);

            spacesFromCommand(wearable, wearableCommand, out var wearableTextureSpace, out var wearableWorldSpace);
            spacesFromCommand(alphaClipper, clipperCommand, out var clipperTextureSpace, out var clipperWorldSpace);

            var wearableUvToClipperUv =
                wearableTextureSpace.CanonicalToLocal
                * wearableWorldSpace.LocalToCanonical
                * clipperWorldSpace.CanonicalToLocal
                * clipperTextureSpace.LocalToCanonical;

            alphaClipEffect ??= EffectLoader.Load("Effects/wearableclip");
            alphaClipEffectParams ??= new Dictionary<WearableSprite, Dictionary<string, object>>();
            if (!alphaClipEffectParams.ContainsKey(wearable)) { alphaClipEffectParams.Add(wearable, new Dictionary<string, object>()); }

            var paramsToPass = new SpriteBatch.EffectWithParams
            {
                Effect = alphaClipEffect,
                Params = alphaClipEffectParams[wearable]
            };

            paramsToPass.Params["wearableUvToClipperUv"] = wearableUvToClipperUv;
            paramsToPass.Params["stencilUVmin"] = new Vector2(
                (float)alphaClipper.Sprite.SourceRect.X / alphaClipper.Sprite.Texture.Width,
                (float)alphaClipper.Sprite.SourceRect.Y / alphaClipper.Sprite.Texture.Height);
            paramsToPass.Params["stencilUVmax"] = new Vector2(
                (float)alphaClipper.Sprite.SourceRect.Right / alphaClipper.Sprite.Texture.Width,
                (float)alphaClipper.Sprite.SourceRect.Bottom / alphaClipper.Sprite.Texture.Height);
            paramsToPass.Params["clipperTexelSize"] = 2f / alphaClipper.Sprite.Texture.Width;
            paramsToPass.Params["aCutoff"] = 2f / 255f;
            paramsToPass.Params["xTexture"] = wearable.Sprite.Texture;
            paramsToPass.Params["xStencil"] = alphaClipper.Sprite.Texture;
            spriteBatch.SwapEffect(paramsToPass);
        }

        private void DrawWearable(WearableSprite wearable, float depthStep, SpriteBatch spriteBatch, Color color, float alpha, SpriteEffects spriteEffect)
        {
            if (wearable.Sprite?.Texture == null) { return; }
            var (finalColor, origin, rotation, scale, depth) = CalculateDrawParameters(wearable, depthStep, color, alpha);
            var prevEffect = spriteBatch.GetCurrentEffect();
            var alphaClipper = WearingItems.Find(w => w.AlphaClipOtherWearables);
            bool shouldApplyAlphaClip = alphaClipper?.Sprite?.Texture != null && wearable != alphaClipper;
            if (shouldApplyAlphaClip)
            {
                ApplyAlphaClip(spriteBatch, wearable, alphaClipper, spriteEffect);
            }
            wearable.Sprite.Draw(spriteBatch, new Vector2(body.DrawPosition.X, -body.DrawPosition.Y), finalColor, origin, rotation, scale, spriteEffect, depth);
            if (shouldApplyAlphaClip)
            {
                spriteBatch.SwapEffect(effect: prevEffect);
            }
        }

        private WearableSprite GetWearableSprite(WearableType type)
        {
            var info = character.Info;
            if (info == null) { return null; }
            ContentXElement element = info.FilterElements(info.Wearables, info.Head.Preset.TagSet, type)?.FirstOrDefault();
            return element != null ? new WearableSprite(element.GetChildElement("sprite"), type) : null;
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

            OtherWearables.ForEach(w => w.Sprite.Remove());
            OtherWearables.Clear();

            HuskSprite?.Sprite?.Remove();
            HuskSprite = null;

            HairWithHatSprite?.Sprite?.Remove();
            HairWithHatSprite = null;

            HerpesSprite?.Sprite?.Remove();
            HerpesSprite = null;

            TintMask?.Remove();
            TintMask = null;
        }
    }
}
