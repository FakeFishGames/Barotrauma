using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.MapCreatures.Behavior;

namespace Barotrauma
{
    partial class Hull : MapEntity, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        private class RemoteDecal
        {
            public readonly UInt32 DecalId;
            public readonly int SpriteIndex;
            public Vector2 NormalizedPos;
            public readonly float Scale;

            public RemoteDecal(UInt32 decalId, int spriteIndex, Vector2 normalizedPos, float scale)
            {
                DecalId = decalId;
                SpriteIndex = spriteIndex;
                NormalizedPos = normalizedPos;
                Scale = scale;
            }
        }

        private float serverUpdateDelay;
        private float remoteWaterVolume, remoteOxygenPercentage;
        private List<Vector3> remoteFireSources;
        private readonly List<BackgroundSection> remoteBackgroundSections = new List<BackgroundSection>();
        private readonly List<RemoteDecal> remoteDecals = new List<RemoteDecal>();

        private readonly HashSet<Decal> pendingDecalUpdates = new HashSet<Decal>();
        
        private double lastAmbientLightEditTime;

        public override bool SelectableInEditor
        {
            get
            {
                return ShowHulls;
            }
        }

        public override bool DrawBelowWater
        {
            get
            {
                return decals.Count > 0 || BallastFlora != null;
            }
        }

        public override bool DrawOverWater
        {
            get
            {
                return true;
            }
        }

        private float paintAmount = 0.0f;
        private float minimumPaintAmountToDraw;

        public override bool IsVisible(Rectangle worldView)
        {
            if (BallastFlora != null) { return true; }

            if (Screen.Selected != GameMain.SubEditorScreen && !GameMain.DebugDraw)
            {
                if (decals.Count == 0 && paintAmount < minimumPaintAmountToDraw) { return false; }

                Rectangle worldRect = WorldRect;
                if (worldRect.X > worldView.Right || worldRect.Right < worldView.X) { return false; }
                if (worldRect.Y < worldView.Y - worldView.Height || worldRect.Y - worldRect.Height > worldView.Y) { return false; }
            }
            return true;
        }

        public override bool IsMouseOn(Vector2 position)
        {
            if (!GameMain.DebugDraw && !ShowHulls) return false;

            return (Submarine.RectContains(WorldRect, position) &&
                !Submarine.RectContains(MathUtils.ExpandRect(WorldRect, -8), position));
        }

