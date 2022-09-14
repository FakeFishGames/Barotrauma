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
            segment = Tutorial.Segment.CreateInfoBoxSegment(Id, ObjectiveTag, AutoPlayVideo ? Tutorials.AutoPlayVideo.Yes : Tutorials.AutoPlayVideo.No,
                new Tutorial.Segment.Text(TextTag, Width, Height, Anchor.Center),
                new Tutorial.Segment.Video(VideoFile, TextTag, Width, Height));
        }
        else if (Type == SegmentActionType.Add)
        {
            segment = Tutorial.Segment.CreateObjectiveSegment(Id, !ObjectiveTag.IsEmpty ? ObjectiveTag : Id);
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
                        tutorial.CompleteTutorialSegment(Id);
                        break;
                    case SegmentActionType.Remove:
                        tutorial.RemoveTutorialSegment(Id);
                        break;
                    case SegmentActionType.CompleteAndRemove:
                        tutorial.CompleteTutorialSegment(Id);
                        tutorial.RemoveTutorialSegment(Id);
                        break;
                }
            }
        }
        else
        {
            DebugConsole.ShowError($"Error in event \"{ParentEvent.Prefab.Identifier}\": attempting to use TutorialSegmentAction during a non-Tutorial game mode!");
        }
    }
}