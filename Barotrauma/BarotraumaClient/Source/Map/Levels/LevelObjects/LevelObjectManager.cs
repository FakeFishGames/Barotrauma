using Barotrauma.Particles;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class LevelObject
    {
        public List<LevelTrigger> ParticleEmitterTrigger = new List<LevelTrigger>();
        public List<ParticleEmitter> ParticleEmitters;

        public LevelTrigger SoundTrigger;
        public Sound Sound;
        public SoundChannel SoundChannel;
    }

    partial class LevelObjectManager
    {
        private List<LevelObject> visibleObjectsBack = new List<LevelObject>();
        private List<LevelObject> visibleObjectsFront = new List<LevelObject>();

        private Rectangle currentGridIndices;

        partial void UpdateProjSpecific(float deltaTime)
        {
            foreach (LevelObject obj in visibleObjectsBack)
            {
                UpdateObject(obj, deltaTime);
            }
            foreach (LevelObject obj in visibleObjectsFront)
            {
                UpdateObject(obj, deltaTime);
            }
        }

        private void UpdateObject(LevelObject obj, float deltaTime)
        {
            if (obj.ParticleEmitters != null)
            {
                for (int i = 0; i < obj.ParticleEmitters.Count; i++)
                {
                    if (obj.ParticleEmitterTrigger[i] != null && !obj.ParticleEmitterTrigger[i].IsTriggered) continue;
                    Vector2 emitterPos = obj.LocalToWorld(obj.Prefab.EmitterPositions[i]);
                    obj.ParticleEmitters[i].Emit(deltaTime, emitterPos, hullGuess: null,
                        angle: obj.ParticleEmitters[i].Prefab.CopyEntityAngle ? obj.Rotation : 0.0f);
                }
            }

            if (obj.Prefab.SwingFrequency > 0.0f)
            {
                obj.SwingTimer += deltaTime * obj.Prefab.SwingFrequency;
                obj.SwingTimer = obj.SwingTimer % MathHelper.TwoPi;
            }

            if (obj.Prefab.ScaleOscillationFrequency > 0.0f)
            {
                obj.ScaleOscillateTimer += deltaTime * obj.Prefab.ScaleOscillationFrequency;
                obj.ScaleOscillateTimer = obj.ScaleOscillateTimer % MathHelper.TwoPi;
            }

            if (obj.Sound != null)
            {
                if (obj.SoundTrigger == null || obj.SoundTrigger.IsTriggered)
                {
                    Vector2 soundPos = obj.LocalToWorld(new Vector2(obj.Prefab.SoundPosition.X, obj.Prefab.SoundPosition.Y));
                    if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), soundPos) < obj.Sound.BaseFar * obj.Sound.BaseFar)
                    {
                        if (!GameMain.SoundManager.IsPlaying(obj.Sound))
                        {
                            obj.SoundChannel = obj.Sound.Play(1.0f, obj.Sound.BaseFar, soundPos);
                        }
                        else
                        {
                            obj.SoundChannel = GameMain.SoundManager.GetChannelFromSound(obj.Sound);
                        }
                        obj.SoundChannel.Position = new Vector3(soundPos.X, soundPos.Y, 0.0f);
                    }
                }
                else if (GameMain.SoundManager.IsPlaying(obj.Sound))
                {
                    obj.SoundChannel?.Dispose();
                    obj.SoundChannel = null;
                }
            }
        }

        /// <summary>
        /// Checks which level objects are in camera view and adds them to the visibleObjects lists
        /// </summary>
        private void RefreshVisibleObjects(Rectangle currentIndices)
        {
            visibleObjectsBack.Clear();
            visibleObjectsFront.Clear();

            for (int x = currentIndices.X; x <= currentIndices.Width; x++)
            {
                for (int y = currentIndices.Y; y <= currentIndices.Height; y++)
                {
                    if (objectGrid[x, y] == null) continue;
                    foreach (LevelObject obj in objectGrid[x, y])
                    {
                        var objectList = obj.Position.Z >= 0 ? visibleObjectsBack : visibleObjectsFront;
                        int drawOrderIndex = 0;
                        for (int i = 0; i < objectList.Count; i++)
                        {
                            if (objectList[i] == obj)
                            {
                                drawOrderIndex = -1;
                                break;
                            }

                            if (objectList[i].Position.Z < obj.Position.Z)
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
                            objectList.Insert(drawOrderIndex, obj);
                        }
                    }
                }
            }

            currentGridIndices = currentIndices;
        }

        public void DrawObjects(SpriteBatch spriteBatch, Camera cam, bool drawFront)
        {
            Rectangle indices = Rectangle.Empty;
            indices.X = (int)Math.Floor(cam.WorldView.X / (float)GridSize);            
            if (indices.X >= objectGrid.GetLength(0)) return;
            indices.Y = (int)Math.Floor((cam.WorldView.Y - cam.WorldView.Height - Level.Loaded.BottomPos) / (float)GridSize);
            if (indices.Y >= objectGrid.GetLength(1)) return;

            indices.Width = (int)Math.Floor(cam.WorldView.Right / (float)GridSize)+1;
            if (indices.Width < 0) return;
            indices.Height = (int)Math.Floor((cam.WorldView.Y - Level.Loaded.BottomPos) / (float)GridSize)+1;
            if (indices.Height < 0) return;

            indices.X = Math.Max(indices.X, 0);
            indices.Y = Math.Max(indices.Y, 0);
            indices.Width = Math.Min(indices.Width, objectGrid.GetLength(0)-1);
            indices.Height = Math.Min(indices.Height, objectGrid.GetLength(1)-1);

            float z = 0.0f;
            if (currentGridIndices != indices)
            {
                RefreshVisibleObjects(indices);
            }

            var objectList = drawFront ? visibleObjectsFront : visibleObjectsBack;
            foreach (LevelObject obj in objectList)
            {              
                Vector2 camDiff = new Vector2(obj.Position.X, obj.Position.Y) - cam.WorldViewCenter;
                camDiff.Y = -camDiff.Y;

                Vector2 scale = Vector2.One * obj.Scale;
                if (obj.Prefab.ScaleOscillationFrequency > 0.0f)
                {
                    float sin = (float)Math.Sin(obj.ScaleOscillateTimer);
                    scale *= new Vector2(
                        1.0f + sin * obj.Prefab.ScaleOscillation.X,
                        1.0f + sin * obj.Prefab.ScaleOscillation.Y);
                }

                float swingAmount = 0.0f;
                if (obj.Prefab.SwingAmount > 0.0f)
                {
                    swingAmount = (float)Math.Sin(obj.SwingTimer) * obj.Prefab.SwingAmount;
                }

                obj.Prefab.Sprite.Draw(
                    spriteBatch,
                    new Vector2(obj.Position.X, -obj.Position.Y) - camDiff * obj.Position.Z / 10000.0f,
                    Color.Lerp(Color.White, Level.Loaded.BackgroundColor, obj.Position.Z / 5000.0f),
                    obj.Prefab.Sprite.Origin,
                    obj.Rotation + swingAmount,
                    scale,
                    SpriteEffects.None,
                    z);
                
                if (GameMain.DebugDraw)
                {
                    GUI.DrawRectangle(spriteBatch, new Vector2(obj.Position.X, -obj.Position.Y), new Vector2(10.0f, 10.0f), Color.Red, true);

                    if (obj.Trigger != null && obj.Trigger.PhysicsBody != null)
                    {
                        obj.Trigger.PhysicsBody.UpdateDrawPosition();
                        obj.Trigger.PhysicsBody.DebugDraw(spriteBatch, Color.Cyan);
                    }
                }

                z += 0.0001f;
            }
        }        
    }
}
