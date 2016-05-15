using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Voronoi2;

namespace Barotrauma
{
    class LevelRenderer : IDisposable
    {
        private static BasicEffect basicEffect;

        private static Sprite background, backgroundTop;
        private static Sprite dustParticles;
        private static Texture2D shaftTexture;

        private static BackgroundSpriteManager backgroundSpriteManager;

        Vector2 dustOffset;

        private Level level;

        private VertexBuffer wallVertices, bodyVertices;
        
        //public VertexPositionTexture[] WallVertices;
        //public VertexPositionColor[] BodyVertices;
        
        public LevelRenderer(Level level)
        {
            if (shaftTexture == null) shaftTexture = TextureLoader.FromFile("Content/Map/shaft.png");

            if (background==null)
            {
                background = new Sprite("Content/Map/background2.png", Vector2.Zero);
                backgroundTop = new Sprite("Content/Map/background.png", Vector2.Zero);
                dustParticles = new Sprite("Content/Map/dustparticles.png", Vector2.Zero);
            }

            if (basicEffect == null)
            {
                basicEffect = new BasicEffect(GameMain.CurrGraphicsDevice);
                basicEffect.VertexColorEnabled = false;

                basicEffect.TextureEnabled = true;
                basicEffect.Texture = TextureLoader.FromFile("Content/Map/iceWall.png");
            }
            
            if (backgroundSpriteManager==null)
            {
                backgroundSpriteManager = new BackgroundSpriteManager("Content/BackgroundSprites/BackgroundSpritePrefabs.xml");
            }

            this.level = level;
        }

        public void PlaceSprites(int amount)
        {
            backgroundSpriteManager.PlaceSprites(level, amount);
        }

        public void Update(float deltaTime)
        {
            dustOffset -= Vector2.UnitY * 10.0f * (float)deltaTime;
        }

        public void SetWallVertices(VertexPositionTexture[] vertices)
        {
            wallVertices = new VertexBuffer(GameMain.CurrGraphicsDevice, VertexPositionTexture.VertexDeclaration, vertices.Length,BufferUsage.WriteOnly);
            wallVertices.SetData(vertices);
        }

        public void SetBodyVertices(VertexPositionColor[] vertices)
        {
            bodyVertices = new VertexBuffer(GameMain.CurrGraphicsDevice, VertexPositionColor.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            bodyVertices.SetData(vertices);
        }

        public void DrawBackground(SpriteBatch spriteBatch, Camera cam, BackgroundCreatureManager backgroundCreatureManager = null)
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearWrap);

            Vector2 backgroundPos = cam.Position;
            //if (Level.Loaded != null) backgroundPos -= Level.Loaded.Position;
            backgroundPos.Y = -backgroundPos.Y;
            backgroundPos /= 20.0f;

