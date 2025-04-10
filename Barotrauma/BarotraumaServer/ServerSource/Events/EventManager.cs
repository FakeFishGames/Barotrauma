using Barotrauma.Extensions;
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
            const float IgnoreTime = 3f;

            UInt16 actionId = inc.ReadUInt16();
            byte selectedOption = inc.ReadByte();
            bool isIgnore = selectedOption == byte.MaxValue;

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
                        if (!isIgnore)
                        {
                            DebugConsole.ThrowError($"Client \"{sender.Name}\" tried to respond to a ConversationAction that was not targeted to them ({convAction.Text}).");
                        }
#endif
                        convAction.IgnoreClient(sender, IgnoreTime);
                        continue;
                    }

                    if (convAction.SelectedOption > -1)
                    {
                        //someone else already chose an option for this conversation: interrupt for this client
                        DebugConsole.Log($"Client replied to {ev.Prefab.Identifier}, but option already selected for conversation, interrupt for the client");
                        convAction.ServerWrite(convAction.Speaker, sender, interrupt: true);
                    }
                    else
                    {
                        if (isIgnore)
                        {
                            DebugConsole.NewMessage($"Client ignored ConversationAction (event {ev.Prefab.Identifier}).");
                            convAction.IgnoreClient(sender, IgnoreTime);
                            //no more target clients (the only/last target ignored the conversation action)
                            // -> reset the action so it can appear when some client becomes available
                            if (convAction.TargetClients.None())
                            {
                                DebugConsole.NewMessage($"No target clients for event {ev.Prefab.Identifier}, retrying in " + (IgnoreTime + 1.0f));
                                convAction.RetriggerAfter(IgnoreTime + 1.0f);
                            }
                        }
                        else
                        {
                            DebugConsole.NewMessage($"Client selected option {selectedOption} for ConversationAction in event {ev.Prefab.Identifier}.");
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
