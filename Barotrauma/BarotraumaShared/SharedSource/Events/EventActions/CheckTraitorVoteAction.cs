#nullable enable
using Barotrauma.Networking;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Checks whether the specific target was voted as the traitor.
    /// </summary>
    class CheckTraitorVoteAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character to check.")]
        public Identifier Target { get; set; }

        public CheckTraitorVoteAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        { 
            if (parentEvent is not TraitorEvent)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\" - {nameof(CheckTraitorVoteAction)} can only be used in traitor events.",
                    contentPackage: element.ContentPackage);
            }
        }

        protected override bool? DetermineSuccess()
        {
            var targetEntities = ParentEvent.GetTargets(Target);
#if SERVER
            if (GameMain.Server?.TraitorManager?.GetClientAccusedAsTraitor() is Client traitorClient)
            {
                return targetEntities.Any(e => e is Character character && traitorClient?.Character == character);
            }
#endif
            return false;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(succeeded.HasValue)} {nameof(CheckTraitorVoteAction)} -> (TargetTag: {Target.ColorizeObject()}";
        }
    }
}