using Barotrauma.Extensions;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Cancels a currently active event.
    /// </summary>
    class CancelEventAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the event to cancel, if empty, cancels the event that this action is running on.")]
        public Identifier Identifier { get; set; }

        private bool isFinished;

        public CancelEventAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (Identifier.IsEmpty)
            {
                if (parentEvent != null)
                {
                    Identifier = parentEvent.Prefab.Identifier;
                }
                else
                {
                    DebugConsole.ThrowError($"Potential error in {nameof(CancelEventAction)}: no event identifier was specified, and the action is not part of a scripted event.");
                }
            }
        }

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

            if (GameMain.GameSession?.EventManager is { } eventManager)
            {
                foreach (var activeEvent in eventManager.ActiveEvents.Where(ev => ev.Prefab.Identifier == Identifier))
                {
                    if (activeEvent is ScriptedEvent scriptedEvent)
                    {
                        foreach (var actionWithIndent in scriptedEvent.GetAllActions())
                        {
                            // Reset conversation (remove conversation option from the NPC)
                            if (actionWithIndent.action is ConversationAction conversationAction)
                            {
                                conversationAction.Reset();
                            }
                            else if (actionWithIndent.action is TriggerAction triggerAction)
                            {
                                triggerAction.Reset();
                            }
                        }
                        activeEvent.Finish();
                    }
                }
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(CancelEventAction)} -> (EventPrefab: {Identifier.ColorizeObject()})";
        }
    }
}