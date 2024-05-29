namespace Barotrauma;

/// <summary>
/// Highlights an UI element of some kind. Generally used in tutorials.
/// </summary>
partial class UIHighlightAction : EventAction
{
    public enum ElementId
    {
        None,
        RepairButton,
        PumpSpeedSlider,
        PassiveSonarIndicator,
        ActiveSonarIndicator,
        SonarModeSwitch,
        DirectionalSonarFrame,
        SteeringModeSwitch,
        MaintainPosTickBox,
        AutoTempSwitch,
        PowerButton,
        FissionRateSlider,
        TurbineOutputSlider,
        DeconstructButton,
        RechargeSpeedSlider,
        CPRButton,
        CloseButton,
        MessageBoxCloseButton
    }

    [Serialize(ElementId.None, IsPropertySaveable.Yes, description: "An arbitrary identifier that must match the userdata of the UI element. The userdatas of the element are hard-coded, so this option is generally intended for the developers' use.")]
    public ElementId Id { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "If the element's userdata is an entity or an entity prefab, it's identifier must match this value.")]
    public Identifier EntityIdentifier { get; set; }

    [Serialize(OrderCategory.Emergency, IsPropertySaveable.Yes, description: "If the element's userdata is an order category, it must match this.")]
    public OrderCategory OrderCategory { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "If the element's userdata is an order, it must match this identifier.")]
    public Identifier OrderIdentifier { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "If the element's userdata is an order with options, it must match this.")]
    public Identifier OrderOption { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "If the element's userdata is an order, the order must target an entity with this tag.")]
    public Identifier OrderTargetTag { get; set; }

    [Serialize(true, IsPropertySaveable.Yes, description: "Should the element bounce up an down in addition to being highlighted.")]
    public bool Bounce { get; set; }

    [Serialize(false, IsPropertySaveable.Yes, description: "Should the action highlight the first matching element it finds, or all of them?")]
    public bool HighlightMultiple { get; set; }

    private bool isFinished;

    public UIHighlightAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

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