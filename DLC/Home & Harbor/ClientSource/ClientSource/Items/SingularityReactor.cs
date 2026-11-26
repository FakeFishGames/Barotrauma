using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics.CodeAnalysis;

namespace HomeAndHarbor;

partial class SingularityReactor : ReactorBase
{
    public GUIButton? AutoTempSwitch { get; private set; }

    public GUIButton? PowerButton { get; private set; }
    private GUITickBox? powerLight;
    private GUITickBox? autoTempLight;

    private const int GraphSize = 25;
    private float graphTimer;
    private float updateGraphInterval = 0.5f;
    
    private Sprite? graphLine;
    private GUICustomComponent? graph;
    private GUICustomComponent? containmentFieldGraph;

    private const int ContainmentFieldGraphSize = 100;
    private float containmentFieldGraphTimer;
    private float containmentFieldUpdateGraphInterval = 0.1f;
    private readonly float[] containmentFieldTargetValues = new float[ContainmentFieldGraphSize];
    private readonly float[] containmentFieldValues = new float[ContainmentFieldGraphSize / 2];

    private GUIFrame? inventoryWindow;
    private GUILayoutGroup? buttonArea;

    private bool shouldMuffleSounds;
    private float lastMuffleCheckTime;
    private RoundSound? feedingRateSound, containmentRateSound;
    private SoundChannel? feedingRateSoundChannel, containmentRateSoundChannel;

    private Color outputColor = Color.Goldenrod;
    private Color loadColor = Color.LightSteelBlue;

    private readonly Color[] meterColors = new Color[] { new Color(52, 126, 56), new Color(156, 136, 59), new Color(232, 128, 79), new Color(255, 0, 0)};

    public GUIScrollBar FeedingRateScrollBar { get; private set; }

    public GUIScrollBar ContainmentRateScrollBar { get; private set; }

    public GUIScrollBar ContainmentFieldOffsetScrollBar { get; private set; }

    public GUIScrollBar ContainmentFieldAmplitudeScrollBar { get; private set; }

    private readonly float[] outputGraph = new float[GraphSize];
    private readonly float[] loadGraph = new float[GraphSize];
    
    private GUITickBox criticalConditionWarning;
    private GUITickBox singularityEscapingWarning;
    private GUITickBox criticalOutputWarning;

    private float lastSentContainmentFieldMatchError;

    private GUITextBlock? singularitySizeTextBlock;
    private GUITextBlock? singularityInstabilityTextBlock;

    private GUITextBlock? sizeText;

    private GUIFrame inventoryContainer;

    private GUILayoutGroup paddedFrame;

    private readonly Dictionary<string, GUIButton> warningButtons = new Dictionary<string, GUIButton>();

    private static readonly string[] warningTexts =
    [
        "ReactorWarningStabilizerBroken", "ReactorWarningStabilizerDamage", "ReactorWarningLowFuel", "ReactorWarningOutputLow"
    ];

    private Sprite? meterBar;

    private GUIFrame? infographic;

    public override bool RecreateGUIOnResolutionChange => true;

    protected override GUIScrollBar PowerGenerationRateScrollBar => FeedingRateScrollBar;

    protected override GUIScrollBar OutputControlRateScrollBar => ContainmentRateScrollBar;

