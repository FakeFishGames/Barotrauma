using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Voronoi2;

namespace Barotrauma
{
    class LevelWallVertexBuffer : IDisposable
    {
        public VertexBuffer WallEdgeBuffer, WallBuffer;
        public readonly Texture2D WallTexture, EdgeTexture;
        private VertexPositionColorTexture[] wallVertices;
        private VertexPositionColorTexture[] wallEdgeVertices;

        public bool IsDisposed
        {
            get;
            private set;
        }

        public LevelWallVertexBuffer(VertexPositionTexture[] wallVertices, VertexPositionTexture[] wallEdgeVertices, Texture2D wallTexture, Texture2D edgeTexture, Color color)
        {
            if (wallVertices.Length == 0)
            {
                throw new ArgumentException("Failed to instantiate a LevelWallVertexBuffer (no wall vertices).");
            }
            if (wallVertices.Length == 0)
            {
                throw new ArgumentException("Failed to instantiate a LevelWallVertexBuffer (no wall edge vertices).");
            }
            this.wallVertices = LevelRenderer.GetColoredVertices(wallVertices, color);
            WallBuffer = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, wallVertices.Length, BufferUsage.WriteOnly);
            WallBuffer.SetData(this.wallVertices);
            WallTexture = wallTexture;

            this.wallEdgeVertices = LevelRenderer.GetColoredVertices(wallEdgeVertices, color);
            WallEdgeBuffer = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, wallEdgeVertices.Length, BufferUsage.WriteOnly);
            WallEdgeBuffer.SetData(this.wallEdgeVertices);
            EdgeTexture = edgeTexture;
        }

        public void Append(VertexPositionTexture[] wallVertices, VertexPositionTexture[] wallEdgeVertices, Color color)
        {
            WallBuffer.Dispose();
            WallBuffer = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, this.wallVertices.Length + wallVertices.Length, BufferUsage.WriteOnly);
            int originalWallVertexCount = this.wallVertices.Length;
            Array.Resize(ref this.wallVertices, originalWallVertexCount + wallVertices.Length);
            Array.Copy(LevelRenderer.GetColoredVertices(wallVertices, color), 0, this.wallVertices, originalWallVertexCount, wallVertices.Length);
            WallBuffer.SetData(this.wallVertices);

            WallEdgeBuffer.Dispose();
            WallEdgeBuffer = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, this.wallEdgeVertices.Length + wallEdgeVertices.Length, BufferUsage.WriteOnly);
            int originalWallEdgeVertexCount = this.wallEdgeVertices.Length;
            Array.Resize(ref this.wallEdgeVertices, originalWallEdgeVertexCount + wallEdgeVertices.Length);
            Array.Copy(LevelRenderer.GetColoredVertices(wallEdgeVertices, color), 0, this.wallEdgeVertices, originalWallEdgeVertexCount, wallEdgeVertices.Length);
            WallEdgeBuffer.SetData(this.wallEdgeVertices);
        }

        public void Dispose()
        {
            IsDisposed = true;
            WallEdgeBuffer?.Dispose();
            WallBuffer?.Dispose();
        }
    }

    class LevelRenderer : IDisposable
    {
        private static BasicEffect wallEdgeEffect, wallCenterEffect;

        private Vector2 waterParticleOffset;
        private Vector2 waterParticleVelocity;

        private float flashCooldown;
        private float flashTimer;
        public Color FlashColor { get; private set; }

        private readonly RasterizerState cullNone;

        private readonly Level level;

        private readonly List<LevelWallVertexBuffer> vertexBuffers = new List<LevelWallVertexBuffer>();

        private float chromaticAberrationStrength;
        public float ChromaticAberrationStrength
        {
            get { return chromaticAberrationStrength; }
            set { chromaticAberrationStrength = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }
        public float CollapseEffectStrength
        {
            get;
            set;
        }
        public Vector2 CollapseEffectOrigin
        {
            get;
            set;
        }


        public LevelRenderer(Level level)
        {
            cullNone = new RasterizerState() { CullMode = CullMode.None };

            if (wallEdgeEffect == null)
            {
                wallEdgeEffect = new BasicEffect(GameMain.Instance.GraphicsDevice)
                {
                    DiffuseColor = new Vector3(0.8f, 0.8f, 0.8f),
                    VertexColorEnabled = true,
                    TextureEnabled = true,
                    Texture = level.GenerationParams.WallEdgeSprite.Texture
                };
                wallEdgeEffect.CurrentTechnique = wallEdgeEffect.Techniques["BasicEffect_Texture"];
            }

            if (wallCenterEffect == null)
            {
                wallCenterEffect = new BasicEffect(GameMain.Instance.GraphicsDevice)
                {
                    VertexColorEnabled = true,
                    TextureEnabled = true,
                    Texture = level.GenerationParams.WallSprite.Texture
                };
                wallCenterEffect.CurrentTechnique = wallCenterEffect.Techniques["BasicEffect_Texture"];
            }
                
            this.level = level;
        }

        public void ReloadTextures()
        {
            level.GenerationParams.WallEdgeSprite.ReloadTexture();
            wallEdgeEffect.Texture = level.GenerationParams.WallEdgeSprite.Texture;
            level.GenerationParams.WallSprite.ReloadTexture();
            wallCenterEffect.Texture = level.GenerationParams.WallSprite.Texture;
        }

        public void Flash()
        {
            flashTimer = 1.0f;
        }
        
        public void Update(float deltaTime, Camera cam)
        {
            if (CollapseEffectStrength > 0.0f)
            {
                CollapseEffectStrength = Math.Max(0.0f, CollapseEffectStrength - deltaTime);
            }
            if (ChromaticAberrationStrength > 0.0f)
            {
                ChromaticAberrationStrength = Math.Max(0.0f, ChromaticAberrationStrength - deltaTime * 10.0f);
            }

            if (level.GenerationParams.FlashInterval.Y > 0)
            {
                flashCooldown -= deltaTime;
                if (flashCooldown <= 0.0f)
                {
                    flashTimer = 1.0f;
                    if (level.GenerationParams.FlashSound != null)
                    {
                        level.GenerationParams.FlashSound.Play(1.0f, "default");
                    }
                    flashCooldown = Rand.Range(level.GenerationParams.FlashInterval.X, level.GenerationParams.FlashInterval.Y, Rand.RandSync.Unsynced);
                }
                if (flashTimer > 0.0f)
                {
                    float brightness = flashTimer * 1.1f - PerlinNoise.GetPerlin((float)Timing.TotalTime, (float)Timing.TotalTime * 0.66f) * 0.1f;
                    FlashColor = level.GenerationParams.FlashColor.Multiply(MathHelper.Clamp(brightness, 0.0f, 1.0f));
                    flashTimer -= deltaTime * 0.5f;
                }
                else
                {
                    FlashColor = Color.TransparentBlack;
                }
            }

            //calculate the sum of the forces of nearby level triggers
            //and use it to move the water texture and water distortion effect
            Vector2 currentWaterParticleVel = level.GenerationParams.WaterParticleVelocity;
            foreach (LevelObject levelObject in level.LevelObjectManager.GetVisibleObjects())
            {
                if (levelObject.Triggers == null) { continue; }
                //use the largest water flow velocity of all the triggers
                Vector2 objectMaxFlow = Vector2.Zero;
                foreach (LevelTrigger trigger in levelObject.Triggers)
                {
                    Vector2 vel = trigger.GetWaterFlowVelocity(cam.WorldViewCenter);
                    if (vel.LengthSquared() > objectMaxFlow.LengthSquared())
                    {
                        objectMaxFlow = vel;
                    }
                }
                currentWaterParticleVel += objectMaxFlow;
            }

            waterParticleVelocity = Vector2.Lerp(waterParticleVelocity, currentWaterParticleVel, deltaTime);
            
            WaterRenderer.Instance?.ScrollWater(waterParticleVelocity, deltaTime);

            if (level.GenerationParams.WaterParticles != null)
            {
                Vector2 waterTextureSize = level.GenerationParams.WaterParticles.size * level.GenerationParams.WaterParticleScale;
                waterParticleOffset += new Vector2(waterParticleVelocity.X, -waterParticleVelocity.Y) * level.GenerationParams.WaterParticleScale * deltaTime;
                while (waterParticleOffset.X <= -waterTextureSize.X) { waterParticleOffset.X += waterTextureSize.X; }
                while (waterParticleOffset.X >= waterTextureSize.X){ waterParticleOffset.X -= waterTextureSize.X; }
                while (waterParticleOffset.Y <= -waterTextureSize.Y) { waterParticleOffset.Y += waterTextureSize.Y; }
                while (waterParticleOffset.Y >= waterTextureSize.Y) { waterParticleOffset.Y -= waterTextureSize.Y; }
            }
        }

        public static VertexPositionColorTexture[] GetColoredVertices(VertexPositionTexture[] vertices, Color color)
        {
            VertexPositionColorTexture[] verts = new VertexPositionColorTexture[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                verts[i] = new VertexPositionColorTexture(vertices[i].Position, color, vertices[i].TextureCoordinate);
            }
            return verts;
        }

        public void SetVertices(VertexPositionTexture[] wallVertices, VertexPositionTexture[] wallEdgeVertices, Texture2D wallTexture, Texture2D edgeTexture, Color color)
        {
            var existingBuffer = vertexBuffers.Find(vb => vb.WallTexture == wallTexture && vb.EdgeTexture == edgeTexture);
            if (existingBuffer != null)
            {
                existingBuffer.Append(wallVertices, wallEdgeVertices,color);
            }
            else
            {
                vertexBuffers.Add(new LevelWallVertexBuffer(wallVertices, wallEdgeVertices, wallTexture, edgeTexture, color));
            }
        }

        public void DrawBackground(SpriteBatch spriteBatch, Camera cam,
            LevelObjectManager backgroundSpriteManager = null,
            BackgroundCreatureManager backgroundCreatureManager = null)
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearWrap);

            Vector2 backgroundPos = cam.WorldViewCenter;

            backgroundPos.Y = -backgroundPos.Y;
            backgroundPos *= 0.05f;

            if (level.GenerationParams.BackgroundTopSprite != null)
            {
                int backgroundSize = (int)level.GenerationParams.BackgroundTopSprite.size.Y;
                if (backgroundPos.Y < backgroundSize)
                {
                    if (backgroundPos.Y < 0)
                    {
                        var backgroundTop = level.GenerationParams.BackgroundTopSprite;
                        backgroundTop.SourceRect = new Rectangle((int)backgroundPos.X, (int)backgroundPos.Y, backgroundSize, (int)Math.Min(-backgroundPos.Y, backgroundSize));
                        backgroundTop.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(GameMain.GraphicsWidth, Math.Min(-backgroundPos.Y, GameMain.GraphicsHeight)),
                            color: level.BackgroundTextureColor);
                    }
                    if (-backgroundPos.Y < GameMain.GraphicsHeight && level.GenerationParams.BackgroundSprite != null)
                    {
                        var background = level.GenerationParams.BackgroundSprite;
                        background.SourceRect = new Rectangle((int)backgroundPos.X, (int)Math.Max(backgroundPos.Y, 0), backgroundSize, backgroundSize);
                        background.DrawTiled(spriteBatch,
                            (backgroundPos.Y < 0) ? new Vector2(0.0f, (int)-backgroundPos.Y) : Vector2.Zero,
                            new Vector2(GameMain.GraphicsWidth, (int)Math.Min(Math.Ceiling(backgroundSize - backgroundPos.Y), backgroundSize)),
                            color: level.BackgroundTextureColor);
                    }
                }
            }

            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.NonPremultiplied,
                SamplerState.LinearWrap, DepthStencilState.DepthRead, null, null,
                cam.Transform);            

            backgroundSpriteManager?.DrawObjectsBack(spriteBatch, cam);
            if (cam.Zoom > 0.05f)
            {
                backgroundCreatureManager?.Draw(spriteBatch, cam);
            }

            if (level.GenerationParams.WaterParticles != null && cam.Zoom > 0.05f)
            {
                float textureScale = level.GenerationParams.WaterParticleScale;

                Rectangle srcRect = new Rectangle(0, 0, 2048, 2048);
                Vector2 origin = new Vector2(cam.WorldView.X, -cam.WorldView.Y);
                Vector2 offset = -origin + waterParticleOffset;
                while (offset.X <= -srcRect.Width * textureScale) offset.X += srcRect.Width * textureScale;
                while (offset.X > 0.0f) offset.X -= srcRect.Width * textureScale;
                while (offset.Y <= -srcRect.Height * textureScale) offset.Y += srcRect.Height * textureScale;
                while (offset.Y > 0.0f) offset.Y -= srcRect.Height * textureScale;
                for (int i = 0; i < 4; i++)
                {
                    float scale = (1.0f - i * 0.2f);

                    //alpha goes from 1.0 to 0.0 when scale is in the range of 0.1 - 0.05
                    float alpha = (cam.Zoom * scale) < 0.1f ? (cam.Zoom * scale - 0.05f) * 20.0f : 1.0f;
                    if (alpha <= 0.0f) continue;

                    Vector2 offsetS = offset * scale
                        + new Vector2(cam.WorldView.Width, cam.WorldView.Height) * (1.0f - scale) * 0.5f
                        - new Vector2(256.0f * i);

                    float texScale = scale * textureScale;

                    while (offsetS.X <= -srcRect.Width * texScale) offsetS.X += srcRect.Width * texScale;
                    while (offsetS.X > 0.0f) offsetS.X -= srcRect.Width * texScale;
                    while (offsetS.Y <= -srcRect.Height * texScale) offsetS.Y += srcRect.Height * texScale;
                    while (offsetS.Y > 0.0f) offsetS.Y -= srcRect.Height * texScale;

                    level.GenerationParams.WaterParticles.DrawTiled(
                        spriteBatch, origin + offsetS, 
                        new Vector2(cam.WorldView.Width - offsetS.X, cam.WorldView.Height - offsetS.Y), 
                        color: level.GenerationParams.WaterParticleColor * alpha, textureScale: new Vector2(texScale));                    
                }
            }
            spriteBatch.End();

            RenderWalls(GameMain.Instance.GraphicsDevice, cam);

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.NonPremultiplied,
                SamplerState.LinearClamp, DepthStencilState.DepthRead, null, null,
                cam.Transform);
            backgroundSpriteManager?.DrawObjectsMid(spriteBatch, cam);
            spriteBatch.End();
        }

        public void DrawForeground(SpriteBatch spriteBatch, Camera cam, LevelObjectManager backgroundSpriteManager = null)
        {
            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.NonPremultiplied,
                SamplerState.LinearClamp, DepthStencilState.DepthRead, null, null,
                cam.Transform);
            backgroundSpriteManager?.DrawObjectsFront(spriteBatch, cam);
            spriteBatch.End();
        }

        public void DrawDebugOverlay(SpriteBatch spriteBatch, Camera cam)
        {
            if (GameMain.DebugDraw && cam.Zoom > 0.1f)
            {
                var cells = level.GetCells(cam.WorldViewCenter, 2);
                foreach (VoronoiCell cell in cells)
                {
                    GUI.DrawRectangle(spriteBatch, new Vector2(cell.Center.X - 10.0f, -cell.Center.Y - 10.0f), new Vector2(20.0f, 20.0f), Color.Cyan, true);

                    GUI.DrawLine(spriteBatch,
                        new Vector2(cell.Edges[0].Point1.X + cell.Translation.X, -(cell.Edges[0].Point1.Y + cell.Translation.Y)),
                        new Vector2(cell.Center.X, -(cell.Center.Y)),
                        Color.Blue * 0.5f);

                    foreach (GraphEdge edge in cell.Edges)
                    {
                        GUI.DrawLine(spriteBatch, new Vector2(edge.Point1.X + cell.Translation.X, -(edge.Point1.Y + cell.Translation.Y)),
                            new Vector2(edge.Point2.X + cell.Translation.X, -(edge.Point2.Y + cell.Translation.Y)), edge.NextToCave ? Color.Red : (cell.Body == null ? Color.Cyan * 0.5f : (edge.IsSolid ? Color.White : Color.Gray)),
                            width: edge.NextToCave ? 8 : 1);
                    }

                    foreach (Vector2 point in cell.BodyVertices)
                    {
                        GUI.DrawRectangle(spriteBatch, new Vector2(point.X + cell.Translation.X, -(point.Y + cell.Translation.Y)), new Vector2(10.0f, 10.0f), Color.White, true);
                    }
                }

                /*foreach (List<Point> nodeList in level.SmallTunnels)
                {
                    for (int i = 1; i < nodeList.Count; i++)
                    {
                        GUI.DrawLine(spriteBatch,
                            new Vector2(nodeList[i - 1].X, -nodeList[i - 1].Y),
                            new Vector2(nodeList[i].X, -nodeList[i].Y),
                            Color.Lerp(Color.Yellow, GUIStyle.Red, i / (float)nodeList.Count), 0, 10);
                    }
                }*/

                foreach (var abyssIsland in level.AbyssIslands)
                {
                    GUI.DrawRectangle(spriteBatch, new Vector2(abyssIsland.Area.X, -abyssIsland.Area.Y - abyssIsland.Area.Height), abyssIsland.Area.Size.ToVector2(), Color.Cyan, thickness: 5);
                }

                foreach (var ruin in level.Ruins)
                {
                    ruin.DebugDraw(spriteBatch);
                }
            }

            Vector2 pos = new Vector2(0.0f, -level.Size.Y);
            if (cam.WorldView.Y >= -pos.Y - 1024)
            {
                int topBarrierWidth = level.GenerationParams.WallEdgeSprite.Texture.Width;
                int topBarrierHeight = level.GenerationParams.WallEdgeSprite.Texture.Height;

                pos.X = cam.WorldView.X - topBarrierWidth;
                int width = (int)(Math.Ceiling(cam.WorldView.Width / 1024 + 4.0f) * topBarrierWidth);

                GUI.DrawRectangle(spriteBatch, new Rectangle(
                    (int)MathUtils.Round(pos.X, topBarrierWidth),
                    -cam.WorldView.Y,
                    width,
                    (int)(cam.WorldView.Y + pos.Y) - 60),
                    Color.Black, true);

                spriteBatch.Draw(level.GenerationParams.WallEdgeSprite.Texture,
                    new Rectangle((int)MathUtils.Round(pos.X, topBarrierWidth), (int)(pos.Y - topBarrierHeight + level.GenerationParams.WallEdgeExpandOutwardsAmount), width, topBarrierHeight),
                    new Rectangle(0, 0, width, -topBarrierHeight),
                    GameMain.LightManager?.LightingEnabled ?? false ? GameMain.LightManager.AmbientLight : level.WallColor, 0.0f,
                    Vector2.Zero,
                    SpriteEffects.None, 0.0f);
            }

            if (cam.WorldView.Y - cam.WorldView.Height < level.SeaFloorTopPos + 1024)
            {
                int bottomBarrierWidth = level.GenerationParams.WallEdgeSprite.Texture.Width;
                int bottomBarrierHeight = level.GenerationParams.WallEdgeSprite.Texture.Height;
                pos = new Vector2(cam.WorldView.X - bottomBarrierWidth, -level.BottomPos);
                int width = (int)(Math.Ceiling(cam.WorldView.Width / bottomBarrierWidth + 4.0f) * bottomBarrierWidth);

                GUI.DrawRectangle(spriteBatch, new Rectangle(
                    (int)(MathUtils.Round(pos.X, bottomBarrierWidth)),
                    -(level.BottomPos - 60),
                    width,
                    level.BottomPos - (cam.WorldView.Y - cam.WorldView.Height)),
                    Color.Black, true);

                spriteBatch.Draw(level.GenerationParams.WallEdgeSprite.Texture,
                    new Rectangle((int)MathUtils.Round(pos.X, bottomBarrierWidth), -level.BottomPos - (int)level.GenerationParams.WallEdgeExpandOutwardsAmount, width, bottomBarrierHeight),
                    new Rectangle(0, 0, width, -bottomBarrierHeight),
                    GameMain.LightManager?.LightingEnabled ?? false ? GameMain.LightManager.AmbientLight : level.WallColor, 0.0f,
                    Vector2.Zero,
                    SpriteEffects.FlipVertically, 0.0f);
            }
        }


        public void RenderWalls(GraphicsDevice graphicsDevice, Camera cam)
        {
            if (!vertexBuffers.Any()) { return; }

            var defaultRasterizerState = graphicsDevice.RasterizerState;

            Matrix transformMatrix = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 100) * 0.5f;

            graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            graphicsDevice.RasterizerState = cullNone;

            //render destructible walls
            for (int i = 0; i < 2; i++)
            {
                var wallList = i == 0 ? level.ExtraWalls : level.UnsyncedExtraWalls;
                foreach (LevelWall wall in wallList)
                {
                    if (!(wall is DestructibleLevelWall destructibleWall) || destructibleWall.Destroyed) { continue; }

                    wallCenterEffect.Texture = level.GenerationParams.DestructibleWallSprite?.Texture ?? level.GenerationParams.WallSprite.Texture;
                    wallCenterEffect.World = wall.GetTransform() * transformMatrix;
                    wallCenterEffect.Alpha = wall.Alpha;
                    wallCenterEffect.CurrentTechnique.Passes[0].Apply();
                    graphicsDevice.SetVertexBuffer(wall.WallBuffer);
                    graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wall.WallBuffer.VertexCount / 3.0f));

                    if (destructibleWall.Damage > 0.0f)
                    {
                        wallCenterEffect.Texture = level.GenerationParams.WallSpriteDestroyed.Texture;
                        wallCenterEffect.Alpha = MathHelper.Lerp(0.2f, 1.0f, destructibleWall.Damage / destructibleWall.MaxHealth) * wall.Alpha;
                        wallCenterEffect.CurrentTechnique.Passes[0].Apply();
                        graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wall.WallEdgeBuffer.VertexCount / 3.0f));
                    }

                    wallEdgeEffect.Texture = level.GenerationParams.DestructibleWallEdgeSprite?.Texture ?? level.GenerationParams.WallEdgeSprite.Texture;
                    wallEdgeEffect.World = wall.GetTransform() * transformMatrix;
                    wallEdgeEffect.Alpha = wall.Alpha;
                    wallEdgeEffect.CurrentTechnique.Passes[0].Apply();
                    graphicsDevice.SetVertexBuffer(wall.WallEdgeBuffer);
                    graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wall.WallEdgeBuffer.VertexCount / 3.0f));
                }
            }

            wallEdgeEffect.Alpha = 1.0f;
            wallCenterEffect.Alpha = 1.0f;

            wallCenterEffect.World = transformMatrix;
            wallEdgeEffect.World = transformMatrix;

            //render static walls
            foreach (var vertexBuffer in vertexBuffers)
            {
                wallCenterEffect.Texture = vertexBuffer.WallTexture;
                wallCenterEffect.CurrentTechnique.Passes[0].Apply();
                graphicsDevice.SetVertexBuffer(vertexBuffer.WallBuffer);
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(vertexBuffer.WallBuffer.VertexCount / 3.0f));

                wallEdgeEffect.Texture = vertexBuffer.EdgeTexture;
                wallEdgeEffect.CurrentTechnique.Passes[0].Apply();
                graphicsDevice.SetVertexBuffer(vertexBuffer.WallEdgeBuffer);
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(vertexBuffer.WallEdgeBuffer.VertexCount / 3.0f));
            }

            wallCenterEffect.Texture = level.GenerationParams.WallSprite.Texture;
            wallEdgeEffect.Texture = level.GenerationParams.WallEdgeSprite.Texture;

            //render non-destructible extra walls
            for (int i = 0; i < 2; i++)
            {
                var wallList = i == 0 ? level.ExtraWalls : level.UnsyncedExtraWalls;
                foreach (LevelWall wall in wallList)
                {
                    if (wall is DestructibleLevelWall) { continue; }
                    //TODO: use LevelWallVertexBuffers for extra walls as well
                    wallCenterEffect.World = wall.GetTransform() * transformMatrix;
                    wallCenterEffect.Alpha = wall.Alpha;
                    wallCenterEffect.CurrentTechnique.Passes[0].Apply();
                    graphicsDevice.SetVertexBuffer(wall.WallBuffer);
                    graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wall.WallBuffer.VertexCount / 3.0f));

                    wallEdgeEffect.World = wall.GetTransform() * transformMatrix;
                    wallEdgeEffect.Alpha = wall.Alpha;
                    wallEdgeEffect.CurrentTechnique.Passes[0].Apply();
                    graphicsDevice.SetVertexBuffer(wall.WallEdgeBuffer);
                    graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wall.WallEdgeBuffer.VertexCount / 3.0f));
                }
            }

            graphicsDevice.RasterizerState = defaultRasterizerState;
        }

        public void Dispose()
        {
            foreach (var vertexBuffer in vertexBuffers)
            {
                vertexBuffer.Dispose();
            }
            vertexBuffers.Clear();
        }
    }
}