            if (backgroundPos.Y < 1024)
            {
                if (backgroundPos.Y > -1024)
                {
                    background.SourceRect = new Rectangle((int)backgroundPos.X, (int)Math.Max(backgroundPos.Y, 0), 1024, 1024);
                    background.DrawTiled(spriteBatch,
                        (backgroundPos.Y < 0) ? new Vector2(0.0f, -backgroundPos.Y) : Vector2.Zero,
                        new Vector2(GameMain.GraphicsWidth, 1024 - backgroundPos.Y),
                        Vector2.Zero, level.BackgroundColor);
                }

                if (backgroundPos.Y < 0)
                {
                    backgroundTop.SourceRect = new Rectangle((int)backgroundPos.X, (int)backgroundPos.Y, 1024, (int)Math.Min(-backgroundPos.Y, 1024));
                    backgroundTop.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(GameMain.GraphicsWidth, Math.Min(-backgroundPos.Y, GameMain.GraphicsHeight)),
                        Vector2.Zero, level.BackgroundColor);
                }
            }

            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                SamplerState.LinearWrap, DepthStencilState.Default, null, null,
                cam.Transform);

            backgroundSpriteManager.DrawSprites(spriteBatch, cam);

            if (backgroundCreatureManager!=null) backgroundCreatureManager.Draw(spriteBatch);

            spriteBatch.End();


            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.Additive,
                SamplerState.LinearWrap, DepthStencilState.Default, null, null,
                cam.Transform);

            for (int i = 1; i < 2; i++)
            {
                Vector2 offset = new Vector2(cam.WorldView.X, cam.WorldView.Y);

                dustParticles.SourceRect = new Rectangle((int)(offset.X), (int)(-offset.Y), (int)(1024), (int)(1024));

                dustParticles.DrawTiled(spriteBatch, new Vector2(cam.WorldView.X, -cam.WorldView.Y),
                    new Vector2(cam.WorldView.Width, cam.WorldView.Height),
                    Vector2.Zero, Color.White);
            }

            spriteBatch.End();
            
            RenderWalls(GameMain.CurrGraphicsDevice, cam);
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
                        Color.White);
                
                    foreach (GraphEdge edge in cell.edges)
                    {
                        GUI.DrawLine(spriteBatch, new Vector2(edge.point1.X, -edge.point1.Y),
                            new Vector2(edge.point2.X, -edge.point2.Y), cell.body==null ? Color.Gray : Color.White);
                    }

                    foreach (Vector2 point in cell.bodyVertices)
                    {
                        GUI.DrawRectangle(spriteBatch, new Vector2(point.X, -point.Y), new Vector2(10.0f, 10.0f), Color.White, true);
                    }
                }

                //RuinGeneration.RuinGenerator.Draw(spriteBatch);
            }


            Vector2 pos = new Vector2(0.0f, -level.Size.Y);// level.EndPosition;

            if (GameMain.GameScreen.Cam.WorldView.Y < -pos.Y - 512) return;

            pos.X = GameMain.GameScreen.Cam.WorldView.X -512.0f;

            int width = (int)(Math.Ceiling(GameMain.GameScreen.Cam.WorldView.Width / 512.0f + 2.0f) * 512.0f);

            spriteBatch.Draw(shaftTexture,
                new Rectangle((int)(MathUtils.Round(pos.X, 512.0f)), (int)pos.Y, width, 512),
                new Rectangle(0, 0, width, 256),
                level.BackgroundColor, 0.0f,
                Vector2.Zero,
                SpriteEffects.None, 0.0f);
        }


        public void RenderWalls(GraphicsDevice graphicsDevice, Camera cam)
        {
            if (wallVertices == null) return;
            
            basicEffect.World = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            graphicsDevice.BlendState = BlendState.AlphaBlend;
            graphicsDevice.SetVertexBuffer(bodyVertices);

            basicEffect.VertexColorEnabled = true;
            basicEffect.TextureEnabled = false;
            basicEffect.CurrentTechnique = basicEffect.Techniques["BasicEffect_VertexColor"];
            basicEffect.CurrentTechnique.Passes[0].Apply();
            
            graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(bodyVertices.VertexCount / 3.0f));

            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < 2; i++)
                {
                    basicEffect.World = Matrix.CreateTranslation(new Vector3(level.WrappingWalls[side, i].Offset, 0.0f)) * cam.ShaderTransform
                        * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;
                    basicEffect.CurrentTechnique.Passes[0].Apply();


                    graphicsDevice.SetVertexBuffer(level.WrappingWalls[side, i].BodyVertices);

                    graphicsDevice.DrawPrimitives(
                        PrimitiveType.TriangleList, 0,
                        (int)Math.Floor(level.WrappingWalls[side, i].BodyVertices.VertexCount / 3.0f));
                }
            }


            graphicsDevice.SetVertexBuffer(wallVertices);
            basicEffect.VertexColorEnabled = false;
            basicEffect.TextureEnabled = true;
            basicEffect.CurrentTechnique = basicEffect.Techniques["BasicEffect_Texture"];
            basicEffect.CurrentTechnique.Passes[0].Apply();
            graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wallVertices.VertexCount / 3.0f));
            
            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < 2; i++)
                {

                    basicEffect.World = Matrix.CreateTranslation(new Vector3(level.WrappingWalls[side,i].Offset, 0.0f)) * cam.ShaderTransform
                        * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;
                    basicEffect.CurrentTechnique.Passes[0].Apply();

                    graphicsDevice.SetVertexBuffer(level.WrappingWalls[side, i].WallVertices);

                    graphicsDevice.DrawPrimitives(
                        PrimitiveType.TriangleList, 0,
                        (int)Math.Floor(level.WrappingWalls[side, i].WallVertices.VertexCount / 3.0f));

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
