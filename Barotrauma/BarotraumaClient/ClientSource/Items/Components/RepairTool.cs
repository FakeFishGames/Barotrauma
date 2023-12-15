﻿using Barotrauma.Particles;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class RepairTool
#if DEBUG
        : IDrawableComponent
#endif
    {
#if DEBUG
        public Vector2 DrawSize
        {
            get { return GameMain.DebugDraw ? Vector2.One * Range : Vector2.Zero; }
        }
#endif

        private readonly List<ParticleEmitter> particleEmitters = new List<ParticleEmitter>();
        private readonly List<ParticleEmitter> particleEmitterHitStructure = new List<ParticleEmitter>();
        private readonly List<ParticleEmitter> particleEmitterHitCharacter = new List<ParticleEmitter>();
        private readonly List<(RelatedItem relatedItem, ParticleEmitter emitter)> particleEmitterHitItem = new List<(RelatedItem relatedItem, ParticleEmitter emitter)>();

        private float prevProgressBarState;
        private Item prevProgressBarTarget = null;

        partial void InitProjSpecific(ContentXElement element)
        {
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "particleemitter":
                        particleEmitters.Add(new ParticleEmitter(subElement));
                        break;
                    case "particleemitterhititem":
                        Identifier[] identifiers = subElement.GetAttributeIdentifierArray("identifiers", Array.Empty<Identifier>());
                        if (identifiers.Length == 0) { identifiers = subElement.GetAttributeIdentifierArray("identifier", Array.Empty<Identifier>()); }
                        Identifier[] excludedIdentifiers = subElement.GetAttributeIdentifierArray("excludedidentifiers", Array.Empty<Identifier>());
                        if (excludedIdentifiers.Length == 0) { excludedIdentifiers = subElement.GetAttributeIdentifierArray("excludedidentifier", Array.Empty<Identifier>()); }
                        
                        particleEmitterHitItem.Add((new RelatedItem(identifiers, excludedIdentifiers), new ParticleEmitter(subElement)));
                        break;
                    case "particleemitterhitstructure":
                        particleEmitterHitStructure.Add(new ParticleEmitter(subElement));
                        break;
                    case "particleemitterhitcharacter":
                        particleEmitterHitCharacter.Add(new ParticleEmitter(subElement));
                        break;
                }
            }
        }


        partial void UseProjSpecific(float deltaTime, Vector2 raystart)
        {
            foreach (ParticleEmitter particleEmitter in particleEmitters)
            {
                float particleAngle = MathHelper.ToRadians(BarrelRotation);
                if (item.body != null)
                {
                    particleAngle += item.body.Rotation + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                }
                particleEmitter.Emit(
                    deltaTime, ConvertUnits.ToDisplayUnits(raystart),
                    item.CurrentHull, particleAngle, particleEmitter.Prefab.Properties.CopyEntityAngle ? -particleAngle : 0);
            }
        }

        partial void FixStructureProjSpecific(Character user, float deltaTime, Structure targetStructure, int sectionIndex)
        {
            Vector2 progressBarPos = targetStructure.SectionPosition(sectionIndex);
            if (targetStructure.Submarine != null)
            {
                progressBarPos += targetStructure.Submarine.DrawPosition;
            }

            var progressBar = user.UpdateHUDProgressBar(
                targetStructure.ID * 1000 + sectionIndex, //unique "identifier" for each wall section
                progressBarPos,
                MathUtils.InverseLerp(targetStructure.Prefab.MinHealth, targetStructure.Health, targetStructure.Health - targetStructure.SectionDamage(sectionIndex)),
                GUIStyle.Red, GUIStyle.Green);

            if (progressBar != null) progressBar.Size = new Vector2(60.0f, 20.0f);

            Vector2 particlePos = ConvertUnits.ToDisplayUnits(pickedPosition);
            if (targetStructure.Submarine != null) particlePos += targetStructure.Submarine.DrawPosition;
            foreach (var emitter in particleEmitterHitStructure)
            {
                float particleAngle = item.body.Rotation + MathHelper.ToRadians(BarrelRotation) + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                emitter.Emit(deltaTime, particlePos, item.CurrentHull, particleAngle + MathHelper.Pi, -particleAngle + MathHelper.Pi);
            }
        }

        partial void FixCharacterProjSpecific(Character user, float deltaTime, Character targetCharacter)
        {
            Vector2 particlePos = ConvertUnits.ToDisplayUnits(pickedPosition);
            if (targetCharacter.Submarine != null) particlePos += targetCharacter.Submarine.DrawPosition;
            foreach (var emitter in particleEmitterHitCharacter)
            {
                float particleAngle = item.body.Rotation + MathHelper.ToRadians(BarrelRotation) + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                emitter.Emit(deltaTime, particlePos, item.CurrentHull, particleAngle + MathHelper.Pi, -particleAngle + MathHelper.Pi);
            }
        }

        partial void FixItemProjSpecific(Character user, float deltaTime, Item targetItem, bool showProgressBar)
        {
            if (showProgressBar)
            {
                float progressBarState = targetItem.ConditionPercentage / 100.0f;
                if (!MathUtils.NearlyEqual(progressBarState, prevProgressBarState) || prevProgressBarTarget != targetItem)
                {
                    var door = targetItem.GetComponent<Door>();
                    if (door == null || door.Stuck <= 0)
                    {
                        Vector2 progressBarPos = targetItem.DrawPosition;
                        var progressBar = user?.UpdateHUDProgressBar(
                            targetItem,
                            progressBarPos,
                            progressBarState,
                            GUIStyle.Red, GUIStyle.Green,
                            progressBarState < prevProgressBarState ? "progressbar.cutting" : "");
                        if (progressBar != null) { progressBar.Size = new Vector2(60.0f, 20.0f); }
                    }
                    prevProgressBarState = progressBarState;
                    prevProgressBarTarget = targetItem;
                }
            }

            Vector2 particlePos = ConvertUnits.ToDisplayUnits(pickedPosition);
            if (targetItem.Submarine != null) particlePos += targetItem.Submarine.DrawPosition;
            foreach ((RelatedItem relatedItem, ParticleEmitter emitter) in particleEmitterHitItem)
            {
                if (!relatedItem.MatchesItem(targetItem)) { continue; }
                float particleAngle = item.body.Rotation + MathHelper.ToRadians(BarrelRotation) + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                emitter.Emit(deltaTime, particlePos, item.CurrentHull, particleAngle + MathHelper.Pi, -particleAngle + MathHelper.Pi);
            }            
        }
#if DEBUG
        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1, Color? overrideColor = null)
        {
            if (GameMain.DebugDraw && IsActive)
            {
                GUI.DrawLine(spriteBatch, 
                    new Vector2(debugRayStartPos.X, -debugRayStartPos.Y),
                    new Vector2(debugRayEndPos.X, -debugRayEndPos.Y),
                    Color.Yellow, width: 3f);
            }
        }
#endif
    }
}
