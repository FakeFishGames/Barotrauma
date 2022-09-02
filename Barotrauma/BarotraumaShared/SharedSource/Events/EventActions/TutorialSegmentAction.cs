#if CLIENT
using Barotrauma.Tutorials;
#endif

namespace Barotrauma
{
    class TutorialSegmentAction : EventAction
    {
        public enum SegmentActionType { Trigger, Complete, Remove };

        [Serialize(SegmentActionType.Trigger, IsPropertySaveable.Yes)]
        public SegmentActionType Type { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Id { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ObjectiveTextTag { get; set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool AutoPlayVideo { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TextTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string VideoFile { get; set; }

        [Serialize(450, IsPropertySaveable.Yes)]
        public int Width { get; set; }

        [Serialize(80, IsPropertySaveable.Yes)]
        public int Height { get; set; }

#if CLIENT
        private readonly Tutorial.Segment segment;
#endif
        private bool isFinished;

        public TutorialSegmentAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
#if CLIENT
            // Only need to create the segment when it's being triggered (otherwise the tutorial already has the segment instance)
            if (Type == SegmentActionType.Trigger)
            {
                segment = new Tutorial.Segment(Id, ObjectiveTextTag, AutoPlayVideo ? Tutorials.AutoPlayVideo.Yes : Tutorials.AutoPlayVideo.No,
                    new Tutorial.Segment.Text(TextTag, Width, Height, Anchor.Center),
                    new Tutorial.Segment.Video(VideoFile, TextTag, Width, Height));
            }
#endif
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

#if CLIENT
            if (GameMain.GameSession?.GameMode is TutorialMode tutorialMode)
            {
                if (tutorialMode.Tutorial is Tutorial tutorial)
                {
                    switch (Type)
                    {
                        case SegmentActionType.Trigger:
                            tutorial.TriggerTutorialSegment(segment);
                            break;
                        case SegmentActionType.Complete:
                            tutorial.CompleteTutorialSegment(Id);
                            break;
                        case SegmentActionType.Remove:
                            tutorial.RemoveTutorialSegment(Id);
                            break;
                    }
                }
            }
            else
            {
                DebugConsole.ShowError($"Error in event \"{ParentEvent.Prefab.Identifier}\": attempting to use TutorialSegmentAction during a non-Tutorial game mode!");
            }
#endif

            isFinished = true;
        }

        public override bool IsFinished(ref string goToLabel)
        {
            return isFinished;
        }

        public override void Reset()
        {
            isFinished = false;
        }
    }
}