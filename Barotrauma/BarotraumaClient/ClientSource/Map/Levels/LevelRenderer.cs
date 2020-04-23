using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Voronoi2;

namespace Barotrauma
{
    class LevelRenderer : IDisposable
    {
        private static BasicEffect wallEdgeEffect, wallCenterEffect;

        private Vector2 dustOffset;
        private Vector2 defaultDustVelocity;
        private Vector2 dustVelocity;

        private RasterizerState cullNone;

        private Level level;

        private VertexBuffer wallVertices, bodyVertices;

        public LevelRenderer(Level level)
        {
            defaultDustVelocity = Vector2.UnitY * 10.0f;

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
        
        public void Update(float deltaTime, Camera cam)
        {
            //calculate the sum of the forces of nearby level triggers
            //and use it to move the dust texture and water distortion effect
            Vector2 currentDustVel = defaultDustVelocity;
            foreach (LevelObject levelObject in level.LevelObjectManager.GetVisibleObjects())
            {
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
                currentDustVel += objectMaxFlow;
            }
            
            dustVelocity = Vector2.Lerp(dustVelocity, currentDustVel, deltaTime);
            
            WaterRenderer.Instance?.ScrollWater(dustVelocity, deltaTime);

            if (level.GenerationParams.WaterParticles != null)
            {
                Vector2 waterTextureSize = level.GenerationParams.WaterParticles.size * level.GenerationParams.WaterParticleScale;
                dustOffset += new Vector2(dustVelocity.X, -dustVelocity.Y) * level.GenerationParams.WaterParticleScale * deltaTime;
                while (dustOffset.X <= -waterTextureSize.X) dustOffset.X += waterTextureSize.X;
                while (dustOffset.X >= waterTextureSize.X) dustOffset.X -= waterTextureSize.X;
                while (dustOffset.Y <= -waterTextureSize.Y) dustOffset.Y += waterTextureSize.Y;
                while (dustOffset.Y >= waterTextureSize.Y) dustOffset.Y -= waterTextureSize.Y;
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

        public void SetWallVertices(VertexPositionTexture[] vertices, Color color)
        {
            wallVertices = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            wallVertices.SetData(GetColoredVertices(vertices, color));
        }

        public void SetBodyVertices(VertexPositionTexture[] vertices, Color color)
        {
            bodyVertices = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            bodyVertices.SetData(GetColoredVertices(vertices, color));
        }

        public void SetWallVertices(VertexPositionColorTexture[] vertices)
        {
            wallVertices = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length,BufferUsage.WriteOnly);
            wallVertices.SetData(vertices);
        }

        public void SetBodyVertices(VertexPositionColorTexture[] vertices)
        {
            bodyVertices = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            bodyVertices.SetData(vertices);
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

            if (backgroundSpriteManager != null) backgroundSpriteManager.DrawObjects(spriteBatch, cam, drawFront: false);
            if (backgroundCreatureManager != null) backgroundCreatureManager.Draw(spriteBatch, cam);

            if (level.GenerationParams.WaterParticles != null)
            {
                float textureScale = level.GenerationParams.WaterParticleScale;

                Rectangle srcRect = new Rectangle(0, 0, 2048, 2048);
                Vector2 origin = new Vector2(cam.WorldView.X, -cam.WorldView.Y);
                Vector2 offset = -origin + dustOffset;
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
                        rect: srcRect, color: Color.White * alpha, textureScale: new Vector2(texScale));                    
                }
            }


            spriteBatch.End();

            RenderWalls(GameMain.Instance.GraphicsDevice, cam, specular: false);

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.NonPremultiplied,
                SamplerState.LinearClamp, DepthStencilState.DepthRead, null, null,
                cam.Transform);
            if (backgroundSpriteManager != null) backgroundSpriteManager.DrawObjects(spriteBatch, cam, drawFront: true);
            spriteBatch.End();
        }

