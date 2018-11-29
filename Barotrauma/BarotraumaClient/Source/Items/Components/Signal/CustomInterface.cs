using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class CustomInterface
    {
        private List<GUIComponent> uiElements = new List<GUIComponent>();

        partial void InitProjSpecific(XElement element)
        {
            uiElements.Clear();

            GUILayoutGroup paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.7f), GuiFrame.RectTransform, Anchor.Center))
            { RelativeSpacing = 0.1f, Stretch = true };

            foreach (CustomInterfaceElement ciElement in customInterfaceElementList)
            {
                if (ciElement.ContinuousSignal)
                {
                    var tickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform), ciElement.text)
                    {
                        UserData = ciElement
                    };
                    tickBox.OnSelected += (tBox) =>
                    {
                        if (GameMain.Client == null)
                        {
                            TickBoxToggled(tBox.UserData as CustomInterfaceElement, tBox.Selected);
                        }
                        else
                        {
                            item.CreateClientEvent(this);
                        }
                        return true;
                    };
                    uiElements.Add(tickBox);
                }
                else
                {
                    var btn = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform), ciElement.text)
                    {
                        UserData = ciElement
                    };
                    btn.OnClicked += (_, userdata) =>
                    {
                        if (GameMain.Client == null)
                        {
                            ButtonClicked(userdata as CustomInterfaceElement);
                        }
                        else
                        {
                            GameMain.Client.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, item.components.IndexOf(this), userdata as CustomInterfaceElement });
                        }
                        return true;
                    };
                    uiElements.Add(btn);
                }
            }
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            //extradata contains an array of buttons clicked by the player (or nothing if the player didn't click anything)
            for (int i = 0; i < customInterfaceElementList.Count; i++)
            {
                if (customInterfaceElementList[i].ContinuousSignal)
                {
                    msg.Write(((GUITickBox)uiElements[i]).Selected);
                }
                else
                {
                    msg.Write(extraData != null && extraData.Any(d => d as CustomInterfaceElement == customInterfaceElementList[i]));
                }
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            for (int i = 0; i < customInterfaceElementList.Count; i++)
            {
                bool elementState = msg.ReadBoolean();
                if (customInterfaceElementList[i].ContinuousSignal)
                {
                    ((GUITickBox)uiElements[i]).Selected = elementState;
                    TickBoxToggled(customInterfaceElementList[i], elementState);
                }
                else if (elementState)
                {
                    ButtonClicked(customInterfaceElementList[i]);
                }
            }
        }
    }
}
