using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FarseerPhysics.Dynamics;
using System.Linq;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Level
    {
        private LevelRenderer renderer;

        private BackgroundCreatureManager backgroundCreatureManager;

        public LevelRenderer Renderer => renderer;

        public void ReloadTextures()
        {
            renderer.ReloadTextures();

            HashSet<Texture2D> uniqueTextures = new HashSet<Texture2D>();
            HashSet<Sprite> uniqueSprites = new HashSet<Sprite>();
            var allLevelObjects = levelObjectManager.GetAllObjects();
            foreach (var levelObj in allLevelObjects)
            {
                foreach (Sprite sprite in levelObj.Prefab.Sprites)
                {
                    if (!uniqueTextures.Contains(sprite.Texture))
                    {
                        uniqueTextures.Add(sprite.Texture);
                        uniqueSprites.Add(sprite);
                    }
                }
                foreach (Sprite specularSprite in levelObj.Prefab.SpecularSprites)
                {
                    if (!uniqueTextures.Contains(specularSprite.Texture))
                    {
                        uniqueTextures.Add(specularSprite.Texture);
                        uniqueSprites.Add(specularSprite);
                    }
                }
            }

            foreach (Sprite sprite in uniqueSprites)
            {
                sprite.ReloadTexture();
            }
        }
        
        public void DrawFront(SpriteBatch spriteBatch, Camera cam)
        {
            if (renderer == null) return;
            renderer.Draw(spriteBatch, cam);

            if (GameMain.DebugDraw)
            {
                foreach (InterestingPosition pos in positionsOfInterest)
                {
                    Color color = Color.Yellow;
                    if (pos.PositionType == PositionType.Cave)
                    {
                        color = Color.DarkOrange;
                    }
                    else if (pos.PositionType == PositionType.Ruin)
                    {
                        color = Color.LightGray;
                    }
                    
                    GUI.DrawRectangle(spriteBatch, new Vector2(pos.Position.X - 15.0f, -pos.Position.Y - 15.0f), new Vector2(30.0f, 30.0f), color, true);
                }

                foreach (RuinGeneration.Ruin ruin in ruins)
                {
                    Rectangle ruinArea = ruin.Area;
                    ruinArea.Y = -ruinArea.Y - ruinArea.Height;

                    GUI.DrawRectangle(spriteBatch, ruinArea, Color.DarkSlateBlue, false, 0, 5);
                }
            }
        }

        public void DrawBack(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam)
        {
            float brightness = MathHelper.Clamp(1.1f + (cam.Position.Y - Size.Y) / 100000.0f, 0.1f, 1.0f);
            var lightColorHLS = generationParams.AmbientLightColor.RgbToHLS();
            lightColorHLS.Y *= brightness;

            GameMain.LightManager.AmbientLight = ToolBox.HLSToRGB(lightColorHLS);

            graphics.Clear(BackgroundColor);

            if (renderer == null) return;
            renderer.DrawBackground(spriteBatch, cam, levelObjectManager, backgroundCreatureManager);
        }
        
        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            foreach (LevelWall levelWall in extraWalls)
            {
                if (levelWall.Body.BodyType == BodyType.Static) continue;

                Vector2 bodyPos = new Vector2(
                    msg.ReadSingle(), 
                    msg.ReadSingle());
                
                levelWall.MoveState = msg.ReadRangedSingle(0.0f, MathHelper.TwoPi, 16);

                if (Vector2.DistanceSquared(bodyPos, levelWall.Body.Position) > 0.5f)
                {
                    levelWall.Body.SetTransformIgnoreContacts(ref bodyPos, levelWall.Body.Rotation);
                }
            }
        }
    }
}
