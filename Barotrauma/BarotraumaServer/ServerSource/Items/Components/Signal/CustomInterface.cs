using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class CustomInterface : ItemComponent, IClientSerializable, IServerSerializable
    {
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            bool[] elementStates = new bool[customInterfaceElementList.Count];
            string[] elementValues = new string[customInterfaceElementList.Count];
            for (int i = 0; i < customInterfaceElementList.Count; i++)
            {
                if (!string.IsNullOrEmpty(customInterfaceElementList[i].PropertyName))
                {
                    elementValues[i] = msg.ReadString();
                }
                else
                {
                    elementStates[i] = msg.ReadBoolean();
                }
            }

            CustomInterfaceElement clickedButton = null;
            if ((c.Character != null && DrawHudWhenEquipped && item.ParentInventory?.Owner == c.Character) || item.CanClientAccess(c))
            {
                for (int i = 0; i < customInterfaceElementList.Count; i++)
                {
                    if (!string.IsNullOrEmpty(customInterfaceElementList[i].PropertyName))
                    {
                        TextChanged(customInterfaceElementList[i], elementValues[i]);
                    }
                    else if (customInterfaceElementList[i].ContinuousSignal)
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
                item.GetComponentIndex(this),
                clickedButton
            });

            item.CreateServerEvent(this);
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            //extradata contains an array of buttons clicked by a client (or nothing if nothing was clicked)
            for (int i = 0; i < customInterfaceElementList.Count; i++)
            {
                if (!string.IsNullOrEmpty(customInterfaceElementList[i].PropertyName))
                {
                    msg.Write(customInterfaceElementList[i].Signal);
                }
                else if(customInterfaceElementList[i].ContinuousSignal)
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