        private GUIComponent CreateEditingHUD(bool inGame = false)
        {
            editingHUD = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.25f), GUI.Canvas, Anchor.CenterRight) { MinSize = new Point(400, 0) }) { UserData = this };
            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(0.95f, 0.8f), editingHUD.RectTransform, Anchor.Center), style: null);
            new SerializableEntityEditor(listBox.Content.RectTransform, this, inGame, showName: true, titleFont: GUI.LargeFont);

            PositionEditingHUD();

            return editingHUD;
        }

        public override void UpdateEditing(Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData as Hull != this)
            {
                editingHUD = CreateEditingHUD(Screen.Selected != GameMain.SubEditorScreen);
            }

            if (!PlayerInput.KeyDown(Keys.Space)) { return; }
            bool lClick = PlayerInput.PrimaryMouseButtonClicked();
            bool rClick = PlayerInput.SecondaryMouseButtonClicked();
            if (!lClick && !rClick) { return; }

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

            foreach (MapEntity entity in mapEntityList)
            {
                if (entity == this || !entity.IsHighlighted) { continue; }
                if (!entity.IsMouseOn(position)) { continue; }
                if (entity.linkedTo == null || !entity.Linkable) { continue; }
                if (entity.linkedTo.Contains(this) || linkedTo.Contains(entity) || rClick)
                {
                    if (entity == this || !entity.IsHighlighted) { continue; }
                    if (!entity.IsMouseOn(position)) { continue; }
                    if (entity.linkedTo.Contains(this))
                    {
                        entity.linkedTo.Remove(this);
                        linkedTo.Remove(entity);
                    }
                }
                else
                {
                    if (!entity.linkedTo.Contains(this)) { entity.linkedTo.Add(this); }
                    if (!linkedTo.Contains(this)) { linkedTo.Add(entity); }                       
                }
            }
        }

        partial void UpdateProjSpecific(float deltaTime, Camera cam)
        {
            serverUpdateDelay -= deltaTime;
            if (serverUpdateDelay <= 0.0f)
            {
                ApplyRemoteState();
            }

            if (networkUpdatePending)
            {
                networkUpdateTimer += deltaTime;
                if (networkUpdateTimer > 0.2f)
                {
                    if (!pendingSectionUpdates.Any() && !pendingDecalUpdates.Any())
                    {
                        GameMain.NetworkMember?.CreateEntityEvent(this);
                    }
                    foreach (Decal decal in pendingDecalUpdates)
                    {
                        GameMain.NetworkMember?.CreateEntityEvent(this, new object[] { decal });
                    }
                    foreach (int pendingSectionUpdate in pendingSectionUpdates)
                    {
                        GameMain.NetworkMember?.CreateEntityEvent(this, new object[] { pendingSectionUpdate });
                    }
                    pendingSectionUpdates.Clear();
                    networkUpdatePending = false;
                    networkUpdateTimer = 0.0f;
                }
            }

            if (EditWater)
            {
                Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
                if (Submarine.RectContains(WorldRect, position))
                {
                    if (PlayerInput.PrimaryMouseButtonHeld())
                    {
                        WaterVolume += 1500.0f;
                        networkUpdatePending = true;
                        serverUpdateDelay = 0.5f;
                    }
                    else if (PlayerInput.SecondaryMouseButtonHeld())
                    {
                        WaterVolume -= 1500.0f;
                        networkUpdatePending = true;
                        serverUpdateDelay = 0.5f;
                    }
                }
            }
            else if (EditFire)
            {
                Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
                if (Submarine.RectContains(WorldRect, position))
                {
                    if (PlayerInput.PrimaryMouseButtonClicked())
                    {
                        new FireSource(position, this, isNetworkMessage: true);
                        networkUpdatePending = true;
                        serverUpdateDelay = 0.5f;
                    }
                }
            }
      
            if (waterVolume < 1.0f) return;
            for (int i = 1; i < waveY.Length - 1; i++)
            {
                float maxDelta = Math.Max(Math.Abs(rightDelta[i]), Math.Abs(leftDelta[i]));
                if (maxDelta > 1.0f && maxDelta > Rand.Range(1.0f, 10.0f))
                {
                    var particlePos = new Vector2(rect.X + WaveWidth * i, surface + waveY[i]);
                    if (Submarine != null) particlePos += Submarine.Position;

                    GameMain.ParticleManager.CreateParticle("mist",
                        particlePos,
                        new Vector2(0.0f, -50.0f), 0.0f, this);
                }
            }
        }

        private void DrawDecals(SpriteBatch spriteBatch)
        {
            Rectangle hullDrawRect = rect;
            if (Submarine != null) hullDrawRect.Location += Submarine.DrawPosition.ToPoint();

            float depth = 1.0f;
            foreach (Decal d in decals)
            {
                d.Draw(spriteBatch, this, depth);
                depth -= 0.000001f;
            }
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (back && Screen.Selected != GameMain.SubEditorScreen)
            {
                BallastFlora?.Draw(spriteBatch);
                DrawDecals(spriteBatch);
                return;
            }

            if (!ShowHulls && !GameMain.DebugDraw) { return; }

            if (!editing && (!GameMain.DebugDraw || Screen.Selected.Cam.Zoom < 0.1f)) { return; }

            float alpha = 1.0f;
            float hideTimeAfterEdit = 3.0f;
            if (lastAmbientLightEditTime > Timing.TotalTime - hideTimeAfterEdit * 2.0f)
            {
                alpha = Math.Min((float)(Timing.TotalTime - lastAmbientLightEditTime) / hideTimeAfterEdit - 1.0f, 1.0f);
            }

            Rectangle drawRect =
                Submarine == null ? rect : new Rectangle((int)(Submarine.DrawPosition.X + rect.X), (int)(Submarine.DrawPosition.Y + rect.Y), rect.Width, rect.Height);

            if ((IsSelected || IsHighlighted) && editing)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(drawRect.X, -drawRect.Y),
                    new Vector2(rect.Width, rect.Height),
                    (IsHighlighted ? Color.LightBlue * 0.8f : GUI.Style.Red * 0.5f) * alpha, false, 0, (int)Math.Max(5.0f / Screen.Selected.Cam.Zoom, 1.0f));
            }

            GUI.DrawRectangle(spriteBatch,
                new Vector2(drawRect.X, -drawRect.Y),
                new Vector2(rect.Width, rect.Height),
                Color.Blue * alpha, false, (ID % 255) * 0.000001f, (int)Math.Max(1.5f / Screen.Selected.Cam.Zoom, 1.0f));

            GUI.DrawRectangle(spriteBatch,
                new Rectangle(drawRect.X, -drawRect.Y, rect.Width, rect.Height),
                GUI.Style.Red * ((100.0f - OxygenPercentage) / 400.0f) * alpha, true, 0, (int)Math.Max(1.5f / Screen.Selected.Cam.Zoom, 1.0f));

            if (GameMain.DebugDraw)
            {
                GUI.SmallFont.DrawString(spriteBatch, "Pressure: " + ((int)pressure - rect.Y).ToString() +
                    " - Oxygen: " + ((int)OxygenPercentage), new Vector2(drawRect.X + 5, -drawRect.Y + 5), Color.White);
                GUI.SmallFont.DrawString(spriteBatch, waterVolume + " / " + Volume, new Vector2(drawRect.X + 5, -drawRect.Y + 20), Color.White);

                GUI.DrawRectangle(spriteBatch, new Rectangle(drawRect.Center.X, -drawRect.Y + drawRect.Height / 2, 10, (int)(100 * Math.Min(waterVolume / Volume, 1.0f))), Color.Cyan, true);
                if (WaterVolume > Volume)
                {
                    float maxExcessWater = Volume * MaxCompress;
                    GUI.DrawRectangle(spriteBatch, new Rectangle(drawRect.Center.X, -drawRect.Y + drawRect.Height / 2, 10, (int)(100 * (waterVolume - Volume) / maxExcessWater)), GUI.Style.Red, true);
                }
                GUI.DrawRectangle(spriteBatch, new Rectangle(drawRect.Center.X, -drawRect.Y + drawRect.Height / 2, 10, 100), Color.Black);

                foreach (FireSource fs in FireSources)
                {
                    Rectangle fireSourceRect = new Rectangle((int)fs.WorldPosition.X, -(int)fs.WorldPosition.Y, (int)fs.Size.X, (int)fs.Size.Y);
                    GUI.DrawRectangle(spriteBatch, fireSourceRect, GUI.Style.Red, false, 0, 5);
                    GUI.DrawRectangle(spriteBatch, new Rectangle(fireSourceRect.X - (int)fs.DamageRange, fireSourceRect.Y, fireSourceRect.Width + (int)fs.DamageRange * 2, fireSourceRect.Height), GUI.Style.Orange, false, 0, 5);
                    //GUI.DrawRectangle(spriteBatch, new Rectangle((int)fs.LastExtinguishPos.X, (int)-fs.LastExtinguishPos.Y, 5,5), Color.Yellow, true);
                }
                foreach (FireSource fs in FakeFireSources)
                {
                    Rectangle fireSourceRect = new Rectangle((int)fs.WorldPosition.X, -(int)fs.WorldPosition.Y, (int)fs.Size.X, (int)fs.Size.Y);
                    GUI.DrawRectangle(spriteBatch, fireSourceRect, GUI.Style.Red, false, 0, 5);
                    GUI.DrawRectangle(spriteBatch, new Rectangle(fireSourceRect.X - (int)fs.DamageRange, fireSourceRect.Y, fireSourceRect.Width + (int)fs.DamageRange * 2, fireSourceRect.Height), GUI.Style.Orange, false, 0, 5);
                    //GUI.DrawRectangle(spriteBatch, new Rectangle((int)fs.LastExtinguishPos.X, (int)-fs.LastExtinguishPos.Y, 5,5), Color.Yellow, true);
                }


                /*GUI.DrawLine(spriteBatch, new Vector2(drawRect.X, -WorldSurface), new Vector2(drawRect.Right, -WorldSurface), Color.Cyan * 0.5f);
                for (int i = 0; i < waveY.Length - 1; i++)
                {
                    GUI.DrawLine(spriteBatch,
                        new Vector2(drawRect.X + WaveWidth * i, -WorldSurface - waveY[i] - 10),
                        new Vector2(drawRect.X + WaveWidth * (i + 1), -WorldSurface - waveY[i + 1] - 10), Color.Blue * 0.5f);
                }*/
            }

            foreach (MapEntity e in linkedTo)
            {
                if (e is Hull linkedHull)
                {
                    Rectangle connectedHullRect = e.Submarine == null ?
                        linkedHull.rect :
                        new Rectangle(
                            (int)(Submarine.DrawPosition.X + linkedHull.WorldPosition.X),
                            (int)(Submarine.DrawPosition.Y + linkedHull.WorldPosition.Y),
                            linkedHull.WorldRect.Width, linkedHull.WorldRect.Height);

                    //center of the hull
                    Rectangle currentHullRect = Submarine == null ?
                        WorldRect :
                        new Rectangle(
                            (int)(Submarine.DrawPosition.X + WorldPosition.X),
                            (int)(Submarine.DrawPosition.Y + WorldPosition.Y),
                            WorldRect.Width, WorldRect.Height);

                    GUI.DrawLine(spriteBatch,
                    new Vector2(currentHullRect.X, -currentHullRect.Y),
                    new Vector2(connectedHullRect.X, -connectedHullRect.Y),
                    GUI.Style.Green, width: 2);
                }
            }
        }

        public void DrawSectionColors(SpriteBatch spriteBatch)
        {
            if (BackgroundSections == null || BackgroundSections.Count == 0) { return; }
            Vector2 drawOffset = Submarine == null ? Vector2.Zero : Submarine.DrawPosition;
            Point sectionSize = BackgroundSections[0].Rect.Size;
            Vector2 drawPos = drawOffset + new Vector2(rect.Location.X + sectionSize.X / 2, rect.Location.Y - sectionSize.Y / 2);

            for (int i = 0; i < BackgroundSections.Count; i++)
            {
                BackgroundSection section = BackgroundSections[i];

                if (section.ColorStrength < 0.01f || section.Color.A < 1) { continue; }

                if (GameMain.DecalManager.GrimeSprites.Count == 0)
                {
                    GUI.DrawRectangle(spriteBatch,
                        new Vector2(drawOffset.X + rect.X + section.Rect.X, -(drawOffset.Y + rect.Y + section.Rect.Y)),
                        new Vector2(sectionSize.X, sectionSize.Y),
                        section.GetStrengthAdjustedColor(), true, 0.0f, (int)Math.Max(1.5f / Screen.Selected.Cam.Zoom, 1.0f));
                }
                else
                {
                    Vector2 sectionPos = new Vector2(drawPos.X + section.Rect.Location.X, -(drawPos.Y + section.Rect.Location.Y));
                    Vector2 randomOffset = new Vector2(section.Noise.X - 0.5f, section.Noise.Y - 0.5f) * 15.0f;
                    var sprite = GameMain.DecalManager.GrimeSprites[i % GameMain.DecalManager.GrimeSprites.Count];
                    sprite.Draw(spriteBatch, sectionPos + randomOffset, section.GetStrengthAdjustedColor(), scale: 1.25f);
                }
            }
        }

        public static void UpdateVertices(Camera cam, WaterRenderer renderer)
        {
            foreach (EntityGrid entityGrid in EntityGrids)
            {
                if (entityGrid.WorldRect.X > cam.WorldView.Right || entityGrid.WorldRect.Right < cam.WorldView.X) { continue; }
                if (entityGrid.WorldRect.Y - entityGrid.WorldRect.Height > cam.WorldView.Y || entityGrid.WorldRect.Y < cam.WorldView.Y - cam.WorldView.Height) { continue; }

                var allEntities = entityGrid.GetAllEntities();
                foreach (Hull hull in allEntities)
                {
                    hull.UpdateVertices(cam, entityGrid, renderer);
                }
            }
        }

        private void UpdateVertices(Camera cam, EntityGrid entityGrid, WaterRenderer renderer)
        {
            Vector2 submarinePos = Submarine == null ? Vector2.Zero : Submarine.DrawPosition;

            //if there's no more space in the buffer, don't render the water in the hull
            //not an ideal solution, but this seems to only happen in cases where the missing 
            //water is not very noticeable (e.g. zoomed very far out so that multiple subs and ruins are visible)
            if (renderer.PositionInBuffer > renderer.vertices.Length - 6)
            {
                return;
            }

            if (!renderer.IndoorsVertices.ContainsKey(entityGrid))
            {
                renderer.IndoorsVertices[entityGrid] = new VertexPositionColorTexture[WaterRenderer.DefaultIndoorsBufferSize];
                renderer.PositionInIndoorsBuffer[entityGrid] = 0;
            }

            //calculate where the surface should be based on the water volume
            float top = rect.Y + submarinePos.Y;
            float bottom = top - rect.Height;
            float renderSurface = drawSurface + submarinePos.Y;

            if (bottom > cam.WorldView.Y || top < cam.WorldView.Y - cam.WorldView.Height) return;

            Matrix transform = cam.Transform * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;            
            if (!update)
            {
                // create the four corners of our triangle.

                Vector3[] corners = new Vector3[4];

                corners[0] = new Vector3(rect.X, rect.Y, 0.0f);
                corners[1] = new Vector3(rect.X + rect.Width, rect.Y, 0.0f);

                corners[2] = new Vector3(corners[1].X, rect.Y - rect.Height, 0.0f);
                corners[3] = new Vector3(corners[0].X, corners[2].Y, 0.0f);

                Vector2[] uvCoords = new Vector2[4];
                for (int i = 0; i < 4; i++)
                {
                    corners[i] += new Vector3(submarinePos, 0.0f);
                    uvCoords[i] = Vector2.Transform(new Vector2(corners[i].X, -corners[i].Y), transform);
                }

                renderer.vertices[renderer.PositionInBuffer] = new VertexPositionTexture(corners[0], uvCoords[0]);
                renderer.vertices[renderer.PositionInBuffer + 1] = new VertexPositionTexture(corners[1], uvCoords[1]);
                renderer.vertices[renderer.PositionInBuffer + 2] = new VertexPositionTexture(corners[2], uvCoords[2]);

                renderer.vertices[renderer.PositionInBuffer + 3] = new VertexPositionTexture(corners[0], uvCoords[0]);
                renderer.vertices[renderer.PositionInBuffer + 4] = new VertexPositionTexture(corners[2], uvCoords[2]);
                renderer.vertices[renderer.PositionInBuffer + 5] = new VertexPositionTexture(corners[3], uvCoords[3]);

                renderer.PositionInBuffer += 6;

                return;
            }

            float x = rect.X;
            if (Submarine != null) { x += Submarine.DrawPosition.X; }

            int start = (int)Math.Floor((cam.WorldView.X - x) / WaveWidth);
            start = Math.Max(start, 0);

            int end = (waveY.Length - 1) - (int)Math.Floor(((x + rect.Width) - (cam.WorldView.Right)) / WaveWidth);
            end = Math.Min(end, waveY.Length - 1);

            x += start * WaveWidth;

            Vector3[] prevCorners = new Vector3[2];
            Vector2[] prevUVs = new Vector2[2];

            int width = WaveWidth;
            
            for (int i = start; i < end; i++)
            {
                Vector3[] corners = new Vector3[6];

                //top left
                corners[0] = new Vector3(x, top, 0.0f);
                //watersurface left
                corners[3] = new Vector3(corners[0].X, renderSurface + waveY[i], 0.0f);
                
                //top right
                corners[1] = new Vector3(x + width, top, 0.0f);
                //watersurface right
                corners[2] = new Vector3(corners[1].X, renderSurface + waveY[i + 1], 0.0f);

                //bottom left
                corners[4] = new Vector3(x, bottom, 0.0f);
                //bottom right
                corners[5] = new Vector3(x + width, bottom, 0.0f);
                
                Vector2[] uvCoords = new Vector2[4];
                for (int n = 0; n < 4; n++)
                {
                    uvCoords[n] = Vector2.Transform(new Vector2(corners[n].X, -corners[n].Y), transform);
                }

                if (renderer.PositionInBuffer <= renderer.vertices.Length - 6)
                {
                    if (i == start)
                    {
                        prevCorners[0] = corners[0];
                        prevCorners[1] = corners[3];
                        prevUVs[0] = uvCoords[0];
                        prevUVs[1] = uvCoords[3];
                    }

                    //we only create a new quad if this is the first or the last one, of if there's a wave large enough that we need more geometry
                    if (i == end - 1 || i == start || Math.Abs(prevCorners[1].Y - corners[2].Y) > 0.01f)
                    {
                        renderer.vertices[renderer.PositionInBuffer] = new VertexPositionTexture(prevCorners[0], prevUVs[0]);
                        renderer.vertices[renderer.PositionInBuffer + 1] = new VertexPositionTexture(corners[1], uvCoords[1]);
                        renderer.vertices[renderer.PositionInBuffer + 2] = new VertexPositionTexture(corners[2], uvCoords[2]);

                        renderer.vertices[renderer.PositionInBuffer + 3] = new VertexPositionTexture(prevCorners[0], prevUVs[0]);
                        renderer.vertices[renderer.PositionInBuffer + 4] = new VertexPositionTexture(corners[2], uvCoords[2]);
                        renderer.vertices[renderer.PositionInBuffer + 5] = new VertexPositionTexture(prevCorners[1], prevUVs[1]);

                        prevCorners[0] = corners[1];
                        prevCorners[1] = corners[2];
                        prevUVs[0] = uvCoords[1];
                        prevUVs[1] = uvCoords[2];

                        renderer.PositionInBuffer += 6;
                    }
                }

                if (renderer.PositionInIndoorsBuffer[entityGrid] <= renderer.IndoorsVertices[entityGrid].Length - 12 &&
                    cam.Zoom > 0.6f)
                {
                    const float SurfaceSize = 10.0f;
                    const float SineFrequency1 = 0.01f;
                    const float SineFrequency2 = 0.05f;

                    //surface shrinks and finally disappears when the water level starts to reach the top of the hull
                    float surfaceScale = 1.0f - MathHelper.Clamp(corners[3].Y - (top - SurfaceSize), 0.0f, 1.0f);

                    Vector3 surfaceOffset = new Vector3(0.0f, -SurfaceSize, 0.0f);
                    surfaceOffset.Y += (float)Math.Sin((rect.X + i * WaveWidth) * SineFrequency1 + renderer.WavePos.X * 0.25f) * 2;
                    surfaceOffset.Y += (float)Math.Sin((rect.X + i * WaveWidth) * SineFrequency2 - renderer.WavePos.X) * 2;
                    surfaceOffset *= surfaceScale;

                    Vector3 surfaceOffset2 = new Vector3(0.0f, -SurfaceSize, 0.0f);
                    surfaceOffset2.Y += (float)Math.Sin((rect.X + i * WaveWidth + width) * SineFrequency1 + renderer.WavePos.X * 0.25f) * 2;
                    surfaceOffset2.Y += (float)Math.Sin((rect.X + i * WaveWidth + width) * SineFrequency2 - renderer.WavePos.X) * 2;
                    surfaceOffset2 *= surfaceScale;

                    int posInBuffer = renderer.PositionInIndoorsBuffer[entityGrid];

                    renderer.IndoorsVertices[entityGrid][posInBuffer + 0] = new VertexPositionColorTexture(corners[3] + surfaceOffset, renderer.IndoorsWaterColor, Vector2.Zero);
                    renderer.IndoorsVertices[entityGrid][posInBuffer + 1] = new VertexPositionColorTexture(corners[2] + surfaceOffset2, renderer.IndoorsWaterColor, Vector2.Zero);
                    renderer.IndoorsVertices[entityGrid][posInBuffer + 2] = new VertexPositionColorTexture(corners[5], renderer.IndoorsWaterColor, Vector2.Zero);

                    renderer.IndoorsVertices[entityGrid][posInBuffer + 3] = new VertexPositionColorTexture(corners[3] + surfaceOffset, renderer.IndoorsWaterColor, Vector2.Zero);
                    renderer.IndoorsVertices[entityGrid][posInBuffer + 4] = new VertexPositionColorTexture(corners[5], renderer.IndoorsWaterColor, Vector2.Zero);
                    renderer.IndoorsVertices[entityGrid][posInBuffer + 5] = new VertexPositionColorTexture(corners[4], renderer.IndoorsWaterColor, Vector2.Zero);

                    posInBuffer += 6;
                    renderer.PositionInIndoorsBuffer[entityGrid] = posInBuffer;

                    if (surfaceScale > 0)
                    {
                        renderer.IndoorsVertices[entityGrid][posInBuffer + 0] = new VertexPositionColorTexture(corners[3], renderer.IndoorsSurfaceTopColor, Vector2.Zero);
                        renderer.IndoorsVertices[entityGrid][posInBuffer + 1] = new VertexPositionColorTexture(corners[2], renderer.IndoorsSurfaceTopColor, Vector2.Zero);
                        renderer.IndoorsVertices[entityGrid][posInBuffer + 2] = new VertexPositionColorTexture(corners[2] + surfaceOffset2, renderer.IndoorsSurfaceBottomColor, Vector2.Zero);

                        renderer.IndoorsVertices[entityGrid][posInBuffer + 3] = new VertexPositionColorTexture(corners[3], renderer.IndoorsSurfaceTopColor, Vector2.Zero);
                        renderer.IndoorsVertices[entityGrid][posInBuffer + 4] = new VertexPositionColorTexture(corners[2] + surfaceOffset2, renderer.IndoorsSurfaceBottomColor, Vector2.Zero);
                        renderer.IndoorsVertices[entityGrid][posInBuffer + 5] = new VertexPositionColorTexture(corners[3] + surfaceOffset, renderer.IndoorsSurfaceBottomColor, Vector2.Zero);

                        renderer.PositionInIndoorsBuffer[entityGrid] += 6;
                    }
                }

                x += WaveWidth;
                //clamp the last segment to the right edge of the hull
                if (i == end - 2)
                {
                    width -= (int)Math.Max((x + WaveWidth) - (Submarine == null ? rect.Right : (rect.Right + Submarine.DrawPosition.X)), 0);
                }
            }
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            if (extraData == null)
            {
                msg.WriteRangedInteger(0, 0, 2);
                msg.WriteRangedSingle(MathHelper.Clamp(waterVolume / Volume, 0.0f, 1.5f), 0.0f, 1.5f, 8);

                msg.Write(FireSources.Count > 0);
                if (FireSources.Count > 0)
                {
                    msg.WriteRangedInteger(Math.Min(FireSources.Count, 16), 0, 16);
                    for (int i = 0; i < Math.Min(FireSources.Count, 16); i++)
                    {
                        var fireSource = FireSources[i];
                        Vector2 normalizedPos = new Vector2(
                            (fireSource.Position.X - rect.X) / rect.Width,
                            (fireSource.Position.Y - (rect.Y - rect.Height)) / rect.Height);

                        msg.WriteRangedSingle(MathHelper.Clamp(normalizedPos.X, 0.0f, 1.0f), 0.0f, 1.0f, 8);
                        msg.WriteRangedSingle(MathHelper.Clamp(normalizedPos.Y, 0.0f, 1.0f), 0.0f, 1.0f, 8);
                        msg.WriteRangedSingle(MathHelper.Clamp(fireSource.Size.X / rect.Width, 0.0f, 1.0f), 0, 1.0f, 8);
                    }
                }
            }
            else if (extraData[0] is Decal decal)
            {
                msg.WriteRangedInteger(1, 0, 2);
                int decalIndex = decals.IndexOf(decal);
                msg.Write((byte)(decalIndex < 0 ? 255 : decalIndex));
                msg.WriteRangedSingle(decal.BaseAlpha, 0.0f, 1.0f, 8);
            }
            else
            {
                msg.WriteRangedInteger(2, 0, 2);
                int sectorToUpdate = (int)extraData[0];
                int start = sectorToUpdate * BackgroundSectionsPerNetworkEvent;
                int end = Math.Min((sectorToUpdate + 1) * BackgroundSectionsPerNetworkEvent, BackgroundSections.Count - 1);
                msg.WriteRangedInteger(sectorToUpdate, 0, BackgroundSections.Count - 1);
                for (int i = start; i < end; i++)
                {
                    msg.WriteRangedSingle(BackgroundSections[i].ColorStrength, 0.0f, 1.0f, 8);
                    msg.Write(BackgroundSections[i].Color.PackedValue);
                }
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage message, float sendingTime)
        {
            bool isBallastFloraUpdate = message.ReadBoolean();
            if (isBallastFloraUpdate)
            {
                BallastFloraBehavior.NetworkHeader header = (BallastFloraBehavior.NetworkHeader) message.ReadByte();
                if (header == BallastFloraBehavior.NetworkHeader.Spawn)
                {
                    string identifier = message.ReadString();
                    float x = message.ReadSingle();
                    float y = message.ReadSingle();
                    BallastFlora = new BallastFloraBehavior(this, BallastFloraPrefab.Find(identifier), new Vector2(x, y), firstGrowth: true)
                    {
                        PowerConsumptionTimer = message.ReadSingle()
                    };
                }
                else if (BallastFlora != null)
                {
                    BallastFlora.ClientRead(message, header);
                }
                return;
            }
            remoteWaterVolume = message.ReadRangedSingle(0.0f, 1.5f, 8) * Volume;
            remoteOxygenPercentage = message.ReadRangedSingle(0.0f, 100.0f, 8);

            bool hasFireSources = message.ReadBoolean();
            remoteFireSources = new List<Vector3>();
            if (hasFireSources)
            {
                int fireSourceCount = message.ReadRangedInteger(0, 16);
                for (int i = 0; i < fireSourceCount; i++)
                {
                    remoteFireSources.Add(new Vector3(
                        MathHelper.Clamp(message.ReadRangedSingle(0.0f, 1.0f, 8), 0.05f, 0.95f),
                        MathHelper.Clamp(message.ReadRangedSingle(0.0f, 1.0f, 8), 0.05f, 0.95f),
                        message.ReadRangedSingle(0.0f, 1.0f, 8)));
                }
            }

            bool hasExtraData = message.ReadBoolean();
            if (hasExtraData)
            {
                bool hasSectionUpdate = message.ReadBoolean();
                if (hasSectionUpdate)
                {
                    int sectorToUpdate = message.ReadRangedInteger(0, BackgroundSections.Count - 1);
                    int start = sectorToUpdate * BackgroundSectionsPerNetworkEvent;
                    int end = Math.Min((sectorToUpdate + 1) * BackgroundSectionsPerNetworkEvent, BackgroundSections.Count - 1);
                    for (int i = start; i < end; i++)
                    {
                        float colorStrength = message.ReadRangedSingle(0.0f, 1.0f, 8);
                        Color color = new Color(message.ReadUInt32());
                        var remoteBackgroundSection = remoteBackgroundSections.Find(s => s.Index == i);
                        if (remoteBackgroundSection != null)
                        {
                            remoteBackgroundSection.SetColorStrength(colorStrength);
                            remoteBackgroundSection.SetColor(color);
                        }
                        else
                        {
                            remoteBackgroundSections.Add(new BackgroundSection(new Rectangle(0, 0, 1, 1), i, colorStrength, color, 0));
                        }
                    }
                    paintAmount = BackgroundSections.Sum(s => s.ColorStrength);
                }
                else
                {
                    int decalCount = message.ReadRangedInteger(0, MaxDecalsPerHull);
                    if (decalCount == 0) { decals.Clear(); }
                    remoteDecals.Clear();
                    for (int i = 0; i < decalCount; i++)
                    {
                        UInt32 decalId = message.ReadUInt32();
                        int spriteIndex = message.ReadByte();
                        float normalizedXPos = message.ReadRangedSingle(0.0f, 1.0f, 8);
                        float normalizedYPos = message.ReadRangedSingle(0.0f, 1.0f, 8);
                        float decalScale = message.ReadRangedSingle(0.0f, 2.0f, 12);
                        remoteDecals.Add(new RemoteDecal(decalId, spriteIndex, new Vector2(normalizedXPos, normalizedYPos), decalScale));
                    }
                }
            }

            if (serverUpdateDelay > 0.0f) { return; }

            ApplyRemoteState();
        }

        private void ApplyRemoteState()
        {
            foreach (BackgroundSection remoteBackgroundSection in remoteBackgroundSections)
            {
                float prevColorStrength = BackgroundSections[remoteBackgroundSection.Index].ColorStrength;
                BackgroundSections[remoteBackgroundSection.Index].SetColor(remoteBackgroundSection.Color);
                BackgroundSections[remoteBackgroundSection.Index].SetColorStrength(remoteBackgroundSection.ColorStrength);
                paintAmount = Math.Max(0, paintAmount + (BackgroundSections[remoteBackgroundSection.Index].ColorStrength - prevColorStrength) / BackgroundSections.Count);
            }
            remoteBackgroundSections.Clear();

            if (remoteDecals.Any())
            {
                decals.Clear();
                foreach (RemoteDecal remoteDecal in remoteDecals)
                {
                    float decalPosX = MathHelper.Lerp(rect.X, rect.Right, remoteDecal.NormalizedPos.X);
                    float decalPosY = MathHelper.Lerp(rect.Y - rect.Height, rect.Y, remoteDecal.NormalizedPos.Y);
                    if (Submarine != null)
                    {
                        decalPosX += Submarine.Position.X;
                        decalPosY += Submarine.Position.Y;
                    }
                    AddDecal(remoteDecal.DecalId, new Vector2(decalPosX, decalPosY), remoteDecal.Scale, isNetworkEvent: true, spriteIndex: remoteDecal.SpriteIndex);
                }
                remoteDecals.Clear();
            }

            if (remoteFireSources == null) { return; }

            WaterVolume = remoteWaterVolume;
            OxygenPercentage = remoteOxygenPercentage;

            for (int i = 0; i < remoteFireSources.Count; i++)
            {
                Vector2 pos = new Vector2(
                    rect.X + rect.Width * remoteFireSources[i].X,
                    rect.Y - rect.Height + (rect.Height * remoteFireSources[i].Y));
                float size = remoteFireSources[i].Z * rect.Width;

                var newFire = i < FireSources.Count ?
                    FireSources[i] :
                    new FireSource(Submarine == null ? pos : pos + Submarine.Position, null, true);
                newFire.Position = pos;
                newFire.Size = new Vector2(size, newFire.Size.Y);

                //ignore if the fire wasn't added to this room (invalid position)?
                if (!FireSources.Contains(newFire))
                {
                    newFire.Remove();
                    continue;
                }
            }

            for (int i = FireSources.Count - 1; i >= remoteFireSources.Count; i--)
            {
                FireSources[i].Remove();
                if (i < FireSources.Count)
                {
                    FireSources.RemoveAt(i);
                }
            }
            remoteFireSources = null;
        }
    }
}
