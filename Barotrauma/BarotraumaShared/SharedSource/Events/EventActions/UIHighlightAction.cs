namespace Barotrauma;

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

    [Serialize(ElementId.None, IsPropertySaveable.Yes)]
    public ElementId Id { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier EntityIdentifier { get; set; }

    [Serialize(OrderCategory.Emergency, IsPropertySaveable.Yes)]
    public OrderCategory OrderCategory { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier OrderIdentifier { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier OrderOption { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier OrderTargetTag { get; set; }

    [Serialize(true, IsPropertySaveable.Yes)]
    public bool Bounce { get; set; }

    [Serialize(false, IsPropertySaveable.Yes)]
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