using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class EventManager
    {
        public void ServerRead(IReadMessage inc, Client sender)
        {
            UInt16 actionId = inc.ReadUInt16();
            byte selectedOption = inc.ReadByte();

            foreach (Event ev in activeEvents)
            {
                if (!(ev is ScriptedEvent scriptedEvent)) { continue; }
                
                var actions = FindActions(scriptedEvent);
                foreach (EventAction action in actions.Select(a => a.Item2))
                {
                    if (!(action is ConversationAction convAction) || convAction.Identifier != actionId) { continue; }
                    if (!convAction.TargetClients.Contains(sender))
                    {
#if DEBUG || UNSTABLE
                        DebugConsole.ThrowError($"Client \"{sender.Name}\" tried to respond to a ConversationAction that was not targeted to them.");
#endif
                        continue;
                    }

                    if (convAction.SelectedOption > -1)
                    {
                        //someone else already chose an option for this conversation: interrupt for this client
                        convAction.ServerWrite(convAction.speaker, sender, interrupt: true);
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
                        }
                    }
                    return;
                }                
            }
        }
    }
}
