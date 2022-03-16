using Barotrauma.Networking;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class CustomInterface : ItemComponent, IClientSerializable, IServerSerializable
    {
        public void ServerEventRead(IReadMessage msg, Client c)
        {
            bool[] elementStates = new bool[customInterfaceElementList.Count];
            string[] elementValues = new string[customInterfaceElementList.Count];
            for (int i = 0; i < customInterfaceElementList.Count; i++)
            {
                if (customInterfaceElementList[i].HasPropertyName)
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
                    if (customInterfaceElementList[i].HasPropertyName)
                    {
                        if (!customInterfaceElementList[i].IsIntegerInput)
                        {
                            TextChanged(customInterfaceElementList[i], elementValues[i]);
                        }
                        else
                        {
                            int.TryParse(elementValues[i], out int value);
                            ValueChanged(customInterfaceElementList[i], value);
                        }
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
            item.CreateServerEvent(this, new EventData(clickedButton));

            item.CreateServerEvent(this);
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            //extradata contains an array of buttons clicked by a client (or nothing if nothing was clicked)
            for (int i = 0; i < customInterfaceElementList.Count; i++)
            {
                if (customInterfaceElementList[i].HasPropertyName)
                {
                    msg.Write(customInterfaceElementList[i].Signal);
                }
                else if(customInterfaceElementList[i].ContinuousSignal)
                {
                    msg.Write(customInterfaceElementList[i].State);
                }
                else
                {
                    msg.Write(extraData is Item.ComponentStateEventData { ComponentData: EventData eventData } && eventData.BtnElement == customInterfaceElementList[i]);
                }
            }
        }
    }
}
