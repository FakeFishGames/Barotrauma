using System;

namespace Barotrauma
{

    /// <summary>
    /// Displays an objective in the top-right corner of the screen, or modifies an existing objective in some way.
    /// </summary>
    partial class EventObjectiveAction : EventAction
    {
        public enum SegmentActionType 
        {
            /// <summary>
            /// Legacy support. Triggers an info box segment, with optional support for video clips.
            /// </summary>
            [Obsolete]
            Trigger,
            /// <summary>
            /// Adds a new objective to the list.
            /// </summary>
            Add,
            /// <summary>
            /// Adds a new objective to the list if there are no existing objectives with the same identifier.
            /// </summary>
            AddIfNotFound,
            /// <summary>
            /// Marks the objective as completed.
            /// </summary>
            Complete,
            /// <summary>
            /// Marks the objective as completed and removes it from the list.
            /// </summary>
            CompleteAndRemove,
            /// <summary>
            /// Removes the objective from the list.
            /// </summary>
            Remove, 
            /// <summary>
            /// Marks the objective as failed.
            /// </summary>
            Fail,
            /// <summary>
            /// Marks the objective as failed and removes it from the list.
            /// </summary>
            FailAndRemove
        };

        [Serialize(SegmentActionType.Add, IsPropertySaveable.Yes, description: "Should the action add a new objective, or do something to an existing objective?")]
        public SegmentActionType Type { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Arbitrary identifier given to the objective. Can be used to complete/remove/fail the objective later. Also used to fetch the text from the text files.")]
        public Identifier Identifier { get; set; }

        [Obsolete, Serialize("", IsPropertySaveable.Yes, description: "Legacy support. Tag of the text to display as an objective in info box segments.")]
        public Identifier ObjectiveTag { get; set; }

        [Obsolete, Serialize(true, IsPropertySaveable.Yes, description: "Legacy support. Is this objective possible to complete if it's used in an info box segment.")]
        public bool CanBeCompleted { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of a parent objective. If set, this objective is displayed as a subobjective under the parent objective.")]
        public Identifier ParentObjectiveId { get; set; }

        [Obsolete, Serialize(false, IsPropertySaveable.Yes, description: "Legacy support. Should the video defined by VideoFile play automatically, or wait for the user to play it.")]
        public bool AutoPlayVideo { get; set; }

        [Obsolete, Serialize("", IsPropertySaveable.Yes, description: "Legacy support. Tag of the main text to display in info box segments.")]
        public Identifier TextTag { get; set; }

        [Obsolete, Serialize("", IsPropertySaveable.Yes, description: "Legacy support. Path of a video file to display in info box segments.")]
        public string VideoFile { get; set; }

        [Obsolete, Serialize(450, IsPropertySaveable.Yes, description: "Legacy support. Width of the info box segment.")]
        public int Width { get; set; }

        [Obsolete, Serialize(80, IsPropertySaveable.Yes, description: "Legacy support. Height of the info box segment.")]
        public int Height { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character(s) to show the objective to.")]
        public Identifier TargetTag { get; set; }

        private bool isFinished;

        public EventObjectiveAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (Identifier.IsEmpty)
            {
                Identifier = element.GetAttributeIdentifier("id", Identifier.Empty);
            }
            if (Type != SegmentActionType.Trigger && !TextTag.IsEmpty)
            {
                DebugConsole.ThrowError(
                    $"Error in {nameof(EventObjectiveAction)} in the event \"{parentEvent.Prefab.Identifier}\""+
                    $" - {nameof(TextTag)} will do nothing unless the action triggers a message box or a video.",
                    contentPackage: element.ContentPackage);
            }
            if (element.GetChildElement("Replace") != null)
            {
                DebugConsole.ThrowError(
                    $"Error in {nameof(EventObjectiveAction)} in the event \"{parentEvent.Prefab.Identifier}\"" +
                    $" - unrecognized child element \"Replace\".",
                    contentPackage: element.ContentPackage);
            }
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }
            UpdateProjSpecific();
            isFinished = true;
        }

        partial void UpdateProjSpecific();

        public override bool IsFinished(ref string goToLabel) => isFinished;

        public override void Reset() => isFinished = false;
    }
}