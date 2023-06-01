using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

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

            var saveFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), saveList.Content.RectTransform) { MinSize = new Point(0, 45) }, style: "ListBoxElement")
            {
                UserData = saveInfo
            };

            var nameText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform), Path.GetFileNameWithoutExtension(saveInfo.FilePath), 
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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform, Anchor.BottomLeft),
                text: saveInfo.SubmarineName, font: GUIStyle.SmallFont)
            {
                CanBeFocused = false,
                UserData = saveInfo.FilePath
            };

            string saveTimeStr = string.Empty;
            if (saveInfo.SaveTime.TryUnwrap(out var time))
            {
                saveTimeStr = time.ToLocalUserString();
            }
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), saveFrame.RectTransform),
                text: saveTimeStr, textAlignment: Alignment.Right, font: GUIStyle.SmallFont)
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
            public SettingValue<StartingBalanceAmount> StartingFunds;
            public SettingValue<GameDifficulty> Difficulty;
            public SettingValue<Identifier> StartItemSet;

            public readonly CampaignSettings CreateSettings()
            {
                return new CampaignSettings(element: null)
                {
                    PresetName = SelectedPreset.GetValue(),
                    TutorialEnabled = TutorialEnabled.GetValue(),
                    RadiationEnabled = RadiationEnabled.GetValue(),
                    MaxMissionCount = MaxMissionCount.GetValue(),
                    StartingBalanceAmount = StartingFunds.GetValue(),
                    Difficulty = Difficulty.GetValue(),
                    StartItemSet = StartItemSet.GetValue()
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

            GUILayoutGroup presetDropdownLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, verticalSize), parent.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), presetDropdownLayout.RectTransform), TextManager.Get("campaignsettingpreset"));
            GUIDropDown presetDropdown = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1f), presetDropdownLayout.RectTransform), elementCount: CampaignModePresets.List.Length + 1);
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

            SettingValue<bool> tutorialEnabled = isSinglePlayer ?
                CreateTickbox(settingsList.Content, TextManager.Get("CampaignOption.EnableTutorial"), TextManager.Get("campaignoption.enabletutorial.tooltip"), prevSettings.TutorialEnabled, verticalSize, OnValuesChanged) :
                new SettingValue<bool>(static () => false, static _ => { });
            SettingValue<bool> radiationEnabled = CreateTickbox(settingsList.Content, TextManager.Get("CampaignOption.EnableRadiation"), TextManager.Get("campaignoption.enableradiation.tooltip"), prevSettings.RadiationEnabled, verticalSize, OnValuesChanged);

            ImmutableArray<SettingCarouselElement<Identifier>> startingSetOptions = StartItemSet.Sets.OrderBy(s => s.Order).Select(set => new SettingCarouselElement<Identifier>(set.Identifier, $"startitemset.{set.Identifier}")).ToImmutableArray();
            SettingCarouselElement<Identifier> prevStartingSet = startingSetOptions.FirstOrNull(element => element.Value == prevSettings.StartItemSet) ?? startingSetOptions[1];
            SettingValue<Identifier> startingSetInput = CreateSelectionCarousel(settingsList.Content, TextManager.Get("startitemset"), TextManager.Get("startitemsettooltip"), prevStartingSet, verticalSize, startingSetOptions, OnValuesChanged);

            ImmutableArray<SettingCarouselElement<StartingBalanceAmount>> fundOptions = ImmutableArray.Create(
                new SettingCarouselElement<StartingBalanceAmount>(StartingBalanceAmount.Low, "startingfunds.low"),
                new SettingCarouselElement<StartingBalanceAmount>(StartingBalanceAmount.Medium, "startingfunds.medium"),
                new SettingCarouselElement<StartingBalanceAmount>(StartingBalanceAmount.High, "startingfunds.high")
            );

            SettingCarouselElement<StartingBalanceAmount> prevStartingFund = fundOptions.FirstOrNull(element => element.Value == prevSettings.StartingBalanceAmount) ?? fundOptions[1];
            SettingValue<StartingBalanceAmount> startingFundsInput = CreateSelectionCarousel(settingsList.Content, TextManager.Get("startingfundsdescription"), TextManager.Get("startingfundstooltip"), prevStartingFund, verticalSize, fundOptions, OnValuesChanged);

            ImmutableArray<SettingCarouselElement<GameDifficulty>> difficultyOptions = ImmutableArray.Create(
                new SettingCarouselElement<GameDifficulty>(GameDifficulty.Easy, "difficulty.easy"),
                new SettingCarouselElement<GameDifficulty>(GameDifficulty.Medium, "difficulty.medium"),
                new SettingCarouselElement<GameDifficulty>(GameDifficulty.Hard, "difficulty.hard"),
                new SettingCarouselElement<GameDifficulty>(GameDifficulty.Hellish, "difficulty.hellish", isHidden: true)
            );

            SettingCarouselElement<GameDifficulty> prevDifficulty = difficultyOptions.FirstOrNull(element => element.Value == prevSettings.Difficulty) ?? difficultyOptions[1];
            SettingValue<GameDifficulty> difficultyInput = CreateSelectionCarousel(settingsList.Content, TextManager.Get("leveldifficulty"), TextManager.Get("leveldifficultyexplanation"), prevDifficulty, verticalSize, difficultyOptions, OnValuesChanged);

            SettingValue<int> maxMissionCountInput = CreateGUINumberInputCarousel(settingsList.Content, TextManager.Get("maxmissioncount"), TextManager.Get("maxmissioncounttooltip"),
                prevSettings.MaxMissionCount,
                valueStep: 1, minValue: CampaignSettings.MinMissionCountLimit, maxValue: CampaignSettings.MaxMissionCountLimit,
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
                difficultyInput.SetValue(settings.Difficulty);
                startingSetInput.SetValue(settings.StartItemSet);
                loadingPreset = false;
                return true;
            };

            void OnValuesChanged()
            {
                if (loadingPreset) { return; }
                presetDropdown.Select(0);
            }

            return new CampaignSettingElements
            {
                SelectedPreset = presetValue,
                TutorialEnabled = tutorialEnabled,
                RadiationEnabled = radiationEnabled,
                MaxMissionCount = maxMissionCountInput,
                StartingFunds = startingFundsInput,
                Difficulty = difficultyInput,
                StartItemSet = startingSetInput
            };

            // Create a number input with plus and minus buttons because for some reason the default GUINumberInput buttons don't work when in a GUIMessageBox
            static SettingValue<int> CreateGUINumberInputCarousel(GUIComponent parent, LocalizedString description, LocalizedString tooltip, int defaultValue, int valueStep, int minValue, int maxValue, float verticalSize, Action onChanged)
            {
                GUILayoutGroup inputContainer = CreateSettingBase(parent, description, tooltip, horizontalSize: 0.55f, verticalSize: verticalSize);

                GUIButton minusButton = new GUIButton(new RectTransform(Vector2.One, inputContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIMinusButton", textAlignment: Alignment.Center)
                {
                    ClickSound = GUISoundType.Decrease,
                    UserData = -valueStep
                };
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(Vector2.One, inputContainer.RectTransform, Anchor.Center), NumberType.Int, textAlignment: Alignment.Center, style: "GUITextBox",
                    hidePlusMinusButtons: true)
                {
                    IntValue = defaultValue,
                    MinValueInt = minValue,
                    MaxValueInt = maxValue
                };
                inputContainer.RectTransform.Parent.MinSize = new Point(0, numberInput.RectTransform.MinSize.Y);
                GUIButton plusButton = new GUIButton(new RectTransform(Vector2.One, inputContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIPlusButton", textAlignment: Alignment.Center)
                {
                    ClickSound = GUISoundType.Increase,
                    UserData = valueStep
                };

                minusButton.OnClicked = plusButton.OnClicked = ChangeValue;

                numberInput.OnValueChanged += _ => onChanged();

                bool ChangeValue(GUIButton btn, object userData)
                {
                    if (userData is not int change) { return false; }

                    numberInput.IntValue += change;
                    return true;
                }

                return new SettingValue<int>(() => numberInput.IntValue, i => numberInput.IntValue = i);
            }

            static SettingValue<T> CreateSelectionCarousel<T>(GUIComponent parent, LocalizedString description, LocalizedString tooltip, SettingCarouselElement<T> defaultValue, float verticalSize,
                                                               ImmutableArray<SettingCarouselElement<T>> options, Action onChanged)
            {
                GUILayoutGroup inputContainer = CreateSettingBase(parent, description, tooltip, horizontalSize: 0.55f, verticalSize: verticalSize);

                GUIButton minusButton = new GUIButton(new RectTransform(Vector2.One, inputContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIButtonToggleLeft", textAlignment: Alignment.Center) { UserData = -1 };
                GUIFrame inputFrame = new GUIFrame(new RectTransform(Vector2.One, inputContainer.RectTransform), style: null);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(Vector2.One, inputFrame.RectTransform, Anchor.Center), NumberType.Int, textAlignment: Alignment.Center, style: "GUITextBox", hidePlusMinusButtons: true)
                {
                    IntValue = options.IndexOf(defaultValue),
                    MinValueInt = 0,
                    MaxValueInt = options.Length,
                    Visible = false
                };
                inputContainer.RectTransform.Parent.MinSize = new Point(0, numberInput.RectTransform.MinSize.Y);
                GUITextBox inputLabel = new GUITextBox(new RectTransform(Vector2.One, inputFrame.RectTransform, Anchor.Center), text: defaultValue.Label.Value, textAlignment: Alignment.Center, createPenIcon: false)
                {
                    CanBeFocused = false
                };

                GUIButton plusButton = new GUIButton(new RectTransform(Vector2.One, inputContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIButtonToggleRight", textAlignment: Alignment.Center) { UserData = 1 };

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

                return new SettingValue<T>(() => options[numberInput.IntValue].Value, t => SetValue(options.IndexOf(e => Equals(e.Value, t))));
            }

            static SettingValue<bool> CreateTickbox(GUIComponent parent, LocalizedString description, LocalizedString tooltip, bool defaultValue, float verticalSize, Action onChanged)
            {
                GUILayoutGroup inputContainer = CreateSettingBase(parent, description, tooltip, 0.7f, verticalSize);
                GUILayoutGroup tickboxContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1.0f), inputContainer.RectTransform), childAnchor: Anchor.Center);
                GUITickBox tickBox = new GUITickBox(new RectTransform(Vector2.One, tickboxContainer.RectTransform), string.Empty)
                {
                    Selected = defaultValue,
                    ToolTip = tooltip
                };
                tickBox.Box.IgnoreLayoutGroups = true;
                tickBox.Box.RectTransform.SetPosition(Anchor.CenterRight);
                inputContainer.RectTransform.Parent.MinSize = new Point(0, tickBox.RectTransform.MinSize.Y);

                tickBox.OnSelected += _ =>
                {
                    onChanged();
                    return true;
                };

                return new SettingValue<bool>(() => tickBox.Selected, b => tickBox.Selected = b);
            }

            static GUILayoutGroup CreateSettingBase(GUIComponent parent, LocalizedString description, LocalizedString tooltip, float horizontalSize, float verticalSize)
            {
                GUILayoutGroup settingHolder = new GUILayoutGroup(new RectTransform(new Vector2(1f, verticalSize), parent.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
                GUITextBlock descriptionBlock = new GUITextBlock(new RectTransform(new Vector2(horizontalSize, 1f), settingHolder.RectTransform), description, font: parent.Rect.Width < 320 ? GUIStyle.SmallFont : GUIStyle.Font,  wrap: true) { ToolTip = tooltip };
                GUILayoutGroup inputContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f - horizontalSize, 0.8f), settingHolder.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    RelativeSpacing = 0.05f,
                    Stretch = true
                };
                inputContainer.RectTransform.IsFixedSize = true;
                settingHolder.RectTransform.MinSize = new Point(0, (int)descriptionBlock.TextSize.Y);
                return inputContainer;
            }
        }

        public abstract void UpdateLoadMenu(IEnumerable<CampaignMode.SaveInfo> saveFiles = null);

        protected bool DeleteSave(GUIButton button, object obj)
        {
            if (obj is not CampaignMode.SaveInfo saveInfo) { return false; }

            var header = TextManager.Get("deletedialoglabel");
            var body = TextManager.GetWithVariable("deletedialogquestion", "[file]", Path.GetFileNameWithoutExtension(saveInfo.FilePath));

            EventEditorScreen.AskForConfirmation(header, body, () =>
            {
                SaveUtil.DeleteSave(saveInfo.FilePath);
                prevSaveFiles?.RemoveAll(s => s.FilePath == saveInfo.FilePath);
                UpdateLoadMenu(prevSaveFiles.ToList());
                return true;
            });

            return true;
        }
    }
}