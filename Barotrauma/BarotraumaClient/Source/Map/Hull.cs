using Barotrauma.Networking;
using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Hull : MapEntity, IPropertyObject, IServerSerializable
    {
        public const int MaxDecalsPerHull = 10;

        public static WaterRenderer renderer;

        private List<Decal> decals = new List<Decal>();

        private Sound currentFlowSound;
        private int soundIndex;
        private float soundVolume;

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
        
        partial void UpdateProjSpecific(float deltaTime, Camera cam)
        {
            if (EditWater)
            {
                Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
                if (Submarine.RectContains(WorldRect, position))
                {
                    if (PlayerInput.LeftButtonHeld())
                    {
                        //waveY[GetWaveIndex(position.X - rect.X - Submarine.Position.X) / WaveWidth] = 100.0f;
                        Volume = Volume + 1500.0f;
                    }
                    else if (PlayerInput.RightButtonHeld())
                    {
                        Volume = Volume - 1500.0f;
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

            float strongestFlow = 0.0f;
            foreach (Gap gap in ConnectedGaps)
            {
                if (gap.IsRoomToRoom)
                {
                    //only the first linked hull plays the flow sound
                    if (gap.linkedTo[1] == this) continue;
                }

                float gapFlow = gap.LerpedFlowForce.Length();

                if (gapFlow > strongestFlow)
                {
                    strongestFlow = gapFlow;
                }
            }

            if (strongestFlow > 1.0f)
            {
                soundVolume = soundVolume + ((strongestFlow < 100.0f) ? -deltaTime * 0.5f : deltaTime * 0.5f);
                soundVolume = MathHelper.Clamp(soundVolume, 0.0f, 1.0f);

                int index = (int)Math.Floor(strongestFlow / 100.0f);
                index = Math.Min(index, 2);

                var flowSound = SoundPlayer.flowSounds[index];
                if (flowSound != currentFlowSound && soundIndex > -1)
                {
                    Sounds.SoundManager.Stop(soundIndex);
                    currentFlowSound = null;
                    soundIndex = -1;
                }

                currentFlowSound = flowSound;
                soundIndex = currentFlowSound.Loop(soundIndex, soundVolume, WorldPosition, Math.Min(strongestFlow * 5.0f, 2000.0f));
            }
            else
            {
                if (soundIndex > -1)
                {
                    Sounds.SoundManager.Stop(soundIndex);
                    currentFlowSound = null;
                    soundIndex = -1;
                }
            }

            for (int i = 0; i < waveY.Length; i++)
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
                        
            foreach (Decal d in decals)
            {
                d.Draw(spriteBatch, this);
            }
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (back && Screen.Selected != GameMain.EditMapScreen)
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

            if ((Character.Controlled == null && GameMain.NilMod.ShowRoomInfo) || GameMain.DebugDraw)
            {
                drawRect =
                Submarine == null ? rect : new Rectangle((int)(Submarine.DrawPosition.X + rect.X), (int)(Submarine.DrawPosition.Y + rect.Y), rect.Width, rect.Height);

                if (rect.Width >= 640 && rect.Height >= 200)
                {
                    GUI.SmallFont.DrawString(spriteBatch, "PRESS: " + ((int)pressure - rect.Y).ToString() +
                    " - OXY: " + ((int)OxygenPercentage), new Vector2(drawRect.X + 45, -drawRect.Y + 35), Color.White, 0f, new Vector2(0, 0), 2.0f, SpriteEffects.None, 0f);
                    if(GameMain.DebugDraw) GUI.SmallFont.DrawString(spriteBatch, volume + " / " + FullVolume, new Vector2(drawRect.X + 45, -drawRect.Y + 60), Color.White, 0f, new Vector2(0, 0), 2.0f, SpriteEffects.None, 0f);
                }
                else if (rect.Width >= 320 && rect.Height >= 160)
                {
                    GUI.SmallFont.DrawString(spriteBatch, "PRESS: " + ((int)pressure - rect.Y).ToString() +
                    " - OXY: " + ((int)OxygenPercentage), new Vector2(drawRect.X + 45, -drawRect.Y + 35), Color.White, 0f, new Vector2(0, 0), 1.5f, SpriteEffects.None, 0f);
                    if (GameMain.DebugDraw) GUI.SmallFont.DrawString(spriteBatch, volume + " / " + FullVolume, new Vector2(drawRect.X + 45, -drawRect.Y + 60), Color.White, 0f, new Vector2(0, 0), 1.5f, SpriteEffects.None, 0f);
                }
                else if (rect.Width >= 160 && rect.Height >= 120)
                {
                    GUI.SmallFont.DrawString(spriteBatch, "PRESS: " + ((int)pressure - rect.Y).ToString() +
                    " - OXY: " + ((int)OxygenPercentage), new Vector2(drawRect.X + 40, -drawRect.Y + 30), Color.White, 0f, new Vector2(0, 0), 1.0f, SpriteEffects.None, 0f);
                    if (GameMain.DebugDraw) GUI.SmallFont.DrawString(spriteBatch, volume + " / " + FullVolume, new Vector2(drawRect.X + 45, -drawRect.Y + 40), Color.White, 0f, new Vector2(0, 0), 1.0f, SpriteEffects.None, 0f);
                }
                else if (rect.Width >= 100 && rect.Height >= 60)
                {
                    GUI.SmallFont.DrawString(spriteBatch, "PRESS: " + ((int)pressure - rect.Y).ToString() +
                    " - OXY: " + ((int)OxygenPercentage), new Vector2(drawRect.X + 40, -drawRect.Y + 25), Color.White, 0f, new Vector2(0, 0), 0.50f, SpriteEffects.None, 0f);
                    if (GameMain.DebugDraw) GUI.SmallFont.DrawString(spriteBatch, volume + " / " + FullVolume, new Vector2(drawRect.X + 40, -drawRect.Y + 35), Color.White, 0f, new Vector2(0, 0), 0.50f, SpriteEffects.None, 0f);
                }
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
                foreach (FireSource fs in fireSources)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)fs.WorldPosition.X, (int)-fs.WorldPosition.Y, (int)fs.Size.X, (int)fs.Size.Y), Color.Orange, false);
                }
            }

            if ((IsSelected || isHighlighted) && editing)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(drawRect.X + 5, -drawRect.Y + 5),
                    new Vector2(rect.Width - 10, rect.Height - 10),
                    isHighlighted ? Color.LightBlue * 0.5f : Color.Red * 0.5f, true, 0, (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));
            }
        }

        public void Render(GraphicsDevice graphicsDevice, Camera cam)
        {
            if (renderer.PositionInBuffer > renderer.vertices.Length - 6) return;

            Vector2 submarinePos = Submarine == null ? Vector2.Zero : Submarine.DrawPosition;

            //calculate where the surface should be based on the water volume
            float top = rect.Y + submarinePos.Y;
            float bottom = top - rect.Height;

            float drawSurface = surface + submarinePos.Y;

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

            for (int i = start; i < end; i++)
            {
                if (renderer.PositionInBuffer > renderer.vertices.Length - 6) return;

                Vector3[] corners = new Vector3[4];

                corners[0] = new Vector3(x, top, 0.0f);
                corners[3] = new Vector3(corners[0].X, drawSurface + waveY[i], 0.0f);

                //skip adjacent "water rects" if the surface of the water is roughly at the same position
                int width = WaveWidth;
                while (i < end - 1 && Math.Abs(waveY[i + 1] - waveY[i]) < 1.0f)
                {
                    width += WaveWidth;
                    i++;
                }

                corners[1] = new Vector3(x + width, top, 0.0f);
                corners[2] = new Vector3(corners[1].X, drawSurface + waveY[i + 1], 0.0f);

                Vector2[] uvCoords = new Vector2[4];
                for (int n = 0; n < 4; n++)
                {
                    uvCoords[n] = Vector2.Transform(new Vector2(corners[n].X, -corners[n].Y), transform);
                }

                renderer.vertices[renderer.PositionInBuffer] = new VertexPositionTexture(corners[0], uvCoords[0]);
                renderer.vertices[renderer.PositionInBuffer + 1] = new VertexPositionTexture(corners[1], uvCoords[1]);
                renderer.vertices[renderer.PositionInBuffer + 2] = new VertexPositionTexture(corners[2], uvCoords[2]);

                renderer.vertices[renderer.PositionInBuffer + 3] = new VertexPositionTexture(corners[0], uvCoords[0]);
                renderer.vertices[renderer.PositionInBuffer + 4] = new VertexPositionTexture(corners[2], uvCoords[2]);
                renderer.vertices[renderer.PositionInBuffer + 5] = new VertexPositionTexture(corners[3], uvCoords[3]);

                renderer.PositionInBuffer += 6;

                x += width;
            }

        }


        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Hull");

            element.Add
            (
                new XAttribute("ID", ID),
                new XAttribute("rect",
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                    rect.Width + "," + rect.Height),
                new XAttribute("water", volume)
            );

            parentElement.Add(element);

            return element;
        }
    }
}
