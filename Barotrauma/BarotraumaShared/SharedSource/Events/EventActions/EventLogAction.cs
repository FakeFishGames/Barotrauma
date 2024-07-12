#nullable enable

using System;
using System.Xml.Linq;

namespace Barotrauma
{

    /// <summary>
    /// Adds an entry to the "event log" displayed in the mission tab of the tab menu.
    /// </summary>
    partial class EventLogAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the entry. If there's already an entry with the same id, it gets overwritten.")]
        public Identifier Id { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Text to add to the event log. Can be the text as-is, or a tag referring to a line in a text file.")]
        public string Text { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character(s) who should see the entry. If empty, the entry is shown to everyone.")]
        public Identifier TargetTag { get; set; }

        public bool ShowInServerLog { get; set; }

        private readonly XElement? textElement;

        public EventLogAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
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
            Text ??= string.Empty;
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
            ShowInServerLog = element.GetAttributeBool(nameof(ShowInServerLog), ParentEvent is TraitorEvent);
        }

        private bool isFinished;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset()
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
            return ParentEvent.ReplaceVariablesInEventText(text);
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }
            AddEntryProjSpecific(GameMain.GameSession?.EventManager?.EventLog, GetDisplayText().Value);
            isFinished = true;
        }

        partial void AddEntryProjSpecific(EventLog? eventLog, string displayText);

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(EventLogAction)} -> (Id: {Id})";
        }
    }
}