using Barotrauma.Extensions;
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
        public GUIButton AutoTempSwitch { get; private set; }

        public GUIButton PowerButton { get; private set; }
        private GUITickBox powerLight;
        private GUITickBox autoTempLight;

        private const int GraphSize = 25;
        private float graphTimer;
        private readonly int updateGraphInterval = 500;
        
        private Sprite fissionRateMeter, turbineOutputMeter;
        private Sprite meterPointer;
        private Sprite sectorSprite;

        private Sprite tempMeterFrame, tempMeterBar;
        private Sprite tempRangeIndicator;

        private Sprite graphLine;
        //private GUIFrame graph;

        private Color optimalRangeColor = new Color(74,238,104,255);
        private Color offRangeColor = Color.Orange;
        private Color warningColor = Color.Red;
        private Color coldColor = Color.LightBlue;
        private Color warmColor = Color.Orange;
        private Color hotColor = Color.Red;
        private Color outputColor = Color.Goldenrod;
        private Color loadColor = Color.LightSteelBlue;

        public GUIScrollBar FissionRateScrollBar { get; private set; }

        public GUIScrollBar TurbineOutputScrollBar { get; private set; }

        private readonly float[] outputGraph = new float[GraphSize];
        private readonly float[] loadGraph = new float[GraphSize];
        
        private GUITickBox criticalHeatWarning;
        private GUITickBox lowTemperatureWarning;
        private GUITickBox criticalOutputWarning;

        private GUIFrame inventoryContainer;

        private readonly Dictionary<string, GUIButton> warningButtons = new Dictionary<string, GUIButton>();

        private static readonly string[] warningTexts = new string[]
        {
            "ReactorWarningLowTemp", "ReactorWarningLowOutput", "ReactorWarningLowFuel", "ReactorWarningMeltdown",
            "ReactorWarningOverheating", "ReactorWarningHighOutput", "ReactorWarningFuelOut", "ReactorWarningSCRAM"
        };

        partial void InitProjSpecific(XElement element)
        {
            // TODO: need to recreate the gui when the resolution changes

            fissionRateMeter = new Sprite(element.GetChildElement("fissionratemeter")?.GetChildElement("sprite"));
            turbineOutputMeter = new Sprite(element.GetChildElement("turbineoutputmeter")?.GetChildElement("sprite"));
            meterPointer = new Sprite(element.GetChildElement("meterpointer")?.GetChildElement("sprite"));
            sectorSprite = new Sprite(element.GetChildElement("sectorsprite")?.GetChildElement("sprite"));
            tempMeterFrame = new Sprite(element.GetChildElement("tempmeterframe")?.GetChildElement("sprite"));
            tempMeterBar = new Sprite(element.GetChildElement("tempmeterbar")?.GetChildElement("sprite"));
            tempRangeIndicator = new Sprite(element.GetChildElement("temprangeindicator")?.GetChildElement("sprite"));
            graphLine = new Sprite(element.GetChildElement("graphline")?.GetChildElement("sprite"));

            var paddedFrame = new GUILayoutGroup(new RectTransform(
                    GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) 
                    { AbsoluteOffset = GUIStyle.ItemFrameOffset }, 
                isHorizontal: true)
            {
                RelativeSpacing = 0.012f,
                Stretch = true
            };

            GUILayoutGroup columnLeft = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), paddedFrame.RectTransform))
            {
                RelativeSpacing = 0.012f,
                Stretch = true
            };
            GUILayoutGroup columnRight = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), paddedFrame.RectTransform))
            {
                CanBeFocused = true,
                RelativeSpacing = 0.012f,
                Stretch = true
            };

            //----------------------------------------------------------
            //left column
            //----------------------------------------------------------

            GUIFrame inventoryWindow = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.75f), GuiFrame.RectTransform, Anchor.TopLeft, Pivot.TopRight)
            {
                MinSize = new Point(85, 220),
                RelativeOffset = new Vector2(-0.02f, 0)
            }, style: "ItemUI");

            GUILayoutGroup inventoryContent = new GUILayoutGroup(new RectTransform(inventoryWindow.Rect.Size - GUIStyle.ItemFrameMargin, inventoryWindow.RectTransform, Anchor.Center) 
                { AbsoluteOffset = GUIStyle.ItemFrameOffset },
                childAnchor: Anchor.TopCenter)
            {
                Stretch = true
            };

            /*new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), inventoryContent.RectTransform), "", 
                textAlignment: Alignment.Center, font: GUI.SubHeadingFont, wrap: true);*/
            inventoryContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), inventoryContent.RectTransform), style: null);

            //----------------------------------------------------------
            //mid column
            //----------------------------------------------------------

            var topLeftArea = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.2f), columnLeft.RectTransform),
                isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };


            Point maxIndicatorSize = new Point(int.MaxValue, (int)(40 * GUI.Scale));
            criticalHeatWarning = new GUITickBox(new RectTransform(new Vector2(0.33f, 1.0f), topLeftArea.RectTransform) { MaxSize = maxIndicatorSize },
                TextManager.Get("ReactorWarningCriticalTemp"), font: GUI.SubHeadingFont, style: "IndicatorLightRed")
            {
                Selected = false,
                Enabled = false,
                ToolTip = TextManager.Get("ReactorHeatTip")
            };
            lowTemperatureWarning = new GUITickBox(new RectTransform(new Vector2(0.33f, 1.0f), topLeftArea.RectTransform) { MaxSize = maxIndicatorSize },
                TextManager.Get("ReactorWarningCriticalLowTemp"), font: GUI.SubHeadingFont, style: "IndicatorLightRed")
            {
                Selected = false,
                Enabled = false,
                ToolTip = TextManager.Get("ReactorTempTip")
            };
            criticalOutputWarning = new GUITickBox(new RectTransform(new Vector2(0.33f, 1.0f), topLeftArea.RectTransform) { MaxSize = maxIndicatorSize },
                TextManager.Get("ReactorWarningCriticalOutput"), font: GUI.SubHeadingFont, style: "IndicatorLightRed")
            {
                Selected = false,
                Enabled = false,
                ToolTip = TextManager.Get("ReactorOutputTip")
            };
            List<GUITickBox> indicatorLights = new List<GUITickBox>() { criticalHeatWarning, lowTemperatureWarning, criticalOutputWarning };
            indicatorLights.ForEach(l => l.TextBlock.OverrideTextColor(GUI.Style.TextColor));
            topLeftArea.Recalculate();

            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), columnLeft.RectTransform), style: "HorizontalLine");

            float relativeYMargin = 0.02f;
            Vector2 relativeTextSize = new Vector2(0.9f, 0.2f);
            Vector2 sliderSize = new Vector2(1.0f, 0.125f);
            Vector2 meterSize = new Vector2(1, 1 - relativeTextSize.Y - relativeYMargin - sliderSize.Y - 0.1f);

            var meterArea = new GUIFrame(new RectTransform(new Vector2(1, 0.6f - relativeYMargin * 2), columnLeft.RectTransform), style: null);
            var leftArea = new GUIFrame(new RectTransform(new Vector2(0.49f, 1), meterArea.RectTransform), style: null);
            var rightArea = new GUIFrame(new RectTransform(new Vector2(0.49f, 1), meterArea.RectTransform, Anchor.TopCenter, Pivot.TopLeft), style: null);

            var fissionRateTextBox = new GUITextBlock(new RectTransform(relativeTextSize, leftArea.RectTransform, Anchor.TopCenter),
                TextManager.Get("ReactorFissionRate"), textColor: GUI.Style.TextColor, textAlignment: Alignment.Center, font: GUI.SubHeadingFont)
            {
                AutoScaleHorizontal = true
            };
            var fissionMeter = new GUICustomComponent(new RectTransform(meterSize, leftArea.RectTransform, Anchor.TopCenter)
            {
                RelativeOffset = new Vector2(0.0f, relativeTextSize.Y + relativeYMargin)
            },
                DrawFissionRateMeter, null)
            {
                ToolTip = TextManager.Get("ReactorTipFissionRate")
            };

            var turbineOutputTextBox = new GUITextBlock(new RectTransform(relativeTextSize, rightArea.RectTransform, Anchor.TopCenter), 
                TextManager.Get("ReactorTurbineOutput"), textColor: GUI.Style.TextColor, textAlignment: Alignment.Center, font: GUI.SubHeadingFont)
            {
                AutoScaleHorizontal = true
            };
            GUITextBlock.AutoScaleAndNormalize(turbineOutputTextBox, fissionRateTextBox);

            var turbineMeter = new GUICustomComponent(new RectTransform(meterSize, rightArea.RectTransform, Anchor.TopCenter)
            {
                RelativeOffset = new Vector2(0.0f, relativeTextSize.Y + relativeYMargin)
            },
                DrawTurbineOutputMeter, null)
            {
                ToolTip = TextManager.Get("ReactorTipTurbineOutput")
            };

            FissionRateScrollBar = new GUIScrollBar(new RectTransform(sliderSize, leftArea.RectTransform, Anchor.TopCenter)
            {
                RelativeOffset = new Vector2(0, fissionMeter.RectTransform.RelativeOffset.Y + meterSize.Y)
            },
                style: "DeviceSlider", barSize: 0.15f)
            {
                Enabled = false,
                Step = 1.0f / 255,
                OnMoved = (GUIScrollBar bar, float scrollAmount) =>
                {
                    LastUser = Character.Controlled;
                    unsentChanges = true;
                    targetFissionRate = scrollAmount * 100.0f;

                    return false;
                }
            };

            TurbineOutputScrollBar = new GUIScrollBar(new RectTransform(sliderSize, rightArea.RectTransform, Anchor.TopCenter)
            {
                RelativeOffset = new Vector2(0, turbineMeter.RectTransform.RelativeOffset.Y + meterSize.Y)
            },
                style: "DeviceSlider", barSize: 0.15f, isHorizontal: true)
            {
                Enabled = false,
                Step = 1.0f / 255,
                OnMoved = (GUIScrollBar bar, float scrollAmount) =>
                {
                    LastUser = Character.Controlled;
                    unsentChanges = true;
                    targetTurbineOutput = scrollAmount * 100.0f;

                    return false;
                }
            };

            var buttonArea = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.2f), columnLeft.RectTransform)) 
            { 
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            var upperButtons = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.5f), buttonArea.RectTransform), isHorizontal: true) 
            { 
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            var lowerButtons = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.5f), buttonArea.RectTransform), isHorizontal: true) 
            { 
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            int buttonCount = warningTexts.Length;
            for (int i = 0; i < buttonCount; i++)
            {
                string text = warningTexts[i];
                var b = new GUIButton(new RectTransform(Vector2.One, (i < 4) ? upperButtons.RectTransform : lowerButtons.RectTransform), 
                    TextManager.Get(text), style: "IndicatorButton")
                {
                    Font = GUI.SubHeadingFont,
                    CanBeFocused = false
                };
                warningButtons.Add(text, b);
            }
            upperButtons.Recalculate();
            lowerButtons.Recalculate();
            //only wrap texts that consist of multiple words and are way too big to fit otherwise
            warningButtons.Values.ForEach(b => b.TextBlock.Wrap = b.Text.Contains(' ') && b.TextBlock.TextSize.X > b.TextBlock.Rect.Width * 1.5f);
            GUITextBlock.AutoScaleAndNormalize(warningButtons.Values.Select(b => b.TextBlock));

            //----------------------------------------------------------
            //right column
            //----------------------------------------------------------

            // Auto temp
            var topRightArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), columnRight.RectTransform),
                isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            topRightArea.RectTransform.MinSize = new Point(0, topLeftArea.Rect.Height);
            topRightArea.RectTransform.MaxSize = new Point(int.MaxValue, topLeftArea.Rect.Height);

            new GUIFrame(new RectTransform(new Vector2(0.01f, 1.0f), topRightArea.RectTransform), style: "VerticalLine");

            AutoTempSwitch = new GUIButton(new RectTransform(new Vector2(0.15f, 0.9f), topRightArea.RectTransform), 
                style: "SwitchVertical")
            {
                Enabled = false,
                Selected = AutoTemp,
                OnClicked = (button, data) =>
                {
                    AutoTemp = !AutoTemp;
                    LastUser = Character.Controlled;
                    unsentChanges = true;
                    return true;
                }
            };
            AutoTempSwitch.RectTransform.MaxSize = new Point((int)(AutoTempSwitch.Rect.Height * 0.4f), int.MaxValue);
            
            autoTempLight = new GUITickBox(new RectTransform(new Vector2(0.4f, 1.0f), topRightArea.RectTransform),
                TextManager.Get("ReactorAutoTemp"), font: GUI.SubHeadingFont, style: "IndicatorLightYellow")
                {
                    ToolTip = TextManager.Get("ReactorTipAutoTemp"),
                    CanBeFocused = false,
                    Selected = AutoTemp
                };
            autoTempLight.RectTransform.MaxSize = new Point(int.MaxValue, criticalHeatWarning.Rect.Height);
            autoTempLight.TextBlock.OverrideTextColor(GUI.Style.TextColor);

            new GUIFrame(new RectTransform(new Vector2(0.01f, 1.0f), topRightArea.RectTransform), style: "VerticalLine");

            // Power button
            var powerArea = new GUIFrame(new RectTransform(new Vector2(0.4f, 1.0f), topRightArea.RectTransform), style: null);
            var paddedPowerArea = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), powerArea.RectTransform, Anchor.Center, scaleBasis: ScaleBasis.BothHeight), style: "PowerButtonFrame");
            powerLight = new GUITickBox(new RectTransform(new Vector2(0.87f, 0.3f), paddedPowerArea.RectTransform, Anchor.TopCenter, Pivot.Center), 
                TextManager.Get("PowerLabel"), font: GUI.SubHeadingFont, style: "IndicatorLightPower")
                {
                    CanBeFocused = false,
                    Selected = _powerOn
                };
            powerLight.TextBlock.Padding = new Vector4(5.0f, 0.0f, 0.0f, 0.0f);
            powerLight.TextBlock.AutoScaleHorizontal = true;
            powerLight.TextBlock.OverrideTextColor(GUI.Style.TextColor);
            PowerButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.75f), paddedPowerArea.RectTransform, Anchor.BottomCenter)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, style: "PowerButton")
            {
                OnClicked = (button, data) =>
                {
                    PowerOn = !PowerOn;
                    LastUser = Character.Controlled;
                    unsentChanges = true;
                    return true;
                }
            };

            topRightArea.Recalculate();
            autoTempLight.TextBlock.Wrap = true;
            indicatorLights.Add(autoTempLight);
            GUITextBlock.AutoScaleAndNormalize(indicatorLights.Select(l => l.TextBlock));

            // right bottom (graph area) -----------------------

            new GUIFrame(new RectTransform(new Vector2(0.95f, 0.01f), columnRight.RectTransform), style: "HorizontalLine");

            var bottomRightArea = new GUILayoutGroup(new RectTransform(Vector2.One, columnRight.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                CanBeFocused = true,
                RelativeSpacing = 0.02f
            };

            new GUIFrame(new RectTransform(new Vector2(0.01f, 1.0f), bottomRightArea.RectTransform), style: "VerticalLine");

            new GUICustomComponent(new RectTransform(new Vector2(0.1f, 1), bottomRightArea.RectTransform, Anchor.Center), DrawTempMeter, null);

            var graphArea = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 1.0f), bottomRightArea.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            relativeTextSize = new Vector2(1.0f, 0.15f);
            var loadText = new GUITextBlock(new RectTransform(relativeTextSize, graphArea.RectTransform),
                "Load", textColor: loadColor, font: GUI.SubHeadingFont, textAlignment: Alignment.CenterLeft)
            {
                ToolTip = TextManager.Get("ReactorTipLoad")
            };
            string loadStr = TextManager.Get("ReactorLoad");
            string kW = TextManager.Get("kilowatt");
            loadText.TextGetter += () => $"{loadStr.Replace("[kw]", ((int)load).ToString())} {kW}";
            
            var graph = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), graphArea.RectTransform), style: "InnerFrameRed");
            new GUICustomComponent(new RectTransform(new Vector2(0.9f, 0.98f), graph.RectTransform, Anchor.Center), DrawGraph, null);

            var outputText = new GUITextBlock(new RectTransform(relativeTextSize, graphArea.RectTransform),
                "Output", textColor: outputColor, font: GUI.SubHeadingFont, textAlignment: Alignment.CenterLeft)
            {
                ToolTip = TextManager.Get("ReactorTipPower")
            };
            string outputStr = TextManager.Get("ReactorOutput");
            outputText.TextGetter += () => $"{outputStr.Replace("[kw]", ((int)-currPowerConsumption).ToString())} {kW}";
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            TurbineOutputScrollBar.BarScroll = targetTurbineOutput / 100.0f;
            FissionRateScrollBar.BarScroll = targetFissionRate / 100.0f;
            var itemContainer = item.GetComponent<ItemContainer>();
            if (itemContainer != null)
            {
                itemContainer.UILabel = "";
                itemContainer.AllowUIOverlap = true;
                itemContainer.Inventory.RectTransform = inventoryContainer.RectTransform;
                /*var inventoryLabel = inventoryContainer.Parent?.GetChild<GUITextBlock>();
                if (inventoryLabel != null)
                {
                    inventoryLabel.RectTransform.MinSize = new Point(100, 0);
                    inventoryLabel.Text = itemContainer.GetUILabel();
                    inventoryLabel.CalculateHeightFromText();
                    (inventoryLabel.Parent as GUILayoutGroup).Recalculate();
                }*/
            }
        }

        private void DrawTempMeter(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            Vector2 meterPos = new Vector2(container.Rect.X, container.Rect.Y);
            Vector2 meterScale = new Vector2(container.Rect.Width / (float)tempMeterFrame.SourceRect.Width, container.Rect.Height / (float)tempMeterFrame.SourceRect.Height);
            tempMeterFrame.Draw(spriteBatch, meterPos, Color.White, tempMeterFrame.Origin, 0.0f, scale: meterScale);

            float tempFill = temperature / 100.0f;
            float meterBarScale = container.Rect.Width / (float)tempMeterBar.SourceRect.Width;
            Vector2 meterBarPos = new Vector2(container.Center.X, container.Rect.Bottom - tempMeterBar.size.Y * meterBarScale - (int)(5 * GUI.yScale));
            while (meterBarPos.Y > container.Rect.Bottom + (int)(5 * GUI.yScale) - container.Rect.Height * tempFill)
            {
                float tempRatio = 1.0f - ((meterBarPos.Y - container.Rect.Y) / container.Rect.Height);
                Color color = ToolBox.GradientLerp(tempRatio, coldColor, optimalRangeColor, warmColor, hotColor);
                tempMeterBar.Draw(spriteBatch, meterBarPos, color: color, scale: meterBarScale);
                int spacing = 2;
                meterBarPos.Y -= tempMeterBar.size.Y * meterBarScale + spacing;
            }

            if (temperature > optimalTemperature.Y)
            {
                GUI.DrawRectangle(spriteBatch,
                    meterPos,
                    new Vector2(container.Rect.Width, (container.Rect.Bottom - container.Rect.Height * optimalTemperature.Y / 100.0f) - container.Rect.Y),
                    warningColor * (float)Math.Sin(Timing.TotalTime * 5.0f) * 0.7f, isFilled: true);
            }
            if (temperature < optimalTemperature.X)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(meterPos.X, container.Rect.Bottom - container.Rect.Height * optimalTemperature.X / 100.0f),
                    new Vector2(container.Rect.Width, container.Rect.Bottom - (container.Rect.Bottom - container.Rect.Height * optimalTemperature.X / 100.0f)),
                    warningColor * (float)Math.Sin(Timing.TotalTime * 5.0f) * 0.7f, isFilled: true);
            }

            float tempRangeIndicatorScale = container.Rect.Width / (float)tempRangeIndicator.SourceRect.Width;
            tempRangeIndicator.Draw(spriteBatch, new Vector2(container.Center.X, container.Rect.Bottom - container.Rect.Height * optimalTemperature.X / 100.0f), Color.White, tempRangeIndicator.Origin, 0, scale: tempRangeIndicatorScale);
            tempRangeIndicator.Draw(spriteBatch, new Vector2(container.Center.X, container.Rect.Bottom - container.Rect.Height * optimalTemperature.Y / 100.0f), Color.White, tempRangeIndicator.Origin, 0, scale: tempRangeIndicatorScale);
        }

        private void DrawGraph(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            if (item.Removed) { return; }
            float maxLoad = loadGraph.Max();
            float xOffset = graphTimer / updateGraphInterval;
            Rectangle graphRect = new Rectangle(container.Rect.X, container.Rect.Y, container.Rect.Width, container.Rect.Height - (int)(5 * GUI.yScale));
            DrawGraph(outputGraph, spriteBatch, graphRect, Math.Max(10000.0f, maxLoad), xOffset, outputColor);
            DrawGraph(loadGraph, spriteBatch, graphRect, Math.Max(10000.0f, maxLoad), xOffset, loadColor);
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
                FissionRateScrollBar.BarScroll = FissionRate / 100.0f;
                TurbineOutputScrollBar.BarScroll = TurbineOutput / 100.0f;
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
            
            bool lightOn = Timing.TotalTime % 0.5f < 0.25f && PowerOn;

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
            warningButtons["ReactorWarningSCRAM"].Selected = temperature > 0.1f && !PowerOn;

            if ((FissionRateScrollBar.Rect.Contains(PlayerInput.MousePosition) || FissionRateScrollBar.Children.Contains(GUIScrollBar.DraggingBar) ||
                TurbineOutputScrollBar.Rect.Contains(PlayerInput.MousePosition) || TurbineOutputScrollBar.Children.Contains(GUIScrollBar.DraggingBar)) &&
                !PlayerInput.KeyDown(InputType.Deselect) && !PlayerInput.KeyHit(InputType.Deselect))
            {
                Character.DisableControls = true;
            }

            if (!PowerOn)
            {
                FissionRateScrollBar.BarScroll = FissionRate / 100.0f;
                TurbineOutputScrollBar.BarScroll = TurbineOutput / 100.0f;
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
                    if (input.X != 0.0f && GUIScrollBar.DraggingBar != FissionRateScrollBar)
                    {
                        targetFissionRate = MathHelper.Clamp(targetFissionRate + input.X, 0.0f, 100.0f);
                        FissionRateScrollBar.BarScroll += input.X / 100.0f;
                    }
                    if (input.Y != 0.0f && GUIScrollBar.DraggingBar != TurbineOutputScrollBar)
                    {
                        targetTurbineOutput = MathHelper.Clamp(targetTurbineOutput + input.Y, 0.0f, 100.0f);
                        TurbineOutputScrollBar.BarScroll += input.Y / 100.0f;
                    }
                }
            }
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
                sectorSprite.Draw(spriteBatch, pos, GUI.Style.Red, MathHelper.PiOver2, scale);
            }
            else
            {
                spriteBatch.End();
                Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
                spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, GameMain.GraphicsWidth, (int)(pos.Y + (meterSprite.size.Y - meterSprite.Origin.Y) * scale) - 3);
                spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);

                float scaleMultiplier = 0.95f;
                sectorSprite.Draw(spriteBatch, pos, optimalRangeColor, MathHelper.PiOver2 + (allowedSectorRad.X + allowedSectorRad.Y) / 2.0f, scale * scaleMultiplier);
                sectorSprite.Draw(spriteBatch, pos, offRangeColor, optimalSectorRad.X, scale * scaleMultiplier);
                sectorSprite.Draw(spriteBatch, pos, warningColor, allowedSectorRad.X, scale * scaleMultiplier);
                sectorSprite.Draw(spriteBatch, pos, offRangeColor, MathHelper.Pi + optimalSectorRad.Y, scale * scaleMultiplier);
                sectorSprite.Draw(spriteBatch, pos, warningColor, MathHelper.Pi + allowedSectorRad.Y, scale * scaleMultiplier);

                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred);
            }

            meterSprite.Draw(spriteBatch, pos, 0, scale);

            float normalizedValue = (value - range.X) / (range.Y - range.X);
            float valueRad = MathHelper.Lerp(sectorRad.X, sectorRad.Y, normalizedValue);
            Vector2 offset = new Vector2(0, 40) * scale;
            meterPointer.Draw(spriteBatch, pos - offset, valueRad, scale);
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
            msg.Write(PowerOn);
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
            PowerOn = msg.ReadBoolean();
            Temperature = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            targetFissionRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            targetTurbineOutput = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            degreeOfSuccess = msg.ReadRangedSingle(0.0f, 1.0f, 8);

            if (Math.Abs(FissionRateScrollBar.BarScroll - targetFissionRate / 100.0f) > 0.01f)
            {
                FissionRateScrollBar.BarScroll = targetFissionRate / 100.0f;
            }
            if (Math.Abs(TurbineOutputScrollBar.BarScroll - targetTurbineOutput / 100.0f) > 0.01f)
            {
                TurbineOutputScrollBar.BarScroll = targetTurbineOutput / 100.0f;
            }

            IsActive = true;
        }

        private void UpdateUIElementStates()
        {
            if (powerLight != null)
            {
                powerLight.Selected = _powerOn;
            }
            if (AutoTempSwitch != null)
            {
                AutoTempSwitch.Selected = autoTemp;
                AutoTempSwitch.Enabled = _powerOn;
            }
            if (autoTempLight != null)
            {
                autoTempLight.Selected = autoTemp && _powerOn;
            }
            if (FissionRateScrollBar != null)
            {
                FissionRateScrollBar.Enabled = _powerOn && !autoTemp;
            }
            if (TurbineOutputScrollBar != null)
            {
                TurbineOutputScrollBar.Enabled = _powerOn && !autoTemp;
            }
        }
    }
}
