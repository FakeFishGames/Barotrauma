#nullable enable

using Barotrauma.Extensions;
using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma;

partial class EventLogAction : EventAction
{
    partial void AddEntryProjSpecific(EventLog? eventLog, string displayText)
    {
        if (eventLog == null) { return; }
        if (!TargetTag.IsEmpty)
        {
            List<Client> targetClients = new List<Client>();
            foreach (var target in ParentEvent.GetTargets(TargetTag))
            {
                if (target is Character character)
                {
                    var ownerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
                    if (ownerClient != null)
                    {
                        targetClients.Add(ownerClient);
                    }
                }
                else
                {
                    DebugConsole.AddWarning($"{target} is not a valid target for an EventLogAction. The target should be a character.",
                        ParentEvent.Prefab.ContentPackage);
                }
            }
            if (eventLog!.TryAddEntry(ParentEvent.Prefab.Identifier, Id, displayText, targetClients) && ShowInServerLog)
            {
                Log(targetClients);
            }
        }
        else
        {
            if (eventLog.TryAddEntry(ParentEvent.Prefab.Identifier, Id, displayText, GameMain.Server.ConnectedClients) && ShowInServerLog)
            {
                Log(targetClients: null);
            }
        }

        void Log(List<Client>? targetClients)
        {
            string clientStr = targetClients == null || targetClients.None() ?
                string.Empty :
                $" ({string.Join(", ", targetClients.Select(c => NetworkMember.ClientLogName(c)))})";
            GameServer.Log($"Event \"{ParentEvent.Prefab.Name}\"{clientStr}: " + displayText,
                ParentEvent is TraitorEvent ? ServerLog.MessageType.Traitors : ServerLog.MessageType.Chat);
        }
    }
}