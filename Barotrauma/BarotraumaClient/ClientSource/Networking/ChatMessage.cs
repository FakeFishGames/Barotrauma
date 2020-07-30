using System;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class ChatMessage
    {
        public virtual void ClientWrite(IWriteMessage msg)
        {
            msg.Write((byte)ClientNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write((byte)Type);
            msg.Write(Text);
        }

        public static void ClientRead(IReadMessage msg)
        {
            UInt16 ID = msg.ReadUInt16();
            ChatMessageType type = (ChatMessageType)msg.ReadByte();
            PlayerConnectionChangeType changeType = PlayerConnectionChangeType.None;
            string txt = "";
            string styleSetting = string.Empty;

            if (type != ChatMessageType.Order)
            {
                changeType = (PlayerConnectionChangeType)msg.ReadByte();
                txt = msg.ReadString();
            }

            string senderName = msg.ReadString();
            Character senderCharacter = null;
            bool hasSenderCharacter = msg.ReadBoolean();
            if (hasSenderCharacter)
            {
                senderCharacter = Entity.FindEntityByID(msg.ReadUInt16()) as Character;
                if (senderCharacter != null)
                {
                    senderName = senderCharacter.Name;
                }
            }

            switch (type)
            {
                case ChatMessageType.Default:
                    break;
                case ChatMessageType.Order:
                    int orderIndex = msg.ReadByte();
                    UInt16 targetCharacterID = msg.ReadUInt16();
                    Character targetCharacter = Entity.FindEntityByID(targetCharacterID) as Character;
                    Entity targetEntity = Entity.FindEntityByID(msg.ReadUInt16());
                    int optionIndex = msg.ReadByte();

                    Order order = null;
                    if (orderIndex < 0 || orderIndex >= Order.PrefabList.Count)
                    {
                        DebugConsole.ThrowError("Invalid order message - order index out of bounds.");
                        if (NetIdUtils.IdMoreRecent(ID, LastID)) LastID = ID;
                        return;
                    }
                    else
                    {
                        order = Order.PrefabList[orderIndex];
                    }
                    string orderOption = "";
                    if (optionIndex >= 0 && optionIndex < order.Options.Length)
                    {
                        orderOption = order.Options[optionIndex];
                    }
                    txt = order.GetChatMessage(targetCharacter?.Name, senderCharacter?.CurrentHull?.DisplayName, givingOrderToSelf: targetCharacter == senderCharacter, orderOption: orderOption);

                    if (GameMain.Client.GameStarted && Screen.Selected == GameMain.GameScreen)
                    {
                        if (order.TargetAllCharacters)
                        {
                            GameMain.GameSession?.CrewManager?.AddOrder(
                                new Order(order.Prefab, targetEntity, (targetEntity as Item)?.Components.FirstOrDefault(ic => ic.GetType() == order.ItemComponentType), orderGiver: senderCharacter),
                                order.Prefab.FadeOutTime);
                        }
                        else if (targetCharacter != null)
                        {
                            targetCharacter.SetOrder(
                                new Order(order.Prefab, targetEntity, (targetEntity as Item)?.Components.FirstOrDefault(ic => ic.GetType() == order.ItemComponentType), orderGiver: senderCharacter),
                                    orderOption, senderCharacter);
                        }
                    }

                    if (NetIdUtils.IdMoreRecent(ID, LastID))
                    {
                        GameMain.Client.AddChatMessage(
                            new OrderChatMessage(order, orderOption, txt, targetEntity, targetCharacter, senderCharacter));
                        LastID = ID;
                    }
                    return;
                case ChatMessageType.ServerMessageBox:
                    txt = TextManager.GetServerMessage(txt);
                    break;
                case ChatMessageType.ServerMessageBoxInGame:
                    styleSetting = msg.ReadString();
                    txt = TextManager.GetServerMessage(txt);
                    break;
            }

            if (NetIdUtils.IdMoreRecent(ID, LastID))
            {
                switch (type)
                {
                    case ChatMessageType.MessageBox:
                    case ChatMessageType.ServerMessageBox:
                        //only show the message box if the text differs from the text in the currently visible box
                        if ((GUIMessageBox.VisibleBox as GUIMessageBox)?.Text?.Text != txt)
                        {
                            new GUIMessageBox("", txt);
                        }
                        break;
                    case ChatMessageType.ServerMessageBoxInGame:
                        new GUIMessageBox("", txt, new string[0], type: GUIMessageBox.Type.InGame, iconStyle: styleSetting);
                        break;
                    case ChatMessageType.Console:
                        DebugConsole.NewMessage(txt, MessageColor[(int)ChatMessageType.Console]);
                        break;
                    case ChatMessageType.ServerLog:
                        if (!Enum.TryParse(senderName, out ServerLog.MessageType messageType))
                        {
                            return;
                        }
                        GameMain.Client.ServerSettings.ServerLog?.WriteLine(txt, messageType);
                        break;
                    default:
                        GameMain.Client.AddChatMessage(txt, type, senderName, senderCharacter, changeType);
                        break;
                }
                LastID = ID;
            }
        }
    }
}
