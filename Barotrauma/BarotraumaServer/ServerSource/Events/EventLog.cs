#nullable enable

using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma;

partial class EventLog
{
    public bool TryAddEntry(Identifier eventPrefabId, Identifier entryId, string text, IEnumerable<Client> targetClients)
    {
        if (TryAddEntryInternal(eventPrefabId, entryId, text))
        {
            foreach (var targetClient in targetClients)
            {
                EventManager.ServerWriteEventLog(targetClient, new EventManager.NetEventLogEntry(eventPrefabId, entryId, text));
            }
            return true;
        }
        return false;
    }
}
