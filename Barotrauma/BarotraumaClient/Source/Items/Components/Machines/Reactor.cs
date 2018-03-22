using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class Reactor : Powered, IServerSerializable, IClientSerializable
    {
        private GUITickBox autoTempTickBox;
        
        private const int GraphSize = 25;
        private float graphTimer;
        private int updateGraphInterval = 500;

        private static Sprite meterSprite;
        private static Sprite sectorSprite;

        private GUIScrollBar fissionRateScrollBar;
        private GUIScrollBar turbineOutputScrollBar;
        
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
                 LastUser = Character.Controlled;
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
                LastUser = Character.Controlled;
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

            GUI.DrawString(spriteBatch, new Vector2(x + 450, y + 90), "Temperature", Color.White);
            DrawMeter(spriteBatch, new Vector2(x + 430, y + 130), temperature, new Vector2(0.0f, 100.0f), optimalTemperature, allowedTemperature);

            GUI.DrawString(spriteBatch, new Vector2(x + 40, y + 250), "Fission rate", Color.White);
            DrawMeter(spriteBatch, new Vector2(x + 40, y + 280), FissionRate, new Vector2(0.0f, 100.0f), optimalFissionRate, allowedFissionRate);

            GUI.DrawString(spriteBatch, new Vector2(x + 260, y + 250), "Turbine output", Color.White);
            DrawMeter(spriteBatch, new Vector2(x + 260, y + 280), TurbineOutput, new Vector2(0.0f, 100.0f), optimalTurbineOutput, allowedTurbineOutput);

            x = x + 580;
            y = y + 30;

            GUI.DrawString(spriteBatch, new Vector2(x, y), "Overheating", temperature > optimalTemperature.Y ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y + 15), "Temperature critical", temperature > allowedTemperature.Y ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y + 30), "Insufficient power output", -currPowerConsumption < load * 0.9f ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y + 45), "High power output", -currPowerConsumption > load * 1.1f ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y + 60), "SCRAM", Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y + 75), "Insufficient temperature", temperature < optimalTemperature.X ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y + 90), "Critically low temperature", temperature < allowedTemperature.X ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y + 105), "Low fuel", prevAvailableFuel < fissionRate ? Color.Red : Color.White);
            GUI.DrawString(spriteBatch, new Vector2(x, y + 120), "Critical fuel", prevAvailableFuel < fissionRate * 0.1f ? Color.Red : Color.White);
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
            LastUser = Character.Controlled;

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
                Color.Black, 0, 3);
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
            msg.WriteRangedSingle(targetFissionRate, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(targetTurbineOutput, 0.0f, 100.0f, 8);

            correctionTimer = CorrectionDelay;
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            if (correctionTimer > 0.0f)
            {
                StartDelayedCorrection(type, msg.ExtractBits(1 + 8 + 8 + 8 + 8), sendingTime);
                return;
            }

            AutoTemp = msg.ReadBoolean();
            Temperature = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            targetFissionRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            targetTurbineOutput = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            degreeOfSuccess = msg.ReadRangedSingle(0.0f, 1.0f, 8);

            fissionRateScrollBar.BarScroll = 1.0f - targetFissionRate / 100.0f;
            turbineOutputScrollBar.BarScroll = 1.0f - targetTurbineOutput / 100.0f;
        }
    }
}
