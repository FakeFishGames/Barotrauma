﻿using System;
using System.Text;

namespace Barotrauma.Networking
{
    partial class ChatMessage
    {
        public static void ServerRead(IReadMessage msg, Client c)
        {
            c.KickAFKTimer = 0.0f;

            UInt16 ID = msg.ReadUInt16();
            ChatMessageType type = (ChatMessageType)msg.ReadRangedInteger(0, Enum.GetValues(typeof(ChatMessageType)).Length - 1);
            ChatMode chatMode = (ChatMode)msg.ReadRangedInteger(0, Enum.GetValues(typeof(ChatMode)).Length - 1);
            string txt;

            Character orderTargetCharacter = null;
            Entity orderTargetEntity = null;
            OrderChatMessage orderMsg = null;
            Order.OrderTargetType orderTargetType = Order.OrderTargetType.Entity;
            int? wallSectionIndex = null;
            Order order = null;
            bool isNewOrder = false;
            if (type == ChatMessageType.Order)
            {
                var orderMessageInfo = OrderChatMessage.ReadOrder(msg);
                if (orderMessageInfo.OrderIdentifier == Identifier.Empty)
                {
                    DebugConsole.ThrowError($"Invalid order message from client \"{c.Name}\" - order identifier is empty.");
                    if (NetIdUtils.IdMoreRecent(ID, c.LastSentChatMsgID)) { c.LastSentChatMsgID = ID; }
                    return;
                }
                isNewOrder = orderMessageInfo.IsNewOrder;
                orderTargetCharacter = orderMessageInfo.TargetCharacter;
                orderTargetEntity = orderMessageInfo.TargetEntity;
                OrderTarget orderTargetPosition = orderMessageInfo.TargetPosition;
                orderTargetType = orderMessageInfo.TargetType;
                wallSectionIndex = orderMessageInfo.WallSectionIndex;
                var orderPrefab = orderMessageInfo.OrderPrefab ?? OrderPrefab.Prefabs[orderMessageInfo.OrderIdentifier];
                Identifier orderOption = orderMessageInfo.OrderOption;
                if (orderOption.IsEmpty)
                {
                    orderOption = orderMessageInfo.OrderOptionIndex == null || orderMessageInfo.OrderOptionIndex < 0 || orderMessageInfo.OrderOptionIndex >= orderPrefab.Options.Length ?
                        Identifier.Empty : orderPrefab.Options[orderMessageInfo.OrderOptionIndex.Value];
                }
                if (orderTargetType == Order.OrderTargetType.Position)
                {
                    order = new Order(orderPrefab, orderOption, orderTargetPosition, orderGiver: c.Character)
                        .WithManualPriority(orderMessageInfo.Priority);
                }
                else if (orderTargetType == Order.OrderTargetType.WallSection)
                {
                    order = new Order(orderPrefab, orderOption, orderTargetEntity as Structure, wallSectionIndex, orderGiver: c.Character)
                        .WithManualPriority(orderMessageInfo.Priority);
                }
                else
                {
                    order = new Order(orderPrefab, orderOption, orderTargetEntity, orderPrefab.GetTargetItemComponent(orderTargetEntity as Item), orderGiver: c.Character)
                        .WithManualPriority(orderMessageInfo.Priority);
                }
                orderMsg = new OrderChatMessage(order, orderTargetCharacter, c.Character);
                txt = orderMsg.Text;
            }
            else
            {
                txt = msg.ReadString() ?? "";
            }

            if (!NetIdUtils.IdMoreRecent(ID, c.LastSentChatMsgID)) { return; }

            c.LastSentChatMsgID = ID;

            if (txt.Length > MaxLength)
            {
                txt = txt.Substring(0, MaxLength);
            }

            c.LastSentChatMessages.Add(txt);
            if (c.LastSentChatMessages.Count > 10)
            {
                c.LastSentChatMessages.RemoveRange(0, c.LastSentChatMessages.Count - 10);
            }

            float similarity = 0.0f;
            for (int i = 0; i < c.LastSentChatMessages.Count; i++)
            {
                float closeFactor = 1.0f / (c.LastSentChatMessages.Count - i);
                    
                if (string.IsNullOrEmpty(txt))
                {
                    similarity += closeFactor;
                }
                else
                {
                    int levenshteinDist = ToolBox.LevenshteinDistance(txt, c.LastSentChatMessages[i]);
                    similarity += Math.Max((txt.Length - levenshteinDist) / (float)txt.Length * closeFactor, 0.0f);
                }
            }
            //order/report messages can be sent a little faster than normal messages without triggering the spam filter
            if (orderMsg != null)
            {
                similarity *= 0.25f;
            }

            bool isSpamExempt = RateLimiter.IsExempt(c);

            if (similarity + c.ChatSpamSpeed > 5.0f && !isSpamExempt)
            {
                GameMain.Server.KarmaManager.OnSpamFilterTriggered(c);

                c.ChatSpamCount++;
                if (c.ChatSpamCount > 3)
                {
                    //kick for spamming too much
                    GameMain.Server.KickClient(c, TextManager.Get("SpamFilterKicked").Value);
                }
                else
                {
                    ChatMessage denyMsg = Create("", TextManager.Get("SpamFilterBlocked").Value, ChatMessageType.Server, null);
                    c.ChatSpamTimer = 10.0f;
                    GameMain.Server.SendDirectChatMessage(denyMsg, c);
                }
                return;
            }

            c.ChatSpamSpeed += similarity + 0.5f;

            if (c.ChatSpamTimer > 0.0f && !isSpamExempt)
            {
                ChatMessage denyMsg = Create("", TextManager.Get("SpamFilterBlocked").Value, ChatMessageType.Server, null);
                c.ChatSpamTimer = 10.0f;
                GameMain.Server.SendDirectChatMessage(denyMsg, c);
                return;
            }

            if (type == ChatMessageType.Order)
            {
                if (c.Character == null || c.Character.SpeechImpediment >= 100.0f || c.Character.IsDead) { return; }
                if (orderMsg.Order.IsReport)
                {
                    HumanAIController.ReportProblem(orderMsg.Sender, orderMsg.Order);
                }
                if (order != null)
                {
                    if (order.TargetAllCharacters)
                    {
                        if (order.IsIgnoreOrder)
                        {
                            switch (orderTargetType)
                            {
                                case Order.OrderTargetType.Entity:
                                    if (!(orderTargetEntity is IIgnorable ignorableEntity)) { break; }
                                    ignorableEntity.OrderedToBeIgnored = order.Identifier == "ignorethis";
                                    break;
                                case Order.OrderTargetType.Position:
                                    throw new NotImplementedException();
                                case Order.OrderTargetType.WallSection:
                                    if (!wallSectionIndex.HasValue) { break; }
                                    if (!(orderTargetEntity is Structure s)) { break; }
                                    if (!(s.GetSection(wallSectionIndex.Value) is IIgnorable ignorableWall)) { break; }
                                    ignorableWall.OrderedToBeIgnored = order.Identifier == "ignorethis";
                                    break;
                            }
                        }
                        GameMain.GameSession?.CrewManager?.AddOrder(order, order.IsIgnoreOrder ? (float?)null : order.FadeOutTime);
                    }
                    else if (orderTargetCharacter != null)
                    {
                        orderTargetCharacter.SetOrder(order, isNewOrder);
                    }
                }
                GameMain.Server.SendOrderChatMessage(orderMsg);
            }
            else
            {
                GameMain.Server.SendChatMessage(txt, senderClient: c, chatMode: chatMode);
            }
        }

