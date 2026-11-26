#nullable enable

using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma;

public partial class EventLog
{
    public void SetEventLogState(Identifier eventPrefabId, ScriptedEvent.State state, IEnumerable<Client> targetClients)
    {
        SetEventLogStateInternal(eventPrefabId, state);

        foreach (var targetClient in targetClients)
        {
            EventManager.ServerWriteEventState(targetClient, new EventManager.NetEventLogState(eventPrefabId, state));
        }
    }

    public bool TryAddEntry(Identifier eventPrefabId, Identifier entryId, string text, bool sendMessage, bool isPersistent, IEnumerable<Client> targetClients)
    {
        if (TryAddEntryInternal(eventPrefabId, entryId, text, isPersistent))
        {
            foreach (var targetClient in targetClients)
            {
                EventManager.ServerWriteEventLog(targetClient, new EventManager.NetEventLogEntry(eventPrefabId, entryId, text, sendMessage, isPersistent));
            }
            return true;
        }
        return false;
    }

    public bool RemoveEvent(Identifier eventPrefabId, IEnumerable<Client> targetClients)
    {
        if (RemoveEventInternal(eventPrefabId))
        {
            foreach (var targetClient in targetClients)
            {
                EventManager.ServerWriteEventLogRemoval(targetClient, new EventManager.NetEventLogRemoval(eventPrefabId));
            }
            return true;
        }
        return false;
    }
}
