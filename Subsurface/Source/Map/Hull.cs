using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Lidgren.Network;

namespace Barotrauma
{

    class Hull : MapEntity
    {
        public static List<Hull> hullList = new List<Hull>();

        public static bool EditWater;

        public static WaterRenderer renderer;
        
        public const float OxygenDistributionSpeed = 500.0f;
        public const float OxygenDetoriationSpeed = 0.3f;
        public const float OxygenConsumptionSpeed = 1000.0f;

        public const int WaveWidth = 16;
        const float WaveStiffness = 0.003f;
        const float WaveSpread = 0.05f;
        const float WaveDampening = 0.01f;

        //how much excess water the room can contain  (= more than the volume of the room)
        public const float MaxCompress = 10000f;
        
        public readonly Dictionary<string, PropertyDescriptor> properties;

        private float lethalPressure;

        private float surface;
        private float volume;
        private float pressure;

        private float oxygen;

        private bool update;

        float[] waveY; //displacement from the surface of the water
        float[] waveVel; //velocity of the point

        float[] leftDelta;
        float[] rightDelta;

        float lastSentVolume;

        public override string Name
        {
            get
            {
                return "Hull";
            }
        }

        public override bool IsLinkable
        {
            get { return true; }
        }

        public float LethalPressure
        {
            get { return lethalPressure; }
            set { lethalPressure = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public Vector2 Size
        {
            get { return new Vector2(rect.Width, rect.Height); }
        }

        public float Surface
        {
            get { return surface; }
        }

        public float Volume
        {
            get { return volume; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                volume = MathHelper.Clamp(value, 0.0f, FullVolume + MaxCompress);
                if (volume < FullVolume) Pressure = rect.Y - rect.Height + volume / rect.Width;
                if (volume > 0.0f) update = true;
            }
        }

        [HasDefaultValue(90.0f, true)]
        public float Oxygen
        {
            get { return oxygen; }
            set { oxygen = MathHelper.Clamp(value, 0.0f, FullVolume); }
        }

        public float OxygenPercentage
        {
            get { return oxygen / FullVolume * 100.0f; }
            set { Oxygen = (value / 100.0f) * FullVolume; }
        }

        public float FullVolume
        {
            get { return (rect.Width * rect.Height); }
        }

        public float Pressure
        {
            get { return pressure; }
            set { pressure = value; }
        }

        public float[] WaveY
        {
            get { return waveY; }
        }

        public float[] WaveVel
        {
            get { return waveVel; }
        }

        public Hull(Rectangle rectangle)
        {
            rect = rectangle;
            
            OxygenPercentage = Rand.Range(90.0f, 100.0f, false);

            properties = TypeDescriptor.GetProperties(GetType())
                .Cast<PropertyDescriptor>()
                .ToDictionary(pr => pr.Name);

            int arraySize = (rectangle.Width / WaveWidth + 1);
            waveY = new float[arraySize];
            waveVel = new float[arraySize];

            leftDelta = new float[arraySize];
            rightDelta = new float[arraySize];

            surface = rect.Y - rect.Height;

            aiTarget = new AITarget(this);
            aiTarget.SightRange = (rect.Width + rect.Height)*10.0f;

            hullList.Add(this);

            Item.UpdateHulls();
            Gap.UpdateHulls();

            Volume = 0.0f;

            InsertToList();
        }

        public override bool Contains(Vector2 position)
        {
            return (Submarine.RectContains(rect, position) &&
                !Submarine.RectContains(new Rectangle(rect.X + 8, rect.Y - 8, rect.Width - 16, rect.Height - 16), position));
        }

        public int GetWaveIndex(Vector2 position)
        {
            return GetWaveIndex(position.X);
        }

        public int GetWaveIndex(float xPos)
        {
            int index = (int)(xPos - rect.X) / WaveWidth;
            index = (int)MathHelper.Clamp(index, 0, waveY.Length - 1);
            return index;
        }

        public override void Move(Vector2 amount)
        {
            rect.X += (int)amount.X;
            rect.Y += (int)amount.Y;

            Item.UpdateHulls();
            Gap.UpdateHulls();
        }

        public override void Remove()
        {
            base.Remove();

            Item.UpdateHulls();
            Gap.UpdateHulls();

            //renderer.Dispose();

            hullList.Remove(this);
        }

        public override void Update(Camera cam, float deltaTime)
        {
            Oxygen -= OxygenDetoriationSpeed * deltaTime;

            if (EditWater)
            {
                Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
                if (Submarine.RectContains(rect, position))
                {
                    if (PlayerInput.LeftButtonDown())
                    {
                        waveY[(int)(position.X - rect.X) / WaveWidth] = 100.0f;
                        Volume = Volume + 1500.0f;
                    }
                    else if (PlayerInput.RightButtonDown())
                    {
                        Volume = Volume - 1500.0f;
                    }
                }
            }

            //update client hulls if the amount of water has changed by >10%
            if (Math.Abs(lastSentVolume - volume) > FullVolume * 0.1f)
            {
                new Networking.NetworkEvent(ID, false);
                lastSentVolume = volume;
            }
            if (!update) return;

            float surfaceY = rect.Y - rect.Height + Volume / rect.Width;
            for (int i = 0; i < waveY.Length; i++)
            {
                float maxDelta = Math.Max(Math.Abs(rightDelta[i]), Math.Abs(leftDelta[i]));
                if (maxDelta > Rand.Range(1.0f,10.0f))
                {
                    GameMain.ParticleManager.CreateParticle("mist",
                        new Vector2(rect.X + WaveWidth * i,surface + waveY[i]),
                        new Vector2(0.0f, -50.0f));
                }

                waveY[i] = waveY[i] + waveVel[i];

                if (surfaceY + waveY[i] > rect.Y)
                {
                    waveY[i] -= (surfaceY + waveY[i]) - rect.Y;
                    waveVel[i] = waveVel[i] * -0.5f;
                }
                else if (surfaceY + waveY[i] < rect.Y - rect.Height)
                {
                    waveY[i] -= (surfaceY + waveY[i]) - (rect.Y - rect.Height);
                    waveVel[i] = waveVel[i] * -0.5f;
                }


                //acceleration
                float a = -WaveStiffness * waveY[i] - waveVel[i] * WaveDampening;
                waveVel[i] = waveVel[i] + a;

            }

            for (int j = 0; j < 2; j++)
            {
                for (int i = 1; i < waveY.Length - 1; i++)
                {
                    leftDelta[i] = WaveSpread * (waveY[i] - waveY[i - 1]);
                    waveVel[i - 1] = waveVel[i - 1] + leftDelta[i];

                    rightDelta[i] = WaveSpread * (waveY[i] - waveY[i + 1]);
                    waveVel[i + 1] = waveVel[i + 1] + rightDelta[i];
                }

                for (int i = 1; i < waveY.Length - 1; i++)
                {
                    waveY[i - 1] = waveY[i - 1] + leftDelta[i];
                    waveY[i + 1] = waveY[i + 1] + rightDelta[i];
                }
            }

            if (volume < FullVolume)
            {
                LethalPressure -= 10.0f * deltaTime;
                if (Volume == 0.0f)
                {
                    for (int i = 1; i < waveY.Length - 1; i++)
                    {
                        if (waveY[i] > 0.1f) return;
                    }
                    update = false;
                }
            }
            else
            {
                
                LethalPressure += ( Submarine.Loaded!=null && Submarine.Loaded.AtDamageDepth) ? 100.0f*deltaTime : 10.0f * deltaTime;
            }


        }

        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (!editing && !GameMain.DebugDraw) return;

            GUI.DrawRectangle(spriteBatch,
                new Vector2(rect.X, -rect.Y),
                new Vector2(rect.Width, rect.Height),
                isHighlighted ? Color.Green : Color.Blue);

            GUI.DrawRectangle(spriteBatch,
                new Rectangle(rect.X, -rect.Y, rect.Width, rect.Height),
                Color.Red*((100.0f-OxygenPercentage)/400.0f), true);

            spriteBatch.DrawString(GUI.Font, "Pressure: " + ((int)pressure - rect.Y).ToString() +
                " - Oxygen: "+((int)OxygenPercentage), new Vector2(rect.X+10, -rect.Y+10), Color.Black);
            spriteBatch.DrawString(GUI.Font, volume +" / "+ FullVolume, new Vector2(rect.X+10, -rect.Y+30), Color.Black);

            if (isSelected && editing)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(rect.X - 5, -rect.Y - 5),
                    new Vector2(rect.Width + 10, rect.Height + 10),
                    Color.Red);
            }
        }

