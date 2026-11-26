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
                var element = customInterfaceElementList[i];
                switch (element.InputType)
                {
                    case CustomInterfaceElement.InputTypeOption.Number:
                    case CustomInterfaceElement.InputTypeOption.Text:
                        elementValues[i] = msg.ReadString();
                        break;
                    case CustomInterfaceElement.InputTypeOption.Button:
                    case CustomInterfaceElement.InputTypeOption.TickBox:
                        elementStates[i] = msg.ReadBoolean();
                        break;
                }
            }

            CustomInterfaceElement clickedButton = null;

            bool allowInteraction = IsEnabled;
            if (allowInteraction)
            {
                allowInteraction = (c.Character != null && DrawHudWhenEquipped && item.ParentInventory?.Owner == c.Character) || item.CanClientAccess(c);
            }

            if (allowInteraction)
            {
                for (int i = 0; i < customInterfaceElementList.Count; i++)
                {
                    var element = customInterfaceElementList[i]; 
                    switch (element.InputType)
                    {
                        case CustomInterfaceElement.InputTypeOption.Number:
                            switch (element.NumberType)
                            {
                                case NumberType.Int when int.TryParse(elementValues[i], out int value):
                                    ValueChanged(element, value);
                                    break;
                                case NumberType.Float when TryParseFloatInvariantCulture(elementValues[i], out float value):
                                    ValueChanged(element, value);
                                    break;
                            }
                            break;
                        case CustomInterfaceElement.InputTypeOption.Text:
                            TextChanged(element, elementValues[i]);
                            break;
                        case CustomInterfaceElement.InputTypeOption.TickBox:
                            TickBoxToggled(element, elementStates[i]);
                            break;
                        case CustomInterfaceElement.InputTypeOption.Button:
                            if (elementStates[i])
                            {
                                clickedButton = element;
                                ButtonClicked(element, user: c.Character);
                            }
                            break;
                    }
                }
            }

            //notify all clients of the new state
            item.CreateServerEvent(this, new EventData(clickedButton, user: c.Character));

            item.CreateServerEvent(this);
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            //extradata contains an array of buttons clicked by a client (or nothing if nothing was clicked)
            for (int i = 0; i < customInterfaceElementList.Count; i++)
            {
                var element = customInterfaceElementList[i];

                switch (element.InputType)
                {
                    case CustomInterfaceElement.InputTypeOption.Number:
                    case CustomInterfaceElement.InputTypeOption.Text:
                        msg.WriteString(element.Signal);                
                        break;
                    case CustomInterfaceElement.InputTypeOption.TickBox:
                        msg.WriteBoolean(element.State);
                        break;
                    case CustomInterfaceElement.InputTypeOption.Button:
                        if (extraData is Item.ComponentStateEventData { ComponentData: EventData eventData } && eventData.BtnElement == customInterfaceElementList[i])
                        {
                            msg.WriteUInt16(eventData.User?.ID ?? Entity.NullEntityID);
                            //button clicked
                            msg.WriteBoolean(true);

                        }
                        else
                        {
                            //button not clicked
                            msg.WriteUInt16(Entity.NullEntityID);
                            msg.WriteBoolean(false);
                        }
                        break;
                }
            }
        }
    }
}
