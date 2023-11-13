using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CrewManager
    {
        partial void CreateRandomConversation()
        {
            List<Character> availableSpeakers = Character.CharacterList.FindAll(c =>
                c.AIController is HumanAIController &&
                !c.IsDead &&
                c.SpeechImpediment <= 100.0f);

            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                if (client.Character != null) availableSpeakers.Remove(client.Character);
            }

            pendingConversationLines.AddRange(NPCConversation.CreateRandom(availableSpeakers));
        }

        /// <summary>
        /// Saves bots in multiplayer
        /// </summary>
        public XElement SaveMultiplayer(XElement parentElement)
        {
            var element = new XElement("bots", new XAttribute("hasbots", HasBots));
            foreach (CharacterInfo info in characterInfos)
            {
                if (Level.Loaded != null)
                {
                    if (!info.IsNewHire && (info.Character == null || info.Character.IsDead)) { continue; }
                }

                XElement characterElement = info.Save(element);
                if (info.InventoryData != null) { characterElement.Add(info.InventoryData); }
                if (info.HealthData != null) { characterElement.Add(info.HealthData); }
                if (info.OrderData != null) { characterElement.Add(info.OrderData); }
            }
            parentElement?.Add(element);
            return element;
        }

        public void ServerWriteActiveOrders(IWriteMessage msg)
        {
            ushort count = (ushort)ActiveOrders.Count(o => o.Order != null && !o.FadeOutTime.HasValue);
            msg.WriteUInt16(count);
            if (count > 0)
            {
                foreach (var activeOrder in ActiveOrders)
                {
                    if (!(activeOrder?.Order is Order order) || activeOrder.FadeOutTime.HasValue) { continue; }
                    OrderChatMessage.WriteOrder(msg, order, null, isNewOrder: true);
                    bool hasOrderGiver = order.OrderGiver != null;
                    msg.WriteBoolean(hasOrderGiver);
                    if (hasOrderGiver)
                    {
                        msg.WriteUInt16(order.OrderGiver.ID);
                    }
                }
            }
        }
    }
}
