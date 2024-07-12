using Barotrauma.Extensions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    abstract class CampaignSetupUI
    {
        protected readonly GUIComponent newGameContainer, loadGameContainer;

        protected GUIListBox saveList;

        protected GUITextBox saveNameBox, seedBox;

        protected GUIButton loadGameButton;

        public Action<SubmarineInfo, string, string, CampaignSettings> StartNewGame;
        public Action<string> LoadGame;

        protected enum CategoryFilter { All = 0, Vanilla = 1, Custom = 2 }
        protected CategoryFilter subFilter = CategoryFilter.All;

        public GUIButton StartButton
        {
            get;
            protected set;
        }

        public GUITextBlock InitialMoneyText
        {
            get;
            protected set;
        }

        public GUIButton CampaignCustomizeButton { get; set; }
        public GUIMessageBox CampaignCustomizeSettings { get; set; }

        public CampaignSetupUI(GUIComponent newGameContainer, GUIComponent loadGameContainer)
        {
            this.newGameContainer = newGameContainer;
            this.loadGameContainer = loadGameContainer;
        }

        protected List<CampaignMode.SaveInfo> prevSaveFiles;
        protected GUIComponent CreateSaveElement(CampaignMode.SaveInfo saveInfo)
        {
            if (string.IsNullOrEmpty(saveInfo.FilePath))
            {
                DebugConsole.AddWarning("Error when updating campaign load menu: path to a save file was empty.\n" + Environment.StackTrace);
                return null;
            }

            var saveFrame = new GUIFrame(
                new RectTransform(new Vector2(1.0f, 0.1f), saveList.Content.RectTransform) { MinSize = new Point(0, 45) },
                style: "ListBoxElement")
            {
                UserData = saveInfo
            };

            var nameText = new GUITextBlock(
                new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform),
                Path.GetFileNameWithoutExtension(saveInfo.FilePath), 
                textColor: GUIStyle.TextColorBright)
            {
                CanBeFocused = false
            };

            if (saveInfo.EnabledContentPackageNames != null && saveInfo.EnabledContentPackageNames.Any())
            {
                if (!GameSession.IsCompatibleWithEnabledContentPackages(saveInfo.EnabledContentPackageNames, out LocalizedString errorMsg))
                {
                    nameText.TextColor = GUIStyle.Red;
                    saveFrame.ToolTip = string.Join("\n", errorMsg, TextManager.Get("campaignmode.contentpackagemismatchwarning"));
                }
            }

            prevSaveFiles ??= new List<CampaignMode.SaveInfo>();
            prevSaveFiles.Add(saveInfo);

            new GUITextBlock(
                new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform, Anchor.BottomLeft),
                text: saveInfo.SubmarineName,
                font: GUIStyle.SmallFont)
            {
                CanBeFocused = false,
                UserData = saveInfo.FilePath
            };

            string saveTimeStr = string.Empty;
            if (saveInfo.SaveTime.TryUnwrap(out var time))
            {
                saveTimeStr = time.ToLocalUserString();
            }
            new GUITextBlock(
                new RectTransform(new Vector2(1.0f, 1.0f), saveFrame.RectTransform),
                text: saveTimeStr,
                textAlignment: Alignment.Right,
                font: GUIStyle.SmallFont)
            {
                CanBeFocused = false,
                UserData = saveInfo.FilePath
            };

            return saveFrame;
        }

        protected void SortSaveList()
        {
            saveList.Content.RectTransform.SortChildren((c1, c2) =>
            {
                if (c1.GUIComponent.UserData is not CampaignMode.SaveInfo file1
                    || c2.GUIComponent.UserData is not CampaignMode.SaveInfo file2)
                {
                    return 0;
                }

                if (!file1.SaveTime.TryUnwrap(out var file1WriteTime)
                    || !file2.SaveTime.TryUnwrap(out var file2WriteTime))
                {
                    return 0;
                }
                
                return file2WriteTime.CompareTo(file1WriteTime);
            });
        }
        
        public struct CampaignSettingElements
        {
            public SettingValue<string> SelectedPreset;
            public SettingValue<bool> TutorialEnabled;
            public SettingValue<bool> RadiationEnabled;
            public SettingValue<int> MaxMissionCount;
            public SettingValue<StartingBalanceAmountOption> StartingFunds;
            public SettingValue<WorldHostilityOption> WorldHostility;
            public SettingValue<Identifier> StartItemSet;
            public SettingValue<float> CrewVitalityMultiplier;
            public SettingValue<float> NonCrewVitalityMultiplier;
            public SettingValue<float> OxygenMultiplier;
            public SettingValue<float> FuelMultiplier;
            public SettingValue<float> MissionRewardMultiplier;
            public SettingValue<float> ShopPriceMultiplier;
            public SettingValue<float> ShipyardPriceMultiplier;
            public SettingValue<float> RepairFailMultiplier;
            public SettingValue<PatdownProbabilityOption> PatdownProbability;
            public SettingValue<bool> ShowHuskWarning;

            public readonly CampaignSettings CreateSettings()
            {
                return new CampaignSettings(element: null)
                {
                    PresetName = SelectedPreset.GetValue(),
                    TutorialEnabled = TutorialEnabled.GetValue(),
                    RadiationEnabled = RadiationEnabled.GetValue(),
                    MaxMissionCount = MaxMissionCount.GetValue(),
                    StartingBalanceAmount = StartingFunds.GetValue(),
                    WorldHostility = WorldHostility.GetValue(),
                    StartItemSet = StartItemSet.GetValue(),
                    CrewVitalityMultiplier = CrewVitalityMultiplier.GetValue(),
                    NonCrewVitalityMultiplier = NonCrewVitalityMultiplier.GetValue(),
                    OxygenMultiplier = OxygenMultiplier.GetValue(),
                    FuelMultiplier = FuelMultiplier.GetValue(),
                    MissionRewardMultiplier = MissionRewardMultiplier.GetValue(),
                    ShopPriceMultiplier = ShopPriceMultiplier.GetValue(),
                    ShipyardPriceMultiplier = ShipyardPriceMultiplier.GetValue(),
                    RepairFailMultiplier = RepairFailMultiplier.GetValue(),
                    PatdownProbability = PatdownProbability.GetValue(),
                    ShowHuskWarning = ShowHuskWarning.GetValue(),
                };
            }
        }

        public readonly struct SettingValue<T>
        {
            private readonly Func<T> getter;
            private readonly Action<T> setter;

            public T GetValue()
            {
                return getter.Invoke();
            }

            public void SetValue(T value)
            {
                setter.Invoke(value);
            }

            public SettingValue(Func<T> get, Action<T> set)
            {
                getter = get;
                setter = set;
            }
        }

        private readonly struct SettingCarouselElement<T>
        {
            public readonly LocalizedString Label;
            public readonly T Value;
            public readonly bool IsHidden;

            public SettingCarouselElement(T value, string label, bool isHidden = false)
            {
                Value = value;
                Label = TextManager.Get(label).Fallback(label);
                IsHidden = isHidden;
            }
        }

        protected static CampaignSettingElements CreateCampaignSettingList(GUIComponent parent, CampaignSettings prevSettings, bool isSinglePlayer)
        {
            const float verticalSize = 0.14f;

            bool loadingPreset = false;

            GUILayoutGroup presetDropdownLayout = new GUILayoutGroup(
                new RectTransform(new Vector2(1f, verticalSize), parent.RectTransform),
                isHorizontal: true,
                childAnchor: Anchor.CenterLeft);
            new GUITextBlock(
                new RectTransform(new Vector2(0.5f, 1f), presetDropdownLayout.RectTransform),
                TextManager.Get("campaignsettingpreset"));
            GUIDropDown presetDropdown = new GUIDropDown(
                new RectTransform(new Vector2(0.5f, 1f), presetDropdownLayout.RectTransform),
                elementCount: CampaignModePresets.List.Length + 1);
            presetDropdown.AddItem(TextManager.Get("karmapreset.custom"), null);
            presetDropdown.Select(0);

            presetDropdownLayout.RectTransform.MinSize = new Point(0, presetDropdown.Rect.Height);

            foreach (CampaignSettings settings in CampaignModePresets.List)
            {
                string name = settings.PresetName;
                presetDropdown.AddItem(TextManager.Get($"preset.{name}").Fallback(name), settings);

                if (settings.PresetName.Equals(prevSettings.PresetName, StringComparison.OrdinalIgnoreCase))
                {
                    presetDropdown.SelectItem(settings);
                }
            }

            var presetValue = new SettingValue<string>(
                get: () => presetDropdown.SelectedData is CampaignSettings settings ? settings.PresetName : string.Empty,
                set: static _ => { }); // we do not need a way to set this value

            GUIListBox settingsList = new GUIListBox(new RectTransform(new Vector2(1f, 1f - verticalSize), parent.RectTransform))
            {
                Spacing = GUI.IntScale(5)
            };

            // GENERAL CAMPAIGN SETTINGS:

            NetLobbyScreen.CreateSubHeader("campaignsettingcategories.general", settingsList.Content);

            // Tutorial
            SettingValue<bool> tutorialEnabled = isSinglePlayer
                ? CreateTickbox(
                    settingsList.Content,
                    TextManager.Get("CampaignOption.EnableTutorial"),
                    TextManager.Get("campaignoption.enabletutorial.tooltip"),
                    prevSettings.TutorialEnabled,
                    verticalSize,
                    OnValuesChanged)
                : new SettingValue<bool>(static () => false, static _ => { });

            // Jovian radiation
            SettingValue<bool> radiationEnabled = CreateTickbox(
                settingsList.Content,
                TextManager.Get("CampaignOption.EnableRadiation"),
                TextManager.Get("campaignoption.enableradiation.tooltip"),
                prevSettings.RadiationEnabled,
                verticalSize,
                OnValuesChanged);

            // RESOURCE-RELATED CAMPAIGN SETTINGS:

            NetLobbyScreen.CreateSubHeader("campaignsettingcategories.resources", settingsList.Content);

            // Starting set
            ImmutableArray<SettingCarouselElement<Identifier>> startingSetOptions =
                StartItemSet.Sets
                    .OrderBy(s => s.Order)
                    .Select(set => new SettingCarouselElement<Identifier>(
                        set.Identifier,
                        $"startitemset.{set.Identifier}"))
                    .ToImmutableArray();
            SettingCarouselElement<Identifier> prevStartingSet = startingSetOptions
                .FirstOrNull(element => element.Value == prevSettings.StartItemSet)
                ?? startingSetOptions[1];
            SettingValue<Identifier> startingSetInput = CreateSelectionCarousel(
                settingsList.Content,
                TextManager.Get("startitemset"),
                TextManager.Get("startitemsettooltip"),
                prevStartingSet,
                verticalSize,
                startingSetOptions,
                OnValuesChanged);

            // Starting money
            ImmutableArray<SettingCarouselElement<StartingBalanceAmountOption>> fundOptions = ImmutableArray.Create(
                new SettingCarouselElement<StartingBalanceAmountOption>(StartingBalanceAmountOption.Low, "startingfunds.low"),
                new SettingCarouselElement<StartingBalanceAmountOption>(StartingBalanceAmountOption.Medium, "startingfunds.medium"),
                new SettingCarouselElement<StartingBalanceAmountOption>(StartingBalanceAmountOption.High, "startingfunds.high")
            );
            SettingCarouselElement<StartingBalanceAmountOption> prevStartingFund = fundOptions
                .FirstOrNull(element => element.Value == prevSettings.StartingBalanceAmount)
                ?? fundOptions[1];
            SettingValue<StartingBalanceAmountOption> startingFundsInput = CreateSelectionCarousel(
                settingsList.Content,
                TextManager.Get("startingfundsdescription"),
                TextManager.Get("startingfundstooltip"),
                prevStartingFund,
                verticalSize,
                fundOptions,
                OnValuesChanged);

            // Max mission count
            SettingValue<int> maxMissionCountInput = CreateGUIIntegerInputCarousel(
                settingsList.Content,
                TextManager.Get("maxmissioncount"),
                TextManager.Get("maxmissioncounttooltip"),
                prevSettings.MaxMissionCount,
                valueStep: 1,
                minValue: CampaignSettings.MinMissionCountLimit,
                maxValue: CampaignSettings.MaxMissionCountLimit,
                verticalSize,
                OnValuesChanged);

            // Mission reward multiplier
            CampaignSettings.MultiplierSettings rewardMultiplierSettings = CampaignSettings.GetMultiplierSettings("MissionRewardMultiplier");
            SettingValue<float> rewardMultiplier = CreateGUIFloatInputCarousel(
                settingsList.Content,
                TextManager.Get("campaignoption.missionrewardmultiplier"),
                TextManager.Get("campaignoption.missionrewardmultiplier.tooltip"),
                prevSettings.MissionRewardMultiplier,
                valueStep: rewardMultiplierSettings.Step,
                minValue: rewardMultiplierSettings.Min,
                maxValue: rewardMultiplierSettings.Max,
                verticalSize,
                OnValuesChanged);

            // Shop buying prices multiplier
            CampaignSettings.MultiplierSettings shopPriceMultiplierSettings = CampaignSettings.GetMultiplierSettings("ShopPriceMultiplier");
            SettingValue<float> shopPriceMultiplier = CreateGUIFloatInputCarousel(
                settingsList.Content,
                TextManager.Get("campaignoption.shoppricemultiplier"),
                TextManager.Get("campaignoption.shoppricemultiplier.tooltip"),
                prevSettings.ShopPriceMultiplier,
                valueStep: shopPriceMultiplierSettings.Step,
                minValue: shopPriceMultiplierSettings.Min,
                maxValue: shopPriceMultiplierSettings.Max,
                verticalSize,
                OnValuesChanged);

            // Shipyard prices multiplier
            CampaignSettings.MultiplierSettings shipyardPriceMultiplierSettings = CampaignSettings.GetMultiplierSettings("ShipyardPriceMultiplier");
            SettingValue<float> shipyardPriceMultiplier = CreateGUIFloatInputCarousel(
                settingsList.Content,
                TextManager.Get("campaignoption.shipyardpricemultiplier"),
                TextManager.Get("campaignoption.shipyardpricemultiplier.tooltip"),
                prevSettings.ShipyardPriceMultiplier,
                valueStep: shipyardPriceMultiplierSettings.Step,
                minValue: shipyardPriceMultiplierSettings.Min,
                maxValue: shipyardPriceMultiplierSettings.Max,
                verticalSize,
                OnValuesChanged);

            // OVERALL HAZARD-RELATED CAMPAIGN SETTINGS:

            NetLobbyScreen.CreateSubHeader("campaignsettingcategories.hazards", settingsList.Content);

            // World hostility (used to be "Difficulty" or level difficulty)
            ImmutableArray<SettingCarouselElement<WorldHostilityOption>> hostilityOptions = ImmutableArray.Create(
                new SettingCarouselElement<WorldHostilityOption>(WorldHostilityOption.Low, "worldhostility.low"),
                new SettingCarouselElement<WorldHostilityOption>(WorldHostilityOption.Medium, "worldhostility.medium"),
                new SettingCarouselElement<WorldHostilityOption>(WorldHostilityOption.High, "worldhostility.high"),
                new SettingCarouselElement<WorldHostilityOption>(WorldHostilityOption.Hellish, "worldhostility.hellish", isHidden: true)
            );
            SettingCarouselElement<WorldHostilityOption> prevHostility = hostilityOptions
                .FirstOrNull(element => element.Value == prevSettings.WorldHostility)
                ?? hostilityOptions[1];
            SettingValue<WorldHostilityOption> hostilityInput = CreateSelectionCarousel(
                settingsList.Content,
                TextManager.Get("worldhostility"),
                TextManager.Get("worldhostility.tooltip"),
                prevHostility,
                verticalSize,
                hostilityOptions,
                OnValuesChanged);

            // Crew max vitality multiplier
            CampaignSettings.MultiplierSettings crewVitalityMultiplierSettings = CampaignSettings.GetMultiplierSettings("CrewVitalityMultiplier");
            SettingValue<float> crewVitalityMultiplier = CreateGUIFloatInputCarousel(
                settingsList.Content,
                TextManager.Get("campaignoption.maxvitalitymultipliercrew"),
                TextManager.Get("campaignoption.maxvitalitymultipliercrew.tooltip"),
                prevSettings.CrewVitalityMultiplier,
                valueStep: crewVitalityMultiplierSettings.Step,
                minValue: crewVitalityMultiplierSettings.Min,
                maxValue: crewVitalityMultiplierSettings.Max,
                verticalSize,
                OnValuesChanged);

            // Non-crew max vitality multiplier
            CampaignSettings.MultiplierSettings nonCrewVitalityMultiplierSettings = CampaignSettings.GetMultiplierSettings("NonCrewVitalityMultiplier");
            SettingValue<float> nonCrewVitalityMultiplier = CreateGUIFloatInputCarousel(
                settingsList.Content,
                TextManager.Get("campaignoption.maxvitalitymultipliernoncrew"),
                TextManager.Get("campaignoption.maxvitalitymultipliernoncrew.tooltip"),
                prevSettings.NonCrewVitalityMultiplier,
                valueStep: nonCrewVitalityMultiplierSettings.Step,
                minValue: nonCrewVitalityMultiplierSettings.Min,
                maxValue: nonCrewVitalityMultiplierSettings.Max,
                verticalSize,
                OnValuesChanged);

            // Oxygen source multiplier
            CampaignSettings.MultiplierSettings oxygenSourceMultiplierSettings = CampaignSettings.GetMultiplierSettings("OxygenMultiplier");
            SettingValue<float> oxygenMultiplier = CreateGUIFloatInputCarousel(
                settingsList.Content,
                TextManager.Get("campaignoption.oxygensourcemultiplier"),
                TextManager.Get("campaignoption.oxygensourcemultiplier.tooltip"),
                prevSettings.OxygenMultiplier,
                valueStep: oxygenSourceMultiplierSettings.Step,
                minValue: oxygenSourceMultiplierSettings.Min,
                maxValue: oxygenSourceMultiplierSettings.Max,
                verticalSize,
                OnValuesChanged);

            // Reactor fuel multiplier
            CampaignSettings.MultiplierSettings reactorFuelMultiplierSettings = CampaignSettings.GetMultiplierSettings("FuelMultiplier");
            SettingValue<float> fuelMultiplier = CreateGUIFloatInputCarousel(
                settingsList.Content,
                TextManager.Get("campaignoption.reactorfuelmultiplier"),
                TextManager.Get("campaignoption.reactorfuelmultiplier.tooltip"),
                prevSettings.FuelMultiplier,
                valueStep: reactorFuelMultiplierSettings.Step,
                minValue: reactorFuelMultiplierSettings.Min,
                maxValue: reactorFuelMultiplierSettings.Max,
                verticalSize,
                OnValuesChanged);

            // Repair fail effect multiplier
            CampaignSettings.MultiplierSettings repairFailMultiplierSettings = CampaignSettings.GetMultiplierSettings("RepairFailMultiplier");
            SettingValue<float> repairFailMultiplier = CreateGUIFloatInputCarousel(
                settingsList.Content,
                TextManager.Get("campaignoption.repairfailmultiplier"),
                TextManager.Get("campaignoption.repairfailmultiplier.tooltip"),
                prevSettings.RepairFailMultiplier,
                valueStep: repairFailMultiplierSettings.Step,
                minValue: repairFailMultiplierSettings.Min,
                maxValue: repairFailMultiplierSettings.Max,
                verticalSize,
                OnValuesChanged);

            ImmutableArray<SettingCarouselElement<PatdownProbabilityOption>> patdownProbabilityPresets = ImmutableArray.Create(
                new SettingCarouselElement<PatdownProbabilityOption>(PatdownProbabilityOption.Off, "probability.off"),
                new SettingCarouselElement<PatdownProbabilityOption>(PatdownProbabilityOption.Low, "probability.low"),
                new SettingCarouselElement<PatdownProbabilityOption>(PatdownProbabilityOption.Medium, "probability.medium"),
                new SettingCarouselElement<PatdownProbabilityOption>(PatdownProbabilityOption.High, "probability.high")
            );
            SettingCarouselElement<PatdownProbabilityOption> prevPatdownProbability = patdownProbabilityPresets
                .FirstOrNull(element => element.Value == prevSettings.PatdownProbability)
                ?? patdownProbabilityPresets[1]; // middle option
            SettingValue<PatdownProbabilityOption> patdownProbability = CreateSelectionCarousel(
                settingsList.Content,
                TextManager.Get("campaignoption.patdownprobability"),
                TextManager.Get("campaignoption.patdownprobability.tooltip"),
                prevPatdownProbability,
                verticalSize,
                patdownProbabilityPresets,
                OnValuesChanged);

            // Show initial husk warning
            SettingValue<bool> huskWarning = CreateTickbox(
                settingsList.Content,
                TextManager.Get("campaignoption.showhuskwarning"),
                TextManager.Get("campaignoption.showhuskwarning.tooltip"),
                prevSettings.ShowHuskWarning,
                verticalSize,
                OnValuesChanged);

            presetDropdown.OnSelected = (_, o) =>
            {
                if (o is not CampaignSettings settings) { return false; }

                loadingPreset = true;
                tutorialEnabled.SetValue(isSinglePlayer && settings.TutorialEnabled);
                radiationEnabled.SetValue(settings.RadiationEnabled);
                maxMissionCountInput.SetValue(settings.MaxMissionCount);
                startingFundsInput.SetValue(settings.StartingBalanceAmount);
                hostilityInput.SetValue(settings.WorldHostility);
                startingSetInput.SetValue(settings.StartItemSet);
                crewVitalityMultiplier.SetValue(settings.CrewVitalityMultiplier);
                nonCrewVitalityMultiplier.SetValue(settings.NonCrewVitalityMultiplier);
                oxygenMultiplier.SetValue(settings.OxygenMultiplier);
                fuelMultiplier.SetValue(settings.FuelMultiplier);
                rewardMultiplier.SetValue(settings.MissionRewardMultiplier);
                shopPriceMultiplier.SetValue(settings.ShopPriceMultiplier);
                shipyardPriceMultiplier.SetValue(settings.ShipyardPriceMultiplier);
                repairFailMultiplier.SetValue(settings.RepairFailMultiplier);
                patdownProbability.SetValue(settings.PatdownProbability);
                huskWarning.SetValue(settings.ShowHuskWarning);
                loadingPreset = false;
                return true;
            };

            void OnValuesChanged()
            {
                if (loadingPreset) { return; }
                presetDropdown.Select(0); // Switch to the Custom preset if this is an actual user-made change
            }

            return new CampaignSettingElements
            {
                SelectedPreset = presetValue,
                TutorialEnabled = tutorialEnabled,
                RadiationEnabled = radiationEnabled,
                MaxMissionCount = maxMissionCountInput,
                StartingFunds = startingFundsInput,
                WorldHostility = hostilityInput,
                StartItemSet = startingSetInput,
                CrewVitalityMultiplier = crewVitalityMultiplier,
                NonCrewVitalityMultiplier = nonCrewVitalityMultiplier,
                OxygenMultiplier = oxygenMultiplier,
                FuelMultiplier = fuelMultiplier,
                MissionRewardMultiplier = rewardMultiplier,
                ShopPriceMultiplier = shopPriceMultiplier,
                ShipyardPriceMultiplier = shipyardPriceMultiplier,
                RepairFailMultiplier = repairFailMultiplier,
                PatdownProbability = patdownProbability,
                ShowHuskWarning = huskWarning,
            };

            // Create a number input with plus and minus buttons because for some reason
            // the default GUINumberInput buttons don't work when in a GUIMessageBox
            static SettingValue<int> CreateGUIIntegerInputCarousel(
                GUIComponent parent,
                LocalizedString description,
                LocalizedString tooltip,
                int defaultValue,
                int valueStep,
                int minValue,
                int maxValue,
                float verticalSize,
                Action onChanged)
            {
                GUILayoutGroup inputContainer = CreateSettingBase(
                    parent,
                    description,
                    tooltip,
                    horizontalSize: 0.55f,
                    verticalSize: verticalSize);

                GUIButton minusButton = new GUIButton(
                    new RectTransform(Vector2.One, inputContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    style: "GUIMinusButton",
                    textAlignment: Alignment.Center);
                RectTransform numberInputRect = new(Vector2.One, inputContainer.RectTransform, Anchor.Center);
                GUIButton plusButton = new GUIButton(
                    new RectTransform(Vector2.One, inputContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    style: "GUIPlusButton",
                    textAlignment: Alignment.Center);
                GUINumberInput numberInput = new GUINumberInput(
                    numberInputRect,
                    NumberType.Int,
                    textAlignment: Alignment.Center,
                    style: "GUITextBox",
                    buttonVisibility: GUINumberInput.ButtonVisibility.ForceVisible,
                    customPlusMinusButtons: (plusButton, minusButton))
                {
                    IntValue = defaultValue,
                    MinValueInt = minValue,
                    MaxValueInt = maxValue,
                    ValueStep = valueStep,
                    ToolTip = tooltip
                };
                inputContainer.RectTransform.Parent.MinSize = new Point(0, numberInput.RectTransform.MinSize.Y);

                numberInput.OnValueChanged += _ => onChanged();

                return new SettingValue<int>(
                    () => numberInput.IntValue,
                    i => numberInput.IntValue = i);
            }

            static SettingValue<float> CreateGUIFloatInputCarousel(
                GUIComponent parent,
                LocalizedString description,
                LocalizedString tooltip,
                float defaultValue,
                float valueStep,
                float minValue,
                float maxValue,
                float verticalSize,
                Action onChanged)
            {
                GUILayoutGroup inputContainer = CreateSettingBase(
                    parent,
                    description,
                    tooltip,
                    horizontalSize: 0.55f,
                    verticalSize: verticalSize);

                GUIButton minusButton = new GUIButton(
                    new RectTransform(Vector2.One, inputContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    style: "GUIMinusButton",
                    textAlignment: Alignment.Center);
                RectTransform numberInputRect = new(Vector2.One, inputContainer.RectTransform, Anchor.Center);
                GUIButton plusButton = new GUIButton(
                    new RectTransform(Vector2.One, inputContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    style: "GUIPlusButton",
                    textAlignment: Alignment.Center);
                GUINumberInput numberInput = new GUINumberInput(
                    numberInputRect,
                    NumberType.Float,
                    textAlignment: Alignment.Center,
                    style: "GUITextBox",
                    buttonVisibility: GUINumberInput.ButtonVisibility.ForceVisible,
                    customPlusMinusButtons: (plusButton, minusButton))
                {
                    FloatValue = defaultValue,
                    MinValueFloat = minValue,
                    MaxValueFloat = maxValue,
                    ValueStep = valueStep,
                    ToolTip = tooltip
                };
                numberInput.RectTransform.Parent.MinSize = new Point(0, numberInput.RectTransform.MinSize.Y);

                numberInput.OnValueChanged += _ => onChanged();

                return new SettingValue<float>(
                    () => numberInput.FloatValue,
                    i => numberInput.FloatValue = (float)Math.Round(i, 1));
            }

            static SettingValue<T> CreateSelectionCarousel<T>(
                GUIComponent parent,
                LocalizedString description,
                LocalizedString tooltip,
                SettingCarouselElement<T> defaultValue,
                float verticalSize,
                ImmutableArray<SettingCarouselElement<T>> options,
                Action onChanged)
            {
                GUILayoutGroup inputContainer = CreateSettingBase(
                    parent,
                    description,
                    tooltip,
                    horizontalSize: 0.55f,
                    verticalSize: verticalSize);

                GUIButton minusButton = new GUIButton(
                    new RectTransform(Vector2.One, inputContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    style: "GUIButtonToggleLeft",
                    textAlignment: Alignment.Center)
                {
                    UserData = -1
                };
                GUIFrame inputFrame = new GUIFrame(
                    new RectTransform(Vector2.One, inputContainer.RectTransform),
                    style: null);
                GUINumberInput numberInput = new GUINumberInput(
                    new RectTransform(Vector2.One, inputFrame.RectTransform, Anchor.Center),
                    NumberType.Int,
                    textAlignment: Alignment.Center,
                    style: "GUITextBox",
                    buttonVisibility: GUINumberInput.ButtonVisibility.ForceHidden)
                {
                    IntValue = options.IndexOf(defaultValue),
                    MinValueInt = 0,
                    MaxValueInt = options.Length,
                    Visible = false,
                    ToolTip = tooltip
                };
                inputContainer.RectTransform.Parent.MinSize = new Point(0, numberInput.RectTransform.MinSize.Y);
                GUITextBox inputLabel = new GUITextBox(
                    new RectTransform(Vector2.One, inputFrame.RectTransform, Anchor.Center),
                    text: defaultValue.Label.Value,
                    textAlignment: Alignment.Center,
                    createPenIcon: false)
                {
                    CanBeFocused = false
                };

                GUIButton plusButton = new GUIButton(
                    new RectTransform(Vector2.One, inputContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    style: "GUIButtonToggleRight",
                    textAlignment: Alignment.Center)
                {
                    UserData = 1
                };

                minusButton.OnClicked = plusButton.OnClicked = ChangeValue;

                bool ChangeValue(GUIButton btn, object userData)
                {
                    if (userData is not int change) { return false; }

                    int hiddenOptions = 0;

                    for (int i = options.Length - 1; i >= 0; i--)
                    {
                        if (options[i].IsHidden)
                        {
                            hiddenOptions++;
                            continue;
                        }
                        break;
                    }

                    int limit = options.Length - hiddenOptions;

                    if (PlayerInput.IsShiftDown())
                    {
                        limit = options.Length;
                    }

                    int newValue = MathUtils.PositiveModulo(Math.Clamp(numberInput.IntValue + change, min: -1, max: limit), limit);
                    SetValue(newValue);
                    return true;
                }

                numberInput.OnValueChanged += _ => onChanged();

                void SetValue(int value)
                {
                    numberInput.IntValue = value;
                    inputLabel.Text = options[value].Label.Value;
                }

                return new SettingValue<T>(
                    () => options[numberInput.IntValue].Value,
                    t => SetValue(options.IndexOf(e => Equals(e.Value, t)))
                );
            }

            static SettingValue<bool> CreateTickbox(
                GUIComponent parent,
                LocalizedString description,
                LocalizedString tooltip,
                bool defaultValue,
                float verticalSize,
                Action onChanged)
            {
                GUILayoutGroup inputContainer = CreateSettingBase(parent, description, tooltip, 0.625f, verticalSize);
                GUILayoutGroup tickboxContainer = new GUILayoutGroup(
                    new RectTransform(new Vector2(0.375f, 1.0f), inputContainer.RectTransform),
                    childAnchor: Anchor.Center);
                GUITickBox tickBox = new GUITickBox(
                    new RectTransform(Vector2.One, tickboxContainer.RectTransform),
                    string.Empty)
                {
                    Selected = defaultValue,
                    ToolTip = tooltip
                };
                tickBox.Box.IgnoreLayoutGroups = true;
                tickBox.Box.RectTransform.SetPosition(Anchor.CenterLeft);
                inputContainer.RectTransform.Parent.MinSize = new Point(0, tickBox.RectTransform.MinSize.Y);

                tickBox.OnSelected += _ =>
                {
                    onChanged();
                    return true;
                };

                return new SettingValue<bool>(() => tickBox.Selected, b => tickBox.Selected = b);
            }

            static GUILayoutGroup CreateSettingBase(
                GUIComponent parent,
                LocalizedString description,
                LocalizedString tooltip,
                float horizontalSize,
                float verticalSize)
            {
                GUILayoutGroup settingHolder = new GUILayoutGroup(
                    new RectTransform(new Vector2(1f, verticalSize), parent.RectTransform),
                    isHorizontal: true,
                    childAnchor: Anchor.CenterLeft);
                GUITextBlock descriptionBlock = new GUITextBlock(
                    new RectTransform(new Vector2(horizontalSize, 1f), settingHolder.RectTransform),
                    description,
                    font: parent.Rect.Width < 320 ? GUIStyle.SmallFont : GUIStyle.Font,
                    wrap: true)
                {
                    ToolTip = tooltip
                };
                GUILayoutGroup inputContainer = new GUILayoutGroup(
                    new RectTransform(new Vector2(1f - horizontalSize, 0.8f), settingHolder.RectTransform),
                    isHorizontal: true,
                    childAnchor: Anchor.CenterLeft)
                {
                    RelativeSpacing = 0.05f,
                    Stretch = true
                };
                inputContainer.RectTransform.IsFixedSize = true;
                settingHolder.RectTransform.MinSize = new Point(0, (int)descriptionBlock.TextSize.Y);
                return inputContainer;
            }
        }

        public abstract void CreateLoadMenu(IEnumerable<CampaignMode.SaveInfo> saveFiles = null);

        protected bool DeleteSave(GUIButton button, object obj)
        {
            if (obj is not CampaignMode.SaveInfo saveInfo) { return false; }

            var header = TextManager.Get("deletedialoglabel");
            var body = TextManager.GetWithVariable("deletedialogquestion", "[file]", Path.GetFileNameWithoutExtension(saveInfo.FilePath));

            EventEditorScreen.AskForConfirmation(header, body, () =>
            {
                SaveUtil.DeleteSave(saveInfo.FilePath);
                prevSaveFiles?.RemoveAll(s => s.FilePath == saveInfo.FilePath);
                CreateLoadMenu(prevSaveFiles.ToList());
                return true;
            });

            return true;
        }
    }
}