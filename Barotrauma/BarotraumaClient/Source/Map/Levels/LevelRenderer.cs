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

        private static Sprite background, backgroundTop;
        private static Sprite dustParticles;
        private static Texture2D shaftTexture;

        Vector2 dustOffset;

        private Level level;

        private VertexBuffer wallVertices, bodyVertices;

        public static Sprite Background
        {
            get
            {
                if (background == null) background = new Sprite("Content/Map/background2.png", Vector2.Zero);
                return background;
            }
        }

        public static Sprite BackgroundTop
        {
            get
            {
                if (backgroundTop == null) backgroundTop = new Sprite("Content/Map/background.png", Vector2.Zero);
                return backgroundTop;
            }
        }

        public LevelRenderer(Level level)
        {
            if (shaftTexture == null) shaftTexture = TextureLoader.FromFile("Content/Map/iceWall.png");

            if (background == null) background = new Sprite("Content/Map/background2.png", Vector2.Zero);
            if (backgroundTop == null) backgroundTop = new Sprite("Content/Map/background.png", Vector2.Zero);
            if (dustParticles == null) dustParticles = new Sprite("Content/Map/dustparticles.png", Vector2.Zero);

            if (wallEdgeEffect == null)
            {
                wallEdgeEffect = new BasicEffect(GameMain.Instance.GraphicsDevice)
                {
                    DiffuseColor = new Vector3(0.8f, 0.8f, 0.8f),
                    VertexColorEnabled = true,
                    TextureEnabled = true,
                    Texture = shaftTexture
                };
                wallEdgeEffect.CurrentTechnique = wallEdgeEffect.Techniques["BasicEffect_Texture"];
            }

            if (wallCenterEffect == null)
            {
                wallCenterEffect = new BasicEffect(GameMain.Instance.GraphicsDevice)
                {
                    VertexColorEnabled = true,
                    TextureEnabled = true,
                    Texture = backgroundTop.Texture
                };
                wallCenterEffect.CurrentTechnique = wallCenterEffect.Techniques["BasicEffect_Texture"];
            }
                
            this.level = level;
        }
        
        public void Update(float deltaTime)
        {
            dustOffset -= Vector2.UnitY * 100.0f * deltaTime;
            while (dustOffset.Y <= -2048.0f) dustOffset.Y += 2048.0f;
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
            BackgroundSpriteManager backgroundSpriteManager = null, 
            BackgroundCreatureManager backgroundCreatureManager = null)
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearWrap);

            Vector2 backgroundPos = cam.WorldViewCenter;
            
            backgroundPos.Y = -backgroundPos.Y;
            backgroundPos *= 0.05f;

            if (backgroundPos.Y < 1024)
            {
                if (backgroundPos.Y < 0)
                {
                    backgroundTop.SourceRect = new Rectangle((int)backgroundPos.X, (int)backgroundPos.Y, 1024, (int)Math.Min(-backgroundPos.Y, 1024));
                    backgroundTop.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(GameMain.GraphicsWidth, Math.Min(-backgroundPos.Y, GameMain.GraphicsHeight)),
                        Vector2.Zero, level.BackgroundColor);
                }
                if (backgroundPos.Y > -1024)
                {
                    background.SourceRect = new Rectangle((int)backgroundPos.X, (int)Math.Max(backgroundPos.Y, 0), 1024, 1024);
                    background.DrawTiled(spriteBatch,
                        (backgroundPos.Y < 0) ? new Vector2(0.0f, (int)-backgroundPos.Y) : Vector2.Zero,
                        new Vector2(GameMain.GraphicsWidth, (int)Math.Ceiling(1024 - backgroundPos.Y)),
                        Vector2.Zero, level.BackgroundColor);
                }
            }

            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearWrap, DepthStencilState.Default, null, null,
                cam.Transform);
            

            if (backgroundSpriteManager != null) backgroundSpriteManager.DrawSprites(spriteBatch, cam);
            if (backgroundCreatureManager != null) backgroundCreatureManager.Draw(spriteBatch);

            Rectangle srcRect = new Rectangle(0, 0, 2048, 2048);
            Vector2 origin = new Vector2(cam.WorldView.X, -cam.WorldView.Y);
            Vector2 offset = -origin + dustOffset;
            while (offset.X <= -srcRect.Width) offset.X += srcRect.Width;
            while (offset.X > 0.0f) offset.X -= srcRect.Width;
            while (offset.Y <= -srcRect.Height) offset.Y += srcRect.Height;
            while (offset.Y > 0.0f) offset.Y -= srcRect.Height;
            for (int i = 0; i < 4; i++)
            {
                float scale = 1.0f - i * 0.2f;
                float recipScale = 1.0f / scale;

                //alpha goes from 1.0 to 0.0 when scale is in the range of 0.5-0.25
                float alpha = (cam.Zoom * scale) < 0.5f ? (cam.Zoom * scale - 0.25f) * 40.0f : 1.0f;
                if (alpha <= 0.0f) continue;

                Vector2 offsetS = offset * scale + new Vector2(cam.WorldView.Width, cam.WorldView.Height) * (1.0f - scale) * 0.5f - new Vector2(256.0f * i);
                while (offsetS.X <= -srcRect.Width * scale) offsetS.X += srcRect.Width * scale;
                while (offsetS.X > 0.0f) offsetS.X -= srcRect.Width * scale;
                while (offsetS.Y <= -srcRect.Height * scale) offsetS.Y += srcRect.Height * scale;
                while (offsetS.Y > 0.0f) offsetS.Y -= srcRect.Height * scale;

                dustParticles.DrawTiled(spriteBatch, origin + offsetS, new Vector2(cam.WorldView.Width - offsetS.X, cam.WorldView.Height - offsetS.Y), Vector2.Zero, srcRect, Color.White * alpha, new Vector2(scale));
            }

            spriteBatch.End();

            RenderWalls(GameMain.Instance.GraphicsDevice, cam);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (GameMain.DebugDraw)
            {
                var cells = level.GetCells(GameMain.GameScreen.Cam.WorldViewCenter, 2);
                foreach (VoronoiCell cell in cells)
                {
                    GUI.DrawRectangle(spriteBatch, new Vector2(cell.Center.X - 10.0f, -cell.Center.Y-10.0f), new Vector2(20.0f, 20.0f), Color.Cyan, true);

                    GUI.DrawLine(spriteBatch, 
                        new Vector2(cell.edges[0].point1.X, -cell.edges[0].point1.Y),
                        new Vector2(cell.Center.X, -cell.Center.Y), 
                        Color.Blue*0.5f);
                
                    foreach (GraphEdge edge in cell.edges)
                    {
                        GUI.DrawLine(spriteBatch, new Vector2(edge.point1.X, -edge.point1.Y),
                            new Vector2(edge.point2.X, -edge.point2.Y), cell.body==null ? Color.Cyan*0.5f : Color.White);
                    }

                    foreach (Vector2 point in cell.bodyVertices)
                    {
                        GUI.DrawRectangle(spriteBatch, new Vector2(point.X, -point.Y), new Vector2(10.0f, 10.0f), Color.White, true);
                    }
                }

                foreach (List<Vector2> nodeList in level.SmallTunnels)
                {
                    for (int i = 1; i<nodeList.Count; i++)
                    {
                        GUI.DrawLine(spriteBatch, 
                            new Vector2(nodeList[i-1].X, -nodeList[i - 1].Y),
                            new Vector2(nodeList[i].X, -nodeList[i].Y), 
                            Color.Lerp(Color.Yellow, Color.Red, i / (float)nodeList.Count), 0, 10);

                    }
                }
            }

            Vector2 pos = new Vector2(0.0f, -level.Size.Y);

            if (GameMain.GameScreen.Cam.WorldView.Y >= -pos.Y - 1024)
            {
                pos.X = GameMain.GameScreen.Cam.WorldView.X -1024;
                int width = (int)(Math.Ceiling(GameMain.GameScreen.Cam.WorldView.Width / 1024 + 4.0f) * 1024);

                GUI.DrawRectangle(spriteBatch,new Rectangle(
                    (int)(MathUtils.Round(pos.X, 1024)), 
                    -GameMain.GameScreen.Cam.WorldView.Y, 
                    width, 
                    (int)(GameMain.GameScreen.Cam.WorldView.Y + pos.Y) - 30),
                    Color.Black, true);

                spriteBatch.Draw(shaftTexture,
                    new Rectangle((int)(MathUtils.Round(pos.X, 1024)), (int)pos.Y-1000, width, 1024),
                    new Rectangle(0, 0, width, -1024),
                    level.BackgroundColor, 0.0f,
                    Vector2.Zero,
                    SpriteEffects.None, 0.0f);
            }

            if (GameMain.GameScreen.Cam.WorldView.Y - GameMain.GameScreen.Cam.WorldView.Height < level.SeaFloorTopPos + 1024)
            {
                pos = new Vector2(GameMain.GameScreen.Cam.WorldView.X - 1024, -level.BottomPos);

                int width = (int)(Math.Ceiling(GameMain.GameScreen.Cam.WorldView.Width / 1024 + 4.0f) * 1024);

                GUI.DrawRectangle(spriteBatch, new Rectangle(
                    (int)(MathUtils.Round(pos.X, 1024)), 
                    (int)-(level.BottomPos - 30), 
                    width, 
                    (int)(level.BottomPos - (GameMain.GameScreen.Cam.WorldView.Y - GameMain.GameScreen.Cam.WorldView.Height))), 
                    Color.Black, true);

                spriteBatch.Draw(shaftTexture,
                    new Rectangle((int)(MathUtils.Round(pos.X, 1024)), (int)-level.BottomPos, width, 1024),
                    new Rectangle(0, 0, width, -1024),
                    level.BackgroundColor, 0.0f,
                    Vector2.Zero,
                    SpriteEffects.FlipVertically, 0.0f);
            }
        }


        public void RenderWalls(GraphicsDevice graphicsDevice, Camera cam)
        {
            if (wallVertices == null) return;

            bool renderLevel = cam.WorldView.Y >= 0.0f;
            bool renderSeaFloor = cam.WorldView.Y - cam.WorldView.Height < level.SeaFloorTopPos + 1024;

            if (!renderLevel && !renderSeaFloor) return;

            wallEdgeEffect.World = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 100) * 0.5f;
            wallCenterEffect.World = wallEdgeEffect.World;
            
            graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            wallCenterEffect.CurrentTechnique.Passes[0].Apply();

            if (renderLevel)
            {
                graphicsDevice.SetVertexBuffer(bodyVertices);
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(bodyVertices.VertexCount / 3.0f));
            }
            if (renderSeaFloor)
            {
                foreach (LevelWall wall in level.ExtraWalls)
                {
                    graphicsDevice.SetVertexBuffer(wall.BodyVertices);
                    graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wall.BodyVertices.VertexCount / 3.0f));
                }
            }

            wallEdgeEffect.CurrentTechnique.Passes[0].Apply();

            if (renderLevel)
            {
                wallEdgeEffect.CurrentTechnique.Passes[0].Apply();
                graphicsDevice.SetVertexBuffer(wallVertices);
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wallVertices.VertexCount / 3.0f));
            }
            if (renderSeaFloor)
            {
                foreach (LevelWall wall in level.ExtraWalls)
                {
                    graphicsDevice.SetVertexBuffer(wall.WallVertices);
                    graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wall.WallVertices.VertexCount / 3.0f));
                }
            }          
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (wallVertices!=null) wallVertices.Dispose();
            if (bodyVertices != null) bodyVertices.Dispose();
        }

    }
}
