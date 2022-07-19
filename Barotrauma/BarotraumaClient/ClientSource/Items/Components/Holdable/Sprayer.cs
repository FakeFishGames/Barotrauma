using Barotrauma.Networking;
using Barotrauma.Particles;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{

    partial class Sprayer : RangedWeapon, IDrawableComponent
    {
#if DEBUG
        private Vector2 debugRayStartPos, debugRayEndPos;
#endif

        public Vector2 DrawSize
        {
            get { return Vector2.Zero; }
        }

        private readonly List<ParticleEmitter> particleEmitters = new List<ParticleEmitter>();
        private Hull targetHull;

        private Vector2 rayStartWorldPosition;

        private Color color;

        partial void InitProjSpecific(ContentXElement element)
        {
            currentCrossHairPointerScale = element.GetAttributeFloat("crosshairscale", 0.1f);

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "particleemitter":
                        particleEmitters.Add(new ParticleEmitter(subElement));
                        break;
                }
            }
        }

        private readonly List<BackgroundSection> targetSections = new List<BackgroundSection>();

        // 0 = 1x1, 1 = 2x2, 2 = 3x3
        private int spraySetting = 0;
        private readonly Point[] sprayArray = new Point[8];

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            if (character == null || !character.IsKeyDown(InputType.Aim)) return;

            if (PlayerInput.KeyHit(InputType.PreviousFireMode))
            {
                if (spraySetting > 0)
                {
                    spraySetting--;
                }
                else
                {
                    spraySetting = 2;
                }

                targetSections.Clear();
            }

            if (PlayerInput.KeyHit(InputType.NextFireMode))
            {
                if (spraySetting < 2)
                {
                    spraySetting++;
                }
                else
                {
                    spraySetting = 0;
                }

                targetSections.Clear();
            }

            crosshairPointerPos = PlayerInput.MousePosition;

            Vector2 rayStart;
            Vector2 sourcePos = character?.AnimController == null ? item.SimPosition : character.AnimController.AimSourceSimPos;
            Vector2 barrelPos = item.SimPosition + TransformedBarrelPos;
            //make sure there's no obstacles between the base of the item (or the shoulder of the character) and the end of the barrel
            if (Submarine.PickBody(sourcePos, barrelPos, collisionCategory: Physics.CollisionItem | Physics.CollisionItemBlocking | Physics.CollisionWall) == null)
            {
                //no obstacles -> we start the raycast at the end of the barrel
                rayStart = ConvertUnits.ToSimUnits(item.WorldPosition) + TransformedBarrelPos;
            }
            else
            {
                targetHull = null;
                targetSections.Clear();
                return;
            }

            Vector2 pos = character.CursorWorldPosition;
            Vector2 rayEnd = ConvertUnits.ToSimUnits(pos);
            rayStartWorldPosition = ConvertUnits.ToDisplayUnits(rayStart);

            if (Vector2.Distance(rayStartWorldPosition, pos) > Range)
            {
                targetHull = null;
                targetSections.Clear();
                return;
            }

#if DEBUG
            debugRayStartPos = ConvertUnits.ToDisplayUnits(rayStart);
            debugRayEndPos = ConvertUnits.ToDisplayUnits(rayEnd);
