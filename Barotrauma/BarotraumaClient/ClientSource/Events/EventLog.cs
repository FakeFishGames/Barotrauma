#nullable enable

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma;

partial class EventLog
{
    public bool UnreadEntries { get; private set; }

    public void AddEntry(Identifier eventPrefabId, Identifier entryId, string text)
    {
        TryAddEntryInternal(eventPrefabId, entryId, text);
        GameMain.GameSession?.EnableEventLogNotificationIcon(enabled: true);
        UnreadEntries = true;
    }

    public void CreateEventLogUI(GUIComponent parent, TraitorManager.TraitorResults? traitorResults = null)
    {
        UnreadEntries = false;

        int spacing = GUI.IntScale(5);
        foreach (var ev in events.Values)
        {
            LocalizedString nameString = string.Empty;
            int difficultyIconCount = 0;

            EventPrefab.Prefabs.TryGet(ev.EventIdentifier, out EventPrefab? eventPrefab);
            if (eventPrefab is not null)
            {
                nameString = RichString.Rich(eventPrefab.Name);
                if (eventPrefab is TraitorEventPrefab traitorEventPrefab)
                {
                    difficultyIconCount = traitorEventPrefab.DangerLevel;
                }
            }
            var textContent = new List<LocalizedString>();
            textContent.AddRange(ev.Entries.Select(e => (LocalizedString)e.Text));

            var icon = GUIStyle.GetComponentStyle("TraitorMissionIcon")?.GetDefaultSprite();

            RoundSummary.CreateMissionEntry(
                parent,
                nameString,
                textContent,
                difficultyIconCount,
                icon, GUIStyle.Red,
                out GUIImage missionIcon);

            if (traitorResults != null && 
                traitorResults.Value.TraitorEventIdentifier == ev.EventIdentifier)
            {
                RoundSummary.UpdateMissionStateIcon(traitorResults.Value.ObjectiveSuccessful, missionIcon);
            }
        }
    }
}

