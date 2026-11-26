#nullable enable

using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Sets the state of the event.
    /// </summary>
    [TypePreviouslyKnownAs("SetTraitorEventStateAction")]
    class SetEventStateAction : EventAction
    {
        public SetEventStateAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        {
        }

        [Serialize(ScriptedEvent.State.Completed, IsPropertySaveable.Yes, description: "The state to set the event to (Incomplete, Completed or Failed).")]
        public ScriptedEvent.State State { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character(s) who should see the event update.")]
        public Identifier TargetTag { get; set; }

        private bool isFinished;

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
            if (isFinished || ParentEvent == null) { return; }
            ParentEvent.CurrentState = State;
            
            if (GameMain.GameSession?.EventManager?.EventLog is { } eventLog)
            {
#if CLIENT
                eventLog.SetEventLogState(ParentEvent.Prefab.Identifier, State);
#elif SERVER
                List<Client> targetClients = new List<Client>();

                if (TargetTag.IsEmpty)
                {
                    targetClients = GameMain.Server.ConnectedClients.ToList();
                }
                else
                {
                    foreach (var target in ParentEvent.GetTargets(TargetTag))
                    {
                        if (target is Character character)
                        {
                            var ownerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
                            if (ownerClient != null)
                            {
                                targetClients.Add(ownerClient);
                            }
                        }
                        else
                        {
                            DebugConsole.AddWarning($"{target} is not a valid target for an EventLogAction. The target should be a character.",
                                ParentEvent.Prefab.ContentPackage);
                        }
                    }
                }

                eventLog.SetEventLogState(ParentEvent.Prefab.Identifier, State, targetClients);
#endif
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(SetEventStateAction)} -> (State: {State})";
        }
    }
}