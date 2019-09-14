using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class LevelObjectManager
    {
        private List<LevelObject> visibleObjectsBack = new List<LevelObject>();
        private List<LevelObject> visibleObjectsFront = new List<LevelObject>();

        private Rectangle currentGridIndices;
        
        partial void UpdateProjSpecific(float deltaTime)
        {
            foreach (LevelObject obj in visibleObjectsBack)
            {
                obj.Update(deltaTime);
            }
            foreach (LevelObject obj in visibleObjectsFront)
            {
                obj.Update(deltaTime);
            }
        }
        
        public IEnumerable<LevelObject> GetVisibleObjects()
        {
            return visibleObjectsBack.Union(visibleObjectsFront);
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


        public void DrawObjects(SpriteBatch spriteBatch, Camera cam, bool drawFront, bool specular = false)
        {
            Rectangle indices = Rectangle.Empty;
            indices.X = (int)Math.Floor(cam.WorldView.X / (float)GridSize);
            if (indices.X >= objectGrid.GetLength(0)) return;
            indices.Y = (int)Math.Floor((cam.WorldView.Y - cam.WorldView.Height - Level.Loaded.BottomPos) / (float)GridSize);
            if (indices.Y >= objectGrid.GetLength(1)) return;

            indices.Width = (int)Math.Floor(cam.WorldView.Right / (float)GridSize) + 1;
            if (indices.Width < 0) return;
            indices.Height = (int)Math.Floor((cam.WorldView.Y - Level.Loaded.BottomPos) / (float)GridSize) + 1;
            if (indices.Height < 0) return;

            indices.X = Math.Max(indices.X, 0);
            indices.Y = Math.Max(indices.Y, 0);
            indices.Width = Math.Min(indices.Width, objectGrid.GetLength(0) - 1);
            indices.Height = Math.Min(indices.Height, objectGrid.GetLength(1) - 1);

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
                
                Sprite activeSprite = specular ? obj.SpecularSprite : obj.Sprite;
                activeSprite?.Draw(
                    spriteBatch,
                    new Vector2(obj.Position.X, -obj.Position.Y) - camDiff * obj.Position.Z / 10000.0f,
                    Color.Lerp(Color.White, Level.Loaded.BackgroundTextureColor, obj.Position.Z / 5000.0f),
                    activeSprite.Origin,
                    obj.CurrentRotation,
                    obj.CurrentScale,
                    SpriteEffects.None,
                    z);

                if (specular) continue;

                if (obj.ActivePrefab.DeformableSprite != null)
                {
                    if (obj.CurrentSpriteDeformation != null)
                    {
                        obj.ActivePrefab.DeformableSprite.Deform(obj.CurrentSpriteDeformation);
                    }
                    else
                    {
                        obj.ActivePrefab.DeformableSprite.Reset();
                    }
                    obj.ActivePrefab.DeformableSprite?.Draw(cam,
                        new Vector3(new Vector2(obj.Position.X, obj.Position.Y) - camDiff * obj.Position.Z / 10000.0f, z * 10.0f),
                        obj.ActivePrefab.DeformableSprite.Origin,
                        obj.CurrentRotation,
                        obj.CurrentScale,
                        Color.Lerp(Color.White, Level.Loaded.BackgroundTextureColor, obj.Position.Z / 5000.0f));
                }

                
                if (GameMain.DebugDraw)
                {
                    GUI.DrawRectangle(spriteBatch, new Vector2(obj.Position.X, -obj.Position.Y), new Vector2(10.0f, 10.0f), Color.Red, true);

                    foreach (LevelTrigger trigger in obj.Triggers)
                    {
                        if (trigger.PhysicsBody == null) continue;
                        GUI.DrawLine(spriteBatch, new Vector2(obj.Position.X, -obj.Position.Y), new Vector2(trigger.WorldPosition.X, -trigger.WorldPosition.Y), Color.Cyan, 0, 3);

                        Vector2 flowForce = trigger.GetWaterFlowVelocity();
                        if (flowForce.LengthSquared() > 1)
                        {
                            flowForce.Y = -flowForce.Y;
                            GUI.DrawLine(spriteBatch, new Vector2(trigger.WorldPosition.X, -trigger.WorldPosition.Y), new Vector2(trigger.WorldPosition.X, -trigger.WorldPosition.Y) + flowForce * 10, Color.Orange, 0, 5);
                        }
                        trigger.PhysicsBody.UpdateDrawPosition();
                        trigger.PhysicsBody.DebugDraw(spriteBatch, trigger.IsTriggered ? Color.Cyan : Color.DarkCyan);
                    }
                }

                z += 0.0001f;
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            int objIndex = msg.ReadRangedInteger(0, objects.Count);
            objects[objIndex].ClientRead(msg);
        }
    }
}
