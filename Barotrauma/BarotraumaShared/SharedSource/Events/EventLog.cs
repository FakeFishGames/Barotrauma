#nullable enable

using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Used to store logs of scripted events (a sort of "quest log")
    /// </summary>
    partial class EventLog
    {
        public class Event
        {
            public readonly Identifier EventIdentifier;
            public readonly List<Entry> Entries = new List<Entry>();

            public Event(Identifier eventPrefabId)
            {
                EventIdentifier = eventPrefabId;
            }
        }

        public class Entry
        {
            public readonly Identifier Identifier;
            public string Text;

            public Entry(Identifier identifier, string text)
            {
                Identifier = identifier;
                Text = text;
            }
        }

        private readonly Dictionary<Identifier, Event> events = new Dictionary<Identifier, Event>();

        private bool TryAddEntryInternal(Identifier eventPrefabId, Identifier entryId, string text)
        {
            if (!events.TryGetValue(eventPrefabId, out Event? ev))
            {
                ev = new Event(eventPrefabId);
                events.Add(eventPrefabId, ev);
            }
            Entry? entry = ev.Entries.FirstOrDefault(e => e.Identifier == entryId);
            if (entry == null)
            {
                ev.Entries.Add(new Entry(entryId, text));
                return true;
            }
            else if (entry.Text != text)
            {                
                entry.Text = text;
                return true;
            }
            return false;
        }

        public void Clear()
        {
            events.Clear();
        }
    }
}
