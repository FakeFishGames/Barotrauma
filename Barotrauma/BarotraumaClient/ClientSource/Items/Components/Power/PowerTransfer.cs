﻿using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class PowerTransfer : Powered
    {
        private GUITickBox powerIndicator;
        private GUITickBox highVoltageIndicator;
        private GUITickBox lowVoltageIndicator;

        private GUITextBlock powerLabel, loadLabel;

        private LanguageIdentifier prevLanguage;

        partial void InitProjectSpecific(XElement element)
        {
            if (GuiFrame == null) { return; }

            var paddedFrame = new GUIFrame(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset },
                style: null)
            {
                CanBeFocused = false
            };

            var lightsArea = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1), paddedFrame.RectTransform, Anchor.CenterLeft))
            {
                Stretch = true
            };
            powerIndicator = new GUITickBox(new RectTransform(new Vector2(1, 0.33f), lightsArea.RectTransform), 
                TextManager.Get("PowerTransferPowered"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightGreen")
            {
                CanBeFocused = false
            };
            highVoltageIndicator = new GUITickBox(new RectTransform(new Vector2(1, 0.33f), lightsArea.RectTransform), 
                TextManager.Get("PowerTransferHighVoltage"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightRed")
            {
                ToolTip = TextManager.Get("PowerTransferTipOvervoltage"),
                Enabled = false
            };
            lowVoltageIndicator = new GUITickBox(new RectTransform(new Vector2(1, 0.33f), lightsArea.RectTransform), 
                TextManager.Get("PowerTransferLowVoltage"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightRed")
            {
                ToolTip = TextManager.Get("PowerTransferTipLowvoltage"),
                Enabled = false                
            };
            powerIndicator.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal);
            highVoltageIndicator.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal);
            lowVoltageIndicator.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal);
            GUITextBlock.AutoScaleAndNormalize(powerIndicator.TextBlock, highVoltageIndicator.TextBlock, lowVoltageIndicator.TextBlock);

            var textContainer = new GUIFrame(new RectTransform(new Vector2(0.58f, 1.0f), paddedFrame.RectTransform, Anchor.CenterRight), style: null);
            var upperTextArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), textContainer.RectTransform, Anchor.TopLeft), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };
            var lowerTextArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), textContainer.RectTransform, Anchor.BottomLeft), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };

            powerLabel = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), upperTextArea.RectTransform),
                TextManager.Get("PowerTransferPowerLabel"), textColor: GUIStyle.TextColorBright, font: GUIStyle.LargeFont, textAlignment: Alignment.CenterRight)
            {
                ToolTip = TextManager.Get("PowerTransferTipPower")
            };
            loadLabel = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), lowerTextArea.RectTransform),
                TextManager.Get("PowerTransferLoadLabel"), textColor: GUIStyle.TextColorBright, font: GUIStyle.LargeFont, textAlignment: Alignment.CenterRight)
            {
                ToolTip = TextManager.Get("PowerTransferTipLoad")
            };

            var digitalBackground = new GUIFrame(new RectTransform(new Vector2(0.55f, 0.8f), upperTextArea.RectTransform), style: "DigitalFrameDark");
            var powerText = new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.95f), digitalBackground.RectTransform, Anchor.Center), 
                "", font: GUIStyle.DigitalFont, textColor: GUIStyle.TextColorDark)
            {
                TextAlignment = Alignment.CenterRight,
                ToolTip = TextManager.Get("PowerTransferTipPower"),
                TextGetter = () => {
                    float currPower = powerLoad < 0 ? -powerLoad: 0;
                    if (this is not RelayComponent && PowerConnections != null && PowerConnections.Count > 0 && PowerConnections[0].Grid != null)
                    {
                        currPower = PowerConnections[0].Grid.Power;
                    }
                    return ((int)Math.Round(currPower)).ToString();
                }
            };
            var kw1 = new GUITextBlock(new RectTransform(new Vector2(0.15f, 0.5f), upperTextArea.RectTransform),
                TextManager.Get("kilowatt"), textColor: GUIStyle.TextColorNormal, font: GUIStyle.Font)
            {
                Padding = Vector4.Zero,
                TextAlignment = Alignment.BottomCenter
            };

            digitalBackground = new GUIFrame(new RectTransform(new Vector2(0.55f, 0.8f), lowerTextArea.RectTransform), style: "DigitalFrameDark");
            var loadText = new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.95f), digitalBackground.RectTransform, Anchor.Center), 
                "", font: GUIStyle.DigitalFont, textColor: GUIStyle.TextColorDark)
            {
                TextAlignment = Alignment.CenterRight,
                ToolTip = TextManager.Get("PowerTransferTipLoad"),
                TextGetter = () =>
                {
                    float load = PowerLoad;
                    if (this is RelayComponent relay)
                    {
                        load = relay.DisplayLoad;
                    }
                    else if (load < 0)
                    {
                        load = 0;
                    }
                    return ((int)Math.Round(load)).ToString();
                }
            };
            var kw2 = new GUITextBlock(new RectTransform(new Vector2(0.15f, 0.5f), lowerTextArea.RectTransform),
                TextManager.Get("kilowatt"), textColor: GUIStyle.TextColorNormal, font: GUIStyle.Font)
            {
                Padding = Vector4.Zero,
                TextAlignment = Alignment.BottomCenter
            };

            GUITextBlock.AutoScaleAndNormalize(powerLabel, loadLabel);
            GUITextBlock.AutoScaleAndNormalize(true, true, powerText, loadText);
            GUITextBlock.AutoScaleAndNormalize(kw1, kw2);

            prevLanguage = GameSettings.CurrentConfig.Language;
        }

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            if (GuiFrame == null) return;

            float voltage = (PowerConnections.Count > 0 && PowerConnections[0].Grid != null) ? PowerConnections[0].Grid.Voltage : 0f;
            powerIndicator.Selected = IsActive && voltage > 0;
            highVoltageIndicator.Selected = Timing.TotalTime % 0.5f < 0.25f && powerIndicator.Selected && voltage > 1.2f;
            lowVoltageIndicator.Selected = Timing.TotalTime % 0.5f < 0.25f && powerIndicator.Selected && voltage < 0.8f;

            if (prevLanguage != GameSettings.CurrentConfig.Language)
            {
                GUITextBlock.AutoScaleAndNormalize(powerIndicator.TextBlock, highVoltageIndicator.TextBlock, lowVoltageIndicator.TextBlock);
                GUITextBlock.AutoScaleAndNormalize(powerLabel, loadLabel);
                prevLanguage = GameSettings.CurrentConfig.Language;
            }
        }
    }
}
