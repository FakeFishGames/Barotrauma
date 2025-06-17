#nullable enable
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components
{
    internal partial class PowerDistributor : PowerTransfer, IServerSerializable, IClientSerializable
    {
        private partial class PowerGroup
        {
            private GUIFrame? frame;
            private GUITextBox? nameBox;
            private GUIScrollBar? ratioSlider;
            private readonly List<GUITextBlock> powerUnitLabels = new List<GUITextBlock>();
            private GUIFrame? divider;

            public bool IsVisible { get; private set; } = true;

            public void CreateGUI()
            {
                frame = new GUIFrame(new RectTransform(new Vector2(1f, 0.25f), distributor.groupList!.Content.RectTransform, minSize: (0, 130)), style: null);
                GUIFrame groupContent = new(new RectTransform(frame.Rect.Size - new Point(10), frame.RectTransform, Anchor.Center), style: null);

                GUILayoutGroup nameGroup = new(new RectTransform(new Vector2(0.65f, 0.33f), groupContent.RectTransform, Anchor.TopLeft), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true
                };
                GUIButton penIcon = new(new RectTransform(new Vector2(0.75f), nameGroup.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "TextBoxIcon")
                {
                    HoverCursor = CursorState.IBeam,
                    OnClicked = (_, _) =>
                    {
                        nameBox!.Select();
                        return true;
                    }
                };
                nameBox = new GUITextBox(new RectTransform(Vector2.One, nameGroup.RectTransform), Name, font: GUIStyle.SubHeadingFont, style: "GUITextBoxNoStyle")
                {
                    MaxTextLength = MaxNameLength,
                    OverflowClip = true,
                    TextBlock = { ForceUpperCase = ForceUpperCase.No },
                    OnEnterPressed = static (textBox, _) =>
                    {
                        textBox.Deselect();
                        return true;
                    }
                };
                nameBox.OnDeselected += (tb, _) =>
                {
                    Name = tb.Text;
                    if (GameMain.Client == null) { return; }
                    distributor.item.CreateClientEvent(distributor, new EventData(this, EventType.NameChange));
                };

                GUITextBlock loadDisplay = GUI.CreateDigitalDisplay(new RectTransform(new Vector2(0.35f, 0.33f), groupContent.RectTransform, Anchor.TopRight) { AbsoluteOffset = (5, 0) },
                    out GUITextBlock? _, out GUITextBlock loadDisplayUnitLabel, TextManager.Get("PowerTransferLoadLabel"), tooltip: TextManager.Get("PowerTransferTipLoad"), leftLabelFont: GUIStyle.Font);
                loadDisplay.TextGetter = () => MathUtils.RoundToInt(Load).ToString();

                ratioSlider = new GUIScrollBar(new RectTransform(new Vector2(1f, 0.33f), groupContent.RectTransform, Anchor.Center), barSize: 0.15f, style: "DeviceSlider")
                {
                    Step = SupplyRatioStep,
                    BarScroll = SupplyRatio,
                    OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                    {
                        if (MathUtils.NearlyEqual(barScroll, SupplyRatio)) { return false; }
                        SupplyRatio = barScroll;
                        if (GameMain.Client != null)
                        {
                            distributor.item.CreateClientEvent(distributor, new EventData(this, EventType.RatioChange));
                            distributor.correctionTimer = CorrectionDelay;
                        }
                        return true;
                    }
                };
                ratioSlider.Bar.RectTransform.MaxSize = new Point(ratioSlider.Bar.Rect.Height);

                GUITextBlock ratioDisplay = GUI.CreateDigitalDisplay(new RectTransform(new Vector2(0.2f, 0.33f), groupContent.RectTransform, Anchor.BottomLeft),
                    out GUITextBlock? _, out GUITextBlock _,
                    rightLabelText: "%");
                ratioDisplay.TextGetter = () => DisplayRatio.ToString();

                GUITextBlock outputDisplay = GUI.CreateDigitalDisplay(new RectTransform(new Vector2(0.35f, 0.33f), groupContent.RectTransform, Anchor.BottomRight) { AbsoluteOffset = (5, 0) },
                    out GUITextBlock? _, out GUITextBlock outputDisplayUnitLabel,
                    TextManager.Get("powerdistributor.supplylabel"), tooltip: TextManager.Get("PowerTransferTipPower"), leftLabelFont: GUIStyle.Font);
                outputDisplay.TextGetter = () => distributor.IsShortCircuited(PowerOut) ? "err" : MathUtils.RoundToInt(distributor.CalculatePowerOut(this)).ToString();

                powerUnitLabels.Add(loadDisplayUnitLabel);
                powerUnitLabels.Add(outputDisplayUnitLabel);
                GUITextBlock.AutoScaleAndNormalize(powerUnitLabels);

                divider = new GUIFrame(new RectTransform(Vector2.UnitX, distributor.groupList!.Content.RectTransform), style: "HorizontalLine");
            }

            private void UpdateNameBox()
            {
                if (nameBox == null || nameBox.Text == DisplayName) { return; }
                nameBox.Text = DisplayName?.Value ?? string.Empty;
            }

            private void UpdateSlider()
            {
                if (ratioSlider == null || MathUtils.NearlyEqual(ratioSlider.BarScroll, supplyRatio)) { return; }
                ratioSlider.BarScroll = supplyRatio;
            }

            public void UpdateGUI()
            {
                IsVisible = PowerOut.Wires.Count >= 1;
                frame!.Visible = IsVisible;
                divider!.Visible = IsVisible && distributor.powerGroups.Last(group => group.frame!.Visible) != this;
                if (distributor.prevLanguage != GameSettings.CurrentConfig.Language) { GUITextBlock.AutoScaleAndNormalize(powerUnitLabels); }
            }
        }

        private GUIListBox? groupList;

        private GUITextBlock? noConnectionsText;

        protected override void CreateGUI()
        {
            if (GuiFrame == null) { return; }
            guiContent = new GUILayoutGroup(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset })
            {
                Stretch = true
            };

            GUIFrame defaultUIContainer = new(new RectTransform(Vector2.UnitX, guiContent.RectTransform, minSize: (0, 125)), style: null)
            {
                CanBeFocused = false
            };
            CreateDefaultPowerUI(defaultUIContainer);

            groupList = new(new RectTransform(Vector2.One, guiContent.RectTransform)) { Enabled = false };
            noConnectionsText = new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.0f), groupList.Content.RectTransform, Anchor.Center), TextManager.Get("powerdistributor.noconnections"), wrap: true)
            {
                Visible = false
            };
            powerGroups.ForEach(group => group.CreateGUI());
        }

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            if (GuiFrame == null) { return; }
            powerGroups.ForEach(group => group.UpdateGUI());
            noConnectionsText!.Visible = powerGroups.None(group => group.IsVisible);
            base.UpdateHUDComponentSpecific(character, deltaTime, cam);
        }

        #region Networking
        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData? extraData = null) => SharedEventWrite(msg, extraData);

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            int msgStartPos = msg.BitPosition;
            SharedEventRead(msg, out EventType eventType, out PowerGroup powerGroup, out string newName, out float newRatio);

            if (correctionTimer > 0f)
            {
                int msgBits = msg.BitPosition - msgStartPos;
                msg.BitPosition -= msgBits;
                StartDelayedCorrection(msg.ExtractBits(msgBits), sendingTime);
                return;
            }

            switch (eventType)
            {
                case EventType.NameChange:
                    powerGroup.Name = newName;
                    break;
                case EventType.RatioChange:
                    powerGroup.SupplyRatio = newRatio;
                    break;
            }
        }
        #endregion
    }
}
