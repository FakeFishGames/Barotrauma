using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class LevelRenderer
    {

        private static BasicEffect basicEffect;

        private static Texture2D shaftTexture;

        private Level level;

        public LevelRenderer(Level level)
        {
            if (shaftTexture == null) shaftTexture = TextureLoader.FromFile("Content/Map/shaft.png");

            if (basicEffect == null)
            {

                basicEffect = new BasicEffect(GameMain.CurrGraphicsDevice);
                basicEffect.VertexColorEnabled = false;

                basicEffect.TextureEnabled = true;
                basicEffect.Texture = TextureLoader.FromFile("Content/Map/iceSurface.png");
            }

            this.level = level;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Vector2 pos = level.EndPosition;
            pos.Y = -pos.Y - level.Position.Y;

            if (GameMain.GameScreen.Cam.WorldView.Y < -pos.Y - 512) return;

            pos.X = GameMain.GameScreen.Cam.WorldView.X - 512.0f;
            //pos.X += Position.X % 512;

            int width = (int)(Math.Ceiling(GameMain.GameScreen.Cam.WorldView.Width / 512.0f + 2.0f) * 512.0f);

            spriteBatch.Draw(shaftTexture,
                new Rectangle((int)(MathUtils.Round(pos.X, 512.0f) + level.Position.X % 512), (int)pos.Y, width, 512),
                new Rectangle(0, 0, width, 256),
                Color.White, 0.0f,
                Vector2.Zero,
                SpriteEffects.None, 0.0f);
        }


        public void Render(GraphicsDevice graphicsDevice, Camera cam, VertexPositionTexture[] vertices)
        {
            if (vertices == null) return;
            if (vertices.Length <= 0) return;

            basicEffect.World = Matrix.CreateTranslation(new Vector3(level.Position, 0.0f)) * cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            basicEffect.CurrentTechnique.Passes[0].Apply();

            graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;

            graphicsDevice.DrawUserPrimitives<VertexPositionTexture>(PrimitiveType.TriangleList, vertices, 0, (int)Math.Floor(vertices.Length / 3.0f));


            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < 2; i++)
                {
                    basicEffect.World = Matrix.CreateTranslation(
                        new Vector3(level.Position + level.WrappingWalls[side, i].Offset, 0.0f)) * 
                        cam.ShaderTransform *
                        Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

                    basicEffect.CurrentTechnique.Passes[0].Apply();

                    graphicsDevice.DrawUserPrimitives(
                        PrimitiveType.TriangleList,
                        level.WrappingWalls[side, i].Vertices, 0, 
                        (int)Math.Floor(level.WrappingWalls[side, i].Vertices.Length / 3.0f));

                }
            }
        }

    }
}
