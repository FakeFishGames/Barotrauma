#nullable enable

namespace Barotrauma;

partial class EventLogAction : EventAction
{
    partial void AddEntryProjSpecific(EventLog? eventLog, string displayText)
    {
        eventLog?.AddEntry(ParentEvent.Prefab.Identifier, Id, displayText);
    }
}