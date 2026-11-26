using Barotrauma;

namespace HomeAndHarbor;

public class DlcEndingAction : EventAction
{
    [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the slideshow prefab to trigger.")]
    public Identifier SlideshowPrefab { get; private set; }

    private bool isFinished;

    public DlcEndingAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
    { 
        
    }

    public override bool IsFinished(ref string goTo)
    {
        return isFinished;
    }
    public override void Reset(bool shouldEndConversations = true)
    {
        isFinished = false;
    }

    private DlcEndingManager? manager;

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }

        if (DlcEndingManager.TryGetEndingManagerForCampaign(GameMain.GameSession?.Campaign, out DlcEndingManager? endingManager))
        {
            endingManager.TriggerEndingSlideshow(SlideshowPrefab);
        }
        else if (manager == null)
        {
            manager = new DlcEndingManager();
            manager.TriggerEndingSlideshow(SlideshowPrefab);
        }


        if (manager != null)
        {
            manager.Update(deltaTime);
        }
        else
        {
            isFinished = true;
        }
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(DlcEndingAction)}";
    }
}