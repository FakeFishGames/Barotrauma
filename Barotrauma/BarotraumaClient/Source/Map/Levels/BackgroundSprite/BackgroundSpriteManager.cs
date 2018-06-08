using Barotrauma.Particles;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class BackgroundSprite
    {
        public List<LevelTrigger> ParticleEmitterTrigger = new List<LevelTrigger>();
        public List<ParticleEmitter> ParticleEmitters;

        public LevelTrigger SoundTrigger;
        public Sound Sound;
        public SoundChannel SoundChannel;
    }

    partial class BackgroundSpriteManager
    {
        private List<BackgroundSprite> visibleSpritesBack = new List<BackgroundSprite>();
        private List<BackgroundSprite> visibleSpritesFront = new List<BackgroundSprite>();

        private Rectangle currentGridIndices;

        partial void UpdateProjSpecific(float deltaTime)
        {
            foreach (BackgroundSprite s in visibleSpritesBack)
            {
                UpdateSprite(s, deltaTime);
            }
            foreach (BackgroundSprite s in visibleSpritesFront)
            {
                UpdateSprite(s, deltaTime);
            }
        }

        private void UpdateSprite(BackgroundSprite s, float deltaTime)
        {
            if (s.ParticleEmitters != null)
            {
                for (int i = 0; i < s.ParticleEmitters.Count; i++)
                {
                    if (s.ParticleEmitterTrigger[i] != null && !s.ParticleEmitterTrigger[i].IsTriggered) continue;
                    Vector2 emitterPos = s.LocalToWorld(s.Prefab.EmitterPositions[i]);
                    s.ParticleEmitters[i].Emit(deltaTime, emitterPos, hullGuess: null,
                        angle: s.ParticleEmitters[i].Prefab.CopyEntityAngle ? s.Rotation : 0.0f);
                }
            }

            if (s.Prefab.SwingFrequency > 0.0f)
            {
                s.SwingTimer += deltaTime * s.Prefab.SwingFrequency;
                s.SwingTimer = s.SwingTimer % MathHelper.TwoPi;
            }

            if (s.Prefab.ScaleOscillationFrequency > 0.0f)
            {
                s.ScaleOscillateTimer += deltaTime * s.Prefab.ScaleOscillationFrequency;
                s.ScaleOscillateTimer = s.ScaleOscillateTimer % MathHelper.TwoPi;
            }

            if (s.Sound != null)
            {
                if (s.SoundTrigger == null || s.SoundTrigger.IsTriggered)
                {
                    Vector2 soundPos = s.LocalToWorld(new Vector2(s.Prefab.SoundPosition.X, s.Prefab.SoundPosition.Y));
                    if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), soundPos) < s.Sound.BaseFar * s.Sound.BaseFar)
                    {
                        if (!GameMain.SoundManager.IsPlaying(s.Sound))
                        {
                            s.SoundChannel = s.Sound.Play(1.0f, s.Sound.BaseFar, soundPos);
                        }
                        else
                        {
                            s.SoundChannel = GameMain.SoundManager.GetChannelFromSound(s.Sound);
                        }
                        s.SoundChannel.Position = new Vector3(soundPos.X, soundPos.Y, 0.0f);
                    }
                }
                else if (GameMain.SoundManager.IsPlaying(s.Sound))
                {
                    s.SoundChannel?.Dispose();
                    s.SoundChannel = null;
                }
            }
        }

        private void RefreshVisibleSprites(Rectangle currentIndices)
        {
            visibleSpritesBack.Clear();
            visibleSpritesFront.Clear();

            for (int x = currentIndices.X; x <= currentIndices.Width; x++)
            {
                for (int y = currentIndices.Y; y <= currentIndices.Height; y++)
                {
                    if (spriteGrid[x, y] == null) continue;
                    foreach (BackgroundSprite sprite in spriteGrid[x, y])
                    {
                        var spriteList = sprite.Position.Z >= 0 ? visibleSpritesBack : visibleSpritesFront;
                        int drawOrderIndex = 0;
                        for (int i = 0; i < spriteList.Count; i++)
                        {
                            if (spriteList[i] == sprite)
                            {
                                drawOrderIndex = -1;
                                break;
                            }

                            if (spriteList[i].Position.Z < sprite.Position.Z)
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
                            spriteList.Insert(drawOrderIndex, sprite);
                        }
                    }
                }
            }

            currentGridIndices = currentIndices;
        }

        public void DrawSprites(SpriteBatch spriteBatch, Camera cam, bool drawFront)
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
                RefreshVisibleSprites(indices);
            }

            var spriteList = drawFront ? visibleSpritesFront : visibleSpritesBack;
            foreach (BackgroundSprite sprite in spriteList)
            {              
                Vector2 camDiff = new Vector2(sprite.Position.X, sprite.Position.Y) - cam.WorldViewCenter;
                camDiff.Y = -camDiff.Y;

                Vector2 scale = Vector2.One * sprite.Scale;
                if (sprite.Prefab.ScaleOscillationFrequency > 0.0f)
                {
                    float sin = (float)Math.Sin(sprite.ScaleOscillateTimer);
                    scale *= new Vector2(
                        1.0f + sin * sprite.Prefab.ScaleOscillation.X,
                        1.0f + sin * sprite.Prefab.ScaleOscillation.Y);
                }

                float swingAmount = 0.0f;
                if (sprite.Prefab.SwingAmount > 0.0f)
                {
                    swingAmount = (float)Math.Sin(sprite.SwingTimer) * sprite.Prefab.SwingAmount;
                }

                sprite.Prefab.Sprite.Draw(
                    spriteBatch,
                    new Vector2(sprite.Position.X, -sprite.Position.Y) - camDiff * sprite.Position.Z / 10000.0f,
                    Color.Lerp(Color.White, Level.Loaded.BackgroundColor, sprite.Position.Z / 5000.0f),
                    sprite.Prefab.Sprite.Origin,
                    sprite.Rotation + swingAmount,
                    scale,
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
