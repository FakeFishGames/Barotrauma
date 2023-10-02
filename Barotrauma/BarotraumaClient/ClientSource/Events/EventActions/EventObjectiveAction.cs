#nullable enable

namespace Barotrauma;

partial class EventObjectiveAction : EventAction
{
    public static void Trigger(
        SegmentActionType Type,
        Identifier Identifier,
        Identifier ObjectiveTag,
        Identifier ParentObjectiveId,
        Identifier TextTag,
        bool CanBeCompleted,
        bool autoPlayVideo = false,
        string videoFile = "",
        int width = 450,
        int height = 80)
    {
        ObjectiveManager.Segment? segment = null;
        // Only need to create the segment when it's being triggered (otherwise the tutorial already has the segment instance)
        if (Type == SegmentActionType.Trigger)
        {
            segment = ObjectiveManager.Segment.CreateInfoBoxSegment(Identifier, ObjectiveTag, autoPlayVideo ? Tutorials.AutoPlayVideo.Yes : Tutorials.AutoPlayVideo.No,
                new ObjectiveManager.Segment.Text(TextTag, width, height, Anchor.Center),
                new ObjectiveManager.Segment.Video(videoFile, TextTag, width, height));
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
                ObjectiveManager.TriggerSegment(segment);
                break;
            case SegmentActionType.Complete:
                ObjectiveManager.CompleteSegment(Identifier);
                break;
            case SegmentActionType.Remove:
                ObjectiveManager.RemoveSegment(Identifier);
                break;
            case SegmentActionType.CompleteAndRemove:
                ObjectiveManager.CompleteSegment(Identifier);
                ObjectiveManager.RemoveSegment(Identifier);
                break;
            case SegmentActionType.Fail:
                ObjectiveManager.FailSegment(Identifier);
                break;
            case SegmentActionType.FailAndRemove:
                ObjectiveManager.FailSegment(Identifier);
                ObjectiveManager.RemoveSegment(Identifier);
                break;
        }
    }

    partial void UpdateProjSpecific()
    {
        Trigger(Type, Identifier, ObjectiveTag, ParentObjectiveId, TextTag, CanBeCompleted, AutoPlayVideo, VideoFile, Width, Height);
    }
}