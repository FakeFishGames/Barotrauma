using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class LevelRenderer
    {
        private static BasicEffect basicEffect;

        private static Sprite background, backgroundTop;
        private static Texture2D dustParticles;
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
                background = new Sprite("Content/Map/background.png", Vector2.Zero);
                backgroundTop = new Sprite("Content/Map/background2.png", Vector2.Zero);
                dustParticles = Sprite.LoadTexture("Content/Map/dustparticles.png");
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
                        Vector2.Zero, Color.White);
                }

                if (backgroundPos.Y < 0)
                {
                    backgroundTop.SourceRect = new Rectangle((int)backgroundPos.X, (int)backgroundPos.Y, 1024, (int)Math.Min(-backgroundPos.Y, 1024));
                    backgroundTop.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(GameMain.GraphicsWidth, Math.Min(-backgroundPos.Y, GameMain.GraphicsHeight)),
                        Vector2.Zero, Color.White);
                }
            }

            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                SamplerState.LinearWrap, DepthStencilState.Default, null, null,
                cam.Transform);

            backgroundSpriteManager.DrawSprites(spriteBatch);

            if (backgroundCreatureManager!=null) backgroundCreatureManager.Draw(spriteBatch);

            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                SamplerState.LinearWrap);

            backgroundPos = new Vector2(cam.WorldView.X, cam.WorldView.Y) + dustOffset;
            //if (Level.Loaded != null) backgroundPos -= Level.Loaded.Position;

            Rectangle viewRect = cam.WorldView;
            viewRect.Y = -viewRect.Y;

            float multiplier = 0.8f;
            for (int i = 1; i < 5; i++)
            {
                spriteBatch.Draw(dustParticles, new Rectangle(0,0,GameMain.GraphicsWidth,GameMain.GraphicsHeight),
                    new Rectangle((int)((backgroundPos.X * multiplier)), (int)((-backgroundPos.Y * multiplier)), cam.WorldView.Width*2, cam.WorldView.Height*2),
                    Color.White * multiplier, 0.0f, Vector2.Zero, SpriteEffects.None, 1.0f - multiplier);
                multiplier -= 0.1f;
            }

            spriteBatch.End();
            
            RenderWalls(GameMain.CurrGraphicsDevice, cam);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Vector2 pos = new Vector2(0.0f, -level.StartPosition.Y);// level.EndPosition;

            if (GameMain.GameScreen.Cam.WorldView.Y < -pos.Y - 512) return;

            pos.X = GameMain.GameScreen.Cam.WorldView.X -512.0f;

            int width = (int)(Math.Ceiling(GameMain.GameScreen.Cam.WorldView.Width / 512.0f + 2.0f) * 512.0f);

            spriteBatch.Draw(shaftTexture,
                new Rectangle((int)(MathUtils.Round(pos.X, 512.0f)), (int)pos.Y, width, 512),
                new Rectangle(0, 0, width, 256),
                Color.White, 0.0f,
                Vector2.Zero,
                SpriteEffects.None, 0.0f);
        }


        public void RenderWalls(GraphicsDevice graphicsDevice, Camera cam)
        {
            if (wallVertices == null) return;

            Stopwatch sw = new Stopwatch();
            sw.Start();

            basicEffect.World = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            graphicsDevice.SetVertexBuffer(bodyVertices);

            basicEffect.VertexColorEnabled = true;
            basicEffect.TextureEnabled = false;
            basicEffect.CurrentTechnique = basicEffect.Techniques["BasicEffect_VertexColor"];
            basicEffect.CurrentTechnique.Passes[0].Apply();


            graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(bodyVertices.VertexCount / 3.0f));

            //for (int side = 0; side < 2; side++)
            //{
            //    for (int i = 0; i < 2; i++)
            //    {
            //        graphicsDevice.DrawUserPrimitives(
            //            PrimitiveType.TriangleList, level.WrappingWalls[side, i].BodyVertices, 0,
            //            (int)Math.Floor(level.WrappingWalls[side, i].BodyVertices.Length / 3.0f));

            //    }
            //}


            graphicsDevice.SetVertexBuffer(wallVertices);
            basicEffect.VertexColorEnabled = false;
            basicEffect.TextureEnabled = true;
            basicEffect.CurrentTechnique = basicEffect.Techniques["BasicEffect_Texture"];
            basicEffect.CurrentTechnique.Passes[0].Apply();
            graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, (int)Math.Floor(wallVertices.VertexCount / 3.0f));

            //for (int side = 0; side < 2; side++)
            //{
            //    for (int i = 0; i < 2; i++)
            //    {
            //        basicEffect.VertexColorEnabled = false;
            //        basicEffect.TextureEnabled = true;
            //        basicEffect.CurrentTechnique = basicEffect.Techniques["BasicEffect_Texture"];
            //        basicEffect.CurrentTechnique.Passes[0].Apply();
            //        graphicsDevice.DrawUserPrimitives(
            //            PrimitiveType.TriangleList, level.WrappingWalls[side, i].WallVertices, 0,
            //            (int)Math.Floor(level.WrappingWalls[side, i].WallVertices.Length / 3.0f));

            //    }
            //}

            sw.Stop();

            Debug.WriteLine("level render: "+sw.ElapsedTicks);
        }

    }
}
