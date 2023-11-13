using Barotrauma.Networking;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class EventManager
    {
        public static void ServerWriteEventLog(Client client, NetEventLogEntry entry)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.WriteByte((byte)ServerPacketHeader.EVENTACTION);
            outmsg.WriteByte((byte)NetworkEventType.EVENTLOG);
            outmsg.WriteNetSerializableStruct(entry);
            GameMain.Server?.ServerPeer?.Send(outmsg, client.Connection, DeliveryMethod.Reliable);
        }

        public static void ServerWriteObjective(Client client, NetEventObjective entry)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.WriteByte((byte)ServerPacketHeader.EVENTACTION);
            outmsg.WriteByte((byte)NetworkEventType.EVENTOBJECTIVE);
            outmsg.WriteNetSerializableStruct(entry);
            GameMain.Server?.ServerPeer?.Send(outmsg, client.Connection, DeliveryMethod.Reliable);
        }

        public void ServerRead(IReadMessage inc, Client sender)
        {
            UInt16 actionId = inc.ReadUInt16();
            byte selectedOption = inc.ReadByte();

            foreach (Event ev in activeEvents)
            {
                if (ev is not ScriptedEvent scriptedEvent) { continue; }
                
                var actions = scriptedEvent.GetAllActions();
                foreach (EventAction action in actions.Select(a => a.action))
                {
                    if (action is not ConversationAction convAction || convAction.Identifier != actionId) { continue; }
                    if (!convAction.TargetClients.Contains(sender))
                    {
#if DEBUG || UNSTABLE
                        DebugConsole.ThrowError($"Client \"{sender.Name}\" tried to respond to a ConversationAction that was not targeted to them ({convAction.Text}).");
#endif
                        continue;
                    }

                    if (convAction.SelectedOption > -1)
                    {
                        //someone else already chose an option for this conversation: interrupt for this client
                        convAction.ServerWrite(convAction.Speaker, sender, interrupt: true);
                    }
                    else
                    {
                        if (selectedOption == byte.MaxValue)
                        {
                            convAction.IgnoreClient(sender, 3f);
                        }
                        else
                        {
                            convAction.SelectedOption = selectedOption;
                            if (convAction.Options.Any() && !convAction.GetEndingOptions().Contains(selectedOption))
                            {
                                foreach (Client c in convAction.TargetClients)
                                {
                                    if (c == sender) { continue; }                                    
                                    convAction.ServerWriteSelectedOption(c);                                    
                                }
                            }
                        }
                    }
                    return;
                }                
            }
        }
    }
}
