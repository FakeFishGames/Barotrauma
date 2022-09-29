using Barotrauma.Tutorials;

namespace Barotrauma;

partial class TutorialSegmentAction : EventAction
{
    private Tutorial.Segment segment;

    partial void UpdateProjSpecific()
    {
        // Only need to create the segment when it's being triggered (otherwise the tutorial already has the segment instance)
        if (Type == SegmentActionType.Trigger)
        {
            segment = Tutorial.Segment.CreateInfoBoxSegment(Identifier, ObjectiveTag, AutoPlayVideo ? Tutorials.AutoPlayVideo.Yes : Tutorials.AutoPlayVideo.No,
                new Tutorial.Segment.Text(TextTag, Width, Height, Anchor.Center),
                new Tutorial.Segment.Video(VideoFile, TextTag, Width, Height));
        }
        else if (Type == SegmentActionType.Add)
        {
            segment = Tutorial.Segment.CreateObjectiveSegment(Identifier, !ObjectiveTag.IsEmpty ? ObjectiveTag : Identifier);
        }
        if (GameMain.GameSession?.GameMode is TutorialMode tutorialMode)
        {
            if (tutorialMode.Tutorial is Tutorial tutorial)
            {
                switch (Type)
                {
                    case SegmentActionType.Trigger:
                    case SegmentActionType.Add:
                        tutorial.TriggerTutorialSegment(segment);
                        break;
                    case SegmentActionType.Complete:
                        tutorial.CompleteTutorialSegment(Identifier);
                        break;
                    case SegmentActionType.Remove:
                        tutorial.RemoveTutorialSegment(Identifier);
                        break;
                    case SegmentActionType.CompleteAndRemove:
                        tutorial.CompleteTutorialSegment(Identifier);
                        tutorial.RemoveTutorialSegment(Identifier);
                        break;
                }
            }
        }
        else
        {
            DebugConsole.ThrowError($"Error in event \"{ParentEvent.Prefab.Identifier}\": attempting to use TutorialSegmentAction during a non-Tutorial game mode!");
        }
    }
}