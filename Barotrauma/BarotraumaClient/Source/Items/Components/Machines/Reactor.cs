using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class Reactor : Powered, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        private GUITickBox autoTempTickBox;
        
        private const int GraphSize = 25;
        private float graphTimer;
        private int updateGraphInterval = 500;

        private float[] fissionRateGraph = new float[GraphSize];
        private float[] coolingRateGraph = new float[GraphSize];
        private float[] tempGraph = new float[GraphSize];
        private float[] loadGraph = new float[GraphSize];

        partial void InitProjSpecific()
        {
            var button = new GUIButton(new Rectangle(410, 70, 40, 40), "-", "", GuiFrame);
            button.OnPressed = () =>
            {
                lastUser = Character.Controlled;
                if (nextServerLogWriteTime == null)
                {
                    nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                }
                unsentChanges = true;
                ShutDownTemp -= 100.0f;

                return false;
            };

            button = new GUIButton(new Rectangle(460, 70, 40, 40), "+", "", GuiFrame);
            button.OnPressed = () =>
            {
                lastUser = Character.Controlled;
                if (nextServerLogWriteTime == null)
                {
                    nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                }
                unsentChanges = true;
                ShutDownTemp += 100.0f;

                return false;
            };

            autoTempTickBox = new GUITickBox(new Rectangle(410, 170, 20, 20), "Automatic temperature control", Alignment.TopLeft, GuiFrame);
            autoTempTickBox.OnSelected = ToggleAutoTemp;

            button = new GUIButton(new Rectangle(210, 290, 40, 40), "+", "", GuiFrame);
            button.OnPressed = () =>
            {
                lastUser = Character.Controlled;
                if (nextServerLogWriteTime == null)
                {
                    nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                }
                unsentChanges = true;
                FissionRate += 1.0f;

                return false;
            };

            button = new GUIButton(new Rectangle(210, 340, 40, 40), "-", "", GuiFrame);
            button.OnPressed = () =>
            {
                lastUser = Character.Controlled;
                if (nextServerLogWriteTime == null)
                {
                    nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                }
                unsentChanges = true;
                FissionRate -= 1.0f;

                return false;
            };

            button = new GUIButton(new Rectangle(500, 290, 40, 40), "+", "", GuiFrame);
            button.OnPressed = () =>
            {
                lastUser = Character.Controlled;
                if (nextServerLogWriteTime == null)
                {
                    nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                }
                unsentChanges = true;
                CoolingRate += 1.0f;

                return false;
            };

            button = new GUIButton(new Rectangle(500, 340, 40, 40), "-", "", GuiFrame);
            button.OnPressed = () =>
            {
                lastUser = Character.Controlled;
                if (nextServerLogWriteTime == null)
                {
                    nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                }
                unsentChanges = true;
                CoolingRate -= 1.0f;

                return false;
            };
        }

        private void UpdateGraph(float deltaTime)
        {
            graphTimer += deltaTime * 1000.0f;

            if (graphTimer > updateGraphInterval)
            {
                UpdateGraph(fissionRateGraph, fissionRate);
                UpdateGraph(coolingRateGraph, coolingRate);
                UpdateGraph(tempGraph, temperature);

                UpdateGraph(loadGraph, load);

                graphTimer = 0.0f;
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            GUI.DrawRectangle(spriteBatch,
                new Vector2(item.Rect.X + item.Rect.Width / 2 - 6, -item.Rect.Y + 29),
                new Vector2(12, 42), Color.Black);

            if (temperature > 0)
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(item.Rect.X + item.Rect.Width / 2 - 5, -item.Rect.Y + 30 + (40.0f * (1.0f - temperature / 10000.0f))),
                    new Vector2(10, 40 * (temperature / 10000.0f)), new Color(temperature / 10000.0f, 1.0f - (temperature / 10000.0f), 0.0f, 1.0f), true);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            IsActive = true;

            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);

            float xOffset = graphTimer / updateGraphInterval;

            //GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black, true);

            GUI.Font.DrawString(spriteBatch, "Output: " + (int)temperature + " kW",
                new Vector2(x + 450, y + 30), Color.Red);
            GUI.Font.DrawString(spriteBatch, "Grid load: " + (int)load + " kW",
                new Vector2(x + 600, y + 30), Color.Yellow);

            float maxLoad = 0.0f;
            foreach (float loadVal in loadGraph)
            {
                maxLoad = Math.Max(maxLoad, loadVal);
            }

            DrawGraph(tempGraph, spriteBatch,
                new Rectangle(x + 30, y + 30, 400, 250), Math.Max(10000.0f, maxLoad), xOffset, Color.Red);

            DrawGraph(loadGraph, spriteBatch,
                new Rectangle(x + 30, y + 30, 400, 250), Math.Max(10000.0f, maxLoad), xOffset, Color.Yellow);

            GUI.Font.DrawString(spriteBatch, "Shutdown Temperature: " + (int)shutDownTemp, new Vector2(x + 450, y + 80), Color.White);

            //GUI.Font.DrawString(spriteBatch, "Automatic Temperature Control: " + ((autoTemp) ? "ON" : "OFF"), new Vector2(x + 450, y + 180), Color.White);

            y += 300;

            GUI.Font.DrawString(spriteBatch, "Fission rate: " + (int)fissionRate + " %", new Vector2(x + 30, y), Color.White);
            DrawGraph(fissionRateGraph, spriteBatch,
                new Rectangle(x + 30, y + 30, 200, 100), 100.0f, xOffset, Color.Orange);


            GUI.Font.DrawString(spriteBatch, "Cooling rate: " + (int)coolingRate + " %", new Vector2(x + 320, y), Color.White);
            DrawGraph(coolingRateGraph, spriteBatch,
                new Rectangle(x + 320, y + 30, 200, 100), 100.0f, xOffset, Color.LightBlue);


            //y = y - 260;
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update(1.0f / 60.0f);
        }

        private bool ToggleAutoTemp(GUITickBox tickBox)
        {
            unsentChanges = true;
            autoTemp = tickBox.Selected;

            return true;
        }

        static void UpdateGraph<T>(IList<T> graph, T newValue)
        {
            for (int i = graph.Count - 1; i > 0; i--)
            {
                graph[i] = graph[i - 1];
            }
            graph[0] = newValue;
        }

        static void DrawGraph(IList<float> graph, SpriteBatch spriteBatch, Rectangle rect, float maxVal, float xOffset, Color color)
        {
            float lineWidth = (float)rect.Width / (float)(graph.Count - 2);
            float yScale = (float)rect.Height / maxVal;

            GUI.DrawRectangle(spriteBatch, rect, Color.White);

            Vector2 prevPoint = new Vector2(rect.Right, rect.Bottom - (graph[1] + (graph[0] - graph[1]) * xOffset) * yScale);

            float currX = rect.Right - ((xOffset - 1.0f) * lineWidth);

            for (int i = 1; i < graph.Count - 1; i++)
            {
                currX -= lineWidth;

                Vector2 newPoint = new Vector2(currX, rect.Bottom - graph[i] * yScale);

                GUI.DrawLine(spriteBatch, prevPoint, newPoint - new Vector2(1.0f, 0), color);

                prevPoint = newPoint;
            }

            Vector2 lastPoint = new Vector2(rect.X,
                rect.Bottom - (graph[graph.Count - 1] + (graph[graph.Count - 2] - graph[graph.Count - 1]) * xOffset) * yScale);

            GUI.DrawLine(spriteBatch, prevPoint, lastPoint, color);
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            msg.Write(autoTemp);
            msg.WriteRangedSingle(shutDownTemp, 0.0f, 10000.0f, 15);

            msg.WriteRangedSingle(coolingRate, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(fissionRate, 0.0f, 100.0f, 8);

            correctionTimer = CorrectionDelay;
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            if (correctionTimer > 0.0f)
            {
                StartDelayedCorrection(type, msg.ExtractBits(16 + 1 + 15 + 8 + 8), sendingTime);
                return;
            }

            Temperature = msg.ReadRangedSingle(0.0f, 10000.0f, 16);

            AutoTemp = msg.ReadBoolean();
            ShutDownTemp = msg.ReadRangedSingle(0.0f, 10000.0f, 15);

            CoolingRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            FissionRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
        }
    }
}