    [MemberNotNull(nameof(FeedingRateScrollBar))]
    [MemberNotNull(nameof(ContainmentRateScrollBar))]
    [MemberNotNull(nameof(ContainmentFieldOffsetScrollBar))]
    [MemberNotNull(nameof(ContainmentFieldAmplitudeScrollBar))]
    [MemberNotNull(nameof(criticalConditionWarning))]
    [MemberNotNull(nameof(singularityEscapingWarning))]
    [MemberNotNull(nameof(criticalOutputWarning))]
    [MemberNotNull(nameof(inventoryContainer))]
    [MemberNotNull(nameof(paddedFrame))]
    partial void InitProjSpecific(ContentXElement element)
    {
        CreateGUI();
        graphLine = new Sprite(element.GetChildElement("graphline")?.GetChildElement("sprite"));
        meterBar = new Sprite(element.GetChildElement("meterbar")?.GetChildElement("sprite"));

        foreach (var subElement in element.Elements())
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "feedingratesound":
                    feedingRateSound = RoundSound.Load(subElement);
                    break;
                case "containmentratesound":
                    containmentRateSound = RoundSound.Load(subElement);
                    break;
            }
        }
    }

    [MemberNotNull(nameof(FeedingRateScrollBar))]
    [MemberNotNull(nameof(ContainmentRateScrollBar))]
    [MemberNotNull(nameof(ContainmentFieldOffsetScrollBar))]
    [MemberNotNull(nameof(ContainmentFieldAmplitudeScrollBar))]
    [MemberNotNull(nameof(criticalConditionWarning))]
    [MemberNotNull(nameof(singularityEscapingWarning))]
    [MemberNotNull(nameof(criticalOutputWarning))]
    [MemberNotNull(nameof(inventoryContainer))]
    [MemberNotNull(nameof(paddedFrame))]
    protected override void CreateGUI()
    {
        warningButtons.Clear();

        paddedFrame = new GUILayoutGroup(new RectTransform(
             GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center)
        { AbsoluteOffset = GUIStyle.ItemFrameOffset },
         isHorizontal: true)
        {
            CanBeFocused = true,
            HoverCursor = CursorState.Default,
            AlwaysOverrideCursor = true,
            RelativeSpacing = 0.012f,
            Stretch = true
        };

        GUILayoutGroup columnLeft = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), paddedFrame.RectTransform))
        {
            RelativeSpacing = 0.03f,
            Stretch = true
        };
        GUILayoutGroup columnRight = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), paddedFrame.RectTransform))
        {
            CanBeFocused = true,
            RelativeSpacing = 0.012f,
            Stretch = true
        };

        //----------------------------------------------------------
        //left column
        //----------------------------------------------------------

        inventoryWindow = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.75f), GuiFrame.RectTransform, Anchor.TopLeft, Pivot.TopRight)
        {
            MinSize = new Point(85, 220),
            RelativeOffset = new Vector2(-0.02f, 0)
        }, style: "ItemUI");

        GUILayoutGroup inventoryContent = new GUILayoutGroup(new RectTransform(inventoryWindow.Rect.Size - GUIStyle.ItemFrameMargin, inventoryWindow.RectTransform, Anchor.Center) 
            { AbsoluteOffset = GUIStyle.ItemFrameOffset },
            childAnchor: Anchor.TopCenter)
        {
            Stretch = true
        };

        /*new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), inventoryContent.RectTransform), "", 
            textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont, wrap: true);*/
        inventoryContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), inventoryContent.RectTransform), style: null);

        //----------------------------------------------------------
        //mid column
        //----------------------------------------------------------

        Point maxIndicatorSize = new Point(int.MaxValue, (int)(50 * GUI.Scale));

        var topLeftArea = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.2f), columnLeft.RectTransform),
            isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true
        };

        singularityEscapingWarning = new GUITickBox(new RectTransform(new Vector2(0.4f, 1.0f), topLeftArea.RectTransform) { MaxSize = maxIndicatorSize },
            TextManager.Get("ReactorWarningSingularityEscaping"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightRed")
        {
            Selected = false,
            Enabled = false,
            ToolTip = TextManager.Get("ReactorSingularityEscapingTip")
        };
        criticalOutputWarning = new GUITickBox(new RectTransform(new Vector2(0.3f, 1.0f), topLeftArea.RectTransform) { MaxSize = maxIndicatorSize },
            TextManager.Get("ReactorWarningCriticalOutput"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightRed")
        {
            Selected = false,
            Enabled = false,
            ToolTip = TextManager.Get("ReactorOutputTip")
        };
        criticalConditionWarning = new GUITickBox(new RectTransform(new Vector2(0.3f, 1.0f), topLeftArea.RectTransform) { MaxSize = maxIndicatorSize },
            TextManager.Get("ReactorWarningCriticalCondition"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightRed")
        {
            Selected = false,
            Enabled = false,
            ToolTip = TextManager.Get("ReactorCriticalConditionTip")
        };
        List<GUITickBox> indicatorLights = [singularityEscapingWarning, criticalConditionWarning, criticalOutputWarning];
        indicatorLights.ForEach(l => l.TextBlock.Wrap = true);
        indicatorLights.ForEach(l => l.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal));
        topLeftArea.Recalculate();

        new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), columnLeft.RectTransform), style: "FlatHorizontalLine", new Color(144, 126, 83));

        float relativeYMargin = 0.1f;
        Vector2 sliderSize = new Vector2(1.0f, 0.3f);

        var innerAreaFrame = new GUIFrame(new RectTransform(new Vector2(1f, 1f), columnLeft.RectTransform), style: null);
        var innerAreaContentFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 1f), innerAreaFrame.RectTransform, Anchor.Center), style: "InnerFrameBlank", new Color(144, 126, 83));
        var innerAreaContent = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.92f), innerAreaContentFrame.RectTransform, Anchor.TopCenter))
        {
            Stretch = true,
            RelativeSpacing = 0.03f,
        };

        new GUIButton(new RectTransform(new Point(GUI.IntScale(40)), innerAreaContentFrame.RectTransform, Anchor.BottomRight, isFixedSize: true) { AbsoluteOffset = new Point(GUI.IntScale(5)) }, style: "HelpIcon")
        {
            OnClicked = (_, _) =>
            {
                new GUIMessageBox(LocalizedString.EmptyString, TextManager.Get("SingularityReactorForDummies"));
                return true;
            },
            IgnoreLayoutGroups = true,
            ToolTip = TextManager.Get("SingularityReactorForDummies.tooltip")
        };

        var meterArea = new GUIFrame(new RectTransform(new Vector2(1, 0.15f), innerAreaContent.RectTransform), style: null);
        var leftArea = new GUILayoutGroup(new RectTransform(new Vector2(0.49f, 1), meterArea.RectTransform), false, Anchor.TopCenter)
        {
            Stretch = true
        };
        var rightArea = new GUILayoutGroup(new RectTransform(new Vector2(0.49f, 1), meterArea.RectTransform, Anchor.TopCenter, Pivot.TopLeft), false, Anchor.TopCenter)
        {
            Stretch = true
        };

        var fissionRateTextBox = new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.5f), leftArea.RectTransform, Anchor.TopCenter),
            TextManager.Get("ReactorFeedingRate"), textColor: GUIStyle.TextColorNormal, textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont)
        {
            ToolTip = TextManager.Get("ReactorFeedingRate.tooltip"),
            AutoScaleHorizontal = true
        };

        var ContainmentRateTextBox = new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.5f), rightArea.RectTransform, Anchor.TopCenter), 
            TextManager.Get("ReactorContainmentRate"), textColor: GUIStyle.TextColorNormal, textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont)
        {
            ToolTip = TextManager.Get("ReactorContainmentRate.tooltip"),
            AutoScaleHorizontal = true
        };
        GUITextBlock.AutoScaleAndNormalize(ContainmentRateTextBox, fissionRateTextBox);

        FeedingRateScrollBar = new GUIScrollBar(new RectTransform(sliderSize, leftArea.RectTransform, Anchor.TopCenter)
        {
            RelativeOffset = new Vector2(0, relativeYMargin)
        },
            style: "DeviceSlider", barSize: 0.15f)
        {
            Enabled = false,
            Step = 1.0f / 255,
            OnMoved = (GUIScrollBar bar, float scrollAmount) =>
            {
                LastUser = Character.Controlled;
                unsentChanges = true;
                TargetFeedingRate = scrollAmount * 100.0f;
                return false;
            }
        };

        ContainmentRateScrollBar = new GUIScrollBar(new RectTransform(sliderSize, rightArea.RectTransform, Anchor.TopCenter)
        {
            RelativeOffset = new Vector2(0, relativeYMargin)
        },
            style: "DeviceSlider", barSize: 0.15f, isHorizontal: true)
        {
            Enabled = false,
            Step = 1.0f / 255,
            OnMoved = (GUIScrollBar bar, float scrollAmount) =>
            {
                LastUser = Character.Controlled;
                unsentChanges = true;
                TargetContainmentRate = scrollAmount * 100.0f;
                return false;
            }   
        };
        //indicator for the disabled area of the containment rate slider
        new GUICustomComponent(new RectTransform(new Vector2(0.97f, 0.5f), ContainmentRateScrollBar.Frame.RectTransform, Anchor.Center), onDraw: (sb, customComponent) =>
        {
            if (maxContainmentRate < 100.0f)
            {
                Rectangle blockedAreaRect = new Rectangle(
                    (int)(customComponent.Rect.X + customComponent.Rect.Width * maxContainmentRate / 100.0f),
                    customComponent.Rect.Y,
                    (int)(customComponent.Rect.Width * (1.0f - maxContainmentRate / 100.0f)),
                    customComponent.Rect.Height);
                GUI.DrawRectangle(sb, blockedAreaRect, GUIStyle.Red * 0.7f, isFilled: true);
            }
        }, onUpdate: (deltaTime, customComponent) =>
        {
            //allow focusing and showing the tooltip when containment rate is restricted
            customComponent.CanBeFocused = maxContainmentRate < 100.0f;
        })
        {
            ToolTip = TextManager.Get("ReactorContainmentRate.limited.tooltip")
        };

        new GUIFrame(new RectTransform(new Vector2(1.1f, 0.01f), innerAreaContent.RectTransform) { RelativeOffset = new Vector2(-0.05f, 0f) }, style: "FlatHorizontalLine", new Color(144, 126, 83));

        //-----------------------------------------------------------

        var containmentFieldAreaMid = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.7f), innerAreaContent.RectTransform))
        {
            Stretch = true
        };

        #region Instability
        var instabilityFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.2f), containmentFieldAreaMid.RectTransform), style: null);
        var instabilityLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1f), instabilityFrame.RectTransform, Anchor.Center), isHorizontal: true) 
        { 
            Stretch = true,
            RelativeSpacing = 0.02f
        };

        var instabilityLeft = new GUILayoutGroup(new RectTransform(new Vector2(0.82f, 1.0f), instabilityLayout.RectTransform)) { Stretch = true };
        var instabilityRight = new GUILayoutGroup(new RectTransform(new Vector2(0.18f, 1.0f), instabilityLayout.RectTransform), false) { Stretch = true };

        var instabilityText = new GUITextBlock(new RectTransform(new Vector2(1f, 1f), instabilityLeft.RectTransform, Anchor.TopRight),
            TextManager.Get("ReactorInstability"), font: GUIStyle.Font, textAlignment: Alignment.BottomRight)
        {
            Padding = new Vector4(0, 0, 0, 2)
        };

        new GUICustomComponent(new RectTransform(new Vector2(1f, 0.5f), instabilityLeft.RectTransform, Anchor.Center), 
            (SpriteBatch sb, GUICustomComponent component) => { DrawMeter(sb, component, GetInstability()); }, null);

        var singularityInstabilityNumberFrame = new GUIFrame(new RectTransform(new Vector2(1f, 1f), instabilityRight.RectTransform), style: "InnerFrameBlank", new Color(100, 100, 100))
        {
            CanBeFocused = true
        };

        singularityInstabilityTextBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 1f), singularityInstabilityNumberFrame.RectTransform, Anchor.Center),
            "100", textColor: loadColor, font: GUIStyle.LargeFont, textAlignment: Alignment.Center);
        singularityInstabilityTextBlock.TextGetter += () => $"{(int)(GetInstability() * 100.0f)}";
        #endregion


        var containmentFieldLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), containmentFieldAreaMid.RectTransform), true) { Stretch = true };

        #region Amplitude
        var containmentAmplitudeLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 1f), containmentFieldLayout.RectTransform), isHorizontal: true) { Stretch = true };
        var containmentAmplitudeFrame = new GUIFrame(new RectTransform(new Vector2(1f, 1f), containmentAmplitudeLayout.RectTransform), style: null);
        var containmentAmplitudeVisual = new GUIFrame(new RectTransform(new Vector2(1f, 0.8f), containmentAmplitudeFrame.RectTransform, Anchor.CenterRight), style: "SingularityAmplitudeSliderFrame");

        ContainmentFieldAmplitudeScrollBar = new GUIScrollBar(new RectTransform(new Vector2(0.25f, 1f), containmentAmplitudeFrame.RectTransform, Anchor.CenterRight),
            style: "SmoothDeviceSlider", barSize: 0.15f, isHorizontal: false)
        {
            Range = new Vector2(MaxContainmentFieldAmplitude, MinContainmentFieldAmplitude),
            Enabled = false,
            Step = 1.0f / 255,
            OnMoved = (GUIScrollBar bar, float scrollAmount) =>
            {
                LastUser = Character.Controlled;
                unsentChanges = true;
                containmentFieldAmplitude = bar.BarScrollValue;
                return false;
            }
        };

        containmentAmplitudeVisual.RectTransform.AbsoluteOffset = new Point(ContainmentFieldAmplitudeScrollBar.Rect.Width - 10, 0);
        #endregion

        var containmentFieldGraphFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), containmentFieldLayout.RectTransform), style: "InnerFrameDark");
        containmentFieldGraph = new GUICustomComponent(new RectTransform(new Vector2(0.85f, 0.9f), containmentFieldGraphFrame.RectTransform, Anchor.Center), DrawContainmentFieldGraph, null);

        #region Offset

        var containmentOffsetLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 1f), containmentFieldLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.TopLeft) { Stretch = true };
        var containmentOffsetFrame = new GUIFrame(new RectTransform(new Vector2(1f, 1f), containmentOffsetLayout.RectTransform), style: null);
        var containmentOffsetVisual = new GUIFrame(new RectTransform(new Vector2(1f, 0.8f), containmentOffsetFrame.RectTransform, Anchor.CenterLeft), style: "SingularityOffsetSliderFrame");

        ContainmentFieldOffsetScrollBar = new GUIScrollBar(new RectTransform(new Vector2(0.25f, 1.0f), containmentOffsetFrame.RectTransform, Anchor.CenterLeft),
            style: "SmoothDeviceSlider", barSize: 0.15f, isHorizontal: false)
        {
            Range = new Vector2(MinContainmentFieldOffset, MaxContainmentFieldOffset),
            Enabled = false,
            Step = 1.0f / 255,
            OnMoved = (GUIScrollBar bar, float scrollAmount) =>
            {
                LastUser = Character.Controlled;
                unsentChanges = true;
                containmentFieldOffset = -bar.BarScrollValue;
                return false;
            }
        };

        containmentOffsetVisual.RectTransform.AbsoluteOffset = new Point(ContainmentFieldOffsetScrollBar.Rect.Width - 10, 0);

        #endregion

        #region Singularity Size

        var sizeFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.2f), containmentFieldAreaMid.RectTransform), style: null);
        var sizeLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1f), sizeFrame.RectTransform, Anchor.Center), isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };

        var sizeLeft = new GUILayoutGroup(new RectTransform(new Vector2(0.18f, 1.0f), sizeLayout.RectTransform)) { Stretch = true };
        var sizeRight = new GUILayoutGroup(new RectTransform(new Vector2(0.82f, 1.0f), sizeLayout.RectTransform)) { Stretch = true };

        sizeText = new GUITextBlock(new RectTransform(new Vector2(1f, 1f), sizeRight.RectTransform, Anchor.TopLeft),
            TextManager.Get("ReactorSingularitySize"), font: GUIStyle.Font, textAlignment: Alignment.BottomLeft)
        {
            Padding = new Vector4(0, 0, 0, 2)
        };

        new GUICustomComponent(new RectTransform(new Vector2(1f, 0.5f), sizeRight.RectTransform, Anchor.Center), 
            (SpriteBatch sb, GUICustomComponent component) => { DrawMeter(sb, component, (Singularity?.Strength ?? 0.0f) / 100f); }, null);

        var sizeNumberFrame = new GUIFrame(new RectTransform(new Vector2(1f, 1f), sizeLeft.RectTransform), style: "InnerFrameBlank", new Color(100, 100, 100))
        {
            CanBeFocused = true
        };

        singularitySizeTextBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 1f), sizeNumberFrame.RectTransform, Anchor.Center),
            "100", textColor: outputColor, font: GUIStyle.LargeFont, textAlignment: Alignment.Center);
        singularitySizeTextBlock.TextGetter += () => $"{(int)(Singularity?.Strength ?? 0.0f)}";

        #endregion

        //-----------------------------------------------------------

        buttonArea = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.18f), columnLeft.RectTransform), isHorizontal: true) 
        { 
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        int buttonCount = warningTexts.Length;
        for (int i = 0; i < buttonCount; i++)
        {
            string text = warningTexts[i];
            var button = new GUIButton(new RectTransform(Vector2.One, buttonArea.RectTransform), 
                TextManager.Get(text), style: "IndicatorButton")
            {
                Font = GUIStyle.SubHeadingFont,
                ToolTip = TextManager.Get(text + ".tooltip")
            };
            button.TextBlock.Wrap = true;
            warningButtons.Add(text, button);
        }
        buttonArea.Recalculate();

        //----------------------------------------------------------
        //right column
        //----------------------------------------------------------

        // Auto temp
        var topRightArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), columnRight.RectTransform),
            isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        topRightArea.RectTransform.MinSize = new Point(0, topLeftArea.Rect.Height);
        topRightArea.RectTransform.MaxSize = new Point(int.MaxValue, topLeftArea.Rect.Height);

        new GUIFrame(new RectTransform(new Vector2(0.01f, 1.0f), topRightArea.RectTransform), style: "VerticalLine");

        AutoTempSwitch = new GUIButton(new RectTransform(new Vector2(0.15f, 0.9f), topRightArea.RectTransform),
            style: "SwitchVertical")
        {
            Enabled = false,
            Selected = AutoTemp,
            ClickSound = GUISoundType.UISwitch,
            OnClicked = (button, data) =>
            {
                AutoTemp = !AutoTemp;
                LastUser = Character.Controlled;
                unsentChanges = true;
                return true;
            }
        };
        AutoTempSwitch.RectTransform.MaxSize = new Point((int)(AutoTempSwitch.Rect.Height * 0.4f), int.MaxValue);
        
        autoTempLight = new GUITickBox(new RectTransform(new Vector2(0.4f, 1.0f), topRightArea.RectTransform),
            TextManager.Get("ReactorAutoTemp"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightYellow")
            {
                ToolTip = TextManager.Get("ReactorTipAutoTemp"),
                CanBeFocused = false,
                Selected = AutoTemp
            };
        autoTempLight.RectTransform.MaxSize = new Point(int.MaxValue, criticalConditionWarning.Rect.Height);
        autoTempLight.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal);

        new GUIFrame(new RectTransform(new Vector2(0.01f, 1.0f), topRightArea.RectTransform), style: "VerticalLine");

        // Power button
        var powerArea = new GUIFrame(new RectTransform(new Vector2(0.4f, 1.0f), topRightArea.RectTransform), style: null);
        var paddedPowerArea = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), powerArea.RectTransform, Anchor.Center, scaleBasis: ScaleBasis.BothHeight), style: "PowerButtonFrame");
        powerLight = new GUITickBox(new RectTransform(new Vector2(0.87f, 0.3f), paddedPowerArea.RectTransform, Anchor.TopCenter, Pivot.Center), 
            TextManager.Get("PowerLabel"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightPower")
            {
                CanBeFocused = false,
                Selected = PowerOn
        };
        powerLight.TextBlock.Padding = new Vector4(5.0f, 0.0f, 0.0f, 0.0f);
        powerLight.TextBlock.AutoScaleHorizontal = true;
        powerLight.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal);
        PowerButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.75f), paddedPowerArea.RectTransform, Anchor.BottomCenter)
        {
            RelativeOffset = new Vector2(0, 0.1f)
        }, style: "PowerButton")
        {
            OnClicked = (button, data) =>
            {
                PowerOn = !PowerOn;
                LastUser = Character.Controlled;
                unsentChanges = true;
                return true;
            }
        };

        paddedFrame.Recalculate();
        columnLeft.Recalculate();
        columnRight.Recalculate();
        topRightArea.Recalculate();
        autoTempLight.TextBlock.Padding = new Vector4(autoTempLight.TextBlock.Padding.X, 0.0f, 0.0f, 0.0f);
        autoTempLight.TextBlock.Text = autoTempLight.TextBlock.Text.Replace(" ", "\n");
        autoTempLight.TextBlock.AutoScaleHorizontal = true;
        GUITextBlock.AutoScaleAndNormalize(indicatorLights.Select(l => l.TextBlock));
        GUITextBlock.AutoScaleAndNormalize(warningButtons.Values.Select(b => b.TextBlock), defaultScale: 0.8f);
        warningButtons.Values.First().RectTransform.ScaleChanged += () =>
        {
            GUITextBlock.AutoScaleAndNormalize(warningButtons.Values.Select(b => b.TextBlock));
        };

        // right bottom (graph area) -----------------------

        new GUIFrame(new RectTransform(new Vector2(0.95f, 0.01f), columnRight.RectTransform), style: "HorizontalLine");

        var bottomRightArea = new GUILayoutGroup(new RectTransform(Vector2.One, columnRight.RectTransform), isHorizontal: true)
        {
            Stretch = true,
            CanBeFocused = true,
            RelativeSpacing = 0.02f
        };

        new GUIFrame(new RectTransform(new Vector2(0.01f, 1.0f), bottomRightArea.RectTransform), style: "VerticalLine");

        var graphArea = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 1.0f), bottomRightArea.RectTransform))
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };

        var loadText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), graphArea.RectTransform),
            "", textColor: loadColor, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft)
        {
            ToolTip = TextManager.Get("ReactorTipLoad")
        };
        LocalizedString loadStr = TextManager.Get("ReactorLoad");
        LocalizedString kW = TextManager.Get("kilowatt");
        loadText.TextGetter += () => $"{loadStr.Replace("[kw]", ((int)Load).ToString())} {kW}";

        var graphFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), graphArea.RectTransform), style: "InnerFrameRed");
        graph = new GUICustomComponent(new RectTransform(new Vector2(0.9f, 0.98f), graphFrame.RectTransform, Anchor.Center), DrawGraph, null);

        var outputText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), graphArea.RectTransform),
            "", textColor: outputColor, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft)
        {
            ToolTip = TextManager.Get("ReactorTipPower")
        };
        LocalizedString outputStr = TextManager.Get("ReactorOutput");
        outputText.TextGetter += () => $"{outputStr.Replace("[kw]", ((int)-currPowerConsumption).ToString())} {kW}";

        var fuelConsumptionText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), outputText.RectTransform, Anchor.CenterRight), "", textAlignment: Alignment.CenterRight);
        LocalizedString fuelUsageStr = TextManager.Get("ReactorFuelUsage");
        fuelConsumptionText.TextGetter += () => fuelUsageStr.Replace("[amount]", GetCurrentFuelConsumption(deltaTime: 60.0f).ToString("0.0"));

        InitInventoryUI();

        // Infographic overlay ---------------------
        int buttonHeight = (int)(GUIStyle.ItemFrameMargin.Y * 0.4f);
        var helpButtonRt = new RectTransform(new Point(buttonHeight), parent: GuiFrame.RectTransform, anchor: Anchor.TopRight)
        {
            AbsoluteOffset = new Point(buttonHeight / 4),
            MinSize = new Point(buttonHeight)
        };
        new GUIButton(helpButtonRt, "", style: "HelpIcon")
        {
            ToolTip = TextManager.Get("infobutton"),
            OnClicked = (_, _) =>
            {
                CreateInfrographic();
                return true;
            }
        };
    }

    private void InitInventoryUI()
    {
        var itemContainer = item.GetComponent<ItemContainer>();
        if (itemContainer != null && inventoryContainer != null)
        {
            itemContainer.UILabel = "";
            itemContainer.AllowUIOverlap = true;
            itemContainer.Inventory.RectTransform = inventoryContainer.RectTransform;
        }
    }

    partial void OnItemLoadedProjSpecific()
    {
        ContainmentRateScrollBar.BarScroll = TargetContainmentRate / 100.0f;
        FeedingRateScrollBar.BarScroll = TargetFeedingRate / 100.0f;
        ContainmentFieldAmplitudeScrollBar.BarScrollValue = containmentFieldAmplitude;
        ContainmentFieldOffsetScrollBar.BarScrollValue = containmentFieldOffset;
        InitInventoryUI();
    }

    protected override void SetScrollBarsToMatchRates()
    {
        PowerGenerationRateScrollBar.BarScroll = PowerGenerationRate / 100.0f;
        //containment rate slider works "backwards" from normal reactors: a rate of 0 results in the highest output
        OutputControlRateScrollBar.BarScroll = 1.0f - OutputControlRate / 100.0f;
    }

    partial void UpdateSingularityReactorSounds()
    {
        UpdateSound(feedingRateSound, ref feedingRateSoundChannel, PowerOn ? feedingRate : 0.0f);
        UpdateSound(containmentRateSound, ref containmentRateSoundChannel, PowerOn ? containmentRate : 0.0f);

        void UpdateSound(RoundSound? sound, ref SoundChannel? channel, float rate)
        {
            float soundGain = 
                item.Condition <= 0.0f || rate < 1.0f ? 
                    0.0f : 
                    MathHelper.Clamp(rate / 100.0f, 0.0f, 1.0f);
            if (sound != null && 
                soundGain > 0.0f &&
                Vector3.DistanceSquared(GameMain.SoundManager.ListenerPosition, new Vector3(item.WorldPosition, 0.0f)) < sound.Range * sound.Range)
            {
                if (channel == null || !channel.IsPlaying)
                {
                    channel = SoundPlayer.PlaySound(sound, item.WorldPosition, hullGuess: item.CurrentHull);
                }
                if (channel != null)
                {
                    if (Timing.TotalTime > lastMuffleCheckTime + 0.2f)
                    {
                        shouldMuffleSounds = SoundPlayer.ShouldMuffleSound(Character.Controlled, item.WorldPosition, sound.Range, Character.Controlled?.CurrentHull);
                        lastMuffleCheckTime = (float)Timing.TotalTime;
                    }
                    channel.Muffled = shouldMuffleSounds;
                    channel.Looping = true;
                    channel.Position = new Vector3(item.WorldPosition, 0.0f);
                    channel.Gain = soundGain;
                }
            }
            else
            {
                channel?.FadeOutAndDispose();
                channel = null;
            }
        }
    }

    private void DrawContainmentFieldGraph(SpriteBatch spriteBatch, GUICustomComponent container)
    {
        if (item.Removed) { return; }

        float xOffset = containmentFieldGraphTimer / containmentFieldUpdateGraphInterval;
        Rectangle graphRect = new Rectangle(container.Rect.X, container.Rect.Y, container.Rect.Width, container.Rect.Height - (int)(5 * GUI.yScale));

        float normalizedOffset = MathUtils.InverseLerp(MinContainmentFieldOffset, MaxContainmentFieldOffset, containmentFieldOffset);
        float offset = MathHelper.Lerp(-0.5f, 0.5f, normalizedOffset);

        float waveCenter = graphRect.Center.Y - offset * graphRect.Height / 2;
        float waveAmplitude = Math.Max(2, graphRect.Height / 2 * containmentFieldAmplitude);

        Color indicatorColor =
            ToolBox.GradientLerp(containmentFieldMatchError, Color.LightGreen, Color.Green, Color.Yellow, Color.Orange, Color.Red);

        GUI.DrawRectangle(spriteBatch, new Rectangle(graphRect.X, (int)(waveCenter - waveAmplitude / 2), graphRect.Width, (int)waveAmplitude), indicatorColor * 0.15f, isFilled: true);

        GUI.DrawLine(spriteBatch, new Vector2(graphRect.Center.X, graphRect.Y), new Vector2(graphRect.Center.X, graphRect.Bottom), indicatorColor * 0.5f);

        containmentFieldUpdateGraphInterval = 0.25f;

        DrawGraph(containmentFieldTargetValues, spriteBatch, graphRect, maxVal: 200, xOffset, Color.LightGray * 0.25f, lineThickness: 5);
        DrawGraph(containmentFieldValues, spriteBatch, new Rectangle(graphRect.X, graphRect.Y, graphRect.Width / 2, graphRect.Height), maxVal: 200, xOffset, Color.Green, lineThickness: 2);
    }

    private void DrawGraph(SpriteBatch spriteBatch, GUICustomComponent container)
    {
        if (item.Removed) { return; }
        float maxLoad = loadGraph.Max();
        float xOffset = graphTimer / updateGraphInterval;
        Rectangle graphRect = new Rectangle(container.Rect.X, container.Rect.Y, container.Rect.Width, container.Rect.Height - (int)(5 * GUI.yScale));
        DrawGraph(outputGraph, spriteBatch, graphRect, Math.Max(10000.0f, maxLoad), xOffset, outputColor);
        DrawGraph(loadGraph, spriteBatch, graphRect, Math.Max(10000.0f, maxLoad), xOffset, loadColor);
    }

    private float containmentFieldT;
    private float containmentFieldTargetT;

    float graphSpeed = 20.0f;

    protected override void UpdateGraphs(float deltaTime)
    {
        graphTimer += deltaTime;
        containmentFieldGraphTimer += deltaTime;
        if (graphTimer > updateGraphInterval)
        {
            UpdateGraph(outputGraph, -currPowerConsumption);
            UpdateGraph(loadGraph, Load);
            graphTimer = 0.0f;
        }

        if (containmentFieldGraphTimer > containmentFieldUpdateGraphInterval)
        {
            float normalizedTargetOffset = MathUtils.InverseLerp(MinContainmentFieldOffset, MaxContainmentFieldOffset, containmentFieldTargetOffset);
            AddValue(normalizedTargetOffset, containmentFieldTargetAmplitude, containmentFieldTargetValues, deltaTime, ref containmentFieldTargetT);
            float timeToScrollMidWay = ContainmentFieldGraphSize * containmentFieldUpdateGraphInterval / 2;                
            containmentFieldT = containmentFieldTargetT - (timeToScrollMidWay * graphSpeed * deltaTime / containmentFieldUpdateGraphInterval);
            float normalizedOffset = MathUtils.InverseLerp(MinContainmentFieldOffset, MaxContainmentFieldOffset, containmentFieldOffset);
            AddValue(normalizedOffset, containmentFieldAmplitude, containmentFieldValues, deltaTime, ref containmentFieldT);
            containmentFieldGraphTimer = 0.0f;

            float accumulatedDiff = 0.0f;
            for (int i = 0; i < containmentFieldValues.Length; i++)
            {
                int containmentFieldTargetIndex = i + (containmentFieldTargetValues.Length - containmentFieldValues.Length);
                accumulatedDiff += Math.Abs(containmentFieldTargetValues[containmentFieldTargetIndex] - containmentFieldValues[i]);
            }
            containmentFieldMatchError = MathHelper.Clamp(accumulatedDiff / containmentFieldValues.Length / 100.0f, 0.0f, 1.0f);
            if (Math.Abs(containmentFieldMatchError - lastSentContainmentFieldMatchError) > 0.1f)
            {
                unsentChanges = true;
            }
        }

        if (AutoTemp)
        {
            FeedingRateScrollBar.BarScroll = FeedingRate / 100.0f;
        }

        void AddValue(float normalizedOffset, float normalizedAmplitude, float[] graph, float deltaTime, ref float t)
        {
            float offset = MathHelper.Lerp(-50.0f, 50.0f, normalizedOffset);
            float amplitude = MathHelper.Lerp(0.0f, 1.0f, normalizedAmplitude);

            t += graphSpeed * deltaTime;

            float currentT = MathF.Sin((float)t) * amplitude;
            UpdateGraph(graph, 100.0f + currentT * 50.0f + offset);
        }
    }
            
    public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
    {
        IsActive = true;

        bool lightOn = Timing.TotalTime % 0.5f < 0.25f && PowerOn;

        ToggleWarningButton("ReactorWarningStabilizerBroken", stabilizers.Any(static pump => pump.Item.Condition <= 0));
        ToggleWarningButton("ReactorWarningStabilizerDamage", stabilizers.Any(static pump => !pump.Item.InWater));
        ToggleWarningButton("ReactorWarningLowFuel", prevAvailableFuel < feedingRate);
        ToggleWarningButton("ReactorWarningOutputLow", prevAvailableFuel < feedingRate * 0.01f);

        void ToggleWarningButton(string buttonKey, bool shouldBeOn)
        {
            warningButtons[buttonKey].Selected = shouldBeOn && lightOn;
        }

        if (singularityInstabilityTextBlock != null)
        {
            singularityInstabilityTextBlock.TextColor = meterColors[Math.Clamp((int)(GetInstability() * meterColors.Length), 0, meterColors.Length - 1)];
        }
        if (singularitySizeTextBlock != null)
        {
            float size = (Singularity?.Strength ?? 0.0f) / 100f;
            singularitySizeTextBlock.TextColor = meterColors[Math.Clamp((int)(size * meterColors.Length), 0, meterColors.Length - 1)];
        }

        criticalConditionWarning.Selected = InCriticalCondition && lightOn;
        singularityEscapingWarning.Selected = IsSingularityEscaping && lightOn;
        criticalOutputWarning.Selected = -currPowerConsumption > Load * 1.5f && lightOn;

        if (paddedFrame != null &&
            paddedFrame.Rect.Contains(PlayerInput.MousePosition) &&
            !PlayerInput.KeyDown(InputType.Deselect) && !PlayerInput.KeyHit(InputType.Deselect))
        {
            Character.DisableControls = true;
        }

        if (!PowerOn)
        {
            ContainmentRateScrollBar.BarScroll = NormalizedContainmentRate;
            FeedingRateScrollBar.BarScroll = NormalizedFeedingRate;
        }
        else if (Character.DisableControls && GUI.KeyboardDispatcher.Subscriber == null)
        {
            float rate = 50.0f; //percentage per second

            Vector2 input = Vector2.Zero;
            if (!AutoTemp)
            {
                if (PlayerInput.KeyDown(InputType.Left)) { input.X += -1.0f; }
                if (PlayerInput.KeyDown(InputType.Right)) { input.X += 1.0f; }
                if (PlayerInput.KeyDown(InputType.Up)) { input.Y += 1.0f; }
                if (PlayerInput.KeyDown(InputType.Down)) { input.Y += -1.0f; }
            }

            Vector2 containmentFieldInput = Vector2.Zero;
            if (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.Left)) { containmentFieldInput.X += -2.0f; }
            if (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.Right)) { containmentFieldInput.X += 2.0f; }
            if (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.Up)) { containmentFieldInput.Y += -2.0f; }
            if (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.Down)) { containmentFieldInput.Y += 2.0f; }

            if (PlayerInput.KeyDown(InputType.Run)) { rate = 200.0f; }
            else if (PlayerInput.KeyDown(InputType.Crouch)) { rate = 20.0f; }

            rate *= deltaTime;
            input *= rate;
            containmentFieldInput *= rate;

            if (input.LengthSquared() > 0)
            {
                LastUser = Character.Controlled;
                unsentChanges = true;
                if (input.X != 0.0f && GUIScrollBar.DraggingBar != FeedingRateScrollBar)
                {
                    TargetFeedingRate = MathHelper.Clamp(TargetFeedingRate + input.X, 0.0f, 100.0f);
                    FeedingRateScrollBar.BarScroll += input.X / 100.0f;
                }
                if (input.Y != 0.0f && GUIScrollBar.DraggingBar != ContainmentRateScrollBar)
                {
                    TargetContainmentRate = MathHelper.Clamp(TargetContainmentRate + input.Y, 0.0f, 100.0f);
                    ContainmentRateScrollBar.BarScroll += input.Y / 100.0f;
                }
            }
            if (containmentFieldInput.LengthSquared() > 0)
            {
                LastUser = Character.Controlled;
                unsentChanges = true;
                if (containmentFieldInput.X != 0.0f && GUIScrollBar.DraggingBar != ContainmentFieldAmplitudeScrollBar)
                {
                    ContainmentFieldAmplitudeScrollBar.BarScroll += containmentFieldInput.X / 100.0f;
                    containmentFieldAmplitude = ContainmentFieldAmplitudeScrollBar.BarScrollValue;
                }
                if (containmentFieldInput.Y != 0.0f && GUIScrollBar.DraggingBar != ContainmentFieldOffsetScrollBar)
                {
                    //containmentFieldAmplitude = MathHelper.Clamp(TargetContainmentRate + input.Y, 0.0f, 100.0f);
                    ContainmentFieldOffsetScrollBar.BarScroll += containmentFieldInput.Y / 100.0f;
                    containmentFieldOffset = -ContainmentFieldOffsetScrollBar.BarScrollValue;
                }
            }
        }
    }

    private void DrawMeter(SpriteBatch spriteBatch, GUICustomComponent container, float fill)
    {
        if (meterBar == null) { return; }

        int spacing = 2;

        float meterBarScaleY = container.Rect.Height / (float)meterBar.SourceRect.Height;

        int tileCount = (int)Math.Floor((container.Rect.Width + spacing) / (meterBar.SourceRect.Width * meterBarScaleY + spacing));

        float totalSpacing = spacing * (tileCount - 1);
        float meterBarScaleX = (container.Rect.Width - totalSpacing) / (tileCount * meterBar.SourceRect.Width);

        float meterBarScale = meterBarScaleX;
        Vector2 meterBarPos = new Vector2(container.Rect.Left, container.Rect.Center.Y);

        while (meterBarPos.X < container.Rect.Right)
        {
            float tempRatio = (meterBarPos.X - container.Rect.Left) / container.Rect.Width;
            Color color = meterColors[Math.Clamp((int)(tempRatio * meterColors.Length), 0, meterColors.Length - 1)];

            if (meterBarPos.X + (meterBar.SourceRect.Width) * meterBarScale < container.Rect.Right - container.Rect.Width * (1f - fill))
            {
                meterBar.Draw(spriteBatch, meterBarPos, color: color, scale: meterBarScale);
            }
            else
            {
                meterBar.Draw(spriteBatch, meterBarPos, color: new Color(50, 50, 50), scale: meterBarScale);
            }
            meterBarPos.X += meterBar.SourceRect.Width * meterBarScale + spacing;
        }
    }

    static void UpdateGraph<T>(IList<T> graph, T newValue)
    {
        for (int i = graph.Count - 1; i > 0; i--)
        {
            graph[i] = graph[i - 1];
        }
        graph[0] = newValue;
    }

    private void DrawGraph(IList<float> graph, SpriteBatch spriteBatch, Rectangle rect, float maxVal, float xOffset, Color color, int lineThickness = 5)
    {
        Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
        spriteBatch.End();
        spriteBatch.GraphicsDevice.ScissorRectangle = rect;
        spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);

        float lineWidth = (float)rect.Width / (float)(graph.Count - 2);
        float yScale = (float)rect.Height / maxVal;

        Vector2 prevPoint = new Vector2(rect.Right, rect.Bottom - (graph[1] + (graph[0] - graph[1]) * xOffset) * yScale);

        float currX = rect.Right - ((xOffset - 1.0f) * lineWidth);

        for (int i = 1; i < graph.Count - 1; i++)
        {
            currX -= lineWidth;

            Vector2 newPoint = new Vector2(currX, rect.Bottom - graph[i] * yScale);
            
            if (graphLine?.Texture == null)
            {
                GUI.DrawLine(spriteBatch, prevPoint, newPoint - new Vector2(1.0f, 0), color);
            }
            else
            {
                Color segmentColor = color;
                int containmentFieldIndex = i - (containmentFieldTargetValues.Length - containmentFieldValues.Length);
                if (graph == containmentFieldTargetValues && containmentFieldIndex >= 0 && containmentFieldIndex < containmentFieldValues.Length)
                {
                    segmentColor = ToolBox.GradientLerp(Math.Abs(containmentFieldTargetValues[i] - containmentFieldValues[containmentFieldIndex]) / 50.0f, Color.LightGreen, Color.Green, Color.Yellow, Color.Orange, Color.Red);
                }

                Vector2 dir = Vector2.Normalize(newPoint - prevPoint);
                GUI.DrawLine(spriteBatch, graphLine.Texture, prevPoint - dir, newPoint + dir, segmentColor, 0, lineThickness);
            }

            prevPoint = newPoint;
        }

        Vector2 lastPoint = new Vector2(rect.X,
            rect.Bottom - (graph[graph.Count - 1] + (graph[graph.Count - 2] - graph[graph.Count - 1]) * xOffset) * yScale);

        if (graphLine?.Texture == null)
        {
            GUI.DrawLine(spriteBatch, prevPoint, lastPoint, color);
        }
        else
        {
            GUI.DrawLine(spriteBatch, graphLine.Texture, prevPoint, lastPoint + (lastPoint - prevPoint), color, 0, 5);
        }

        spriteBatch.End();
        spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
        spriteBatch.Begin(SpriteSortMode.Deferred);
    }

    public override void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData? extraData = null)
    {
        msg.WriteBoolean(AutoTemp);
        msg.WriteBoolean(PowerOn);
        msg.WriteRangedSingle(TargetFeedingRate, 0.0f, 100.0f, 8);
        msg.WriteRangedSingle(TargetContainmentRate, 0.0f, 100.0f, 8);
        msg.WriteRangedSingle(containmentFieldAmplitude, MinContainmentFieldAmplitude, MaxContainmentFieldAmplitude, 16);
        msg.WriteRangedSingle(containmentFieldOffset, MinContainmentFieldOffset, MaxContainmentFieldOffset, 16);
        msg.WriteRangedSingle(containmentFieldMatchError, 0.0f, 1.0f, 16);

        lastSentContainmentFieldMatchError = containmentFieldMatchError;

        correctionTimer = CorrectionDelay;
    }

    public override void ClientEventRead(IReadMessage msg, float sendingTime)
    {
        int msgStartPos = msg.BitPosition;

        bool newAutoTemp = msg.ReadBoolean();
        bool newPowerOn = msg.ReadBoolean();
        ushort singularityId = msg.ReadUInt16();
        float newTargetFeedingRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
        float newTargetContainmentRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
        float newContainmentFieldAmplitude = msg.ReadRangedSingle(MinContainmentFieldAmplitude, MaxContainmentFieldAmplitude, 16);
        float newContainmentFieldOffset = msg.ReadRangedSingle(MinContainmentFieldOffset, MaxContainmentFieldOffset, 16);
        float newContainmentFieldTargetAmplitude = msg.ReadRangedSingle(MinContainmentFieldAmplitude, MaxContainmentFieldAmplitude, 16);
        float newContainmentFieldTargetOffset = msg.ReadRangedSingle(MinContainmentFieldOffset, MaxContainmentFieldOffset, 16);
        float newDegreeOfSuccess = msg.ReadRangedSingle(0.0f, 1.0f, 8);

        if (correctionTimer > 0.0f)
        {
            int msgLength = msg.BitPosition - msgStartPos;
            msg.BitPosition = msgStartPos;
            StartDelayedCorrection(msg.ExtractBits(msgLength), sendingTime);
            return;
        }

        AutoTemp = newAutoTemp;
        PowerOn = newPowerOn;
        TargetFeedingRate = newTargetFeedingRate;
        TargetContainmentRate = newTargetContainmentRate;
        containmentFieldAmplitude = newContainmentFieldAmplitude;
        containmentFieldOffset = newContainmentFieldOffset;
        containmentFieldTargetAmplitude = newContainmentFieldTargetAmplitude;
        containmentFieldTargetOffset = newContainmentFieldTargetOffset;
        degreeOfSuccess = newDegreeOfSuccess;

        if (FeedingRateScrollBar != null && Math.Abs(FeedingRateScrollBar.BarScroll - TargetFeedingRate / 100.0f) > 0.01f)
        {
            FeedingRateScrollBar.BarScroll = TargetFeedingRate / 100.0f;
        }
        if (ContainmentRateScrollBar != null && Math.Abs(ContainmentRateScrollBar.BarScroll - TargetContainmentRate / 100.0f) > 0.01f)
        {
            ContainmentRateScrollBar.BarScroll = TargetContainmentRate / 100.0f;
        }
        if (ContainmentFieldAmplitudeScrollBar != null && Math.Abs(ContainmentFieldAmplitudeScrollBar.BarScrollValue - newContainmentFieldAmplitude) > 0.01f)
        {
            ContainmentFieldAmplitudeScrollBar.BarScrollValue = newContainmentFieldAmplitude;
        }
        float containmentFieldSliderValue = -newContainmentFieldOffset;
        if (ContainmentFieldOffsetScrollBar != null && Math.Abs(ContainmentFieldOffsetScrollBar.BarScrollValue - containmentFieldSliderValue) > 0.01f)
        {
            ContainmentFieldOffsetScrollBar.BarScrollValue = containmentFieldSliderValue;
        }
        Singularity = 
            singularityId == Entity.NullEntityID ? 
            null : 
            (Entity.FindEntityByID(singularityId) as Item)?.GetComponent<SingularityComponent>();

        IsActive = true;
    }

    protected override void UpdateUIElementStates()
    {
        if (powerLight != null)
        {
            powerLight.Selected = PowerOn;
        }
        if (AutoTempSwitch != null)
        {
            AutoTempSwitch.Selected = AutoTemp;
            AutoTempSwitch.Enabled = PowerOn;
        }
        if (autoTempLight != null)
        {
            autoTempLight.Selected = AutoTemp && PowerOn;
        }
        TrySetEnabled(ContainmentRateScrollBar, PowerOn);
        TrySetEnabled(ContainmentFieldAmplitudeScrollBar, PowerOn);
        TrySetEnabled(ContainmentFieldOffsetScrollBar, PowerOn);
        //autotemp controls feeding rate, disable the scrollbar if autotemp is on
        TrySetEnabled(FeedingRateScrollBar, PowerOn && !AutoTemp);

        static void TrySetEnabled(GUIScrollBar? scrollbar, bool enabled)
        {
            if (scrollbar != null)
            {
                scrollbar.Enabled = enabled;
            }
        }
    }

    private enum InfographicArrowStyle { Straight, Curved };

    private void CreateInfrographic()
    {
        if (infographic != null || inventoryWindow == null) { return; }
        var dimColor = Color.Lerp(Color.Black, Color.TransparentBlack, 0.25f);
        // Dim reactor interface
        infographic = new GUIFrame(new RectTransform(Vector2.One, GuiFrame.RectTransform))
        {
            CanBeFocused = false,
            Color = dimColor
        };
        // Dim inventory window
        new GUIFrame(new RectTransform(inventoryWindow.Rect.Size, infographic.RectTransform) { AbsoluteOffset = inventoryWindow.Rect.Location - GuiFrame.Rect.Location }, color: dimColor)
        {
            CanBeFocused = false
        };
        int arrowSize = (int)(70 * GUI.Scale);
        var arrows = new Dictionary<string, GUIImage>()
            {
                { "fuelslots", CreateArrow(InfographicArrowStyle.Curved, inventoryWindow, Anchor.TopLeft, Pivot.TopRight, SpriteEffects.FlipVertically) },
                { "power", CreateArrow(InfographicArrowStyle.Straight, PowerButton, Anchor.BottomCenter, Pivot.TopCenter) },
                { "feedingrate", CreateArrow(InfographicArrowStyle.Straight, FeedingRateScrollBar, Anchor.TopCenter, Pivot.BottomCenter, rotationDegrees: 180f) },
                { "containmentrate", CreateArrow(InfographicArrowStyle.Straight, ContainmentRateScrollBar, Anchor.TopCenter, Pivot.BottomCenter, rotationDegrees: 180f) },
                { "containmentfield", CreateArrow(InfographicArrowStyle.Straight, ContainmentFieldOffsetScrollBar, Anchor.CenterRight, Pivot.CenterLeft, rotationDegrees: 270) },
                { "singularitysize", CreateArrow(InfographicArrowStyle.Straight, sizeText, Anchor.BottomCenter, Pivot.TopCenter) },
                { "stabilizers", CreateArrow(InfographicArrowStyle.Straight, warningButtons["ReactorWarningStabilizerBroken"], Anchor.BottomCenter, Pivot.TopCenter) }
            };
        new GUICustomComponent(new RectTransform(Vector2.One, infographic.RectTransform),
            onDraw: (sb, c) =>
            {
                DrawToolTip("feedingrate", Anchor.TopCenter, Pivot.BottomRight, arrows["feedingrate"]);
                DrawToolTip("containmentrate", Anchor.TopCenter, Pivot.BottomLeft, arrows["containmentrate"]);
                DrawToolTip("power", Anchor.BottomCenter, Pivot.TopCenter, arrows["power"]);
                DrawToolTip("containmentfield", Anchor.CenterRight, Pivot.CenterLeft, arrows["containmentfield"]);
                DrawToolTip("singularitysize", Anchor.BottomCenter, Pivot.TopCenter, arrows["singularitysize"]);
                DrawToolTip("stabilizers", Anchor.BottomCenter, Pivot.TopCenter, arrows["stabilizers"]);

                void DrawToolTip(string textTag, Anchor anchor, Pivot pivot, GUIComponent? targetComponent)
                {
                    if (targetComponent == null) { return; }
                    GUIComponent.DrawToolTip(sb,
                        RichString.Rich(TextManager.Get($"infographic.reactor.{textTag}")),
                        targetComponent.Rect,
                        anchor: anchor,
                        pivot: pivot);
                }
            })
        {
            CanBeFocused = false
        };
        var closeButtonRt = new RectTransform(new Point(200, 50).Multiply(GUI.Scale), infographic.RectTransform, Anchor.TopRight, Pivot.BottomRight)
        {
            AbsoluteOffset = new Point(0, -50).Multiply(GUI.Scale)
        };
        new GUIButton(closeButtonRt, TextManager.Get("closeinfographic"))
        {
            UserData = UIHighlightAction.ElementId.CloseButton,
            OnClicked = (_, _) =>
            {
                CloseInfographic(Character.Controlled);
                return true;
            }
        };
        item.OnDeselect += CloseInfographic;

        GUIImage CreateArrow(InfographicArrowStyle arrowStyle, GUIComponent? parent, Anchor anchor, Pivot pivot, SpriteEffects spriteEffects = SpriteEffects.None, float rotationDegrees = 0f, Point? additionalOffset = null)
        {
            Point offset = parent == null ?
                Point.Zero :
                (additionalOffset ?? Point.Zero) + RectTransform.CalculateAnchorPoint(anchor, parent.Rect) - GuiFrame.Rect.Location;
            var rt = new RectTransform(new Point(arrowSize), infographic.RectTransform, pivot: pivot)
            {
                AbsoluteOffset = offset
            };
            string style = arrowStyle == InfographicArrowStyle.Straight ? "InfographicArrow" : "InfographicArrowCurved";
            return new GUIImage(rt, style)
            {
                CanBeFocused = false,
                Rotation = MathHelper.ToRadians(rotationDegrees),
                SpriteEffects = spriteEffects
            };
        }

        void CloseInfographic(Character character)
        {
            if (character != Character.Controlled) { return; }
            GuiFrame.RemoveChild(infographic);
            infographic = null;
            item.OnDeselect -= CloseInfographic;
        }
    }
}
