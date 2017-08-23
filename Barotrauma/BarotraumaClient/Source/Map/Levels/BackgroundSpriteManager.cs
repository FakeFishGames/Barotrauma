using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class BackgroundSprite
    {
        public ParticleEmitter ParticleEmitter;
    }

    partial class BackgroundSpriteManager
    {
        private List<BackgroundSprite> visibleSprites = new List<BackgroundSprite>();

        private Rectangle currentGridIndices;

        partial void UpdateProjSpecific(float deltaTime)
        {
            foreach (BackgroundSprite s in visibleSprites)
            {
                if (s.Prefab.ParticleEmitterPrefab != null)
                {
                    Vector2 emitterPos = new Vector2(s.Prefab.EmitterPosition.X, s.Prefab.EmitterPosition.Y) * s.Scale;

                    if (s.Rotation != 0.0f || s.Prefab.SwingAmount != 0.0f)
                    {
                        var ca = (float)Math.Cos(s.Rotation + swingState * s.Prefab.SwingAmount);
                        var sa = (float)Math.Sin(s.Rotation + swingState * s.Prefab.SwingAmount);

                        emitterPos = new Vector2(
                            ca * emitterPos.X + sa * emitterPos.Y,
                            -sa * emitterPos.X + ca * emitterPos.Y);
                    }

                    s.ParticleEmitter.Emit(deltaTime, new Vector2(s.Position.X, s.Position.Y) + emitterPos);
                }
            }
        }

        public void DrawSprites(SpriteBatch spriteBatch, Camera cam)
        {
            Rectangle indices = Rectangle.Empty;
            indices.X = (int)Math.Floor(cam.WorldView.X / (float)GridSize);            
            if (indices.X >= spriteGrid.GetLength(0)) return;
            indices.Y = (int)Math.Floor((cam.WorldView.Y - cam.WorldView.Height - Level.Loaded.BottomPos) / (float)GridSize);
            if (indices.Y >= spriteGrid.GetLength(1)) return;

            indices.Width = (int)Math.Floor(cam.WorldView.Right / (float)GridSize)+1;
            if (indices.Width < 0) return;
            indices.Height = (int)Math.Floor((cam.WorldView.Y - Level.Loaded.BottomPos) / (float)GridSize)+1;
            if (indices.Height < 0) return;

            indices.X = Math.Max(indices.X, 0);
            indices.Y = Math.Max(indices.Y, 0);
            indices.Width = Math.Min(indices.Width, spriteGrid.GetLength(0)-1);
            indices.Height = Math.Min(indices.Height, spriteGrid.GetLength(1)-1);

            float z = 0.0f;
            if (currentGridIndices != indices)
            {
                visibleSprites.Clear();

                for (int x = indices.X; x <= indices.Width; x++)
                {
                    for (int y = indices.Y; y <= indices.Height; y++)
                    {
                        if (spriteGrid[x, y] == null) continue;
                        foreach (BackgroundSprite sprite in spriteGrid[x, y])
                        {
                            int drawOrderIndex = 0;
                            for (int i = 0; i < visibleSprites.Count; i++)
                            {
                                if (visibleSprites[i] == sprite)
                                {
                                    drawOrderIndex = -1;
                                    break;
                                }

                                if (visibleSprites[i].Position.Z > sprite.Position.Z)
                                {
                                    break;
                                }
                                else
                                {
                                    drawOrderIndex = i + 1;
                                }
                            }

                            if (drawOrderIndex >= 0)
                            {
                                visibleSprites.Insert(drawOrderIndex, sprite);
                            }
                        }
                    }
                }

                currentGridIndices = indices;
            }

            foreach (BackgroundSprite sprite in visibleSprites)
            {              
                Vector2 camDiff = new Vector2(sprite.Position.X, sprite.Position.Y) - cam.WorldViewCenter;
                camDiff.Y = -camDiff.Y;

                sprite.Prefab.Sprite.Draw(
                    spriteBatch,
                    new Vector2(sprite.Position.X, -sprite.Position.Y) - camDiff * sprite.Position.Z / 10000.0f,
                    Color.Lerp(Color.White, Level.Loaded.BackgroundColor, sprite.Position.Z / 5000.0f),
                    sprite.Rotation + swingState * sprite.Prefab.SwingAmount,
                    sprite.Scale,
                    SpriteEffects.None,
                    z);
                
                if (GameMain.DebugDraw)
                {
                    GUI.DrawRectangle(spriteBatch, new Vector2(sprite.Position.X, -sprite.Position.Y), new Vector2(10.0f, 10.0f), Color.Red, true);

                    if (sprite.Trigger != null && sprite.Trigger.PhysicsBody != null)
                    {
                        sprite.Trigger.PhysicsBody.UpdateDrawPosition();
                        sprite.Trigger.PhysicsBody.DebugDraw(spriteBatch, Color.Cyan);
                    }
                }

                z += 0.0001f;
            }
        }        
    }
}
