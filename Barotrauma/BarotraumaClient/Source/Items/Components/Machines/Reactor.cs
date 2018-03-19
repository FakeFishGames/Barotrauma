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

        private static Sprite meterSprite;
        private static Sprite sectorSprite;

        GUIScrollBar fissionRateScrollBar;
        GUIScrollBar turbineOutputScrollBar;

        //private float[] fissionRateGraph = new float[GraphSize];
        //private float[] coolingRateGraph = new float[GraphSize];
        private float[] outputGraph = new float[GraphSize];
        private float[] loadGraph = new float[GraphSize];

        partial void InitProjSpecific()
        {
            if (meterSprite == null)
            {
                meterSprite = new Sprite("Content/Items/Reactor/Meter.png", null, null);
                sectorSprite = new Sprite("Content/Items/Reactor/Sector.png", null, null);
            }

            fissionRateScrollBar = new GUIScrollBar(new Rectangle(170, 260, 30, 120), "", 0.1f, GuiFrame);
            fissionRateScrollBar.BarScroll = 1.0f;
            fissionRateScrollBar.OnMoved = (GUIScrollBar bar, float scrollAmount) =>
             {
                 lastUser = Character.Controlled;
                 if (nextServerLogWriteTime == null)
                 {
                     nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                 }
                 unsentChanges = true;
                 targetFissionRate = (1.0f - scrollAmount) * 100.0f;

                 return false;
             };

            turbineOutputScrollBar = new GUIScrollBar(new Rectangle(390, 260, 30, 120), "", 0.1f, GuiFrame);
            turbineOutputScrollBar.BarScroll = 1.0f;
            turbineOutputScrollBar.OnMoved = (GUIScrollBar bar, float scrollAmount) =>
            {
                lastUser = Character.Controlled;
                if (nextServerLogWriteTime == null)
                {
                    nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                }
                unsentChanges = true;
                targetTurbineOutput = (1.0f - scrollAmount) * 100.0f;

                return false;
            };

            autoTempTickBox = new GUITickBox(new Rectangle(430, 300, 20, 20), TextManager.Get("ReactorAutoTemp"), Alignment.TopLeft, GuiFrame);
            autoTempTickBox.OnSelected = ToggleAutoTemp;

            /*var button = new GUIButton(new Rectangle(410, 70, 40, 40), "-", "", GuiFrame);
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

            autoTempTickBox = new GUITickBox(new Rectangle(410, 170, 20, 20), TextManager.Get("ReactorAutoTemp"), Alignment.TopLeft, GuiFrame);
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
            };*/
        }

        private void UpdateGraph(float deltaTime)
        {
            graphTimer += deltaTime * 1000.0f;

            if (graphTimer > updateGraphInterval)
            {
                UpdateGraph(outputGraph, -currPowerConsumption);
                UpdateGraph(loadGraph, load);

                graphTimer = 0.0f;
            }

            if (autoTemp)
            {
                fissionRateScrollBar.BarScroll = 1.0f - FissionRate / 100.0f;
                turbineOutputScrollBar.BarScroll = 1.0f - TurbineOutput / 100.0f;
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            GUI.DrawRectangle(spriteBatch,
                new Vector2(item.Rect.X + item.Rect.Width / 2 - 6, -item.Rect.Y + 29),
                new Vector2(12, 42), Color.Black);

            /*if (temperature > 0)
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(item.Rect.X + item.Rect.Width / 2 - 5, -item.Rect.Y + 30 + (40.0f * (1.0f - temperature / 10000.0f))),
                    new Vector2(10, 40 * (temperature / 10000.0f)), new Color(temperature / 10000.0f, 1.0f - (temperature / 10000.0f), 0.0f, 1.0f), true);*/
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            IsActive = true;

            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);

            float xOffset = graphTimer / updateGraphInterval;
            
            GUI.Font.DrawString(spriteBatch, TextManager.Get("ReactorOutput") + ": " + (int)-currPowerConsumption + " kW",
                new Vector2(x + 450, y + 30), Color.Red);
            GUI.Font.DrawString(spriteBatch, TextManager.Get("ReactorGridLoad") + ": " + (int)load + " kW",
                new Vector2(x + 450, y + 60), Color.Yellow);

            float maxLoad = 0.0f;
            foreach (float loadVal in loadGraph)
            {
                maxLoad = Math.Max(maxLoad, loadVal);
            }

            DrawGraph(outputGraph, spriteBatch,
                new Rectangle(x + 30, y + 30, 360, 200), Math.Max(10000.0f, maxLoad), xOffset, Color.Red);

            DrawGraph(loadGraph, spriteBatch,
                new Rectangle(x + 30, y + 30, 360, 200), Math.Max(10000.0f, maxLoad), xOffset, Color.Yellow);

            GUI.DrawString(spriteBatch, new Vector2(x + 450, y + 90), "Coolant flow", Color.White);

            DrawMeter(spriteBatch, new Vector2(x + 430, y + 130), coolantFlow, new Vector2(0.0f, 100.0f), optimalCoolantFlow, allowedCoolantFlow);
            /*GUI.DrawRectangle(spriteBatch, new Rectangle(x + 450, y + 110, 50, 120), Color.Black, true);
            GUI.DrawRectangle(spriteBatch, new Rectangle(x + 450, y + 110, 50, 120), Color.White, false);
            GUI.DrawRectangle(spriteBatch, new Rectangle(x + 450, y + 110 + (int)((1.0f - coolantFlow / 100.0f) * 120.0f), 50, 3), Color.White, true);*/

            GUI.DrawString(spriteBatch, new Vector2(x + 40, y + 250), "Fission rate", Color.White);
            DrawMeter(spriteBatch, new Vector2(x + 40, y + 280), FissionRate, new Vector2(0.0f, 100.0f), optimalFissionRate, allowedFissionRate);

            /*GUI.DrawRectangle(spriteBatch, new Rectangle(x+40, y+300, 50, 120), Color.Black, true);
            GUI.DrawRectangle(spriteBatch, new Rectangle(x + 40, y + 300, 50, 120), Color.White, false);
            GUI.DrawRectangle(spriteBatch, new Rectangle(x + 40, y + 300 + (int)((1.0f - fissionRate / 100.0f) * 120.0f), 50, 3), Color.White, true);
            */

            GUI.DrawString(spriteBatch, new Vector2(x + 260, y + 250), "Turbine output", Color.White);
            DrawMeter(spriteBatch, new Vector2(x + 260, y + 280), TurbineOutput, new Vector2(0.0f, 100.0f), optimalTurbineOutput, allowedTurbineOutput);


            /*GUI.DrawRectangle(spriteBatch, new Rectangle(x + 230, y + 300, 50, 120), Color.Black, true);
            GUI.DrawRectangle(spriteBatch, new Rectangle(x + 230, y + 300, 50, 120), Color.White, false);
            GUI.DrawRectangle(spriteBatch, new Rectangle(x + 230, y + 300 + (int)((1.0f - turbineOutput / 100.0f) * 120.0f), 50, 3), Color.White, true);
            */
            x = x+580;
            y = y+30;

            GUI.DrawString(spriteBatch, new Vector2(x, y), "Overheating", coolantFlow > optimalCoolantFlow.Y ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y+15), "Temperature critical", coolantFlow > allowedCoolantFlow.Y ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y+30), "Insufficient power output", -currPowerConsumption < load * 0.9f ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y+45), "High power output", -currPowerConsumption > load * 1.1f ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y+60), "SCRAM", Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y+75), "Insufficient coolant flow", coolantFlow < optimalCoolantFlow.X ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y+90), "Critically low coolant flow", coolantFlow < allowedCoolantFlow.X ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y+105), "Low fuel", prevAvailableFuel < 100.0f ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y+120), "Critical fuel", prevAvailableFuel < 10.0f ? Color.Red : Color.White);
            //GUI.DrawString(spriteBatch, new Vector2(x, y), "Meltdown warning", melt ? Color.Red : Color.White);


            /*GUI.Font.DrawString(spriteBatch, TextManager.Get("ReactorShutdownTemp") + ": " + (int)shutDownTemp, new Vector2(x + 450, y + 80), Color.White);

            y += 300;

            GUI.Font.DrawString(spriteBatch, TextManager.Get("ReactorFissionRate") + ": " + (int)fissionRate + " %", new Vector2(x + 30, y), Color.White);
            DrawGraph(fissionRateGraph, spriteBatch,
                new Rectangle(x + 30, y + 30, 200, 100), 100.0f, xOffset, Color.Orange);


            GUI.Font.DrawString(spriteBatch, TextManager.Get("ReactorCoolingRate") + ": " + (int)coolingRate + " %", new Vector2(x + 320, y), Color.White);
            DrawGraph(coolingRateGraph, spriteBatch,
                new Rectangle(x + 320, y + 30, 200, 100), 100.0f, xOffset, Color.LightBlue);*/
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

        private void DrawMeter(SpriteBatch spriteBatch, Vector2 pos, float value, Vector2 range, Vector2 optimalRange, Vector2 allowedRange)
        {
            meterSprite.Draw(spriteBatch, pos);

            Vector2 optimalRangeNormalized = new Vector2(
                (optimalRange.X - range.X) / (range.Y - range.X), 
                (optimalRange.Y - range.X) / (range.Y - range.X));

            Vector2 allowedRangeNormalized = new Vector2(
                (allowedRange.X - range.X) / (range.Y - range.X),
                (allowedRange.Y - range.X) / (range.Y - range.X));

            Vector2 sectorRad = new Vector2(-0.8f, 0.8f);

            Vector2 optimalSectorRad = new Vector2(
                MathHelper.Lerp(sectorRad.X, sectorRad.Y, optimalRangeNormalized.X),
                MathHelper.Lerp(sectorRad.X, sectorRad.Y, optimalRangeNormalized.Y));

            Vector2 allowedSectorRad = new Vector2(
                MathHelper.Lerp(sectorRad.X, sectorRad.Y, allowedRangeNormalized.X),
                MathHelper.Lerp(sectorRad.X, sectorRad.Y, allowedRangeNormalized.Y));


            float stepRad = MathHelper.ToRadians(5.0f);
            for (float x = sectorRad.X; x < sectorRad.Y; x += stepRad)
            {
                if (x > optimalSectorRad.X && x < optimalSectorRad.Y)
                {
                    sectorSprite.Draw(spriteBatch, pos + new Vector2(74, 105),
                        Color.Green * 0.8f, new Vector2(0.0f, sectorSprite.size.Y), x - stepRad / 2.0f, 0.2f);
                }
                else if (x > allowedSectorRad.X && x < allowedSectorRad.Y)
                {
                    sectorSprite.Draw(spriteBatch, pos + new Vector2(74, 105),
                        Color.Orange * 0.8f, new Vector2(0.0f, sectorSprite.size.Y), x - stepRad / 2.0f, 0.2f);
                }
                else
                {
                    sectorSprite.Draw(spriteBatch, pos + new Vector2(74, 105),
                        Color.Red * 0.8f, new Vector2(0.0f, sectorSprite.size.Y), x - stepRad / 2.0f, 0.2f);

                }
            }

            float normalizedValue = (value - range.X) / (range.Y - range.X);
            float valueRad = MathHelper.Lerp(sectorRad.X, sectorRad.Y, normalizedValue) - MathHelper.PiOver2;
            GUI.DrawLine(spriteBatch,
                pos + new Vector2(74, 105),
                pos + new Vector2(74 + (float)Math.Cos(valueRad) * 60.0f, 105 + (float)Math.Sin(valueRad) * 60.0f),
                Color.Black, 0, 5);
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
            /*msg.Write(autoTemp);
            msg.WriteRangedSingle(shutDownTemp, 0.0f, 10000.0f, 15);

            msg.WriteRangedSingle(coolingRate, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(fissionRate, 0.0f, 100.0f, 8);

            correctionTimer = CorrectionDelay;*/
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            /*if (correctionTimer > 0.0f)
            {
                StartDelayedCorrection(type, msg.ExtractBits(16 + 1 + 15 + 8 + 8), sendingTime);
                return;
            }

            Temperature = msg.ReadRangedSingle(0.0f, 10000.0f, 16);

            AutoTemp = msg.ReadBoolean();
            ShutDownTemp = msg.ReadRangedSingle(0.0f, 10000.0f, 15);

            CoolingRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            FissionRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);*/
        }
    }
}
