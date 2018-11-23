using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    class CustomInterfaceElement
    {
        public string text, connection, signal;
    }

    partial class CustomInterface
    {
        protected List<CustomInterfaceElement> customInterfaceElementList;

        partial void InitProjSpecific(XElement element)
        {
            customInterfaceElementList = new List<CustomInterfaceElement>();

            GUILayoutGroup paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.7f), GuiFrame.RectTransform, Anchor.Center))
            { RelativeSpacing = 0.1f, Stretch = true };

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "button":
                        CustomInterfaceElement CIElement = new CustomInterfaceElement();
                        CIElement.text = subElement.GetAttributeString("text", "Default name");
                        CIElement.connection = subElement.GetAttributeString("connection", "");
                        CIElement.signal = subElement.GetAttributeString("signal", "1");

                        var btn = new GUIButton(new RectTransform(
                            new Vector2(1.0f, 0.1f), paddedFrame.RectTransform),
                            CIElement.text);
                        btn.UserData = CIElement.connection;
                        btn.OnClicked += ( _, userdata) =>
                        {
                            item.SendSignal(0, "1", (string)userdata, null);
                            return true;
                        };

                        break;

                    case "tickbox":

                        break;
                }
            }
        }
    }
}
