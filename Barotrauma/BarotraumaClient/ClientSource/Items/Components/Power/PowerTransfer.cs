using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class PowerTransfer : Powered
    {
        private GUITickBox powerIndicator;
        private GUITickBox highVoltageIndicator;
        private GUITickBox lowVoltageIndicator;

        partial void InitProjectSpecific(XElement element)
        {
            if (GuiFrame == null) return;

            Point indicatorSize = new Point((int)(30 * GUI.Scale));

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.7f), GuiFrame.RectTransform, Anchor.Center), style: null);
            powerIndicator = new GUITickBox(new RectTransform(indicatorSize, paddedFrame.RectTransform),
                TextManager.Get("PowerTransferPowered"), style: "IndicatorLightGreen")
            {
                Enabled = false
            };
            powerIndicator.TextColor = powerIndicator.DefaultTextColor.Value;

            highVoltageIndicator = new GUITickBox(new RectTransform(indicatorSize, paddedFrame.RectTransform) { AbsoluteOffset = new Point(0, (int)(40 * GUI.yScale)) },
                TextManager.Get("PowerTransferHighVoltage"), style: "IndicatorLightRed")
            {
                ToolTip = TextManager.Get("PowerTransferTipOvervoltage"),
                Enabled = false
            };
            highVoltageIndicator.TextColor = highVoltageIndicator.DefaultTextColor.Value;

            lowVoltageIndicator = new GUITickBox(new RectTransform(indicatorSize, paddedFrame.RectTransform) { AbsoluteOffset = new Point(0, (int)(80 * GUI.yScale)) },
                TextManager.Get("PowerTransferLowVoltage"), style: "IndicatorLightRed")
            {
                ToolTip = TextManager.Get("PowerTransferTipLowvoltage"),
                Enabled = false                
            };
            lowVoltageIndicator.TextColor = lowVoltageIndicator.DefaultTextColor.Value;

            var textContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), paddedFrame.RectTransform, Anchor.TopRight));

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContainer.RectTransform),
                TextManager.Get("PowerTransferPowerLabel"), font: GUI.LargeFont)
            {
                ToolTip = TextManager.Get("PowerTransferTipPower")
            };
            string powerStr = TextManager.Get("PowerTransferPower");
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), textContainer.RectTransform), "", textColor: Color.LightGreen)
            {
                ToolTip = TextManager.Get("PowerTransferTipPower"),
                TextGetter = () => { return powerStr.Replace("[power]", ((int)Math.Round(-currPowerConsumption)).ToString()); }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContainer.RectTransform),
                TextManager.Get("PowerTransferLoadLabel"), font: GUI.LargeFont)
            {
                ToolTip = TextManager.Get("PowerTransferTipLoad")
                
            };
            string loadStr = TextManager.Get("PowerTransferLoad");
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), textContainer.RectTransform), "", textColor: Color.LightBlue)
            {
                ToolTip = TextManager.Get("PowerTransferTipLoad"),
                TextGetter = () => 
                { 
                    return loadStr.Replace("[load]", 
                        this is RelayComponent relay ?
                        ((int)Math.Round(relay.DisplayLoad)).ToString() :
                        ((int)Math.Round(powerLoad)).ToString()); 
                }
            };
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            if (GuiFrame == null) return;

            float voltage = powerLoad <= 0.0f ? 1.0f : -currPowerConsumption / powerLoad;
            powerIndicator.Selected = IsActive && currPowerConsumption < -0.1f;
            highVoltageIndicator.Selected = Timing.TotalTime % 0.5f < 0.25f && powerIndicator.Selected && voltage > 1.2f;
            lowVoltageIndicator.Selected = Timing.TotalTime % 0.5f < 0.25f && powerIndicator.Selected && voltage < 0.8f;
        }
    }
}
