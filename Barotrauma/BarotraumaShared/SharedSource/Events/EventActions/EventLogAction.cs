#nullable enable

using System;
using System.Xml.Linq;

namespace Barotrauma
{

    /// <summary>
    /// Adds an entry to the "event log" displayed in the mission tab of the tab menu. Can also be used to remove
    /// all the entries associated with a given EventIdentifier (see the Remove property).
    /// </summary>
    partial class EventLogAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Identifier used to group this log entry with others in the event log, and to determine which icon/name is shown for the group. If empty, defaults to the identifier of the event this action is placed in. Also determines which entries get removed when Remove is set to true.")]
        public Identifier EventIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the entry. If there's already an entry with the same id, it gets overwritten. "+
            "Can contain the same variables as the text (e.g. [location2]), which can be used to keep the entries of different instances of the same mission separate.")]
        public Identifier Id { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Text to add to the event log. Can be the text as-is, or a tag referring to a line in a text file.")]
        public string Text { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character(s) who should see the entry. If empty, the entry is shown to everyone.")]
        public Identifier TargetTag { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should a message popup appear with the text of this log entry?")]
        public bool SendMessage { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If enabled, this log entry is saved persistently: it's stored in the campaign save file and remains visible in the event log for the rest of the campaign, instead of getting cleared when the round ends.")]
        public bool Persistent { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If set to true, all the log entries associated with EventIdentifier are removed instead of adding a new entry. Useful for getting rid of outdated persistent entries, e.g. when a quest chain progresses past the point where they're relevant.")]
        public bool Remove { get; set; }

        public bool ShowInServerLog { get; set; }

        private readonly XElement? textElement;

        public EventLogAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            Text ??= string.Empty;
            if (!Remove)
            {
                if (Id == Identifier.Empty)
                {
                    DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\". {nameof(EventLogAction)} with no id.",
                        contentPackage: element.ContentPackage);
                }
                //append the target tag so logs targeted to different players don't interfere with each other even if they use the same Id
                Id = (Id.ToString() + TargetTag).ToIdentifier();

                foreach (var elem in element.Elements())
                {
                    if (elem.Name.LocalName.Equals("text", StringComparison.OrdinalIgnoreCase))
                    {
                        textElement = elem;
                        break;
                    }
                }
                if (textElement == null)
                {
                    if (Text.IsNullOrEmpty())
                    {
                        DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\". {nameof(EventLogAction)} with no text set ({element}).",
                            contentPackage: element.ContentPackage);
                    }
                    else
                    {
                        Text = TextManager.Get(Text).Fallback(Text).Value;
                    }
                }
            }
            ShowInServerLog = element.GetAttributeBool(nameof(ShowInServerLog), ParentEvent is TraitorEvent);
        }

        private bool isFinished;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset(bool shouldEndConversations = true)
        {
            isFinished = false;
        }


        public LocalizedString GetDisplayText()
        {
            LocalizedString text = Text;
            if (textElement != null)
            {
                LocalizedString tempDescription = string.Empty;
                TextManager.ConstructDescription(ref tempDescription, textElement, ParentEvent.GetTextForReplacementElement);
                text = tempDescription.Value;
            }
            return ReplaceVariables(text);
        }

        /// <summary>
        /// <see cref="Id"/> with the variables (e.g. [location2]) replaced.
        /// </summary>
        private Identifier ResolvedId => ReplaceVariables(Id.Value).Value.ToIdentifier();

        private LocalizedString ReplaceVariables(LocalizedString text)
        {
            //no parent event when executed by a mission (e.g. in the mission's ActionsOnComplete), replace the mission's variables (e.g. [location2]) instead
            return ParentEvent?.ReplaceVariablesInEventText(text) ?? ParentMission?.ReplaceVariablesInMissionMessage(text, Submarine.MainSub) ?? text;
        }

        private Identifier ResolvedEventIdentifier => EventIdentifier.IsEmpty ? ParentEvent.Prefab.Identifier : EventIdentifier;

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }
            if (Remove)
            {
                RemoveEntriesProjSpecific(GameMain.GameSession?.EventManager?.EventLog, ResolvedEventIdentifier);
            }
            else
            {
                AddEntryProjSpecific(GameMain.GameSession?.EventManager?.EventLog, ResolvedEventIdentifier, GetDisplayText().Value);
            }
            isFinished = true;
        }

        partial void AddEntryProjSpecific(EventLog? eventLog, Identifier eventIdentifier, string displayText);
        partial void RemoveEntriesProjSpecific(EventLog? eventLog, Identifier eventIdentifier);

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(EventLogAction)} -> (Id: {Id})";
        }
    }
}