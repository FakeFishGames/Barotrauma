using Barotrauma.Networking;
using Barotrauma.Particles;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using System.Linq;

namespace Barotrauma
{
    partial class Hull : MapEntity, ISerializableEntity, IServerSerializable
    {
        public const int MaxDecalsPerHull = 10;
        
        private List<Decal> decals = new List<Decal>();

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
                return decals.Count > 0;
            }
        }

        public override bool DrawOverWater
        {
            get
            {
                return true;
            }
        }
        
        public override bool IsMouseOn(Vector2 position)
        {
            if (!GameMain.DebugDraw && !ShowHulls) return false;

            return (Submarine.RectContains(WorldRect, position) &&
                !Submarine.RectContains(MathUtils.ExpandRect(WorldRect, -8), position));
        }

        public Decal AddDecal(string decalName, Vector2 worldPosition, float scale = 1.0f)
        {
            if (decals.Count >= MaxDecalsPerHull) return null;

            var decal = GameMain.DecalManager.CreateDecal(decalName, scale, worldPosition, this);

            if (decal != null)
            {
                decals.Add(decal);
            }

            return decal;
        }
        
        private GUIComponent CreateEditingHUD(bool inGame = false)
        {
            editingHUD = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.25f), GUI.Canvas, Anchor.CenterRight) { MinSize = new Point(400, 0) }) { UserData = this };
            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(0.95f, 0.8f), editingHUD.RectTransform, Anchor.Center), style: null);
            new SerializableEntityEditor(listBox.Content.RectTransform, this, inGame, showName: true);

            PositionEditingHUD();

            return editingHUD;
        }

        public override void UpdateEditing(Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData as Hull != this)
            {
                editingHUD = CreateEditingHUD(Screen.Selected != GameMain.SubEditorScreen);
            }
            
            if (!PlayerInput.KeyDown(Keys.Space)) return;
            bool lClick = PlayerInput.LeftButtonClicked();
            bool rClick = PlayerInput.RightButtonClicked();
            if (!lClick && !rClick) return;

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

            if (lClick)
            {
                foreach (MapEntity entity in mapEntityList)
                {
                    if (entity == this || !entity.IsHighlighted) continue;
                    if (!entity.IsMouseOn(position)) continue;
                    
                    if (entity.Linkable && entity.linkedTo != null) entity.linkedTo.Add(this);
                }
            }
            else
            {
                foreach (MapEntity entity in mapEntityList)
                {
                    if (entity == this || !entity.IsHighlighted) continue;
                    if (!entity.IsMouseOn(position)) continue;
                    
                    if (entity.linkedTo != null && entity.linkedTo.Contains(this)) entity.linkedTo.Remove(this);
                }
            }
        }

        partial void UpdateProjSpecific(float deltaTime, Camera cam)
        {
            if (EditWater)
            {
                Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
                if (Submarine.RectContains(WorldRect, position))
                {
                    if (PlayerInput.LeftButtonHeld())
                    {
                        WaterVolume += 1500.0f;
                    }
                    else if (PlayerInput.RightButtonHeld())
                    {
                        WaterVolume -= 1500.0f;
                    }
                }
            }
            else if (EditFire)
            {
                Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
                if (Submarine.RectContains(WorldRect, position))
                {
                    if (PlayerInput.LeftButtonClicked())
                    {
                        new FireSource(position, this);
                    }
                }
            }

            foreach (Decal decal in decals)
            {
                decal.Update(deltaTime);
            }

            decals.RemoveAll(d => d.FadeTimer >= d.LifeTime);
                        
            if (waterVolume < 1.0f) return;
            for (int i = 1; i < waveY.Length - 1; i++)
            {
                float maxDelta = Math.Max(Math.Abs(rightDelta[i]), Math.Abs(leftDelta[i]));
                if (maxDelta > Rand.Range(1.0f, 10.0f))
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
                DrawDecals(spriteBatch);
                return;
            }

            Rectangle drawRect;
            if (!Visible)
            {
                drawRect =
                    Submarine == null ? rect : new Rectangle((int)(Submarine.DrawPosition.X + rect.X), (int)(Submarine.DrawPosition.Y + rect.Y), rect.Width, rect.Height);

                GUI.DrawRectangle(spriteBatch,
                    new Vector2(drawRect.X, -drawRect.Y),
                    new Vector2(rect.Width, rect.Height),
                    Color.Black, true,
                    0, (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));
            }

            if (!ShowHulls && !GameMain.DebugDraw) return;

            if (!editing && !GameMain.DebugDraw) return;

            if (aiTarget != null) aiTarget.Draw(spriteBatch);

            drawRect =
                Submarine == null ? rect : new Rectangle((int)(Submarine.DrawPosition.X + rect.X), (int)(Submarine.DrawPosition.Y + rect.Y), rect.Width, rect.Height);

            GUI.DrawRectangle(spriteBatch,
                new Vector2(drawRect.X, -drawRect.Y),
                new Vector2(rect.Width, rect.Height),
                Color.Blue, false, (ID % 255) * 0.000001f, (int)Math.Max((1.5f / Screen.Selected.Cam.Zoom), 1.0f));

            GUI.DrawRectangle(spriteBatch,
                new Rectangle(drawRect.X, -drawRect.Y, rect.Width, rect.Height),
                Color.Red * ((100.0f - OxygenPercentage) / 400.0f), true, 0, (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));

            if (GameMain.DebugDraw)
            {
                GUI.SmallFont.DrawString(spriteBatch, "Pressure: " + ((int)pressure - rect.Y).ToString() +
                    " - Oxygen: " + ((int)OxygenPercentage), new Vector2(drawRect.X + 5, -drawRect.Y + 5), Color.White);
                GUI.SmallFont.DrawString(spriteBatch, waterVolume + " / " + Volume, new Vector2(drawRect.X + 5, -drawRect.Y + 20), Color.White);

                GUI.DrawRectangle(spriteBatch, new Rectangle(drawRect.Center.X, -drawRect.Y + drawRect.Height / 2, 10, (int)(100 * Math.Min(waterVolume / Volume, 1.0f))), Color.Cyan, true);
                if (WaterVolume > Volume)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle(drawRect.Center.X, -drawRect.Y + drawRect.Height / 2, 10, (int)(100 * (waterVolume - Volume) / MaxCompress)), Color.Red, true);
                }
                GUI.DrawRectangle(spriteBatch, new Rectangle(drawRect.Center.X, -drawRect.Y + drawRect.Height / 2, 10, 100), Color.Black);

                foreach (FireSource fs in fireSources)
                {
                    Rectangle fireSourceRect = new Rectangle((int)fs.WorldPosition.X, -(int)fs.WorldPosition.Y, (int)fs.Size.X, (int)fs.Size.Y);
                    GUI.DrawRectangle(spriteBatch, fireSourceRect, Color.Orange, false, 0, 5);
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

            if ((IsSelected || isHighlighted) && editing)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(drawRect.X + 5, -drawRect.Y + 5),
                    new Vector2(rect.Width - 10, rect.Height - 10),
                    isHighlighted ? Color.LightBlue * 0.5f : Color.Red * 0.5f, true, 0, (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));
            }
        }

        public void UpdateVertices(GraphicsDevice graphicsDevice, Camera cam, WaterRenderer renderer)
        {
            Vector2 submarinePos = Submarine == null ? Vector2.Zero : Submarine.DrawPosition;

            if (!renderer.IndoorsVertices.ContainsKey(Submarine))
            {
                renderer.IndoorsVertices[Submarine] = new VertexPositionColorTexture[WaterRenderer.DefaultIndoorsBufferSize];
                renderer.PositionInIndoorsBuffer[Submarine] = 0;
            }

            //calculate where the surface should be based on the water volume
            float top = rect.Y + submarinePos.Y;
            float bottom = top - rect.Height;

            float renderSurface = drawSurface + submarinePos.Y;

            Matrix transform = cam.Transform * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;
            
            if (bottom > cam.WorldView.Y || top < cam.WorldView.Y - cam.WorldView.Height) return;

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

            float x = rect.X + Submarine.DrawPosition.X;
            int start = (int)Math.Floor((cam.WorldView.X - x) / WaveWidth);
            start = Math.Max(start, 0);

            int end = (waveY.Length - 1)
                - (int)Math.Floor((float)((x + rect.Width) - (cam.WorldView.X + cam.WorldView.Width)) / WaveWidth);
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
                    if (i == end - 1 || i == start || Math.Abs(prevCorners[1].Y - corners[3].Y) > 1.0f)
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

                if (renderer.PositionInIndoorsBuffer[Submarine] <= renderer.IndoorsVertices[Submarine].Length - 12)
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

                    int posInBuffer = renderer.PositionInIndoorsBuffer[Submarine];

                    renderer.IndoorsVertices[Submarine][posInBuffer + 0] = new VertexPositionColorTexture(corners[3] + surfaceOffset, renderer.IndoorsWaterColor, Vector2.Zero);
                    renderer.IndoorsVertices[Submarine][posInBuffer + 1] = new VertexPositionColorTexture(corners[2] + surfaceOffset2, renderer.IndoorsWaterColor, Vector2.Zero);
                    renderer.IndoorsVertices[Submarine][posInBuffer + 2] = new VertexPositionColorTexture(corners[5], renderer.IndoorsWaterColor, Vector2.Zero);

                    renderer.IndoorsVertices[Submarine][posInBuffer + 3] = new VertexPositionColorTexture(corners[3] + surfaceOffset, renderer.IndoorsWaterColor, Vector2.Zero);
                    renderer.IndoorsVertices[Submarine][posInBuffer + 4] = new VertexPositionColorTexture(corners[5], renderer.IndoorsWaterColor, Vector2.Zero);
                    renderer.IndoorsVertices[Submarine][posInBuffer + 5] = new VertexPositionColorTexture(corners[4], renderer.IndoorsWaterColor, Vector2.Zero);

                    posInBuffer += 6;
                    renderer.PositionInIndoorsBuffer[Submarine] = posInBuffer;

                    if (surfaceScale > 0)
                    {
                        renderer.IndoorsVertices[Submarine][posInBuffer + 0] = new VertexPositionColorTexture(corners[3], renderer.IndoorsSurfaceTopColor, Vector2.Zero);
                        renderer.IndoorsVertices[Submarine][posInBuffer + 1] = new VertexPositionColorTexture(corners[2], renderer.IndoorsSurfaceTopColor, Vector2.Zero);
                        renderer.IndoorsVertices[Submarine][posInBuffer + 2] = new VertexPositionColorTexture(corners[2] + surfaceOffset2, renderer.IndoorsSurfaceBottomColor, Vector2.Zero);

                        renderer.IndoorsVertices[Submarine][posInBuffer + 3] = new VertexPositionColorTexture(corners[3], renderer.IndoorsSurfaceTopColor, Vector2.Zero);
                        renderer.IndoorsVertices[Submarine][posInBuffer + 4] = new VertexPositionColorTexture(corners[2] + surfaceOffset2, renderer.IndoorsSurfaceBottomColor, Vector2.Zero);
                        renderer.IndoorsVertices[Submarine][posInBuffer + 5] = new VertexPositionColorTexture(corners[3] + surfaceOffset, renderer.IndoorsSurfaceBottomColor, Vector2.Zero);

                        renderer.PositionInIndoorsBuffer[Submarine] += 6;
                    }
                }

                x += WaveWidth;
                //clamp the last segment to the right edge of the hull
                if (i == end - 2)
                {
                    width -= (int)Math.Max((x + WaveWidth) - (rect.Right + Submarine.DrawPosition.X), 0);
                }
            }
        }        
    }
}