        public void Render(GraphicsDevice graphicsDevice, Camera cam)
        {
            if (renderer.PositionInBuffer > renderer.vertices.Length - 6) return;

            //calculate where the surface should be based on the water volume
            float top = rect.Y;
            float bottom = rect.Y - rect.Height;
            float surfaceY = bottom + Volume / rect.Width;

            //interpolate the position of the rendered surface towards the "target surface"
            surface = surface + (surfaceY - surface) / 10.0f;

            Matrix transform =  cam.Transform * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            if (bottom > cam.WorldView.Y || top < cam.WorldView.Y - cam.WorldView.Height) return;

            if (!update)
            {
                // create the four corners of our triangle.

                Vector3[] corners = new Vector3[4];

                corners[0] = new Vector3(rect.X, top, 0.0f);
                corners[1] = new Vector3(rect.X + rect.Width, top, 0.0f);

                corners[2] = new Vector3(corners[1].X, bottom, 0.0f);
                corners[3] = new Vector3(corners[0].X, bottom, 0.0f);

                Vector2[] uvCoords = new Vector2[4];
                for (int i = 0; i < 4; i++ )
                {
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

            int x = rect.X;
            int start = (int)Math.Floor((float)(cam.WorldView.X - x) / WaveWidth);
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
                corners[3] = new Vector3(corners[0].X, surface + waveY[i], 0.0f);

                //skip adjacent "water rects" if the surface of the water is roughly at the same position
                int width = WaveWidth;
                while (i < end - 1 && Math.Abs(waveY[i + 1] - waveY[i]) < 1.0f)
                {
                    width += WaveWidth;
                    i++;
                }

                corners[1] = new Vector3(x + width, top, 0.0f);
                corners[2] = new Vector3(corners[1].X, surface + waveY[i+1], 0.0f);
                
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

        //returns the water block which contains the point (or null if it isn't inside any)
        public static Hull FindHull(Vector2 position, Hull guess = null)
        {
            return FindHull(position, hullList, guess);
        }

        public static Hull FindHull(Vector2 position, List<Hull> hulls, Hull guess = null)
        {
            if (guess != null && hulls.Contains(guess))
            {
                if (Submarine.RectContains(guess.rect, position)) return guess;
            }

            foreach (Hull w in hulls)
            {
                if (Submarine.RectContains(w.rect, position)) return w;
            }

            return null;
        }

        public override XElement Save(XDocument doc)
        {
            XElement element = new XElement("Hull");

            element.Add(new XAttribute("ID", ID),
                new XAttribute("x", rect.X),
                new XAttribute("y", rect.Y),
                new XAttribute("width", rect.Width),
                new XAttribute("height", rect.Height),
                new XAttribute("water", volume));
            
            doc.Root.Add(element);

            return element;
        }

        public static void Load(XElement element)
        {
            Rectangle rect = new Rectangle(
                int.Parse(element.Attribute("x").Value),
                int.Parse(element.Attribute("y").Value),
                int.Parse(element.Attribute("width").Value),
                int.Parse(element.Attribute("height").Value));

            Hull h = new Hull(rect);

            h.volume = ToolBox.GetAttributeFloat(element, "pressure", 0.0f);

            h.ID = (ushort)int.Parse(element.Attribute("ID").Value);
        }

        public override bool FillNetworkData(Networking.NetworkEventType type, NetBuffer message, object data)
        {            
            message.WriteRangedSingle(MathHelper.Clamp(volume/FullVolume, 0.0f, 1.5f), 0.0f, 1.5f, 6);

            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message, out object data)
        {
            data = null;

            float newVolume = this.volume;

            try
            {
                float newPercentage = message.ReadRangedSingle(0.0f, 1.5f, 6);
                newVolume = newPercentage * FullVolume;
            }

            catch
            {
                return;
            }

            Volume = newVolume;
        }
    

    }
}