#endif

            Submarine parentSub = character.Submarine ?? item.Submarine;
            if (parentSub != null)
            {
                rayStart -= parentSub.SimPosition;
                rayEnd -= parentSub.SimPosition;
            }

            var obstacles = Submarine.PickBodies(rayStart, rayEnd, collisionCategory: Physics.CollisionItem | Physics.CollisionItemBlocking | Physics.CollisionWall);
            foreach (var body in obstacles)
            {
                if (body.UserData is Item item)
                {
                    var door = item.GetComponent<Door>();
                    if (door != null && door.CanBeTraversed) { continue; }
                }

                targetHull = null;
                targetSections.Clear();
                return;
            }

            targetHull = Hull.GetCleanTarget(pos);
            if (targetHull == null)
            {
                targetSections.Clear();
                return;
            }

            BackgroundSection mousedOverSection = targetHull.GetBackgroundSection(pos);

            if (mousedOverSection == null)
            {
                targetSections.Clear();
                return;
            }

            // No need to refresh
            if (targetSections.Count > 0 && mousedOverSection == targetSections[0])
            {
                return;
            }

            targetSections.Clear();

            targetSections.Add(mousedOverSection);
            int mousedOverIndex = mousedOverSection.Index;

            // Start with 2x2
            if (spraySetting > 0)
            {
                sprayArray[0].X = mousedOverIndex + 1;
                sprayArray[0].Y = mousedOverSection.RowIndex;

                sprayArray[1].X = mousedOverIndex + targetHull.xBackgroundMax;
                sprayArray[1].Y = mousedOverSection.RowIndex + 1;

                sprayArray[2].X = sprayArray[1].X + 1;
                sprayArray[2].Y = sprayArray[1].Y;

                for (int i = 0; i < 3; i++)
                {
                    if (targetHull.DoesSectionMatch(sprayArray[i].X, sprayArray[i].Y))
                    {
                        targetSections.Add(targetHull.BackgroundSections[sprayArray[i].X]);
                    }
                }

                // Add more if it's 3x3
                if (spraySetting == 2)
                {
                    sprayArray[3].X = mousedOverIndex - 1;
                    sprayArray[3].Y = mousedOverSection.RowIndex;

                    sprayArray[4].X = sprayArray[1].X - 1;
                    sprayArray[4].Y = sprayArray[1].Y;

                    sprayArray[5].X = sprayArray[3].X - targetHull.xBackgroundMax;
                    sprayArray[5].Y = sprayArray[3].Y - 1;

                    sprayArray[6].X = sprayArray[5].X + 1;
                    sprayArray[6].Y = sprayArray[5].Y;

                    sprayArray[7].X = sprayArray[6].X + 1;
                    sprayArray[7].Y = sprayArray[6].Y;

                    for (int i = 3; i < sprayArray.Length; i++)
                    {
                        if (targetHull.DoesSectionMatch(sprayArray[i].X, sprayArray[i].Y))
                        {
                            targetSections.Add(targetHull.BackgroundSections[sprayArray[i].X]);
                        }
                    }
                }
            }
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (character == null || !character.IsKeyDown(InputType.Aim)) { return; }
            GUI.HideCursor = targetSections.Count > 0;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null) { return false; }
            if (character == Character.Controlled)
            {
                if (targetSections.Count == 0) { return false; }           
                Spray(deltaTime);
                return true;
            }
            else
            {
                //allow remote players to use the sprayer, but don't actually color the walls (we'll receive the data from the server)
                return character.IsRemotePlayer;
            }
        }

        public void Spray(float deltaTime)
        {
            if (targetSections.Count == 0) { return; }

            Item liquidItem = liquidContainer?.Inventory.FirstOrDefault();
            if (liquidItem == null) { return; }

            bool isCleaning = false;
            liquidColors.TryGetValue(liquidItem.Prefab.Identifier, out color);

            // Ethanol or other cleaning solvent
            if (color.A == 0) { isCleaning = true; }

            float sizeAdjustedSprayStrength = SprayStrength / targetSections.Count;

            if (!isCleaning)
            {
                for (int i = 0; i < targetSections.Count; i++)
                {
                    targetHull.IncreaseSectionColorOrStrength(targetSections[i], color, sizeAdjustedSprayStrength * deltaTime, true, false);
                }
                if (GameMain.GameSession != null)
                {
                    GameMain.GameSession.TimeSpentCleaning += deltaTime;
                }
            }
            else
            {
                for (int i = 0; i < targetSections.Count; i++)
                {
                    targetHull.CleanSection(targetSections[i], -sizeAdjustedSprayStrength * deltaTime, true);
                }
                if (GameMain.GameSession != null)
                {
                    GameMain.GameSession.TimeSpentPainting += deltaTime;
                }
            }

            Vector2 particleStartPos = item.WorldPosition + ConvertUnits.ToDisplayUnits(TransformedBarrelPos);
            Vector2 particleEndPos = Vector2.Zero;
            for (int i = 0; i < targetSections.Count; i++)
            {
                particleEndPos += new Vector2(targetSections[i].Rect.Center.X, targetSections[i].Rect.Y - targetSections[i].Rect.Height / 2) + targetHull.Rect.Location.ToVector2();
            }
            particleEndPos /= targetSections.Count;
            if (targetHull?.Submarine != null)
            {
                particleEndPos += targetHull.Submarine.Position;
            }
            float dist = Vector2.Distance(particleStartPos, particleEndPos);

            foreach (ParticleEmitter particleEmitter in particleEmitters)
            {
                float particleAngle = item.body.Rotation + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                float particleRange = particleEmitter.Prefab.Properties.VelocityMax * particleEmitter.Prefab.ParticlePrefab.LifeTime;
                particleEmitter.Emit(
                    deltaTime, particleStartPos,
                    item.CurrentHull, particleAngle, particleEmitter.Prefab.Properties.CopyEntityAngle ? -particleAngle : 0, velocityMultiplier: dist / particleRange * 1.5f,
                    colorMultiplier: new Color(color.R, color.G, color.B, (byte)255));
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
#if DEBUG
            if (GameMain.DebugDraw && Character.Controlled != null && Character.Controlled.IsKeyDown(InputType.Aim))
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(debugRayStartPos.X, -debugRayStartPos.Y),
                    new Vector2(debugRayEndPos.X, -debugRayEndPos.Y),
                    Color.Yellow);
            }
#endif
            if (Character.Controlled == null || !Character.Controlled.HasEquippedItem(item) || !Character.Controlled.IsKeyDown(InputType.Aim) || targetHull == null || targetSections.Count == 0) return;

            Vector2 drawOffset = targetHull.Submarine == null ? Vector2.Zero : targetHull.Submarine.DrawPosition;
            Point sectionSize = targetSections[0].Rect.Size;
            Rectangle drawPositionRect = new Rectangle((int)(drawOffset.X + targetHull.Rect.X), (int)(drawOffset.Y + targetHull.Rect.Y), sectionSize.X, sectionSize.Y);

            if (crosshairSprite == null && crosshairPointerSprite == null)
            {
                for (int i = 0; i < targetSections.Count; i++)
                {
                    GUI.DrawRectangle(spriteBatch, new Vector2(drawPositionRect.X + targetSections[i].Rect.X, -(drawPositionRect.Y + targetSections[i].Rect.Y)), new Vector2(sectionSize.X, sectionSize.Y), Color.White, false, 0.0f, 1);
                }
            }
            else if (targetSections.Count > 0)
            {
                Vector2 drawPos = Vector2.Zero;
                for (int i = 0; i < targetSections.Count; i++)
                {
                    drawPos += new Vector2(drawPositionRect.X + targetSections[i].Rect.X + sectionSize.X / 2, -(drawPositionRect.Y + targetSections[i].Rect.Y - sectionSize.Y / 2));
                }
                drawPos /= targetSections.Count;
                crosshairSprite?.Draw(spriteBatch, drawPos, scale: sectionSize.X * 3 / crosshairSprite.size.X);
                crosshairPointerSprite?.Draw(spriteBatch, drawPos, scale: sectionSize.X * (spraySetting + 1) / crosshairPointerSprite.size.X);
            }
        }
    }
}
