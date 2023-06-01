﻿using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class Gap : MapEntity
    {
        private float particleTimer;

        public override bool SelectableInEditor
        {
            get
            {
                return ShowGaps && SubEditorScreen.IsLayerVisible(this);
            }
        }

        public override bool IsVisible(Rectangle worldView)
        {
            return Screen.Selected == GameMain.SubEditorScreen || GameMain.DebugDraw;
        }

        public override void Draw(SpriteBatch sb, bool editing, bool back = true)
        {
            float depth = (ID % 255) * 0.000001f;

            if (GameMain.DebugDraw && Screen.Selected.Cam.Zoom > 0.1f)
            {
                if (FlowTargetHull != null)
                {
                    DrawArrow(FlowTargetHull, IsHorizontal ? rect.Height: rect.Width, Math.Abs(lerpedFlowForce.Length()), Color.Red * 0.3f);
                }

                if (Submarine != null && outsideCollisionBlocker != null && outsideCollisionBlocker.Enabled)
                {
                    var edgeShape = outsideCollisionBlocker.FixtureList[0].Shape as FarseerPhysics.Collision.Shapes.EdgeShape;
                    Vector2 startPos = ConvertUnits.ToDisplayUnits(outsideCollisionBlocker.GetWorldPoint(edgeShape.Vertex1)) + Submarine.Position;
                    Vector2 endPos = ConvertUnits.ToDisplayUnits(outsideCollisionBlocker.GetWorldPoint(edgeShape.Vertex2)) + Submarine.Position;
                    startPos.Y = -startPos.Y;
                    endPos.Y = -endPos.Y;
                    GUI.DrawLine(sb, startPos, endPos, Color.Gray, 0, 5);
                }
            }

            if (!editing || !ShowGaps || !SubEditorScreen.IsLayerVisible(this)) { return; }

            Color clr = (open == 0.0f) ? GUIStyle.Red : Color.Cyan;
            if (IsHighlighted) { clr = Color.Gold; }

            GUI.DrawRectangle(
                sb, new Rectangle(WorldRect.X, -WorldRect.Y, rect.Width, rect.Height),
                clr * 0.2f, true, depth);

            int lineWidth = 5;
            if (IsHorizontal)
            {
                GUI.DrawLine(sb,
                    new Vector2(WorldRect.X, -WorldRect.Y + lineWidth / 2),
                    new Vector2(WorldRect.Right, -WorldRect.Y + lineWidth / 2),
                    clr * 0.6f, width: lineWidth);
                GUI.DrawLine(sb,
                    new Vector2(WorldRect.X, -WorldRect.Y + rect.Height - lineWidth / 2),
                    new Vector2(WorldRect.Right, -WorldRect.Y + rect.Height - lineWidth / 2),
                    clr * 0.6f, width: lineWidth);
            }
            else
            {
                GUI.DrawLine(sb,
                    new Vector2(WorldRect.X + lineWidth / 2, -WorldRect.Y),
                    new Vector2(WorldRect.X + lineWidth / 2, -WorldRect.Y + rect.Height),
                    clr * 0.6f, width: lineWidth);
                GUI.DrawLine(sb,
                    new Vector2(WorldRect.Right - lineWidth / 2, -WorldRect.Y),
                    new Vector2(WorldRect.Right - lineWidth / 2, -WorldRect.Y + rect.Height),
                    clr * 0.6f, width: lineWidth);
            }

            if (linkedTo.Count != 2 || linkedTo[0] != linkedTo[1])
            {
                for (int i = 0; i < linkedTo.Count; i++)
                {
                    if (linkedTo[i] is Hull hull)
                    {
                        DrawArrow(hull, 32.0f, 15f, clr);
                    }
                }
            }

            void DrawArrow(Hull targetHull, float arrowWidth, float arrowLength, Color clr)
            {
                Vector2 dir = IsHorizontal ?
                    new Vector2(Math.Sign(targetHull.Rect.Center.X - rect.Center.X), 0.0f)
                    : new Vector2(0.0f, Math.Sign((rect.Y - rect.Height / 2.0f) - (targetHull.Rect.Y - targetHull.Rect.Height / 2.0f)));

                Vector2 arrowPos = new Vector2(WorldRect.Center.X, -(WorldRect.Y - WorldRect.Height / 2));
                arrowPos += new Vector2(dir.X * (WorldRect.Width / 2), dir.Y * (WorldRect.Height / 2));

                bool invalidDir = false;
                if (dir == Vector2.Zero)
                {
                    invalidDir = true;
                    dir = IsHorizontal ? Vector2.UnitX : Vector2.UnitY;
                }

                GUI.Arrow.Draw(sb,
                    arrowPos, invalidDir ? Color.Red : clr * 0.8f,
                    GUI.Arrow.Origin, MathUtils.VectorToAngle(dir) + MathHelper.PiOver2,
                    IsHorizontal ?
                        new Vector2(Math.Min(rect.Height, arrowWidth) / GUI.Arrow.size.X, arrowLength / GUI.Arrow.size.Y) :
                        new Vector2(Math.Min(rect.Width, arrowWidth) / GUI.Arrow.size.X, arrowLength / GUI.Arrow.size.Y),
                    SpriteEffects.None, depth);
            }

            if (IsSelected)
            {
                GUI.DrawRectangle(sb,
                    new Vector2(WorldRect.X - 5, -WorldRect.Y - 5),
                    new Vector2(rect.Width + 10, rect.Height + 10),
                    GUIStyle.Red,
                    false,
                    depth,
                    (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));
            }
        }

        partial void EmitParticles(float deltaTime)
        {
            if (flowTargetHull == null) { return; }

            if (linkedTo.Count == 2 && linkedTo[0] is Hull hull1 && linkedTo[1] is Hull hull2)
            {
                //no flow particles between linked hulls (= rooms consisting of multiple hulls)
                if (hull1.linkedTo.Contains(hull2)) { return; }
                foreach (var linkedEntity in hull1.linkedTo)
                {
                    if (linkedEntity is Hull h && h.linkedTo.Contains(hull1) && h.linkedTo.Contains(hull2)) { return; }
                }
                foreach (var linkedEntity in hull2.linkedTo)
                {
                    if (linkedEntity is Hull h && h.linkedTo.Contains(hull1) && h.linkedTo.Contains(hull2)) { return; }
                }
            }

            Vector2 pos = Position;
            if (IsHorizontal)
            {
                pos.X += Math.Sign(flowForce.X);
                pos.Y = MathHelper.Clamp(Rand.Range(higherSurface, lowerSurface), rect.Y - rect.Height, rect.Y);
            }
            if (flowTargetHull != null)
            {
                pos.X = MathHelper.Clamp(pos.X, flowTargetHull.Rect.X + 1, flowTargetHull.Rect.Right - 1);
                pos.Y = MathHelper.Clamp(pos.Y, flowTargetHull.Rect.Y - flowTargetHull.Rect.Height + 1, flowTargetHull.Rect.Y - 1);
            }

            //spawn less particles when there's already a large number of them
            float particleAmountMultiplier = 1.0f - GameMain.ParticleManager.ParticleCount / (float)GameMain.ParticleManager.MaxParticles;
            particleAmountMultiplier *= particleAmountMultiplier;

            //heavy flow -> strong waterfall type of particles
            if (LerpedFlowForce.LengthSquared() > 20000.0f)
            {
                particleTimer += deltaTime;
                if (IsHorizontal)
                {
                    float particlesPerSec = open * rect.Height * 0.1f * particleAmountMultiplier;
                    if (openedTimer > 0.0f) { particlesPerSec *= 1.0f + openedTimer * 10.0f; }
                    float emitInterval = 1.0f / particlesPerSec;
                    while (particleTimer > emitInterval)
                    {
                        Vector2 velocity = new Vector2(
                            MathHelper.Clamp(flowForce.X, -5000.0f, 5000.0f) * Rand.Range(0.5f, 0.7f),
                        flowForce.Y * Rand.Range(0.5f, 0.7f));

                        if (flowTargetHull.WaterVolume < flowTargetHull.Volume * 0.95f)
                        {
                            var particle = GameMain.ParticleManager.CreateParticle(
                                "watersplash",
                                (Submarine == null ? pos : pos + Submarine.Position) - Vector2.UnitY * Rand.Range(0.0f, 10.0f),
                                velocity, 0, flowTargetHull);
                            if (particle != null)
                            {
                                if (particle.CurrentHull == null) { GameMain.ParticleManager.RemoveParticle(particle); }
                                particle.Size *= Math.Min(Math.Abs(flowForce.X / 500.0f), 5.0f);
                            }
                            if (GapSize() <= Structure.WallSectionSize || !IsRoomToRoom)
                            {
                                CreateWaterSpatter();
                            }
                        }

                        if (Math.Abs(flowForce.X) > 300.0f && flowTargetHull.WaterVolume > flowTargetHull.Volume * 0.1f)
                        {
                            pos.X += Math.Sign(flowForce.X) * 10.0f;
                            if (rect.Height < 32)
                            {
                                pos.Y = rect.Y - rect.Height / 2;
                            }
                            else
                            {
                                float bottomY = rect.Y - rect.Height + 16;
                                float topY = MathHelper.Clamp(lowerSurface, bottomY, rect.Y - 16);
                                pos.Y = Rand.Range(bottomY, topY);
                            }
                            GameMain.ParticleManager.CreateParticle(
                                "bubbles",
                                Submarine == null ? pos : pos + Submarine.Position,
                                velocity, 0, flowTargetHull);
                        }
                        particleTimer -= emitInterval;
                    }
                }
                else
                {
                    if (Math.Sign(flowTargetHull.Rect.Y - rect.Y) != Math.Sign(lerpedFlowForce.Y)) { return; }

                    float particlesPerSec = Math.Max(open * rect.Width * particleAmountMultiplier, 10.0f);
                    float emitInterval = 1.0f / particlesPerSec;
                    while (particleTimer > emitInterval)
                    {
                        pos.X = Rand.Range(rect.X, rect.X + rect.Width + 1);
                        Vector2 velocity = new Vector2(
                            lerpedFlowForce.X * Rand.Range(0.5f, 0.7f),
                            MathHelper.Clamp(lerpedFlowForce.Y, -500.0f, 1000.0f) * Rand.Range(0.5f, 0.7f));

                        if (flowTargetHull.WaterVolume < flowTargetHull.Volume * 0.95f)
                        {
                            var splash = GameMain.ParticleManager.CreateParticle(
                                "watersplash",
                                Submarine == null ? pos : pos + Submarine.Position,
                                velocity, 0, FlowTargetHull);
                            if (splash != null) 
                            {
                                if (splash.CurrentHull == null) { GameMain.ParticleManager.RemoveParticle(splash); }
                                splash.Size *= MathHelper.Clamp(rect.Width / 50.0f, 1.5f, 4.0f);
                            }
                            if (GapSize() <= Structure.WallSectionSize || !IsRoomToRoom)
                            {
                                CreateWaterSpatter();
                            }
                        }
                        if (Math.Abs(flowForce.Y) > 190.0f && Rand.Range(0.0f, 1.0f) < 0.3f && flowTargetHull.WaterVolume > flowTargetHull.Volume * 0.1f)
                        {
                            GameMain.ParticleManager.CreateParticle(
                                "bubbles",
                                Submarine == null ? pos : pos + Submarine.Position,
                                flowForce / 2.0f, 0, FlowTargetHull);
                        }
                        particleTimer -= emitInterval;
                    }
                }
            }
            //light dripping
            else if (LerpedFlowForce.LengthSquared() > 100.0f && 
                /*no dripping from large gaps between rooms (looks bad)*/
                ((GapSize() <= Structure.WallSectionSize) || !IsRoomToRoom))
            {
                particleTimer += deltaTime; 
                float particlesPerSec = open * 10.0f * particleAmountMultiplier;
                float emitInterval = 1.0f / particlesPerSec;
                while (particleTimer > emitInterval)
                {
                    Vector2 velocity = flowForce;
                    if (!IsHorizontal)
                    {
                        velocity.X *= Rand.Range(1.0f, 3.0f);
                    }

                    if (flowTargetHull.WaterVolume < flowTargetHull.Volume)
                    {
                        GameMain.ParticleManager.CreateParticle(
                            Rand.Range(0.0f, open) < 0.05f ? "waterdrop" : "watersplash",
                            Submarine == null ? pos : pos + Submarine.Position,
                            velocity, 0, flowTargetHull);
                        CreateWaterSpatter();
                    }

                    GameMain.ParticleManager.CreateParticle(
                        "bubbles",
                        (Submarine == null ? pos : pos + Submarine.Position),
                        velocity, 0, flowTargetHull);

                    particleTimer -= emitInterval;
                }
            }
            else
            {
                particleTimer = 0.0f;
            }

            void CreateWaterSpatter()
            {
                Vector2 spatterPos = pos;
                float rotation;
                if (IsHorizontal)
                {
                    rotation = LerpedFlowForce.X > 0 ? 0 : MathHelper.Pi;
                    spatterPos.Y = rect.Y - rect.Height / 2;
                }
                else
                {
                    rotation = LerpedFlowForce.Y > 0 ? -MathHelper.PiOver2 : MathHelper.PiOver2;
                    spatterPos.X = rect.Center.X;
                }
                var spatter = GameMain.ParticleManager.CreateParticle(
                    "waterspatter",
                    Submarine == null ? spatterPos : spatterPos + Submarine.Position,
                    Vector2.Zero, rotation, flowTargetHull);
                if (spatter != null)
                {
                    if (spatter.CurrentHull == null) { GameMain.ParticleManager.RemoveParticle(spatter); }
                    spatter.Size *= MathHelper.Clamp(LerpedFlowForce.Length() / 200.0f, 0.5f, 1.0f);
                }                
            }

            float GapSize()
            {
                return IsHorizontal ? rect.Height : rect.Width;
            }
        }

        public override void UpdateEditing(Camera cam, float deltaTime)
        {
            if (editingHUD == null || editingHUD.UserData != this)
            {
                editingHUD = CreateEditingHUD();
            }
        }
        private GUIComponent CreateEditingHUD(bool inGame = false)
        {

            editingHUD = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.15f), GUI.Canvas, Anchor.CenterRight) { MinSize = new Point(400, 0) })
            {
                UserData = this
            };

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), editingHUD.RectTransform, Anchor.Center))
            {
                Stretch = true,
                AbsoluteSpacing = (int)(GUI.Scale * 5)
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), TextManager.Get("entityname.gap"), font: GUIStyle.LargeFont);
            var hiddenInGameTickBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 1.0f), paddedFrame.RectTransform), TextManager.Get("sp.hiddeningame.name"))
            {
                Selected = HiddenInGame
            };
            hiddenInGameTickBox.OnSelected += (GUITickBox tickbox) =>
            {
                HiddenInGame = tickbox.Selected;
                return true;
            };
            editingHUD.RectTransform.Resize(new Point(
                editingHUD.Rect.Width,
                (int)(paddedFrame.Children.Sum(c => c.Rect.Height + paddedFrame.AbsoluteSpacing) / paddedFrame.RectTransform.RelativeSize.Y * 1.25f)));

            PositionEditingHUD();

            return editingHUD;
        }
    }
}
