#nullable enable

using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Used to store logs of scripted events (a sort of "quest log")
    /// </summary>
    public partial class EventLog
    {
        public class Event
        {
            public ScriptedEvent.State State;
            public readonly Identifier EventIdentifier;
            public readonly List<Entry> Entries = new List<Entry>();

            public Event(Identifier eventPrefabId)
            {
                EventIdentifier = eventPrefabId;
                State = ScriptedEvent.State.Incomplete;
            }
        }

        public class Entry
        {
            public readonly Identifier Identifier;
            public string Text;
            /// <summary>
            /// Persistent entries are saved to the campaign save file and remain visible in the log across game sessions,
            /// instead of getting cleared when a new round starts.
            /// </summary>
            public bool IsPersistent;

            public Entry(Identifier identifier, string text, bool isPersistent = false)
            {
                Identifier = identifier;
                Text = text;
                IsPersistent = isPersistent;
            }
        }

        private readonly Dictionary<Identifier, Event> events = new Dictionary<Identifier, Event>();

        private void SetEventLogStateInternal(Identifier eventPrefabId, ScriptedEvent.State state)
        {
            if (!events.TryGetValue(eventPrefabId, out Event? ev))
            {
                ev = new Event(eventPrefabId);
                events.Add(eventPrefabId, ev);
            }

            ev.State = state;
        }

        private bool TryAddEntryInternal(Identifier eventPrefabId, Identifier entryId, string text, bool isPersistent = false)
        {
            if (!events.TryGetValue(eventPrefabId, out Event? ev))
            {
                ev = new Event(eventPrefabId);
                events.Add(eventPrefabId, ev);
            }
            Entry? entry = ev.Entries.FirstOrDefault(e => e.Identifier == entryId);
            if (entry == null)
            {
                ev.Entries.Add(new Entry(entryId, text, isPersistent));
                return true;
            }
            bool changed = false;
            if (entry.Text != text)
            {
                entry.Text = text;
                changed = true;
            }
            if (isPersistent && !entry.IsPersistent)
            {
                entry.IsPersistent = true;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Removes all the log entries associated with the given event.
        /// </summary>
        private bool RemoveEventInternal(Identifier eventPrefabId)
        {
            return events.Remove(eventPrefabId);
        }

        /// <summary>
        /// Clears the log, keeping only the entries marked as persistent (see <see cref="Entry.IsPersistent"/>).
        /// Called when a new round starts: non-persistent entries only apply to the current round, while persistent
        /// ones (e.g. main quest progress) are meant to remain visible for the rest of the campaign.
        /// </summary>
        /// <param name="includePersistent">Clear the persistent entries too (e.g. when the end of the campaign is reached).</param>
        public void Clear(bool includePersistent = false)
        {
            List<Identifier> eventsToRemove = new List<Identifier>();
            foreach (var (eventId, ev) in events)
            {
                ev.Entries.RemoveAll(e => includePersistent || !e.IsPersistent);
                if (ev.Entries.Count == 0)
                {
                    eventsToRemove.Add(eventId);
                }
            }
            foreach (var eventId in eventsToRemove)
            {
                events.Remove(eventId);
            }
        }

        public XElement Save()
        {
            XElement eventLogElement = new XElement("eventlog");
            foreach (var ev in events.Values)
            {
                var persistentEntries = ev.Entries.Where(e => e.IsPersistent).ToList();
                if (persistentEntries.Count == 0) { continue; }

                XElement eventElement = new XElement("Event",
                    new XAttribute("identifier", ev.EventIdentifier),
                    new XAttribute("state", ev.State));
                foreach (var entry in persistentEntries)
                {
                    eventElement.Add(new XElement("Entry",
                        new XAttribute("identifier", entry.Identifier),
                        new XAttribute("text", entry.Text)));
                }
                eventLogElement.Add(eventElement);
            }
            return eventLogElement;
        }

        public void Load(XElement element)
        {
            foreach (var eventElement in element.Elements())
            {
                Identifier eventId = eventElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                if (eventId.IsEmpty) { continue; }

                SetEventLogStateInternal(eventId, eventElement.GetAttributeEnum("state", ScriptedEvent.State.Incomplete));
                foreach (var entryElement in eventElement.Elements())
                {
                    Identifier entryId = entryElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                    string text = entryElement.GetAttributeString("text", string.Empty);
                    TryAddEntryInternal(eventId, entryId, text, isPersistent: true);
                }
            }
        }
    }
}
