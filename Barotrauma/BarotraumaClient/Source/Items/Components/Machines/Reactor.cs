using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Reactor : Powered, IServerSerializable, IClientSerializable
    {
        public GUIScrollBar AutoTempSlider
        {
            get { return autoTempSlider; }
        }
        private GUIScrollBar autoTempSlider;

        public GUIScrollBar OnOffSwitch
        {
            get { return onOffSwitch; }
        }
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

        public GUIScrollBar FissionRateScrollBar
        {
            get { return fissionRateScrollBar; }
        }
        private GUIScrollBar fissionRateScrollBar;

        public GUIScrollBar TurbineOutputScrollBar
        {
            get { return turbineOutputScrollBar; }
        }
        private GUIScrollBar turbineOutputScrollBar;

        private float[] outputGraph = new float[GraphSize];
        private float[] loadGraph = new float[GraphSize];
        
        private GUITickBox criticalHeatWarning;
        private GUITickBox lowTemperatureWarning;
        private GUITickBox criticalOutputWarning;

        private GUIFrame inventoryContainer;

        private GUIComponent leftHUDColumn;
        private GUIComponent midHUDColumn;
        private GUIComponent rightHUDColumn;

        private GUILayoutGroup sliderControlsContainer;

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

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.85f), GuiFrame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                RelativeSpacing = 0.012f,
                Stretch = true
            };

            GUIFrame columnLeft = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f), paddedFrame.RectTransform), style: null);
            GUIFrame columnMid = new GUIFrame(new RectTransform(new Vector2(0.45f, 1.0f), paddedFrame.RectTransform), style: null);
            GUIFrame columnRight = new GUIFrame(new RectTransform(new Vector2(0.3f, 1.0f), paddedFrame.RectTransform), style: null);
            leftHUDColumn = columnLeft;
            midHUDColumn = columnMid;
            rightHUDColumn = columnRight;

            //----------------------------------------------------------
            //left column
            //----------------------------------------------------------

            int buttonsPerRow = 2;
            int spacing = 5;
            int buttonWidth = columnLeft.Rect.Width / buttonsPerRow - (spacing * (buttonsPerRow - 1));
            int buttonHeight = (int)(columnLeft.Rect.Height * 0.5f) / 4;
            for (int i = 0; i < warningTexts.Length; i++)
            {
                var warningBtn = new GUIButton(new RectTransform(new Point(buttonWidth, buttonHeight), columnLeft.RectTransform)
                { AbsoluteOffset = new Point((i % buttonsPerRow) * (buttonWidth + spacing), (int)Math.Floor(i / (float)buttonsPerRow) * (buttonHeight + spacing)) },
                    TextManager.Get(warningTexts[i]), style: "IndicatorButton")
                {
                    CanBeFocused = false
                };

                var btnText = warningBtn.GetChild<GUITextBlock>();
                btnText.Font = GUI.Font;
                btnText.Wrap = true;
                btnText.SetTextPos();
                warningButtons.Add(warningTexts[i], warningBtn);
            }
            GUITextBlock.AutoScaleAndNormalize(warningButtons.Values.Select(b => b.TextBlock));

            inventoryContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.45f), columnLeft.RectTransform, Anchor.BottomLeft), style: null);

            //----------------------------------------------------------
            //mid column
            //----------------------------------------------------------
            
            criticalHeatWarning = new GUITickBox(new RectTransform(new Point(columnMid.Rect.Width / 3, (int)(30 * GUI.Scale)), columnMid.RectTransform),
                TextManager.Get("ReactorWarningCriticalTemp"), font: GUI.SmallFont, style: "IndicatorLightRed")
            {
                CanBeFocused = false
            };
            lowTemperatureWarning = new GUITickBox(new RectTransform(new Point(columnMid.Rect.Width / 3, (int)(30 * GUI.Scale)), columnMid.RectTransform) { RelativeOffset = new Vector2(0.27f, 0.0f) },
                TextManager.Get("ReactorWarningCriticalLowTemp"), font: GUI.SmallFont, style: "IndicatorLightRed")
            {
                CanBeFocused = false
            };
            criticalOutputWarning = new GUITickBox(new RectTransform(new Point(columnMid.Rect.Width / 3, (int)(30 * GUI.Scale)), columnMid.RectTransform) { RelativeOffset = new Vector2(0.66f, 0.0f) },
                TextManager.Get("ReactorWarningCriticalOutput"), font: GUI.SmallFont, style: "IndicatorLightRed")
            {
                CanBeFocused = false
            };

            GUITextBlock.AutoScaleAndNormalize(criticalHeatWarning.TextBlock, lowTemperatureWarning.TextBlock, criticalOutputWarning.TextBlock);

            float gaugeOffset = criticalHeatWarning.Rect.Height / (float)columnMid.Rect.Height + 0.05f * GUI.Scale;
            
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.05f), columnMid.RectTransform) { RelativeOffset = new Vector2(0.0f, gaugeOffset) },
                TextManager.Get("ReactorFissionRate"));
            new GUICustomComponent(new RectTransform(new Vector2(0.5f, 0.5f), columnMid.RectTransform) { RelativeOffset = new Vector2(0.0f, gaugeOffset + 0.05f) },
                DrawFissionRateMeter, null)
            {
                ToolTip = TextManager.Get("ReactorTipFissionRate")
            };

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.05f), columnMid.RectTransform, Anchor.TopRight) { RelativeOffset = new Vector2(0.0f, gaugeOffset) },
                TextManager.Get("ReactorTurbineOutput"));
            new GUICustomComponent(new RectTransform(new Vector2(0.5f, 0.5f), columnMid.RectTransform, Anchor.TopRight) { RelativeOffset = new Vector2(0.0f, gaugeOffset + 0.05f) },
                DrawTurbineOutputMeter, null)
            {
                ToolTip = TextManager.Get("ReactorTipTurbineOutput")
            };

            GUILayoutGroup sliderControls = new GUILayoutGroup(new RectTransform(new Point(columnMid.Rect.Width, (int)(114 * GUI.Scale)), columnMid.RectTransform, Anchor.BottomCenter))
            {
                Stretch = true,
                AbsoluteSpacing = (int)(5 * GUI.Scale)
            };
            sliderControlsContainer = sliderControls;

            new GUITextBlock(new RectTransform(new Point(0, (int)(20 * GUI.Scale)), sliderControls.RectTransform, Anchor.TopLeft),
                TextManager.Get("ReactorFissionRate"));
            fissionRateScrollBar = new GUIScrollBar(new RectTransform(new Point(sliderControls.Rect.Width, (int)(30 * GUI.Scale)), sliderControls.RectTransform, Anchor.TopCenter),
                style: "GUISlider", barSize: 0.1f)
            {
                OnMoved = (GUIScrollBar bar, float scrollAmount) =>
                {
                    LastUser = Character.Controlled;
                    unsentChanges = true;
                    targetFissionRate = scrollAmount * 100.0f;

                    return false;
                }
            };

            new GUITextBlock(new RectTransform(new Point(0, (int)(20 * GUI.Scale)), sliderControls.RectTransform, Anchor.BottomLeft),
                TextManager.Get("ReactorTurbineOutput"));
            turbineOutputScrollBar = new GUIScrollBar(new RectTransform(new Point(sliderControls.Rect.Width, (int)(30 * GUI.Scale)), sliderControls.RectTransform, Anchor.BottomCenter),
                style: "GUISlider", barSize: 0.1f, isHorizontal: true)
            {
                OnMoved = (GUIScrollBar bar, float scrollAmount) =>
                {
                    LastUser = Character.Controlled;
                    unsentChanges = true;
                    targetTurbineOutput = scrollAmount * 100.0f;

                    return false;
                }
            };

            //----------------------------------------------------------
            //right column
            //----------------------------------------------------------

            new GUITextBlock(new RectTransform(new Vector2(0.7f, 0.1f), columnRight.RectTransform), TextManager.Get("ReactorAutoTemp"))
            {
                ToolTip = TextManager.Get("ReactorTipAutoTemp"),
                AutoScale = true
            };
            autoTempSlider = new GUIScrollBar(new RectTransform(new Vector2(0.6f, 0.15f), columnRight.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.1f) },
                barSize: 0.55f, style: "OnOffSlider", isHorizontal: true)
            {
                ToolTip = TextManager.Get("ReactorTipAutoTemp"),
                IsBooleanSwitch = true,
                BarScroll = 1.0f,
                OnMoved = (scrollBar, scrollAmount) =>
                {
                    LastUser = Character.Controlled;
                    unsentChanges = true;
                    return true;
                }
            };
            var sliderSprite = autoTempSlider.Frame.Style.Sprites[GUIComponent.ComponentState.None].First();
            autoTempSlider.RectTransform.MaxSize = new Point((int)(sliderSprite.Sprite.SourceRect.Size.X * GUI.Scale), (int)(sliderSprite.Sprite.SourceRect.Size.Y * GUI.Scale));

            onOffSwitch = new GUIScrollBar(new RectTransform(new Vector2(0.4f, 0.3f), columnRight.RectTransform, Anchor.TopRight),
                barSize: 0.2f, style: "OnOffLever", isHorizontal: false)
            {
                IsBooleanSwitch = true,
                MinValue = 0.25f, 
                MaxValue = 0.75f,
                OnMoved = (scrollBar, scrollAmount) =>
                {
                    LastUser = Character.Controlled;
                    unsentChanges = true;
                    return true;
                }
            };
            var switchSprite = onOffSwitch.Frame.Style.Sprites[GUIComponent.ComponentState.None].First();
            onOffSwitch.RectTransform.MaxSize = new Point((int)(switchSprite.Sprite.SourceRect.Size.X * GUI.Scale), (int)(switchSprite.Sprite.SourceRect.Size.Y * GUI.Scale));

            var lever = onOffSwitch.GetChild<GUIButton>();
            lever.RectTransform.NonScaledSize = new Point(lever.Rect.Width + (int)(30 * GUI.Scale), lever.Rect.Height);

            var graphArea = new GUICustomComponent(new RectTransform(new Vector2(1.0f, 0.5f), columnRight.RectTransform, Anchor.BottomCenter) { AbsoluteOffset = new Point(0, 30) },
                DrawGraph, null);

            Point textSize = new Point((int)(100 * GUI.Scale), (int)(30 * GUI.Scale));

            var loadText = new GUITextBlock(new RectTransform(textSize, graphArea.RectTransform, Anchor.TopLeft, Pivot.BottomLeft),
                "Load", textColor: Color.LightBlue, textAlignment: Alignment.CenterLeft)
            {
                ToolTip = TextManager.Get("ReactorTipLoad")
            };
            string loadStr = TextManager.Get("ReactorLoad");
            loadText.TextGetter += () => { return loadStr.Replace("[kw]", ((int)load).ToString()); };

            var outputText = new GUITextBlock(new RectTransform(textSize, graphArea.RectTransform, Anchor.BottomLeft, Pivot.TopLeft),
                "Output", textColor: Color.LightGreen, textAlignment: Alignment.CenterLeft)
            {
                ToolTip = TextManager.Get("ReactorTipPower")
            };
            string outputStr = TextManager.Get("ReactorOutput");
            outputText.TextGetter += () => { return outputStr.Replace("[kw]", ((int)-currPowerConsumption).ToString()); };
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            turbineOutputScrollBar.BarScroll = targetTurbineOutput / 100.0f;
            fissionRateScrollBar.BarScroll = targetFissionRate / 100.0f;
            var itemContainer = item.GetComponent<ItemContainer>();
            if (itemContainer != null)
            {
                itemContainer.AllowUIOverlap = true;
                itemContainer.Inventory.RectTransform = inventoryContainer.RectTransform;
            }
        }

        private void DrawGraph(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            if (item.Removed) { return; }

            Rectangle graphArea = new Rectangle(container.Rect.X + 30, container.Rect.Y, container.Rect.Width - 30, container.Rect.Height);

            float maxLoad = loadGraph.Max();

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

            if (temperature > optimalTemperature.Y)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(graphArea.X - 30, graphArea.Y),
                    new Vector2(tempMeterFrame.SourceRect.Width, (graphArea.Bottom - graphArea.Height * optimalTemperature.Y / 100.0f) - graphArea.Y),
                    Color.Red * (float)Math.Sin(Timing.TotalTime * 5.0f) * 0.7f, isFilled: true);
            }
            if (temperature < optimalTemperature.X)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(graphArea.X - 30, graphArea.Bottom - graphArea.Height * optimalTemperature.X / 100.0f),
                    new Vector2(tempMeterFrame.SourceRect.Width, graphArea.Bottom - (graphArea.Bottom - graphArea.Height * optimalTemperature.X / 100.0f)),
                    Color.Red * (float)Math.Sin(Timing.TotalTime * 5.0f) * 0.7f, isFilled: true);
            }

            tempRangeIndicator.Draw(spriteBatch, new Vector2(meterBarPos.X, graphArea.Bottom - graphArea.Height * optimalTemperature.X / 100.0f));
            tempRangeIndicator.Draw(spriteBatch, new Vector2(meterBarPos.X, graphArea.Bottom - graphArea.Height * optimalTemperature.Y / 100.0f));

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

        private void DrawFissionRateMeter(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            if (item.Removed) { return; }

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = container.Rect;
            spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);

            //make the pointer jitter a bit if it's at the upper limit of the fission rate
            float jitter = 0.0f;
            if (FissionRate > allowedFissionRate.Y - 5.0f)
            {
                float jitterAmount = Math.Min(targetFissionRate - allowedFissionRate.Y, 10.0f);
                float t = graphTimer / updateGraphInterval;

                jitter = (PerlinNoise.GetPerlin(t * 0.5f, t * 0.1f) - 0.5f) * jitterAmount;
            }

            DrawMeter(spriteBatch, container.Rect,
                fissionRateMeter, FissionRate + jitter, new Vector2(0.0f, 100.0f), optimalFissionRate, allowedFissionRate);

            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
            spriteBatch.Begin(SpriteSortMode.Deferred);
        }

        private void DrawTurbineOutputMeter(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            if (item.Removed) { return; }

            DrawMeter(spriteBatch, container.Rect,
                turbineOutputMeter, TurbineOutput, new Vector2(0.0f, 100.0f), optimalTurbineOutput, allowedTurbineOutput);
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            IsActive = true;
            
            bool lightOn = Timing.TotalTime % 0.5f < 0.25f && onOffSwitch.BarScroll < 0.5f;

            fissionRateScrollBar.Enabled = !autoTemp;
            turbineOutputScrollBar.Enabled = !autoTemp;

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

            AutoTemp = autoTempSlider.BarScroll < 0.5f;
            shutDown = onOffSwitch.BarScroll > 0.5f;

            if ((sliderControlsContainer.Rect.Contains(PlayerInput.MousePosition) || sliderControlsContainer.Children.Contains(GUIScrollBar.draggingBar)) &&
                !PlayerInput.KeyDown(InputType.Deselect) && !PlayerInput.KeyHit(InputType.Deselect))
            {
                Character.DisableControls = true;
            }

            if (shutDown)
            {
                fissionRateScrollBar.BarScroll = FissionRate / 100.0f;
                turbineOutputScrollBar.BarScroll = TurbineOutput / 100.0f;
            }
            else if (!autoTemp && Character.DisableControls && GUI.KeyboardDispatcher.Subscriber == null)
            {
                Vector2 input = Vector2.Zero;
                float rate = 50.0f; //percentage per second

                if (PlayerInput.KeyDown(InputType.Left)) input.X += -1.0f;
                if (PlayerInput.KeyDown(InputType.Right)) input.X += 1.0f;
                if (PlayerInput.KeyDown(InputType.Up)) input.Y += 1.0f;
                if (PlayerInput.KeyDown(InputType.Down)) input.Y += -1.0f;
                if (PlayerInput.KeyDown(InputType.Run))
                    rate = 200.0f;
                else if (PlayerInput.KeyDown(InputType.Crouch))
                    rate = 20.0f;

                rate *= deltaTime;
                input.X *= rate;
                input.Y *= rate;

                if (input.LengthSquared() > 0)
                {
                    LastUser = Character.Controlled;
                    unsentChanges = true;
                    if (input.X != 0.0f && GUIScrollBar.draggingBar != fissionRateScrollBar)
                    {
                        targetFissionRate = MathHelper.Clamp(targetFissionRate + input.X, 0.0f, 100.0f);
                        fissionRateScrollBar.BarScroll += input.X / 100.0f;
                    }
                    if (input.Y != 0.0f && GUIScrollBar.draggingBar != turbineOutputScrollBar)
                    {
                        targetTurbineOutput = MathHelper.Clamp(targetTurbineOutput + input.Y, 0.0f, 100.0f);
                        turbineOutputScrollBar.BarScroll += input.Y / 100.0f;
                    }
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
                MathHelper.Clamp((optimalRange.X - range.X) / (range.Y - range.X), 0.0f, 0.95f),
                MathHelper.Clamp((optimalRange.Y - range.X) / (range.Y - range.X), 0.0f, 1.0f));

            Vector2 allowedRangeNormalized = new Vector2(
                MathHelper.Clamp((allowedRange.X - range.X) / (range.Y - range.X), 0.0f, 0.95f),
                MathHelper.Clamp((allowedRange.Y - range.X) / (range.Y - range.X), 0.0f, 1.0f));

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
                spriteBatch.End();
                Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
                spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, GameMain.GraphicsWidth, (int)(pos.Y + (meterSprite.size.Y - meterSprite.Origin.Y) * scale) - 3);
                spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);

                sectorSprite.Draw(spriteBatch, pos, Color.LightGreen, MathHelper.PiOver2 + (allowedSectorRad.X + allowedSectorRad.Y) / 2.0f, scale);

                sectorSprite.Draw(spriteBatch, pos, Color.Orange, optimalSectorRad.X, scale);
                sectorSprite.Draw(spriteBatch, pos, Color.Red, allowedSectorRad.X, scale);

                sectorSprite.Draw(spriteBatch, pos, Color.Orange, MathHelper.Pi + optimalSectorRad.Y, scale);
                sectorSprite.Draw(spriteBatch, pos, Color.Red, MathHelper.Pi + allowedSectorRad.Y, scale);

                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred);
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
            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = rect;
            spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);

            float lineWidth = (float)rect.Width / (float)(graph.Count - 2);
            float yScale = (float)rect.Height / maxVal;

            GUI.DrawRectangle(spriteBatch, rect, Color.White);

            Vector2 prevPoint = new Vector2(rect.Right, rect.Bottom - (graph[1] + (graph[0] - graph[1]) * xOffset) * yScale);

            float currX = rect.Right - ((xOffset - 1.0f) * lineWidth);

            for (int i = 1; i < graph.Count - 1; i++)
            {
                currX -= lineWidth;

                Vector2 newPoint = new Vector2(currX, rect.Bottom - graph[i] * yScale);
                
                if (graphLine?.Texture == null)
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

            if (graphLine?.Texture == null)
            {
                GUI.DrawLine(spriteBatch, prevPoint, lastPoint, color);
            }
            else
            {
                GUI.DrawLine(spriteBatch, graphLine.Texture, prevPoint, lastPoint + (lastPoint - prevPoint), color, 0, 5);
            }

            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
            spriteBatch.Begin(SpriteSortMode.Deferred);
        }
        
        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            graphLine?.Remove();
            fissionRateMeter?.Remove();
            turbineOutputMeter?.Remove();
            meterPointer?.Remove();
            sectorSprite?.Remove();
            tempMeterFrame?.Remove();
            tempMeterBar?.Remove();
            tempRangeIndicator?.Remove();
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            msg.Write(autoTemp);
            msg.Write(shutDown);
            msg.WriteRangedSingle(targetFissionRate, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(targetTurbineOutput, 0.0f, 100.0f, 8);

            correctionTimer = CorrectionDelay;
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            if (correctionTimer > 0.0f)
            {
                StartDelayedCorrection(type, msg.ExtractBits(1 + 1 + 8 + 8 + 8 + 8), sendingTime);
                return;
            }

            AutoTemp = msg.ReadBoolean();
            shutDown = msg.ReadBoolean();
            Temperature = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            targetFissionRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            targetTurbineOutput = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            degreeOfSuccess = msg.ReadRangedSingle(0.0f, 1.0f, 8);

            fissionRateScrollBar.BarScroll = targetFissionRate / 100.0f;
            turbineOutputScrollBar.BarScroll = targetTurbineOutput / 100.0f;
            onOffSwitch.BarScroll = shutDown ? Math.Max(onOffSwitch.BarScroll, 0.55f) : Math.Min(onOffSwitch.BarScroll, 0.45f);

            IsActive = true;
        }
    }
}
