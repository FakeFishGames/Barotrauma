using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Reactor : Powered, IServerSerializable, IClientSerializable
    {
        private GUIScrollBar autoTempTickBox;
        private GUIScrollBar onOffSwitch;

        private const int GraphSize = 25;
        private float graphTimer;
        private int updateGraphInterval = 500;

        private Sprite fissionRateMeter, turbineOutputMeter;
        private Sprite meterPointer;
        private Sprite sectorSprite;

        private Sprite tempMeterFrame, tempMeterBar;
        private Sprite tempRangeIndicator;

        private Sprite graphLine;

        private GUIScrollBar fissionRateScrollBar;
        private GUIScrollBar turbineOutputScrollBar;

        private float[] outputGraph = new float[GraphSize];
        private float[] loadGraph = new float[GraphSize];
        
        private GUITickBox criticalHeatWarning;
        private GUITickBox lowTemperatureWarning;
        private GUITickBox criticalOutputWarning;

        private Dictionary<string, GUIButton> warningButtons = new Dictionary<string, GUIButton>();

        private static string[] warningTexts = new string[]
        {
            "ReactorWarningLowTemp","ReactorWarningOverheating",
            "ReactorWarningLowOutput", "ReactorWarningHighOutput",
            "ReactorWarningLowFuel", "ReactorWarningFuelOut",
            "ReactorWarningMeltdown","ReactorWarningSCRAM"
        };



        partial void InitProjSpecific(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "fissionratemeter":
                        fissionRateMeter = new Sprite(subElement);
                        break;
                    case "turbineoutputmeter":
                        turbineOutputMeter = new Sprite(subElement);
                        break;
                    case "meterpointer":
                        meterPointer = new Sprite(subElement);
                        break;
                    case "sectorsprite":
                        sectorSprite = new Sprite(subElement);
                        break;
                    case "tempmeterframe":
                        tempMeterFrame = new Sprite(subElement);
                        break;
                    case "tempmeterbar":
                        tempMeterBar = new Sprite(subElement);
                        break;
                    case "temprangeindicator":
                        tempRangeIndicator = new Sprite(subElement);
                        break;
                    case "graphline":
                        graphLine = new Sprite(subElement);
                        break;
                }
            }

            GuiFrame.Padding = Vector4.One * 20.0f;

            int frameWidth = GuiFrame.Rect.Width - (int)GuiFrame.Padding.X - (int)GuiFrame.Padding.Z;
            GUIFrame columnLeft = new GUIFrame(new Rectangle(0, 0, (int)(frameWidth * 0.25f), 0), null, GuiFrame);
            GUIFrame columnMid = new GUIFrame(new Rectangle((int)(frameWidth * 0.25f) + 10, 0, (int)(frameWidth * 0.4f), 0), null, GuiFrame);
            GUIFrame columnRight = new GUIFrame(new Rectangle((int)(frameWidth * 0.65f) + 20, 0, (int)(frameWidth * 0.35f) - 20, 0), null, GuiFrame);

            int buttonsPerRow = 2;
            int buttonWidth = columnLeft.Rect.Width / buttonsPerRow;
            int buttonHeight = (int)(GuiFrame.Rect.Height * 0.35f) / 3;
            for (int i = 0; i < warningTexts.Length; i++)
            {
                var warningBtn = new GUIButton(new Rectangle((i % buttonsPerRow) * buttonWidth, (int)Math.Floor(i / (float)buttonsPerRow) * buttonHeight, buttonWidth, buttonHeight),
                    TextManager.Get(warningTexts[i]), "IndicatorButton", columnLeft);
                warningBtn.GetChild<GUITextBlock>().Wrap = true;
                warningBtn.GetChild<GUITextBlock>().SetTextPos();
                warningButtons.Add(warningTexts[i], warningBtn);
            }

            new GUITextBlock(new Rectangle(00, -90, 0, 20), TextManager.Get("ReactorFissionRate"), "", Alignment.Bottom, Alignment.Center, columnMid);
            fissionRateScrollBar = new GUIScrollBar(new Rectangle(0, -60, 0, 30), null, 0.1f, Alignment.Bottom, "GUISlider", columnMid);
            fissionRateScrollBar.BarScroll = 1.0f;
            fissionRateScrollBar.OnMoved = (GUIScrollBar bar, float scrollAmount) =>
            {
                LastUser = Character.Controlled;
                if (nextServerLogWriteTime == null)
                {
                    nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                }
                unsentChanges = true;
                targetFissionRate = scrollAmount * 100.0f;

                return false;
            };


            new GUITextBlock(new Rectangle(0, -30, 0, 20), TextManager.Get("ReactorTurbineOutput"), "", Alignment.Bottom, Alignment.Center, columnMid);
            turbineOutputScrollBar = new GUIScrollBar(new Rectangle(0, 0, 0, 30), null, 0.1f, Alignment.Bottom, "GUISlider", columnMid);
            turbineOutputScrollBar.BarScroll = 1.0f;
            turbineOutputScrollBar.OnMoved = (GUIScrollBar bar, float scrollAmount) =>
            {
                LastUser = Character.Controlled;
                if (nextServerLogWriteTime == null)
                {
                    nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                }
                unsentChanges = true;
                targetTurbineOutput = scrollAmount * 100.0f;

                return false;
            };
            

            criticalHeatWarning = new GUITickBox(new Rectangle(0, 0, 30, 30), TextManager.Get("ReactorWarningCriticalTemp"), Alignment.TopLeft, GUI.SmallFont, "IndicatorLightRed", columnMid);

            lowTemperatureWarning = new GUITickBox(new Rectangle((int)(columnMid.Rect.Width * 0.25f), 0, 30, 30), TextManager.Get("ReactorWarningCriticalLowTemp"), Alignment.TopLeft, GUI.SmallFont, "IndicatorLightRed", columnMid);
            lowTemperatureWarning.CanBeFocused = false;
            criticalOutputWarning = new GUITickBox(new Rectangle((int)(columnMid.Rect.Width * 0.66f), 0, 30, 30), TextManager.Get("ReactorWarningCriticalOutput"), Alignment.TopLeft, GUI.SmallFont, "IndicatorLightRed", columnMid);
            criticalOutputWarning.CanBeFocused = false;

            new GUITextBlock(new Rectangle(0, 60, columnMid.Rect.Width / 2, 20), TextManager.Get("ReactorFissionRate"), "", Alignment.TopLeft, Alignment.Center, columnMid);
            new GUITextBlock(new Rectangle(columnMid.Rect.Width / 2, 60, columnMid.Rect.Width / 2, 20), TextManager.Get("ReactorTurbineOutput"), "", Alignment.TopLeft, Alignment.Center, columnMid);

            new GUITextBlock(new Rectangle(0, 0, 100, 20), TextManager.Get("ReactorAutoTemp"), "", columnRight);
            autoTempTickBox = new GUIScrollBar(new Rectangle(0, 30, 100, 30), Color.White, 0.5f, Alignment.TopLeft, "OnOffSlider", columnRight);
            autoTempTickBox.OnMoved = (scrollBar, scrollAmount) =>
            {
                LastUser = Character.Controlled;
                if (nextServerLogWriteTime == null)
                {
                    nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                }
                unsentChanges = true;
                return true;
            };

            onOffSwitch = new GUIScrollBar(new Rectangle(0, 0, 50, 80), Color.White, 0.2f, Alignment.TopRight, "OnOffLever", columnRight);
            onOffSwitch.OnMoved = (scrollBar, scrollAmount) =>
            {
                LastUser = Character.Controlled;
                if (nextServerLogWriteTime == null)
                {
                    nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
                }
                unsentChanges = true;
                return true;
            };

            var lever = onOffSwitch.GetChild<GUIButton>();
            lever.Rect = new Rectangle(lever.Rect.X - 15, lever.Rect.Y, lever.Rect.Width + 30, lever.Rect.Height);

            var loadText = new GUITextBlock(new Rectangle(40, columnRight.Rect.Height / 2 - 50, 100, 20), "Load", "", columnRight);
            loadText.TextColor = Color.LightBlue;
            string loadStr = TextManager.Get("ReactorLoad");
            loadText.TextGetter += () => { return loadStr.Replace("[kw]", ((int)load).ToString()); };

            var outputText = new GUITextBlock(new Rectangle(40, 0, 100, 20), "Output", "", Alignment.BottomLeft, Alignment.TopLeft, columnRight);
            outputText.TextColor = Color.LightGreen;
            string outputStr = TextManager.Get("ReactorOutput");
            outputText.TextGetter += () => { return outputStr.Replace("[kw]", ((int)-currPowerConsumption).ToString()); };
        }

        public override void OnItemLoaded()
        {
            Inventory inventory = item.GetComponent<ItemContainer>()?.Inventory;
            inventory.CenterPos = new Vector2(
                GuiFrame.children[0].Rect.Center.X / (float)GameMain.GraphicsWidth, 
                (GuiFrame.children[0].Rect.Y + GuiFrame.children[0].Rect.Height * 0.75f) / GameMain.GraphicsHeight);
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
                fissionRateScrollBar.BarScroll = FissionRate / 100.0f;
                turbineOutputScrollBar.BarScroll = TurbineOutput / 100.0f;
            }
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            IsActive = true;

            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);

            float maxLoad = 0.0f;
            foreach (float loadVal in loadGraph)
            {
                maxLoad = Math.Max(maxLoad, loadVal);
            }

            Rectangle graphArea = new Rectangle(
                GuiFrame.children[2].Rect.X + 30,
                GuiFrame.children[2].Rect.Center.Y - 25,
                GuiFrame.children[2].Rect.Width - 30,
                GuiFrame.children[2].Rect.Height / 2);

            float xOffset = graphTimer / updateGraphInterval;
            DrawGraph(outputGraph, spriteBatch,
                graphArea, Math.Max(10000.0f, maxLoad), xOffset, Color.LightGreen);

            DrawGraph(loadGraph, spriteBatch,
                graphArea, Math.Max(10000.0f, maxLoad), xOffset, Color.LightBlue);

            tempMeterFrame.Draw(spriteBatch, new Vector2(graphArea.X - 30, graphArea.Y), Color.White, Vector2.Zero, 0.0f, new Vector2(1.0f, graphArea.Height / tempMeterFrame.size.Y));
            float tempFill = temperature / 100.0f;

            int barPadding = 5;
            Vector2 meterBarPos = new Vector2(graphArea.X - 30 + tempMeterFrame.size.X / 2, graphArea.Bottom - tempMeterBar.size.Y);
            while (meterBarPos.Y > graphArea.Bottom - graphArea.Height * tempFill)
            {
                float tempRatio = 1.0f - ((meterBarPos.Y - graphArea.Y) / graphArea.Height);
                Color color = tempRatio < 0.5f ?
                    Color.Lerp(Color.Green, Color.Orange, tempRatio * 2.0f) :
                    Color.Lerp(Color.Orange, Color.Red, (tempRatio - 0.5f) * 2.0f);

                tempMeterBar.Draw(spriteBatch, meterBarPos, color);
                meterBarPos.Y -= (tempMeterBar.size.Y + barPadding);
            }

            tempRangeIndicator.Draw(spriteBatch, new Vector2(meterBarPos.X, graphArea.Bottom - graphArea.Height * optimalTemperature.X / 100.0f));
            tempRangeIndicator.Draw(spriteBatch, new Vector2(meterBarPos.X, graphArea.Bottom - graphArea.Height * optimalTemperature.Y / 100.0f));

            Rectangle meterArea = GuiFrame.children[1].Rect;
            DrawMeter(spriteBatch,
                new Rectangle(meterArea.X, meterArea.Y + 80, meterArea.Width / 2, meterArea.Height),
                fissionRateMeter, FissionRate, new Vector2(0.0f, 100.0f), optimalFissionRate, allowedFissionRate);

            DrawMeter(spriteBatch,
                new Rectangle(meterArea.Center.X, meterArea.Y + 80, meterArea.Width / 2, meterArea.Height),
                turbineOutputMeter, TurbineOutput, new Vector2(0.0f, 100.0f), optimalTurbineOutput, allowedTurbineOutput);
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update(1.0f / 60.0f);

            bool lightOn = Timing.TotalTime % 0.5f < 0.25f && onOffSwitch.BarScroll < 0.5f;

            criticalHeatWarning.Selected = temperature > allowedTemperature.Y && lightOn;
            lowTemperatureWarning.Selected = temperature < allowedTemperature.X && lightOn;
            criticalOutputWarning.Selected = -currPowerConsumption > load * 1.5f && lightOn;

            warningButtons["ReactorWarningOverheating"].Selected = temperature > optimalTemperature.Y && lightOn;
            warningButtons["ReactorWarningHighOutput"].Selected = -currPowerConsumption > load * 1.1f && lightOn;
            warningButtons["ReactorWarningLowTemp"].Selected = temperature < optimalTemperature.X && lightOn;
            warningButtons["ReactorWarningLowOutput"].Selected = -currPowerConsumption < load * 0.9f && lightOn;
            warningButtons["ReactorWarningFuelOut"].Selected = prevAvailableFuel < fissionRate * 0.01f && lightOn;
            warningButtons["ReactorWarningLowFuel"].Selected = prevAvailableFuel < fissionRate && lightOn;
            warningButtons["ReactorWarningMeltdown"].Selected = meltDownTimer > MeltdownDelay * 0.5f || item.Condition == 0.0f && lightOn;
            warningButtons["ReactorWarningSCRAM"].Selected = temperature > 0.1f && onOffSwitch.BarScroll > 0.5f;

            if (!PlayerInput.LeftButtonHeld() || (GUIComponent.MouseOn != autoTempTickBox && !autoTempTickBox.IsParentOf(GUIComponent.MouseOn)))
            {
                int dir = Math.Sign(autoTempTickBox.BarScroll - 0.5f);
                if (dir == 0) dir = 1;
                autoTempTickBox.BarScroll += dir * 0.1f;

                AutoTemp = dir < 0;
            }
            if (!PlayerInput.LeftButtonHeld() || (GUIComponent.MouseOn != onOffSwitch && !onOffSwitch.IsParentOf(GUIComponent.MouseOn)))
            {
                int dir = Math.Sign(onOffSwitch.BarScroll - 0.5f);
                if (dir == 0) dir = 1;
                onOffSwitch.BarScroll += dir * 0.1f;
                onOffSwitch.BarScroll = MathHelper.Clamp(onOffSwitch.BarScroll, 0.25f, 0.75f);
                if (dir == 1)
                {
                    targetFissionRate = 0.0f;
                    targetTurbineOutput = 0.0f;
                    fissionRateScrollBar.BarScroll = FissionRate / 100.0f;
                    turbineOutputScrollBar.BarScroll = TurbineOutput / 100.0f;
                }
            }
        }

        private bool ToggleAutoTemp(GUITickBox tickBox)
        {
            unsentChanges = true;
            autoTemp = tickBox.Selected;
            LastUser = Character.Controlled;

            return true;
        }

        private void DrawMeter(SpriteBatch spriteBatch, Rectangle rect, Sprite meterSprite, float value, Vector2 range, Vector2 optimalRange, Vector2 allowedRange)
        {
            float scale = Math.Min(rect.Width / meterSprite.size.X, rect.Height / meterSprite.size.Y);
            Vector2 pos = new Vector2(rect.Center.X, rect.Y + meterSprite.Origin.Y * scale);

            Vector2 optimalRangeNormalized = new Vector2(
                (optimalRange.X - range.X) / (range.Y - range.X),
                (optimalRange.Y - range.X) / (range.Y - range.X));

            Vector2 allowedRangeNormalized = new Vector2(
                (allowedRange.X - range.X) / (range.Y - range.X),
                (allowedRange.Y - range.X) / (range.Y - range.X));

            Vector2 sectorRad = new Vector2(-1.57f, 1.57f);

            Vector2 optimalSectorRad = new Vector2(
                MathHelper.Lerp(sectorRad.X, sectorRad.Y, optimalRangeNormalized.X),
                MathHelper.Lerp(sectorRad.X, sectorRad.Y, optimalRangeNormalized.Y));

            Vector2 allowedSectorRad = new Vector2(
                MathHelper.Lerp(sectorRad.X, sectorRad.Y, allowedRangeNormalized.X),
                MathHelper.Lerp(sectorRad.X, sectorRad.Y, allowedRangeNormalized.Y));

            if (optimalRangeNormalized.X == optimalRangeNormalized.Y)
            {
                sectorSprite.Draw(spriteBatch, pos, Color.Red, MathHelper.PiOver2, scale);
            }
            else
            {
                sectorSprite.Draw(spriteBatch, pos, Color.LightGreen, MathHelper.PiOver2, scale);

                Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
                spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, GameMain.GraphicsWidth, (int)(pos.Y + (meterSprite.size.Y - meterSprite.Origin.Y) * scale));
                sectorSprite.Draw(spriteBatch, pos, Color.Orange, optimalSectorRad.X, scale);
                sectorSprite.Draw(spriteBatch, pos, Color.Red, allowedSectorRad.X, scale);

                sectorSprite.Draw(spriteBatch, pos, Color.Orange, MathHelper.Pi + optimalSectorRad.Y, scale);
                sectorSprite.Draw(spriteBatch, pos, Color.Red, MathHelper.Pi + allowedSectorRad.Y, scale);

                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
            }

            meterSprite.Draw(spriteBatch, pos, 0, scale);

            float normalizedValue = (value - range.X) / (range.Y - range.X);
            float valueRad = MathHelper.Lerp(sectorRad.X, sectorRad.Y, normalizedValue);
            meterPointer.Draw(spriteBatch, pos, valueRad, scale);
        }

        static void UpdateGraph<T>(IList<T> graph, T newValue)
        {
            for (int i = graph.Count - 1; i > 0; i--)
            {
                graph[i] = graph[i - 1];
            }
            graph[0] = newValue;
        }

        private void DrawGraph(IList<float> graph, SpriteBatch spriteBatch, Rectangle rect, float maxVal, float xOffset, Color color)
        {
            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            spriteBatch.GraphicsDevice.ScissorRectangle = rect;

            float lineWidth = (float)rect.Width / (float)(graph.Count - 2);
            float yScale = (float)rect.Height / maxVal;

            GUI.DrawRectangle(spriteBatch, rect, Color.White);

            Vector2 prevPoint = new Vector2(rect.Right, rect.Bottom - (graph[1] + (graph[0] - graph[1]) * xOffset) * yScale);

            float currX = rect.Right - ((xOffset - 1.0f) * lineWidth);

            for (int i = 1; i < graph.Count - 1; i++)
            {
                currX -= lineWidth;

                Vector2 newPoint = new Vector2(currX, rect.Bottom - graph[i] * yScale);
                
                if (graphLine == null)
                {
                    GUI.DrawLine(spriteBatch, prevPoint, newPoint - new Vector2(1.0f, 0), color);
                }
                else
                {
                    Vector2 dir = Vector2.Normalize(newPoint - prevPoint);
                    GUI.DrawLine(spriteBatch, graphLine.Texture, prevPoint - dir, newPoint + dir, color, 0, 5);
                }

                prevPoint = newPoint;
            }

            Vector2 lastPoint = new Vector2(rect.X,
                rect.Bottom - (graph[graph.Count - 1] + (graph[graph.Count - 2] - graph[graph.Count - 1]) * xOffset) * yScale);

            if (graphLine == null)
            {
                GUI.DrawLine(spriteBatch, prevPoint, lastPoint, color);
            }
            else
            {
                GUI.DrawLine(spriteBatch, graphLine.Texture, prevPoint, lastPoint + (lastPoint - prevPoint), color, 0, 5);
            }

            spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
        }
        
        protected override void RemoveComponentSpecific()
        {
            graphLine.Remove();
            fissionRateMeter.Remove();
            turbineOutputMeter.Remove();
            meterPointer.Remove();
            sectorSprite.Remove();
            tempMeterFrame.Remove();
            tempMeterBar.Remove();
            tempRangeIndicator.Remove();
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
