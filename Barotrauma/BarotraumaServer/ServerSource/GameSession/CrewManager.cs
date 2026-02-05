using System;
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
            foreach (CharacterInfo info in GetCharacterInfos(includeReserveBench: true))
            {
                if (Level.Loaded != null)
                {
                    //new hires and reserve benched CharacterInfos should be saved even though the Character doesn't exist
                    if (!info.IsNewHire && !info.IsOnReserveBench) 
                    {
                        //character being null either means the character has been removed, or that it hasn't spawn yet
                        if (info.Character == null && !info.PendingSpawnToActiveService) { continue; }
                        if (info.Character is { IsDead: true }) { continue; }
                    }
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
        
        public void ReadToggleReserveBenchMessage(IReadMessage inc, Client sender)
        {
            UInt16 botId = inc.ReadUInt16();
            bool pendingHire = inc.ReadBoolean();
            
            if (GameMain.GameSession?.GameMode is not MultiPlayerCampaign mpCampaign) { return; }
            if (!CampaignMode.AllowedToManageCampaign(sender, ClientPermissions.ManageHires))
            {
                DebugConsole.NewMessage($"Client {sender.Name} is not allowed to modify the reserve bench status of bots (requires ManageHires)");
                return;
            }
            
            if (pendingHire && mpCampaign.Map.CurrentLocation?.HireManager.PendingHires.FirstOrDefault(ci => ci.ID == botId) is CharacterInfo pendingCharacterInfo)
            {
                ToggleReserveBenchStatus(pendingCharacterInfo, sender, pendingHire: true);
            }
            else if (GameMain.GameSession.CrewManager?.GetCharacterInfos(includeReserveBench: true)?.FirstOrDefault(i => i.ID == botId) is CharacterInfo characterInfo)
            {
                ToggleReserveBenchStatus(characterInfo, sender);
            }
        }

        /// <summary>
        /// Used to correctly handle (and document) transitions between the different possible statuses (BotStatus) bots might have
        /// relating to the reserve bench, assigning them the correct new status and into the right CrewManager lists.
        /// This will only take care of things relevant to the CrewManager (like maximum crew size), and will assume requirements
        /// to hiring (money, permissions) have already been handled.
        /// </summary>
        /// <param name="characterInfo">CharacterInfo of the bot</param>
        /// <param name="client">Which client requested changing the reserve bench status?</param>
        /// <param name="pendingHire">Is the bot a pending hire?</param>
        /// <param name="confirmPendingHire">Has the hire been confirmed now? This will store the bot in the CrewManager.</param>
        /// <param name="sendUpdate">By default, the method will trigger sending updated crew data to the clients, but this may not always be useful – eg. if this method is called as part of a longer procedure that will send the update in the end anyway.</param>
        public void ToggleReserveBenchStatus(CharacterInfo characterInfo, Client client, bool pendingHire = false, bool confirmPendingHire = false, bool sendUpdate = true)
        {
            if (GameMain.GameSession?.GameMode is not MultiPlayerCampaign mpCampaign) { return; }
            
            if (confirmPendingHire && !pendingHire)
            {
                DebugConsole.ThrowError($"ToggleReserveBenchStatus: cannot confirm a hire that is not pending (bot {characterInfo.DisplayName})");
            }
            
            BotStatus currentStatus = characterInfo.BotStatus;
            if (pendingHire && !confirmPendingHire)
            {
                if (!(mpCampaign.Map.CurrentLocation?.HireManager.PendingHires.Contains(characterInfo) ?? false))
                {
                    DebugConsole.ThrowError($"ToggleReserveBenchStatus: bot {characterInfo.DisplayName} is supposed to be in the pending hires list, but can't be found there");
                }
                
                if (currentStatus == BotStatus.PendingHireToActiveService)
                {
                    characterInfo.BotStatus = BotStatus.PendingHireToReserveBench;
                    GameServer.Log($"Client \"{client.Name}\" moved the pending hire \"{characterInfo.DisplayName}\" to the reserve bench.", ServerLog.MessageType.ServerMessage);
                }
                else if (currentStatus == BotStatus.PendingHireToReserveBench)
                {
                    if (GetCharacterInfos().Count() >= MaxCrewSize)
                    {
                        DebugConsole.NewMessage($"ToggleReserveBenchStatus: Tried moving pending hire {characterInfo.DisplayName} to active service, but MaxCrewSize has already been reached");
                        return;
                    }
                    characterInfo.BotStatus = BotStatus.PendingHireToActiveService;
                    GameServer.Log($"Client \"{client.Name}\" moved the pending hire \"{characterInfo.DisplayName}\" from the reserve bench to active service.", ServerLog.MessageType.ServerMessage);
                }
            }
            else if (GetCharacterInfos(includeReserveBench: true).Contains(characterInfo) || confirmPendingHire)
            {
                if (currentStatus == BotStatus.ActiveService || (confirmPendingHire && currentStatus == BotStatus.PendingHireToReserveBench))
                {
                    if (reserveBench.Contains(characterInfo))
                    {
                        DebugConsole.ThrowError($"ToggleReserveBenchStatus: Tried to add the same CharacterInfo ({characterInfo.DisplayName}) to reserve bench twice");
                    }
                    RemoveCharacterInfo(characterInfo);
                    characterInfo.BotStatus = BotStatus.ReserveBench;
                    GameServer.Log($"Client \"{client.Name}\" moved the bot \"{characterInfo.DisplayName}\" from active service to the reserve bench.", ServerLog.MessageType.ServerMessage);
                    reserveBench.Add(characterInfo);
                }
                else if (currentStatus == BotStatus.ReserveBench || (confirmPendingHire && currentStatus == BotStatus.PendingHireToActiveService))
                {
                    if (GetCharacterInfos().Count() >= MaxCrewSize)
                    {
                        DebugConsole.NewMessage($"ToggleReserveBenchStatus: Tried moving {characterInfo.DisplayName} to active service, but MaxCrewSize has already been reached");
                        return;
                    }
                    RemoveCharacterInfo(characterInfo);
                    characterInfo.BotStatus = BotStatus.ActiveService;
                    GameServer.Log($"Client \"{client.Name}\" moved the bot \"{characterInfo.DisplayName}\" from the reserve bench to active service.", ServerLog.MessageType.ServerMessage);
                    AddCharacterInfo(characterInfo);
                }
            }
            else
            {
                DebugConsole.ThrowError($"ToggleReserveBenchStatus: bot {characterInfo.DisplayName} not found from CrewManager");
            }
            
            if (sendUpdate)
            {
                mpCampaign.SendCrewState();
            }
        }
    }
}
