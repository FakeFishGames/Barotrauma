using Barotrauma.Networking;
using Lidgren.Network;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class CustomInterface : ItemComponent, IClientSerializable, IServerSerializable
    {
        class CustomInterfaceElement
        {
            public bool ContinuousSignal;
            public bool State;
            public string text, connection, signal;

            public CustomInterfaceElement(XElement element)
            {
                text = element.GetAttributeString("text", "");
                connection = element.GetAttributeString("connection", "");
                signal = element.GetAttributeString("signal", "1");
            }
        }

        private List<CustomInterfaceElement> customInterfaceElementList = new List<CustomInterfaceElement>();

        public CustomInterface(Item item, XElement element)
            : base(item, element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "button":
                        var button = new CustomInterfaceElement(subElement)
                        {
                            ContinuousSignal = false
                        };
                        if (string.IsNullOrEmpty(button.text))
                        {
                            button.text = "Signal out " + customInterfaceElementList.Count(e => !e.ContinuousSignal);
                        }
                        customInterfaceElementList.Add(button);
                        break;
                    case "tickbox":
                        var tickBox = new CustomInterfaceElement(subElement)
                        {
                            ContinuousSignal = true
                        };
                        if (string.IsNullOrEmpty(tickBox.text))
                        {
                            tickBox.text = "Signal out " + customInterfaceElementList.Count(e => !e.ContinuousSignal);
                        }
                        customInterfaceElementList.Add(tickBox);
                        break;
                }
            }
            IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);     
        
        private void ButtonClicked(CustomInterfaceElement btnElement)
        {
            if (btnElement == null) return;
            item.SendSignal(0, btnElement.signal, btnElement.connection, sender: null, source: item);
        }

        private void TickBoxToggled(CustomInterfaceElement tickBoxElement, bool state)
        {
            if (tickBoxElement == null) return;
            tickBoxElement.State = state;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            foreach (CustomInterfaceElement ciElement in customInterfaceElementList)
            {
                if (!ciElement.ContinuousSignal) { continue; }
                //TODO: allow changing output when a tickbox is not selected
                item.SendSignal(0, ciElement.State ? ciElement.signal : "0", ciElement.connection, sender: null, source: item);
            }
        }


        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            bool[] elementStates = new bool[customInterfaceElementList.Count];
            for (int i = 0; i < customInterfaceElementList.Count; i++)
            {
                elementStates[i] = msg.ReadBoolean();
            }

            CustomInterfaceElement clickedButton = null;
            if (item.CanClientAccess(c))
            {
                for (int i = 0; i < customInterfaceElementList.Count; i++)
                {
                    if (customInterfaceElementList[i].ContinuousSignal)
                    {
                        TickBoxToggled(customInterfaceElementList[i], elementStates[i]);
                    }
                    else if (elementStates[i])
                    {
                        clickedButton = customInterfaceElementList[i];
                        ButtonClicked(customInterfaceElementList[i]);
                    }
                }
            }

            //notify all clients of the new state
            GameMain.Server.CreateEntityEvent(item, new object[] 
            {
                NetEntityEvent.Type.ComponentState,
                item.components.IndexOf(this),
                clickedButton
            });

            item.CreateServerEvent(this);
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            //extradata contains an array of buttons clicked by a client (or nothing if nothing was clicked)
            for (int i = 0; i < customInterfaceElementList.Count; i++)
            {
                if (customInterfaceElementList[i].ContinuousSignal)
                {
                    msg.Write(customInterfaceElementList[i].State);
                }
                else
                {
                    msg.Write(extraData != null && extraData.Any(d => d as CustomInterfaceElement == customInterfaceElementList[i]));
                }
            }
        }
    }
}
