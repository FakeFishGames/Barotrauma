using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;

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
            terminalButtonStyles = new string[RequiredSignalCount];
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
                bool buttonsEnabled = AllowUsingButtons;
                foreach (var child in component.Children)
                {
                    if (!(child is GUIButton)) { continue; }
                    if (!(child.UserData is int)) { continue; }
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

            float x = 1.0f / (1 + RequiredSignalCount);
            float y = Math.Min((x * paddedFrame.Rect.Width) / paddedFrame.Rect.Height, 0.5f);
            Vector2 relativeSize = new Vector2(x, y);

            var containerSection = new GUIFrame(new RectTransform(new Vector2(x, 1.0f), paddedFrame.RectTransform), style: null);
            var containerSlot = new GUIFrame(new RectTransform(new Vector2(1.0f, y), containerSection.RectTransform, anchor: Anchor.Center), style: null);
            containerHolder = new GUIFrame(new RectTransform(new Vector2(1f, 1.2f), containerSlot.RectTransform, Anchor.BottomCenter), style: null);
            containerIndicator = new GUIImage(new RectTransform(new Vector2(0.5f, 0.5f * (1.0f - y)), containerSection.RectTransform, anchor: Anchor.BottomCenter),
                style: "IndicatorLightRed", scaleToFit: true);

            for (int i = 0; i < RequiredSignalCount; i++)
            {
                var button = new GUIButton(new RectTransform(relativeSize, paddedFrame.RectTransform), style: null)
                {
                    UserData = i,
                    OnClicked = (button, userData) =>
                    {
                        if (GameMain.IsSingleplayer)
                        {
                            SendSignal((int)userData, Character.Controlled);
                        }
                        else
                        {
                            item.CreateClientEvent(this, new object[] { userData });
                        }
                        return true;
                    }
                };
                var image = new GUIImage(new RectTransform(Vector2.One, button.RectTransform), terminalButtonStyles[i], scaleToFit: true);
            }
        }

        protected override void OnResolutionChanged()
        {
            base.OnResolutionChanged();
            OnItemLoadedProjSpecific();
        }

        partial void OnItemLoadedProjSpecific()
        {
            if (Container == null) { return; }
            Container.AllowUIOverlap = true;
            Container.Inventory.RectTransform = containerHolder.RectTransform;
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            Write(msg, extraData);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            SendSignal(msg.ReadRangedInteger(0, Signals.Length - 1), sender: null, isServerMessage: true);
        }
    }
}