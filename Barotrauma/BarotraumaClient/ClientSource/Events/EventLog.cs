#nullable enable

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma;

partial class EventLog
{
    public bool UnreadEntries { get; private set; }

    public void SetEventLogState(Identifier eventPrefabId, ScriptedEvent.State state)
    {
        SetEventLogStateInternal(eventPrefabId, state);
    }

    /// <summary>
    /// Removes all the log entries associated with the given event.
    /// </summary>
    public void RemoveEvent(Identifier eventPrefabId)
    {
        RemoveEventInternal(eventPrefabId);
    }

    public void AddEntry(Identifier eventPrefabId, Identifier entryId, string text, bool sendMessage, bool isPersistent = false)
    {
        TryAddEntryInternal(eventPrefabId, entryId, text, isPersistent);
        GameMain.GameSession?.EnableEventLogNotificationIcon(enabled: true);
        UnreadEntries = true;

        if (sendMessage && EventPrefab.Prefabs.TryGet(eventPrefabId, out EventPrefab? eventPrefab))
        {
            new GUIMessageBox(eventPrefab.Name, text,
                Array.Empty<LocalizedString>(), type: GUIMessageBox.Type.InGame, icon: eventPrefab.Icon, minSize: new Point(512, 128))
            {
                IconColor = eventPrefab.IconColor
            };
        }
    }

    /// <param name="showPersistentLogs">If false, persistent logs aren't shown. 
    /// If there's an event that mixes persistent and non-persistent entries, setting this to false will only hide the persistent ones.</param>
    public void CreateEventLogUI(GUIComponent parent, TraitorManager.TraitorResults? traitorResults = null, bool showPersistentLogs = true)
    {
        UnreadEntries = false;

        int spacing = GUI.IntScale(5);
        foreach (var ev in events.Values)
        {
            if (!showPersistentLogs && ev.Entries.All(entry => entry.IsPersistent))
            {
                continue;
            }
            
            LocalizedString nameString = string.Empty;
            int difficultyIconCount = 0;
            Color iconColor = GUIStyle.Red;
            Sprite? icon = null;

            EventPrefab.Prefabs.TryGet(ev.EventIdentifier, out EventPrefab? eventPrefab);
            if (eventPrefab is not null)
            {
                nameString = RichString.Rich(eventPrefab.Name);
                if (eventPrefab is TraitorEventPrefab traitorEventPrefab)
                {
                    difficultyIconCount = traitorEventPrefab.DangerLevel;
                }

                if (eventPrefab.DifficultyIconCount >= 0)
                {
                    difficultyIconCount = eventPrefab.DifficultyIconCount;
                }

                icon = eventPrefab.Icon;
                iconColor = eventPrefab.IconColor;
            }

            var textContent = new List<LocalizedString>();
            textContent.AddRange(
                ev.Entries
                    .Where(e => showPersistentLogs || !e.IsPersistent)
                    .Select(e => (LocalizedString)e.Text));

            icon = icon ?? GUIStyle.GetComponentStyle("TraitorMissionIcon")?.GetDefaultSprite();

            RoundSummary.CreateMissionEntry(
                parent,
                nameString,
                textContent,
                difficultyIconCount,
                icon, iconColor,
                difficultyTooltipText: null,
                out GUIImage missionIcon);

            if (traitorResults != null && 
                traitorResults.Value.TraitorEventIdentifier == ev.EventIdentifier)
            {
                RoundSummary.UpdateMissionStateIcon(traitorResults.Value.ObjectiveSuccessful, missionIcon);
            }
            else if (ev.State != ScriptedEvent.State.Incomplete)
            {
                RoundSummary.UpdateMissionStateIcon(ev.State == ScriptedEvent.State.Completed, missionIcon);
            }
        }
    }
}

