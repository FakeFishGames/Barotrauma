#nullable enable

using System;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class EventLogAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Id { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string Text { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        public bool ShowInServerLog { get; set; }

        private readonly XElement? textElement;

        public EventLogAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        {
            if (Id == Identifier.Empty)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\". {nameof(EventLogAction)} with no id.");
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
                    DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\". {nameof(EventLogAction)} with no text set ({element}).");
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