using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class PowerTransfer : Powered
    {
        public override bool RecreateGUIOnResolutionChange => true;
        protected GUIComponent guiContent;

        private GUITickBox powerIndicator;
        private GUITickBox highVoltageIndicator;
        private GUITickBox lowVoltageIndicator;

        private GUITextBlock powerLabel, loadLabel;
        protected GUITextBlock powerDisplay, loadDisplay;

        protected LanguageIdentifier prevLanguage;

        partial void InitProjectSpecific(XElement element)
        {
            if (GuiFrame == null) { return; }
            CreateGUI();
            prevLanguage = GameSettings.CurrentConfig.Language;
        }

        protected override void CreateGUI()
        {
            if (GuiFrame == null) { return; }
            guiContent = new GUIFrame(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset }, style: null)
            {
                CanBeFocused = false
            };
            CreateDefaultPowerUI(guiContent);
        }

        protected void CreateDefaultPowerUI(GUIComponent parent)
        {
            GUILayoutGroup lightsArea = new(new RectTransform(new Vector2(0.4f, 1f), parent.RectTransform, Anchor.CenterLeft))
            {
                Stretch = true
            };
            powerIndicator = GUI.CreateIndicatorLight(new RectTransform(new Vector2(1, 0.33f), lightsArea.RectTransform),
                "IndicatorLightGreen", TextManager.Get("PowerTransferPowered"));
            highVoltageIndicator = GUI.CreateIndicatorLight(new RectTransform(new Vector2(1, 0.33f), lightsArea.RectTransform),
                "IndicatorLightRed", TextManager.Get("PowerTransferHighVoltage"), TextManager.Get("PowerTransferTipOvervoltage"));
            lowVoltageIndicator = GUI.CreateIndicatorLight(new RectTransform(new Vector2(1, 0.33f), lightsArea.RectTransform),
                "IndicatorLightRed", TextManager.Get("PowerTransferLowVoltage"), TextManager.Get("PowerTransferTipLowvoltage"));
            GUITextBlock.AutoScaleAndNormalize(powerIndicator.TextBlock, highVoltageIndicator.TextBlock, lowVoltageIndicator.TextBlock);

            GUIFrame textContainer = new(new RectTransform(new Vector2(0.58f, 1f), parent.RectTransform, Anchor.CenterRight), style: null);

            powerDisplay = GUI.CreateDigitalDisplay(new RectTransform(new Vector2(1f, 0.5f), textContainer.RectTransform, Anchor.TopLeft),
                out powerLabel, out GUITextBlock unitLabel1, TextManager.Get("PowerTransferPowerLabel"), TextManager.Get("kilowatt"), TextManager.Get("PowerTransferTipPower"));

            powerDisplay.TextGetter = () =>
            {
                float currPower = powerLoad < 0 ? -powerLoad : 0;
                if (this is not RelayComponent && PowerConnections != null && PowerConnections.Count > 0 && PowerConnections[0].Grid != null)
                {
                    currPower = PowerConnections[0].Grid.Power;
                }
                return MathUtils.RoundToInt(currPower).ToString();
            };

            loadDisplay = GUI.CreateDigitalDisplay(new RectTransform(new Vector2(1f, 0.5f), textContainer.RectTransform, Anchor.BottomLeft),
                out loadLabel, out GUITextBlock unitLabel2, TextManager.Get("PowerTransferLoadLabel"), TextManager.Get("kilowatt"), TextManager.Get("PowerTransferTipLoad"));

            loadDisplay.TextGetter = () =>
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
                return MathUtils.RoundToInt(load).ToString();
            };

            GUITextBlock.AutoScaleAndNormalize(powerLabel, loadLabel);
            GUITextBlock.AutoScaleAndNormalize(true, true, powerDisplay, loadDisplay);
            GUITextBlock.AutoScaleAndNormalize(unitLabel1, unitLabel2);
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
