#nullable enable

namespace Barotrauma;

partial class EventLogAction : EventAction
{
    partial void AddEntryProjSpecific(EventLog? eventLog, Identifier eventIdentifier, string displayText)
    {
        eventLog?.AddEntry(eventIdentifier, ResolvedId, displayText, SendMessage, Persistent);
    }

    partial void RemoveEntriesProjSpecific(EventLog? eventLog, Identifier eventIdentifier)
    {
        eventLog?.RemoveEvent(eventIdentifier);
    }
}