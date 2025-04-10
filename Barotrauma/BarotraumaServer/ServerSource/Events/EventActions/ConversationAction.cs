using Barotrauma.Extensions;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class ConversationAction : EventAction
    {
        public int SelectedOption
        {
            get { return selectedOption; }
            set { selectedOption = value; }
        }


        private static readonly Dictionary<Client, ConversationAction> lastActiveAction = new Dictionary<Client, ConversationAction>();

        /// <summary>
        /// Clients who this Conversation prompt is being currently shown to
        /// </summary>
        private readonly HashSet<Client> targetClients = new HashSet<Client>();
        private readonly Dictionary<Client, DateTime> ignoredClients = new Dictionary<Client, DateTime>();

        /// <summary>
        /// Clients who this Conversation prompt is being currently shown to
        /// </summary>
        public IEnumerable<Client> TargetClients
        {
            get
            {
                UpdateIgnoredClients();
                return targetClients.Where(c => !ignoredClients.ContainsKey(c));
            }
        }

        private void UpdateIgnoredClients()
        {
            if (ignoredClients.Any())
            {
                HashSet<Client> clientsToRemove = null;
                foreach (var k in ignoredClients.Keys)
                {
                    if (ignoredClients[k] < DateTime.Now)
                    {
                        clientsToRemove ??= new HashSet<Client>();
                        clientsToRemove.Add(k);
                    }
                }
                if (clientsToRemove is not null)
                {
                    foreach (var k in clientsToRemove)
                    {
                        ignoredClients.Remove(k);
                    }
                }
            }
        }

        public bool CanClientStartConversation(Client client)
        {
            if (!TargetTag.IsEmpty)
            {
                var targets = ParentEvent.GetTargets(TargetTag).Where(e => IsValidTarget(e));
                return targets.Contains(client.Character);
            }
            return true;
        }

        public void IgnoreClient(Client c, float seconds)
        {
            if (!ignoredClients.ContainsKey(c)) { ignoredClients.Add(c, DateTime.Now); }
            ignoredClients[c] = DateTime.Now + TimeSpan.FromSeconds(seconds);
            //this action is not active for the client if they decided to ignore it
            if (lastActiveAction.TryGetValue(c, out ConversationAction lastActive) && lastActive == this)
            {
                lastActiveAction.Remove(c);
            }
            Reset();
        }

        private bool IsBlockedByAnotherConversation(IEnumerable<Entity> targets, float duration)
        {
            if (targets == null || targets.None())
            {
                //if the action doesn't target anyone in specific, it's shown to every client
                foreach (var client in GameMain.Server.ConnectedClients)
                {
                    if (IsBlockedByAnotherConversation(client, duration)) { return true; }
                }
            }
            else
            {
                foreach (Entity e in targets)
                {
                    if (e is not Character character || !character.IsRemotePlayer) { continue; }
                    Client targetClient = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
                    if (targetClient != null && IsBlockedByAnotherConversation(targetClient, duration)) { return true; }                    
                }
            }
            return false;
        }

        private bool IsBlockedByAnotherConversation(Client targetClient, float duration)
        {
            if (lastActiveAction.ContainsKey(targetClient) &&
                !lastActiveAction[targetClient].ParentEvent.IsFinished &&
                lastActiveAction[targetClient].ParentEvent != ParentEvent &&
                Timing.TotalTime < lastActiveAction[targetClient].lastActiveTime + duration)
            {
                return true;
            }
            return false;
        }

        partial void ShowDialog(Character speaker, Character targetCharacter)
        {
            targetClients.Clear();
            if (!TargetTag.IsEmpty)
            {
                IEnumerable<Entity> entities = ParentEvent.GetTargets(TargetTag);
                foreach (Entity e in entities)
                {
                    if (e is not Character character || !character.IsRemotePlayer) { continue; }
                    Client targetClient = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
                    if (targetClient != null) 
                    {
                        targetClients.Add(targetClient);
                        lastActiveAction[targetClient] = this;
                        lastActiveTime = Timing.TotalTime;
                        ServerWrite(speaker, targetClient, interrupt); 
                    }
                }
            }
            else
            {
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    if (CanClientReceive(c))
                    {
                        if (targetCharacter == null || targetCharacter == c.Character)
                        {
                            targetClients.Add(c);
                            lastActiveAction[c] = this;
                            lastActiveTime = Timing.TotalTime;
                            DebugConsole.Log($"Sending conversationaction {ParentEvent.Prefab.Identifier} to client...");
                            ServerWrite(speaker, c, interrupt);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Is it possible for the client to receive ConversationActions 
        /// (just checking if they're in game, controlling a character and not marked as ignoring the action, 
        /// but not accounting for whether this action targets them or not).
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        private bool CanClientReceive(Client c)
        {
            return c != null && c.InGame && c.Character != null && !ignoredClients.ContainsKey(c);
        }

        public void ServerWrite(Character speaker, Client client, bool interrupt)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.WriteByte((byte)ServerPacketHeader.EVENTACTION);
            outmsg.WriteByte((byte)EventManager.NetworkEventType.CONVERSATION);
            outmsg.WriteUInt16(Identifier);
            outmsg.WriteString(EventSprite);
            outmsg.WriteByte((byte)DialogType);
            outmsg.WriteBoolean(ContinueConversation);
            if (interrupt)
            {
                outmsg.WriteUInt16(speaker?.ID ?? Entity.NullEntityID);
                outmsg.WriteString(string.Empty);
                outmsg.WriteBoolean(false);
                outmsg.WriteByte((byte)0);
                outmsg.WriteByte((byte)0);
            }
            else
            {
                outmsg.WriteUInt16(speaker?.ID ?? Entity.NullEntityID);
                outmsg.WriteString(GetDisplayText()?.Value ?? string.Empty);
                outmsg.WriteBoolean(FadeToBlack);
                outmsg.WriteByte((byte)Options.Count);
                for (int i = 0; i < Options.Count; i++)
                {
                    outmsg.WriteString(Options[i].Text);
                }

                int[] endings = GetEndingOptions();
                outmsg.WriteByte((byte)endings.Length);
                foreach (var end in endings)
                {
                    outmsg.WriteByte((byte)end);
                }
            }
            GameMain.Server?.ServerPeer?.Send(outmsg, client.Connection, DeliveryMethod.Reliable);
        }

        public void ServerWriteSelectedOption(Client client)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.WriteByte((byte)ServerPacketHeader.EVENTACTION);
            outmsg.WriteByte((byte)EventManager.NetworkEventType.CONVERSATION_SELECTED_OPTION);
            outmsg.WriteUInt16(Identifier);
            outmsg.WriteByte((byte)(selectedOption + 1));
            GameMain.Server?.ServerPeer?.Send(outmsg, client.Connection, DeliveryMethod.Reliable);
        }
    }
}