        public int EstimateLengthBytesServer(Client c)
        {
            int length = 1 + //(byte)ServerNetObject.CHAT_MESSAGE
                            2 + //(UInt16)NetStateID
                            1 + //(byte)Type
                            Encoding.UTF8.GetBytes(Text).Length + 2;
            
            if (SenderClient != null)
            {
                length += 8; //SteamID or local ID (ulong)
            }
            if (Sender != null && c.InGame)
            {
                length += 2; //sender ID (UInt16)
            }
            if (SenderName != null)
            {
                length += Encoding.UTF8.GetBytes(SenderName).Length + 2;
            }

            return length;
        }

        public virtual void ServerWrite(in SegmentTableWriter<ServerNetSegment> segmentTable, IWriteMessage msg, Client c)
        {
            segmentTable.StartNewSegment(ServerNetSegment.ChatMessage);
            msg.WriteUInt16(NetStateID);
            msg.WriteRangedInteger((int)Type, 0, Enum.GetValues(typeof(ChatMessageType)).Length - 1);
            msg.WriteByte((byte)ChangeType);
            msg.WriteString(Text);

            msg.WriteString(SenderName);
            msg.WriteBoolean(SenderClient != null);
            if (SenderClient != null)
            {
                msg.WriteString(SenderClient.AccountId.TryUnwrap(out var accountId) ? accountId.StringRepresentation : SenderClient.SessionId.ToString());
            }
            msg.WriteBoolean(Sender != null && c.InGame);
            if (Sender != null && c.InGame)
            {
                msg.WriteUInt16(Sender.ID);
            }
            msg.WriteBoolean(customTextColor != null);
            if (customTextColor != null)
            {
                msg.WriteColorR8G8B8A8(customTextColor.Value);
            }
            msg.WritePadBits();
            if (Type == ChatMessageType.ServerMessageBoxInGame)
            {
                msg.WriteString(IconStyle);
            }
        }
    }
}
