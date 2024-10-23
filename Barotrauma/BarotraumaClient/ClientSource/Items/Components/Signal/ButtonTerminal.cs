using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class ButtonTerminal : ItemComponent, IClientSerializable, IServerSerializable
    {
        private string[] terminalButtonStyles;
        private GUIFrame containerHolder;
        private GUIImage containerIndicator;
        private GUIComponentStyle indicatorStyleRed, indicatorStyleGreen;

        partial void InitProjSpecific(ContentXElement element)
        {
            terminalButtonStyles = new string[requiredSignalCount];
            int i = 0;
            foreach (var childElement in element.GetChildElements("TerminalButton"))
            {
                string style = childElement.GetAttributeString("style", null);
                if (style == null) { continue; }
                terminalButtonStyles[i++] = style;
            }
            indicatorStyleRed = GUIStyle.GetComponentStyle("IndicatorLightRed");
            indicatorStyleGreen = GUIStyle.GetComponentStyle("IndicatorLightGreen");
            CreateGUI();
        }

        protected override void CreateGUI()
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), GuiFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.08f
            };
            paddedFrame.OnAddedToGUIUpdateList += (component) =>
            {
                bool buttonsEnabled = IsActivated;
                foreach (GUIComponent child in component.Children)
                {
                    if (child is not GUIButton) { continue; }
                    if (child.UserData is not int) { continue; }
                    child.Enabled = buttonsEnabled;
                    child.Children.ForEach(c => c.Enabled = buttonsEnabled);
                }
                if (Container == null) { return; }
                bool itemsContained = Container.Inventory.AllItems.Any();
                if (itemsContained)
                {
                    var indicatorStyle = buttonsEnabled ? indicatorStyleGreen : indicatorStyleRed;
                    if (containerIndicator.Style != indicatorStyle)
                    {
                        containerIndicator.ApplyStyle(indicatorStyle);
                    }
                }
                containerIndicator.OverrideState = itemsContained ? GUIComponent.ComponentState.Selected : GUIComponent.ComponentState.None;
            };

            float x = 1.0f / (1 + requiredSignalCount);
            float y = Math.Min((x * paddedFrame.Rect.Width) / paddedFrame.Rect.Height, 0.5f);
            Vector2 relativeSize = new Vector2(x, y);

            var containerSection = new GUIFrame(new RectTransform(new Vector2(x, 1.0f), paddedFrame.RectTransform), style: null);
            var containerSlot = new GUIFrame(new RectTransform(new Vector2(1.0f, y), containerSection.RectTransform, anchor: Anchor.Center), style: null);
            containerHolder = new GUIFrame(new RectTransform(new Vector2(1f, 1.2f), containerSlot.RectTransform, Anchor.BottomCenter), style: null);
            containerIndicator = new GUIImage(new RectTransform(new Vector2(0.5f, 0.5f * (1.0f - y)), containerSection.RectTransform, anchor: Anchor.BottomCenter),
                style: "IndicatorLightRed", scaleToFit: true);

            for (int i = 0; i < requiredSignalCount; i++)
            {
                var button = new GUIButton(new RectTransform(relativeSize, paddedFrame.RectTransform), style: null)
                {
                    UserData = i,
                    OnClicked = (button, userData) =>
                    {
                        int signalIndex = (int)userData;
                        if (GameMain.IsSingleplayer)
                        {
                            SendSignal(signalIndex, Character.Controlled);
                        }
                        else
                        {
                            item.CreateClientEvent(this, new EventData(signalIndex));
                        }
                        return true;
                    }
                };
                var image = new GUIImage(new RectTransform(Vector2.One, button.RectTransform), terminalButtonStyles[i], scaleToFit: true);
            }
        }

        protected override void OnResolutionChanged()
        {
            OnItemLoadedProjSpecific();
        }

        partial void OnItemLoadedProjSpecific()
        {
            if (Container == null) { return; }
            Container.AllowUIOverlap = true;
            Container.Inventory.RectTransform = containerHolder.RectTransform;
        }

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            Write(msg, extraData);
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            SendSignal(msg.ReadRangedInteger(0, Signals.Length - 1), sender: null, ignoreState: true);
        }
    }
}