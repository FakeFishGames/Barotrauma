using Barotrauma.Extensions;
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
        private readonly List<LevelObject> visibleObjectsBack = new List<LevelObject>();
        private readonly List<LevelObject> visibleObjectsMid = new List<LevelObject>();
        private readonly List<LevelObject> visibleObjectsFront = new List<LevelObject>();

        private double NextRefreshTime;

        //Maximum number of visible objects drawn at once. Should be large enough to not have an effect during normal gameplay, 
        //but small enough to prevent wrecking performance when zooming out very far
        const int MaxVisibleObjects = 500;

        private Rectangle currentGridIndices;

        public bool ForceRefreshVisibleObjects;
        
        partial void UpdateProjSpecific(float deltaTime)
        {
            foreach (LevelObject obj in visibleObjectsBack)
            {
                obj.Update(deltaTime);
            }
            foreach (LevelObject obj in visibleObjectsMid)
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
            return visibleObjectsBack.Union(visibleObjectsMid).Union(visibleObjectsFront);
        }

        /// <summary>
        /// Checks which level objects are in camera view and adds them to the visibleObjects lists
        /// </summary>
        private void RefreshVisibleObjects(Rectangle currentIndices, float zoom)
        {
            visibleObjectsBack.Clear();
            visibleObjectsMid.Clear();
            visibleObjectsFront.Clear();

            float minSizeToDraw = MathHelper.Lerp(10.0f, 5.0f, Math.Min(zoom * 20.0f, 1.0f));

            //start from the grid cell at the center of the view
            //(if objects needs to be culled, better to cull at the edges of the view)
            int midIndexX = (currentIndices.X + currentIndices.Width) / 2;
            int midIndexY = (currentIndices.Y + currentIndices.Height) / 2;
            CheckIndex(midIndexX, midIndexY);

            for (int x = currentIndices.X; x <= currentIndices.Width; x++)
            {
                for (int y = currentIndices.Y; y <= currentIndices.Height; y++)
                {
                    if (x != midIndexX || y != midIndexY) { CheckIndex(x, y); }
                }
            }

            void CheckIndex(int x, int y)
            {
                if (objectGrid[x, y] == null) { return; }
                foreach (LevelObject obj in objectGrid[x, y])
                {
                    if (!obj.CanBeVisible) { continue; }
                    if (obj.Prefab.HideWhenBroken && obj.Health <= 0.0f) { continue; }

                    if (zoom < 0.05f)
                    {
                        //hide if the sprite is very small when zoomed this far out
                        if ((obj.Sprite != null && Math.Min(obj.Sprite.size.X * zoom, obj.Sprite.size.Y * zoom) < 5.0f) ||
                            (obj.ActivePrefab?.DeformableSprite != null && Math.Min(obj.ActivePrefab.DeformableSprite.Sprite.size.X * zoom, obj.ActivePrefab.DeformableSprite.Sprite.size.Y * zoom) < minSizeToDraw))
                        {
                            continue;
                        }

                        float zCutoff = MathHelper.Lerp(5000.0f, 500.0f, (0.05f - zoom) * 20.0f);
                        if (obj.Position.Z > zCutoff)
                        {
                            continue;
                        }
                    }

                    var objectList =
                        obj.Position.Z >= 0 ?
                            visibleObjectsBack :
                            (obj.Position.Z < -1 ? visibleObjectsFront : visibleObjectsMid);
                    if (objectList.Count >= MaxVisibleObjects) { continue; }

                    int drawOrderIndex = 0;
                    for (int i = 0; i < objectList.Count; i++)
                    {
                        if (objectList[i] == obj)
                        {
                            drawOrderIndex = -1;
                            break;
                        }

                        if (objectList[i].Position.Z > obj.Position.Z)
                        {
                            break;
                        }
                        else
                        {
                            drawOrderIndex = i + 1;
                            if (drawOrderIndex >= MaxVisibleObjects) { break; }
                        }
                    }

                    if (drawOrderIndex >= 0 && drawOrderIndex < MaxVisibleObjects)
                    {
                        objectList.Insert(drawOrderIndex, obj);
                    }
                }
            }

            //object grid is sorted in an ascending order
            //(so we prefer the objects in the foreground instead of ones in the background if some need to be culled)
            //rendering needs to be done in a descending order though to get the background objects to be drawn first -> reverse the lists
            visibleObjectsBack.Reverse();
            visibleObjectsMid.Reverse();
            visibleObjectsFront.Reverse();

            currentGridIndices = currentIndices;
        }

        /// <summary>
        /// Draw the objects behind the level walls
        /// </summary>
        public void DrawObjectsBack(SpriteBatch spriteBatch, Camera cam)
        {
            DrawObjects(spriteBatch, cam, visibleObjectsBack);
        }

        /// <summary>
        /// Draw the objects in front of the level walls, but behind characters
        /// </summary>
        public void DrawObjectsMid(SpriteBatch spriteBatch, Camera cam)
        {
            DrawObjects(spriteBatch, cam, visibleObjectsMid);
        }

        /// <summary>
        /// Draw the objects in front of the level walls and characters
        /// </summary>
        public void DrawObjectsFront(SpriteBatch spriteBatch, Camera cam)
        {
            DrawObjects(spriteBatch, cam, visibleObjectsFront);
        }

        private void DrawObjects(SpriteBatch spriteBatch, Camera cam, List<LevelObject> objectList)
        {
            Rectangle indices = Rectangle.Empty;
            indices.X = (int)Math.Floor(cam.WorldView.X / (float)GridSize);
            if (indices.X >= objectGrid.GetLength(0)) { return; }
            indices.Y = (int)Math.Floor((cam.WorldView.Y - cam.WorldView.Height - Level.Loaded.BottomPos) / (float)GridSize);
            if (indices.Y >= objectGrid.GetLength(1)) { return; }

            indices.Width = (int)Math.Floor(cam.WorldView.Right / (float)GridSize) + 1;
            if (indices.Width < 0) { return; }
            indices.Height = (int)Math.Floor((cam.WorldView.Y - Level.Loaded.BottomPos) / (float)GridSize) + 1;
            if (indices.Height < 0) { return; }

            indices.X = Math.Max(indices.X, 0);
            indices.Y = Math.Max(indices.Y, 0);
            indices.Width = Math.Min(indices.Width, objectGrid.GetLength(0) - 1);
            indices.Height = Math.Min(indices.Height, objectGrid.GetLength(1) - 1);

            float z = 0.0f;
            if (ForceRefreshVisibleObjects || (currentGridIndices != indices && Timing.TotalTime > NextRefreshTime))
            {
                RefreshVisibleObjects(indices, cam.Zoom);
                ForceRefreshVisibleObjects = false;
                if (cam.Zoom < 0.1f)
                {
                    //when zoomed very far out, refresh a little less often
                    NextRefreshTime = Timing.TotalTime + MathHelper.Lerp(1.0f, 0.0f, cam.Zoom * 10.0f);
                }
            }

            foreach (LevelObject obj in objectList)
            {              
                Vector2 camDiff = new Vector2(obj.Position.X, obj.Position.Y) - cam.WorldViewCenter;
                camDiff.Y = -camDiff.Y;
                
                Sprite activeSprite = obj.Sprite;
                activeSprite?.Draw(
                    spriteBatch,
                    new Vector2(obj.Position.X, -obj.Position.Y) - camDiff * obj.Position.Z / 10000.0f,
                    Color.Lerp(obj.Prefab.SpriteColor, obj.Prefab.SpriteColor.Multiply(Level.Loaded.BackgroundTextureColor), obj.Position.Z / 3000.0f),
                    activeSprite.Origin,
                    obj.CurrentRotation,
                    obj.CurrentScale,
                    SpriteEffects.None,
                    z);

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
                        Color.Lerp(obj.Prefab.SpriteColor, obj.Prefab.SpriteColor.Multiply(Level.Loaded.BackgroundTextureColor), obj.Position.Z / 5000.0f));
                }

                
                if (GameMain.DebugDraw)
                {
                    GUI.DrawRectangle(spriteBatch, new Vector2(obj.Position.X, -obj.Position.Y), new Vector2(10.0f, 10.0f), GUIStyle.Red, true);

                    if (obj.Triggers == null) { continue; }
                    foreach (LevelTrigger trigger in obj.Triggers)
                    {
                        if (trigger.PhysicsBody == null) continue;
                        GUI.DrawLine(spriteBatch, new Vector2(obj.Position.X, -obj.Position.Y), new Vector2(trigger.WorldPosition.X, -trigger.WorldPosition.Y), Color.Cyan, 0, 3);

                        Vector2 flowForce = trigger.GetWaterFlowVelocity();
                        if (flowForce.LengthSquared() > 1)
                        {
                            flowForce.Y = -flowForce.Y;
                            GUI.DrawLine(spriteBatch, new Vector2(trigger.WorldPosition.X, -trigger.WorldPosition.Y), new Vector2(trigger.WorldPosition.X, -trigger.WorldPosition.Y) + flowForce * 10, GUIStyle.Orange, 0, 5);
                        }
                        trigger.PhysicsBody.UpdateDrawPosition();
                        trigger.PhysicsBody.DebugDraw(spriteBatch, trigger.IsTriggered ? Color.Cyan : Color.DarkCyan);
                    }
                }

                z += 0.0001f;
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            int objIndex = msg.ReadRangedInteger(0, objects.Count);
            objects[objIndex].ClientRead(msg);
        }
    }
}
