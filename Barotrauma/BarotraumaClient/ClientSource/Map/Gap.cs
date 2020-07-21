using FarseerPhysics;
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
                return ShowGaps;
            }
        }

        public override bool IsVisible(Rectangle worldView)
        {
            return Screen.Selected == GameMain.SubEditorScreen || GameMain.DebugDraw;
        }

        public override void Draw(SpriteBatch sb, bool editing, bool back = true)
        {
            if (GameMain.DebugDraw && Screen.Selected.Cam.Zoom > 0.1f)
            {
                Vector2 center = new Vector2(WorldRect.X + rect.Width / 2.0f, -(WorldRect.Y - rect.Height / 2.0f));
                GUI.DrawLine(sb, center, center + new Vector2(flowForce.X, -flowForce.Y) / 10.0f, GUI.Style.Red);
                GUI.DrawLine(sb, center + Vector2.One * 5.0f, center + new Vector2(lerpedFlowForce.X, -lerpedFlowForce.Y) / 10.0f + Vector2.One * 5.0f, GUI.Style.Orange);

                if (outsideCollisionBlocker.Enabled && Submarine != null)
                {
                    var edgeShape = outsideCollisionBlocker.FixtureList[0].Shape as FarseerPhysics.Collision.Shapes.EdgeShape;
                    Vector2 startPos = ConvertUnits.ToDisplayUnits(outsideCollisionBlocker.GetWorldPoint(edgeShape.Vertex1)) + Submarine.Position;
                    Vector2 endPos = ConvertUnits.ToDisplayUnits(outsideCollisionBlocker.GetWorldPoint(edgeShape.Vertex2)) + Submarine.Position;
                    startPos.Y = -startPos.Y;
                    endPos.Y = -endPos.Y;
                    GUI.DrawLine(sb, startPos, endPos, Color.Gray, 0, 5);
                }
            }

            if (!editing || !ShowGaps) { return; }

            Color clr = (open == 0.0f) ? GUI.Style.Red : Color.Cyan;
            if (IsHighlighted) clr = Color.Gold;

            float depth = (ID % 255) * 0.000001f;

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
                    Vector2 dir = IsHorizontal ?
                        new Vector2(Math.Sign(linkedTo[i].Rect.Center.X - rect.Center.X), 0.0f)
                        : new Vector2(0.0f, Math.Sign((rect.Y - rect.Height / 2.0f) - (linkedTo[i].Rect.Y - linkedTo[i].Rect.Height / 2.0f)));

                    Vector2 arrowPos = new Vector2(WorldRect.Center.X, -(WorldRect.Y - WorldRect.Height / 2));
                    arrowPos += new Vector2(dir.X * (WorldRect.Width / 2), dir.Y * (WorldRect.Height / 2));

                    float arrowWidth = 32.0f;
                    float arrowSize = 15.0f;

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
                            new Vector2(Math.Min(rect.Height, arrowWidth) / GUI.Arrow.size.X, arrowSize / GUI.Arrow.size.Y) :
                            new Vector2(Math.Min(rect.Width, arrowWidth) / GUI.Arrow.size.X, arrowSize / GUI.Arrow.size.Y),
                        SpriteEffects.None, depth);
                }
            }

            if (IsSelected)
            {
                GUI.DrawRectangle(sb,
                    new Vector2(WorldRect.X - 5, -WorldRect.Y - 5),
                    new Vector2(rect.Width + 10, rect.Height + 10),
                    GUI.Style.Red,
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
                if (hull1.linkedTo.Any(h => h.linkedTo.Contains(hull1) && h.linkedTo.Contains(hull2))) { return; }
                if (hull2.linkedTo.Any(h => h.linkedTo.Contains(hull1) && h.linkedTo.Contains(hull2))) { return; }
            }

            Vector2 pos = Position;
            if (IsHorizontal)
            {
                pos.X += Math.Sign(flowForce.X);
                pos.Y = MathHelper.Clamp(Rand.Range(higherSurface, lowerSurface), rect.Y - rect.Height, rect.Y);
            }
            else
            {
                pos.Y += Math.Sign(flowForce.Y) * rect.Height / 2.0f;
            }

            //spawn less particles when there's already a large number of them
            float particleAmountMultiplier = 1.0f - GameMain.ParticleManager.ParticleCount / (float)GameMain.ParticleManager.MaxParticles;
            particleAmountMultiplier *= particleAmountMultiplier;

            //light dripping
            if (open < 0.2f && LerpedFlowForce.LengthSquared() > 100.0f)
            {
                particleTimer += deltaTime;
                float particlesPerSec = open * 100.0f * particleAmountMultiplier;
                float emitInterval = 1.0f / particlesPerSec;
                while (particleTimer > emitInterval)
                {
                    Vector2 velocity = flowForce;
                    if (!IsHorizontal)
                    {
                        velocity.X = Rand.Range(-500.0f, 500.0f) * open;
                    }
                    else
                    {
                        velocity.X *= Rand.Range(1.0f, 3.0f);
                    }

                    if (flowTargetHull.WaterVolume < flowTargetHull.Volume)
                    {
                        GameMain.ParticleManager.CreateParticle(
                            Rand.Range(0.0f, open) < 0.05f ? "waterdrop" : "watersplash",
                            (Submarine == null ? pos : pos + Submarine.Position),
                            velocity, 0, flowTargetHull);
                    }

                    GameMain.ParticleManager.CreateParticle(
                        "bubbles",
                        (Submarine == null ? pos : pos + Submarine.Position),
                        velocity, 0, flowTargetHull);
                    
                    particleTimer -= emitInterval;
                }
            }
            //heavy flow -> strong waterfall type of particles
            else if (LerpedFlowForce.LengthSquared() > 20000.0f)
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
                                particle.Size *= Math.Min(Math.Abs(flowForce.X / 500.0f), 5.0f);
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
                    if (Math.Sign(flowTargetHull.Rect.Y - rect.Y) != Math.Sign(lerpedFlowForce.Y)) return;

                    float particlesPerSec = open * rect.Width * 0.3f * particleAmountMultiplier;
                    float emitInterval = 1.0f / particlesPerSec;
                    while (particleTimer > emitInterval)
                    {
                        pos.X = Rand.Range(rect.X, rect.X + rect.Width);
                        Vector2 velocity = new Vector2(
                            lerpedFlowForce.X * Rand.Range(0.5f, 0.7f),
                            MathHelper.Clamp(lerpedFlowForce.Y, -500.0f, 1000.0f) * Rand.Range(0.5f, 0.7f));

                        if (flowTargetHull.WaterVolume < flowTargetHull.Volume * 0.95f)
                        {
                            var splash = GameMain.ParticleManager.CreateParticle(
                            "watersplash",
                            Submarine == null ? pos : pos + Submarine.Position,
                            velocity, 0, FlowTargetHull);
                            if (splash != null) splash.Size = splash.Size * MathHelper.Clamp(rect.Width / 50.0f, 0.8f, 4.0f);
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
            else
            {
                particleTimer = 0.0f;
            }
        }
    }
}
