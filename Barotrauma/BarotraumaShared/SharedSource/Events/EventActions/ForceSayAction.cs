using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Forces an entity to say a message in chat.
    /// </summary>
    /// <remarks>
    /// Only entities that implement <see cref="ISpeakable"/> are supported.
    /// </remarks>
    class ForceSayAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the entity that should say the message.")]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The message that the entity should say. Can be the text as-is, or a tag referring to a line in a text file.")]
        public string Message { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the message that the entity says be sent in radio?")]
        public bool SayInRadio { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the message be stripped of any quotation mark characters?")]
        public bool RemoveQuotes { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the chat message be stripped of all text between square brackets (including the brackets themselves)?")]
        public bool RemoveSquareBrackets { get; set; }

        public ForceSayOptions ForceSayOptions { get; set; }

        public ForceSayAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (RemoveQuotes) { ForceSayOptions |= ForceSayOptions.RemoveQuotes; }
            if (RemoveSquareBrackets) { ForceSayOptions |= ForceSayOptions.RemoveSquaresBrackets; }
        }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset(bool shouldEndConversations = true)
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            var targets = ParentEvent.GetTargets(TargetTag);

            LocalizedString messageToSay = TextManager.Get(Message).Fallback(Message);
            foreach (var target in targets)
            {
                if (target != null && target is ISpeakable speakable)
                {
                    speakable.ForceSay(messageToSay, 
                        sayInRadio: SayInRadio, 
                        ForceSayOptions);
                }
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(ForceSayAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"Message: {Message})";
        }
    }
}