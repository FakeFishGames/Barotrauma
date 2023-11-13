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
                    var element = customInterfaceElementList[i];
                    if (element.HasPropertyName)
                    {
                        if (!element.IsNumberInput)
                        {
                            TextChanged(element, elementValues[i]);
                        }
                        else
                        {
                            switch (element.NumberType)
                            {
                                case NumberType.Int when int.TryParse(elementValues[i], out int value):
                                    ValueChanged(element, value);
                                    break;
                                case NumberType.Float when TryParseFloatInvariantCulture(elementValues[i], out float value):
                                    ValueChanged(element, value);
                                    break;
                            }
                        }
                    }
                    else if (element.ContinuousSignal)
                    {
                        TickBoxToggled(element, elementStates[i]);
                    }
                    else if (elementStates[i])
                    {
                        clickedButton = element;
                        ButtonClicked(element);
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
                var element = customInterfaceElementList[i];
                if (element.HasPropertyName)
                {
                    msg.WriteString(element.Signal);
                }
                else if(element.ContinuousSignal)
                {
                    msg.WriteBoolean(element.State);
                }
                else
                {
                    msg.WriteBoolean(extraData is Item.ComponentStateEventData { ComponentData: EventData eventData } && eventData.BtnElement == customInterfaceElementList[i]);
                }
            }
        }
    }
}
