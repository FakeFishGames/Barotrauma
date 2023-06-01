﻿using Barotrauma.Networking;
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

        private readonly HashSet<Client> targetClients = new HashSet<Client>();
        private readonly Dictionary<Client, DateTime> ignoredClients = new Dictionary<Client, DateTime>();

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

        public void IgnoreClient(Client c, float seconds)
        {
            if (!ignoredClients.ContainsKey(c)) { ignoredClients.Add(c, DateTime.Now); }
            ignoredClients[c] = DateTime.Now + TimeSpan.FromSeconds(seconds);
            Reset();
        }

        private bool IsBlockedByAnotherConversation(IEnumerable<Entity> targets, float duration)
        {
            foreach (Entity e in targets)
            {
                if (e is not Character character || !character.IsRemotePlayer) { continue; }
                Client targetClient = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
                if (targetClient != null)
                {
                    if (lastActiveAction.ContainsKey(targetClient) && 
                        lastActiveAction[targetClient].ParentEvent != ParentEvent && 
                        Timing.TotalTime < lastActiveAction[targetClient].lastActiveTime + duration)
                    {
                        return true;
                    }
                }
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
                        ServerWrite(speaker, targetClient, interrupt); 
                    }
                }
            }
            else
            {
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    if (c.InGame && c.Character != null)
                    {
                        if (targetCharacter == null || targetCharacter == c.Character)
                        {
                            targetClients.Add(c);
                            lastActiveAction[c] = this;
                            ServerWrite(speaker, c, interrupt);
                        }
                    }
                }
            }
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
                outmsg.WriteString(Text ?? string.Empty);
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
