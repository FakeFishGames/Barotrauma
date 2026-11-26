using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Action for opening one of the campaign menus (store, crew, medical clinic etc.) for a player.
/// Intended to be used in a conversation option, so an NPC can keep their "talk" interaction and offer the menu as
/// one of the dialogue options, instead of the menu replacing the dialogue entirely.
/// </summary>
internal partial class OpenCampaignInteractionAction(ScriptedEvent parentEvent, ContentXElement element) : EventAction(parentEvent, element)
{
    public static void Init()
    {
        InitProjectSpecific();
    }

    static partial void InitProjectSpecific();

    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character(s) to open the menu for. Used in MP to identify which client should open the menu.")]
    public Identifier TargetTag { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the NPC the menu is considered to belong to, meaning this also determines what exactly the InteractionType opens (eg. which Store).")]
    public Identifier SpeakerTag { get; set; }

    [Serialize(CampaignMode.InteractionType.None, IsPropertySaveable.Yes, description: "The menu to open (eg. Store, Crew, Upgrade, PurchaseSub, MedicalClinic).")]
    public CampaignMode.InteractionType InteractionType { get; set; }

    private bool isFinished = false;

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

        if (InteractionType == CampaignMode.InteractionType.None)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {nameof(OpenCampaignInteractionAction)} has no {nameof(InteractionType)} defined.");
            isFinished = true;
            return;
        }

        if (TargetTag.IsEmpty)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {nameof(OpenCampaignInteractionAction)} has no {nameof(TargetTag)} defined, the menu won't open for anyone.");
            isFinished = true;
            return;
        }

        // The menus need an NPC to belong to (e.g. the store menu opens the speaker's store), and the client silently ignores a missing one
        if (ParentEvent.GetTargets(SpeakerTag).FirstOrDefault(e => e is Character) is not Character speaker)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {nameof(OpenCampaignInteractionAction)} could not find a speaker with the tag '{SpeakerTag}'.");
            isFinished = true;
            return;
        }

#if CLIENT
        if (GameMain.GameSession?.GameMode is CampaignMode campaignMode)
        {
            // In singleplayer the menu can just be opened directly
            foreach (Entity entity in ParentEvent.GetTargets(TargetTag))
            {
                if (entity == Character.Controlled && Character.Controlled is Character interactor)
                {
                    campaignMode.ShowNPCMenuInteraction(speaker, interactor, InteractionType);
                    break;
                }
            }
        }
#endif

        OpenMenuProjSpecific(speaker);
        isFinished = true;
    }

    partial void OpenMenuProjSpecific(Character speaker);

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(OpenCampaignInteractionAction)} -> ({InteractionType})";
    }
}
