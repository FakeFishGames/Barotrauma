using Barotrauma.Networking;
using System.Collections.Generic;

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
        public IEnumerable<Client> TargetClients
        {
            get { return targetClients; }
        }

        private bool IsBlockedByAnotherConversation(IEnumerable<Entity> targets)
        {
            foreach (Entity e in targets)
            {
                if (!(e is Character character) || !character.IsRemotePlayer) { continue; }
                Client targetClient = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
                if (targetClient != null)
                {
                    if (lastActiveAction.ContainsKey(targetClient) && 
                        lastActiveAction[targetClient].ParentEvent != ParentEvent && 
                        Timing.TotalTime < lastActiveAction[targetClient].lastActiveTime + BlockOtherConversationsDuration)
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
            if (!string.IsNullOrEmpty(TargetTag))
            {
                IEnumerable<Entity> entities = ParentEvent.GetTargets(TargetTag);
                foreach (Entity e in entities)
                {
                    if (!(e is Character character) || !character.IsRemotePlayer) { continue; }
                    Client targetClient = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
                    if (targetClient != null) 
                    {
                        targetClients.Add(targetClient);
                        lastActiveAction[targetClient] = this;
                        ServerWrite(speaker, targetClient); 
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
                            ServerWrite(speaker, c);
                        }
                    }
                }
            }
        }

        private void ServerWrite(Character speaker, Client client)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.Write((byte)ServerPacketHeader.EVENTACTION);
            outmsg.Write((byte)EventManager.NetworkEventType.CONVERSATION);
            outmsg.Write(Identifier);
            outmsg.Write(EventSprite);
            outmsg.Write((byte)DialogType);
            outmsg.Write(ContinueConversation);
            if (interrupt)
            {
                outmsg.Write(speaker?.ID ?? Entity.NullEntityID);
                outmsg.Write(string.Empty);
                outmsg.Write(false);
                outmsg.Write((byte)0);
                outmsg.Write((byte)0);
            }
            else
            {
                outmsg.Write(speaker?.ID ?? Entity.NullEntityID);
                outmsg.Write(Text ?? string.Empty);
                outmsg.Write(FadeToBlack);
                outmsg.Write((byte)Options.Count);
                for (int i = 0; i < Options.Count; i++)
                {
                    outmsg.Write(Options[i].Text);
                }

                int[] endings = GetEndingOptions();
                outmsg.Write((byte)endings.Length);
                foreach (var end in endings)
                {
                    outmsg.Write((byte)end);
                }
            }
            GameMain.Server?.ServerPeer?.Send(outmsg, client.Connection, DeliveryMethod.Reliable);
        }
    }
}
