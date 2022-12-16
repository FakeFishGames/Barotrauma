namespace Barotrauma;

partial class TutorialSegmentAction : EventAction
{
    private ObjectiveManager.Segment segment;

    partial void UpdateProjSpecific()
    {
        // Only need to create the segment when it's being triggered (otherwise the tutorial already has the segment instance)
        if (Type == SegmentActionType.Trigger)
        {
            segment = ObjectiveManager.Segment.CreateInfoBoxSegment(Identifier, ObjectiveTag, AutoPlayVideo ? Tutorials.AutoPlayVideo.Yes : Tutorials.AutoPlayVideo.No,
                new ObjectiveManager.Segment.Text(TextTag, Width, Height, Anchor.Center),
                new ObjectiveManager.Segment.Video(VideoFile, TextTag, Width, Height));
        }
        else if (Type == SegmentActionType.Add)
        {
            segment = ObjectiveManager.Segment.CreateObjectiveSegment(Identifier, !ObjectiveTag.IsEmpty ? ObjectiveTag : Identifier);
        }
        if (segment is not null)
        {
            segment.CanBeCompleted = CanBeCompleted;
            segment.ParentId = ParentObjectiveId;
        }
        switch (Type)
        {
            case SegmentActionType.Trigger:
            case SegmentActionType.Add:
                ObjectiveManager.TriggerTutorialSegment(segment);
                break;
            case SegmentActionType.Complete:
                ObjectiveManager.CompleteTutorialSegment(Identifier);
                break;
            case SegmentActionType.Remove:
                ObjectiveManager.RemoveTutorialSegment(Identifier);
                break;
            case SegmentActionType.CompleteAndRemove:
                ObjectiveManager.CompleteTutorialSegment(Identifier);
                ObjectiveManager.RemoveTutorialSegment(Identifier);
                break;
        }
    }
}