        public void Draw(SpriteBatch spriteBatch, Camera cam)
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
                            new Vector2(edge.Point2.X + cell.Translation.X, -(edge.Point2.Y + cell.Translation.Y)), cell.Body == null ? Color.Cyan * 0.5f : Color.White);
                    }

                    foreach (Vector2 point in cell.BodyVertices)
                    {
                        GUI.DrawRectangle(spriteBatch, new Vector2(point.X + cell.Translation.X, -(point.Y + cell.Translation.Y)), new Vector2(10.0f, 10.0f), Color.White, true);
                    }
                }

                foreach (List<Point> nodeList in level.SmallTunnels)
                {
                    for (int i = 1; i < nodeList.Count; i++)
                    {
                        GUI.DrawLine(spriteBatch,
                            new Vector2(nodeList[i - 1].X, -nodeList[i - 1].Y),
                            new Vector2(nodeList[i].X, -nodeList[i].Y),
                            Color.Lerp(Color.Yellow, GUI.Style.Red, i / (float)nodeList.Count), 0, 10);
                    }
                }

                foreach (var ruin in level.Ruins)
                {
                    ruin.DebugDraw(spriteBatch);
                }
            }

            Vector2 pos = new Vector2(0.0f, -level.Size.Y);

            if (cam.WorldView.Y >= -pos.Y - 1024)
            {
                pos.X = cam.WorldView.X -1024;
                int width = (int)(Math.Ceiling(cam.WorldView.Width / 1024 + 4.0f) * 1024);

                GUI.DrawRectangle(spriteBatch,new Rectangle(
                    (int)(MathUtils.Round(pos.X, 1024)), 
                    -cam.WorldView.Y, 
                    width, 
                    (int)(cam.WorldView.Y + pos.Y) - 30),
                    Color.Black, true);

                spriteBatch.Draw(level.GenerationParams.WallEdgeSprite.Texture,
                    new Rectangle((int)(MathUtils.Round(pos.X, 1024)), (int)pos.Y-1000, width, 1024),
                    new Rectangle(0, 0, width, -1024),
                    level.BackgroundTextureColor, 0.0f,
                    Vector2.Zero,
                    SpriteEffects.None, 0.0f);
            }

            if (cam.WorldView.Y - cam.WorldView.Height < level.SeaFloorTopPos + 1024)
            {
                pos = new Vector2(cam.WorldView.X - 1024, -level.BottomPos);

                int width = (int)(Math.Ceiling(cam.WorldView.Width / 1024 + 4.0f) * 1024);

                GUI.DrawRectangle(spriteBatch, new Rectangle(
                    (int)(MathUtils.Round(pos.X, 1024)), 
                    (int)-(level.BottomPos - 30), 
                    width, 
                    (int)(level.BottomPos - (cam.WorldView.Y - cam.WorldView.Height))), 
                    Color.Black, true);

                spriteBatch.Draw(level.GenerationParams.WallEdgeSprite.Texture,
                    new Rectangle((int)(MathUtils.Round(pos.X, 1024)), (int)-level.BottomPos, width, 1024),
                    new Rectangle(0, 0, width, -1024),
                    level.BackgroundTextureColor, 0.0f,
                    Vector2.Zero,
                    SpriteEffects.FlipVertically, 0.0f);
            }
        }


        public void RenderWalls(GraphicsDevice graphicsDevice, Camera cam, bool specular)
        {
            if (wallVertices == null) return;

            bool renderLevel = cam.WorldView.Y >= 0.0f;
            bool renderSeaFloor = cam.WorldView.Y - cam.WorldView.Height < level.SeaFloorTopPos + 1024;

            if (!renderLevel && !renderSeaFloor) return;

            Matrix transformMatrix = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 100) * 0.5f;

            wallEdgeEffect.Texture = specular && level.GenerationParams.WallEdgeSpriteSpecular != null ?
                level.GenerationParams.WallEdgeSpriteSpecular.Texture :
                level.GenerationParams.WallEdgeSprite.Texture;
            wallEdgeEffect.World = transformMatrix;
            wallCenterEffect.Texture = specular && level.GenerationParams.WallSpriteSpecular != null ?
                level.GenerationParams.WallSpriteSpecular.Texture :
                level.GenerationParams.WallSprite.Texture;
            wallCenterEffect.World = transformMatrix;
            
            graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            wallCenterEffect.CurrentTechnique.Passes[0].Apply();

            if (renderLevel)
            {
                graphicsDevice.SetVertexBuffer(bodyVertices);
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(bodyVertices.VertexCount / 3.0f));
            }

            foreach (LevelWall wall in level.ExtraWalls)
            {
                if (!renderSeaFloor && wall == level.SeaFloor) continue;
                wallCenterEffect.World = wall.GetTransform() * transformMatrix;
                wallCenterEffect.CurrentTechnique.Passes[0].Apply();
                graphicsDevice.SetVertexBuffer(wall.BodyVertices);
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wall.BodyVertices.VertexCount / 3.0f));
            }

            var defaultRasterizerState = graphicsDevice.RasterizerState;
            graphicsDevice.RasterizerState = cullNone;
            wallEdgeEffect.World = transformMatrix;
            wallEdgeEffect.CurrentTechnique.Passes[0].Apply();

            if (renderLevel)
            {
                wallEdgeEffect.CurrentTechnique.Passes[0].Apply();
                graphicsDevice.SetVertexBuffer(wallVertices);
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wallVertices.VertexCount / 3.0f));
            }
            foreach (LevelWall wall in level.ExtraWalls)
            {
                if (!renderSeaFloor && wall == level.SeaFloor) continue;
                wallEdgeEffect.World = wall.GetTransform() * transformMatrix;
                wallEdgeEffect.CurrentTechnique.Passes[0].Apply();
                graphicsDevice.SetVertexBuffer(wall.WallVertices);
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wall.WallVertices.VertexCount / 3.0f));
            }
            graphicsDevice.RasterizerState = defaultRasterizerState;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (wallVertices != null) wallVertices.Dispose();
            if (bodyVertices != null) bodyVertices.Dispose();
        }
    }
}
