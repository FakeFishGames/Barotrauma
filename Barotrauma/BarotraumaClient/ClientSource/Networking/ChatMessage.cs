using Microsoft.Xna.Framework;
using System;

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
            UInt16 id = msg.ReadUInt16();
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
            Client senderClient = null;
            bool hasSenderClient = msg.ReadBoolean();
            if (hasSenderClient)
            {
                UInt64 clientId = msg.ReadUInt64();
                senderClient = GameMain.Client.ConnectedClients.Find(c => c.SteamID == clientId || c.ID == clientId);
                if (senderClient != null) { senderName = senderClient.Name; }
            }
            bool hasSenderCharacter = msg.ReadBoolean();
            if (hasSenderCharacter)
            {
                senderCharacter = Entity.FindEntityByID(msg.ReadUInt16()) as Character;
                if (senderCharacter != null)
                {
                    senderName = senderCharacter.Name;
                }
            }

            Color? textColor = null;
            if (msg.ReadBoolean())
            {
                textColor = msg.ReadColorR8G8B8A8();
            }

            msg.ReadPadBits();

            switch (type)
            {
                case ChatMessageType.Default:
                    break;
                case ChatMessageType.Order:
                    var orderMessageInfo = OrderChatMessage.ReadOrder(msg);
                    if (orderMessageInfo.OrderIdentifier == Identifier.Empty)
                    {
                        DebugConsole.ThrowError("Invalid order message - order index out of bounds.");
                        if (NetIdUtils.IdMoreRecent(id, LastID)) { LastID = id; }
                        return;
                    }
                    var orderPrefab = orderMessageInfo.OrderPrefab ?? OrderPrefab.Prefabs[orderMessageInfo.OrderIdentifier];
                    Identifier orderOption = orderMessageInfo.OrderOption;
                    orderOption = orderOption.IfEmpty(
                        orderMessageInfo.OrderOptionIndex.HasValue && orderMessageInfo.OrderOptionIndex >= 0 && orderMessageInfo.OrderOptionIndex < orderPrefab.Options.Length
                            ? orderPrefab.Options[orderMessageInfo.OrderOptionIndex.Value]
                            : Identifier.Empty);
                    string targetRoom;

                    if (orderMessageInfo.TargetEntity is Hull targetHull)
                    {
                        targetRoom = targetHull.DisplayName.Value;
                    }
                    else
                    {
                        targetRoom = senderCharacter?.CurrentHull?.DisplayName?.Value;
                    }

                    txt = orderPrefab.GetChatMessage(orderMessageInfo.TargetCharacter?.Name, targetRoom,
                        givingOrderToSelf: orderMessageInfo.TargetCharacter == senderCharacter,
                        orderOption: orderOption,
                        isNewOrder: orderMessageInfo.IsNewOrder);

                    if (GameMain.Client.GameStarted && Screen.Selected == GameMain.GameScreen)
                    {
                        Order order = null;
                        switch (orderMessageInfo.TargetType)
                        {
                            case Order.OrderTargetType.Entity:
                                order = new Order(orderPrefab, orderOption, orderMessageInfo.TargetEntity, orderPrefab.GetTargetItemComponent(orderMessageInfo.TargetEntity as Item), orderGiver: senderCharacter);
                                break;
                            case Order.OrderTargetType.Position:
                                order = new Order(orderPrefab, orderOption, orderMessageInfo.TargetPosition, orderGiver: senderCharacter);
                                break;
                            case Order.OrderTargetType.WallSection:
                                order = new Order(orderPrefab, orderOption, orderMessageInfo.TargetEntity as Structure, orderMessageInfo.WallSectionIndex, orderGiver: senderCharacter);
                                break;
                        }

                        if (order != null)
                        {
                            order = order.WithManualPriority(orderMessageInfo.Priority);
                            if (order.TargetAllCharacters)
                            {
                                var fadeOutTime = !orderPrefab.IsIgnoreOrder ? (float?)orderPrefab.FadeOutTime : null;
                                GameMain.GameSession?.CrewManager?.AddOrder(order, fadeOutTime);
                            }
                            else
                            {
                                orderMessageInfo.TargetCharacter?.SetOrder(order, orderMessageInfo.IsNewOrder);
                            }
                        }
                    }

                    if (NetIdUtils.IdMoreRecent(id, LastID))
                    {
                        Order order = null;
                        if (orderMessageInfo.TargetPosition != null)
                        {
                            order = new Order(orderPrefab, orderOption, orderMessageInfo.TargetPosition, orderGiver: senderCharacter)
                                .WithManualPriority(orderMessageInfo.Priority);
                        }
                        else if (orderMessageInfo.WallSectionIndex != null)
                        {
                            order = new Order(orderPrefab, orderOption, orderMessageInfo.TargetEntity as Structure, orderMessageInfo.WallSectionIndex, orderGiver: senderCharacter)
                                .WithManualPriority(orderMessageInfo.Priority);
                        }
                        else
                        {
                            order = new Order(orderPrefab, orderOption, orderMessageInfo.TargetEntity, orderPrefab.GetTargetItemComponent(orderMessageInfo.TargetEntity as Item), orderGiver: senderCharacter)
                                .WithManualPriority(orderMessageInfo.Priority);
                        }
                        GameMain.Client.AddChatMessage(
                            new OrderChatMessage(order, txt, orderMessageInfo.TargetCharacter, senderCharacter));
                        LastID = id;
                    }
                    return;
                case ChatMessageType.ServerMessageBox:
                    txt = TextManager.GetServerMessage(txt).Value;
                    break;
                case ChatMessageType.ServerMessageBoxInGame:
                    styleSetting = msg.ReadString();
                    txt = TextManager.GetServerMessage(txt).Value;
                    break;
            }

            if (NetIdUtils.IdMoreRecent(id, LastID))
            {
                switch (type)
                {
                    case ChatMessageType.MessageBox:
                    case ChatMessageType.ServerMessageBox:
                        //only show the message box if the text differs from the text in the currently visible box
                        if ((GUIMessageBox.VisibleBox as GUIMessageBox)?.Text?.Text != txt)
                        {
                            GUIMessageBox messageBox = new GUIMessageBox("", txt);
                            if (textColor != null) { messageBox.Text.TextColor = textColor.Value; }
                        }
                        break;
                    case ChatMessageType.ServerMessageBoxInGame:
                        {
                            GUIMessageBox messageBox = new GUIMessageBox("", txt, Array.Empty<LocalizedString>(), type: GUIMessageBox.Type.InGame, iconStyle: styleSetting);
                            if (textColor != null) { messageBox.Text.TextColor = textColor.Value; }
                        }
                        break;
                    case ChatMessageType.Console:
                        DebugConsole.NewMessage(txt, textColor == null ? MessageColor[(int)ChatMessageType.Console] : textColor.Value);
                        break;
                    case ChatMessageType.ServerLog:
                        if (!Enum.TryParse(senderName, out ServerLog.MessageType messageType))
                        {
                            return;
                        }
                        GameMain.Client.ServerSettings.ServerLog?.WriteLine(txt, messageType);
                        break;
                    default:
                        GameMain.Client.AddChatMessage(txt, type, senderName, senderClient, senderCharacter, changeType, textColor: textColor);
                        break;
                }
                LastID = id;
            }
        }
    }
}
