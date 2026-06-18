using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public interface ILevelRenderableObject
    {
        public Vector3 Position { get; }

    }

    partial class LevelObjectManager
    {

        // Pre-initialized to the max size, so that we don't have to resize the lists at runtime. TODO: Could the capacity (of some collections?) be lower?
        private readonly List<ILevelRenderableObject> visibleObjectsBack = new List<ILevelRenderableObject>(MaxVisibleObjects);
        private readonly List<ILevelRenderableObject> visibleObjectsMid = new List<ILevelRenderableObject>(MaxVisibleObjects);
        private readonly List<ILevelRenderableObject> visibleObjectsFront = new List<ILevelRenderableObject>(MaxVisibleObjects);
        private readonly HashSet<ILevelRenderableObject> allVisibleObjects = new HashSet<ILevelRenderableObject>(MaxVisibleObjects);

        private double NextRefreshTime;

        //Maximum number of visible objects drawn at once. Should be large enough to not have an effect during normal gameplay, 
        //but small enough to prevent wrecking performance when zooming out very far
        const int MaxVisibleObjects = 600;

        private Rectangle currentGridIndices;

        public bool ForceRefreshVisibleObjects;

        partial void RemoveProjSpecific()
        {
            visibleObjectsBack.Clear();
            visibleObjectsMid.Clear();
            visibleObjectsFront.Clear();
        }

        partial void UpdateProjSpecific(float deltaTime, Camera cam)
        {
            foreach (ILevelRenderableObject obj in visibleObjectsBack)
            {
                if (obj is LevelObject levelObj)
                {
                    levelObj.Update(deltaTime, cam);
                }
            }
            foreach (ILevelRenderableObject obj in visibleObjectsMid)
            {
                if (obj is LevelObject levelObj)
                {
                    levelObj.Update(deltaTime, cam);
                }
            }
            foreach (ILevelRenderableObject obj in visibleObjectsFront)
            {
                if (obj is LevelObject levelObj)
                {
                    levelObj.Update(deltaTime, cam);
                }
            }
        }
        
        /// <summary>
        /// Returns all visible objects, but not in order, because internally uses a HashSet.
        /// </summary>
        public IEnumerable<ILevelRenderableObject> GetAllVisibleObjects()
        {
            allVisibleObjects.Clear();
            foreach (ILevelRenderableObject obj in visibleObjectsBack)
            {
                allVisibleObjects.Add(obj);
            }
            foreach (ILevelRenderableObject obj in visibleObjectsMid)
            {
                allVisibleObjects.Add(obj);
            }
            foreach (ILevelRenderableObject obj in visibleObjectsFront)
            {
                allVisibleObjects.Add(obj);
            }
            return allVisibleObjects;
        }

        /// <summary>
        /// Checks which level objects are in camera view and adds them to the visibleObjects lists
        /// </summary>
        private void RefreshVisibleObjects(Rectangle currentIndices, BackgroundCreatureManager backgroundCreatureManager, float zoom)
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

            foreach (var backgroundCreature in backgroundCreatureManager.VisibleCreatures)
            {
                int drawOrderIndex = 0;
                for (int i = 0; i < visibleObjectsBack.Count; i++)
                {
                    if (visibleObjectsBack[i].Position.Z > backgroundCreature.Position.Z)
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
                    visibleObjectsBack.Insert(drawOrderIndex, backgroundCreature);
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
        public void DrawObjectsBack(SpriteBatch spriteBatch, BackgroundCreatureManager backgroundCreatureManager, Camera cam)
        {
            DrawObjects(spriteBatch, cam, backgroundCreatureManager, visibleObjectsBack);
        }

        /// <summary>
        /// Draw the objects in front of the level walls, but behind characters
        /// </summary>
        public void DrawObjectsMid(SpriteBatch spriteBatch, BackgroundCreatureManager backgroundCreatureManager, Camera cam)
        {
            DrawObjects(spriteBatch, cam, backgroundCreatureManager, visibleObjectsMid);
        }

        /// <summary>
        /// Draw the objects in front of the level walls and characters
        /// </summary>
        public void DrawObjectsFront(SpriteBatch spriteBatch, BackgroundCreatureManager backgroundCreatureManager, Camera cam)
        {
            DrawObjects(spriteBatch, cam, backgroundCreatureManager, visibleObjectsFront);
        }

        private void DrawObjects(SpriteBatch spriteBatch, Camera cam, BackgroundCreatureManager backgroundCreatureManager, List<ILevelRenderableObject> objectList)
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
                RefreshVisibleObjects(indices, backgroundCreatureManager, cam.Zoom);
                ForceRefreshVisibleObjects = false;
                if (cam.Zoom < 0.1f)
                {
                    //when zoomed very far out, refresh a little less often
                    NextRefreshTime = Timing.TotalTime + MathHelper.Lerp(1.0f, 0.0f, cam.Zoom * 10.0f);
                }
            }

            bool prevObjectHasDeformableSprite = false;
            foreach (ILevelRenderableObject obj2 in objectList)
            {              
                Vector2 camDiff = new Vector2(obj2.Position.X, obj2.Position.Y) - cam.WorldViewCenter;
                camDiff.Y = -camDiff.Y;

                bool hasDeformableSprite = false;
                if (obj2 is LevelObject levelObject)
                {
                    hasDeformableSprite = levelObject.ActivePrefab.DeformableSprite != null;
                    if (hasDeformableSprite != prevObjectHasDeformableSprite)
                    {
                        spriteBatch.End();
                        spriteBatch.Begin(SpriteSortMode.Deferred,
                            BlendState.NonPremultiplied,
                            SamplerState.LinearWrap, DepthStencilState.DepthRead,
                            transformMatrix: cam.Transform);
                    }

                    Sprite activeSprite = levelObject.Sprite;
                    activeSprite?.Draw(
                        spriteBatch,
                        new Vector2(levelObject.Position.X, -levelObject.Position.Y) - camDiff * levelObject.Position.Z * ParallaxStrength,
                        Color.Lerp(levelObject.Prefab.SpriteColor, levelObject.Prefab.SpriteColor.Multiply(Level.Loaded.BackgroundTextureColor), levelObject.Position.Z / levelObject.Prefab.FadeOutDepth),
                        activeSprite.Origin,
                        levelObject.CurrentRotation,
                        levelObject.CurrentScale,
                        SpriteEffects.None,
                        z);

                    if (hasDeformableSprite)
                    {
                        if (levelObject.CurrentSpriteDeformation != null)
                        {
                            levelObject.ActivePrefab.DeformableSprite.Deform(levelObject.CurrentSpriteDeformation);
                        }
                        else
                        {
                            levelObject.ActivePrefab.DeformableSprite.Reset();
                        }
                        levelObject.ActivePrefab.DeformableSprite?.Draw(cam,
                            new Vector3(new Vector2(levelObject.Position.X, levelObject.Position.Y) - camDiff * levelObject.Position.Z * ParallaxStrength, z * 10.0f),
                            levelObject.ActivePrefab.DeformableSprite.Origin,
                            levelObject.CurrentRotation,
                            levelObject.CurrentScale,
                            Color.Lerp(levelObject.Prefab.SpriteColor, levelObject.Prefab.SpriteColor.Multiply(Level.Loaded.BackgroundTextureColor), levelObject.Position.Z / 5000.0f));
                    }
                    prevObjectHasDeformableSprite = hasDeformableSprite;

                    if (GameMain.DebugDraw)
                    {
                        GUI.DrawRectangle(spriteBatch, new Vector2(levelObject.Position.X, -levelObject.Position.Y), new Vector2(10.0f, 10.0f), GUIStyle.Red, true);

                        if (levelObject.Triggers == null) { continue; }
                        foreach (LevelTrigger trigger in levelObject.Triggers)
                        {
                            if (trigger.PhysicsBody == null) continue;
                            GUI.DrawLine(spriteBatch, new Vector2(levelObject.Position.X, -levelObject.Position.Y), new Vector2(trigger.WorldPosition.X, -trigger.WorldPosition.Y), Color.Cyan, 0, 3);

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

                }
                else if (obj2 is BackgroundCreature backgroundCreature && cam.Zoom > 0.05f)
                {
                    hasDeformableSprite = backgroundCreature.Prefab.DeformableSprite != null;
                    if (hasDeformableSprite != prevObjectHasDeformableSprite)
                    {
                        spriteBatch.End();
                        spriteBatch.Begin(SpriteSortMode.Deferred,
                            BlendState.NonPremultiplied,
                            SamplerState.LinearWrap, DepthStencilState.DepthRead,
                            transformMatrix: cam.Transform);
                    }

                    backgroundCreature.Draw(spriteBatch, cam);
                }
                prevObjectHasDeformableSprite = hasDeformableSprite;


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
