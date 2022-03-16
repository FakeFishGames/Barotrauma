using Barotrauma.Tutorials;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using System.Globalization;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class SinglePlayerCampaignSetupUI : CampaignSetupUI
    {
        private GUIListBox subList;

        protected GUILayoutGroup subPreviewContainer;

        public CharacterInfo.AppearanceCustomizationMenu[] CharacterMenus { get; private set; }

        private GUIButton nextButton;
        private GUILayoutGroup characterInfoColumns;
    
        public SinglePlayerCampaignSetupUI(GUIComponent newGameContainer, GUIComponent loadGameContainer, IEnumerable<SubmarineInfo> submarines, IEnumerable<string> saveFiles = null)
            : base(newGameContainer, loadGameContainer)
        {
            UpdateNewGameMenu(submarines);
            UpdateLoadMenu(saveFiles);
        }

        private int currentPage = 0;
        private GUIListBox pageContainer;

        public void Update()
        {
            float targetScroll =
                (float)currentPage / ((float)pageContainer.Content.CountChildren - 1);

            pageContainer.BarScroll = MathHelper.Lerp(pageContainer.BarScroll, targetScroll, 0.2f);
            if (MathUtils.NearlyEqual(pageContainer.BarScroll, targetScroll, 0.001f))
            {
                pageContainer.BarScroll = targetScroll;
            }

            for (int i = 0; i < CharacterMenus.Length; i++)
            {
                CharacterMenus[i]?.Update();
            }

            pageContainer.HoverCursor = CursorState.Default;
            pageContainer.Content.HoverCursor = CursorState.Default;
        }

        public void SetPage(int pageIndex)
        {
            currentPage = pageIndex;
            for (int i = 0; i < pageContainer.Content.CountChildren; i++)
            {
                var child = pageContainer.Content.GetChild(i);
                child.CanBeFocused = (i == currentPage);
                child.GetAllChildren().ForEach(c =>
                {
                    if (c is GUIDropDown dd)
                    {
                        dd.Dropped = false;
                    }
                    c.CanBeFocused = (i == currentPage);
                });
            }
            var previewListBox = subPreviewContainer.GetAllChildren<GUIListBox>().FirstOrDefault();
            previewListBox?.GetAllChildren()?.ForEach(c =>
            {
                c.CanBeFocused = false;
            });
        }
        
        private void UpdateNewGameMenu(IEnumerable<SubmarineInfo> submarines)
        {
            pageContainer =
                new GUIListBox(new RectTransform(Vector2.One, newGameContainer.RectTransform), style: null, isHorizontal: true)
                {
                    ScrollBarEnabled = false,
                    ScrollBarVisible = false,
                    AllowArrowKeyScroll = false,
                    HoverCursor = CursorState.Default
                };

            GUILayoutGroup createPageLayout()
            {
                var containerItem =
                    new GUIFrame(new RectTransform(Vector2.One, pageContainer.Content.RectTransform), style: null);
                return new GUILayoutGroup(new RectTransform(Vector2.One * 0.95f, containerItem.RectTransform,
                    Anchor.Center));
            }
            
            CreateFirstPage(createPageLayout(), submarines);
            CreateSecondPage(createPageLayout());
            
            pageContainer.RecalculateChildren();
            pageContainer.GetAllChildren().ForEach(c =>
            {
                c.ClampMouseRectToParent = true;
            });
            pageContainer.GetAllChildren<GUIDropDown>().ForEach(dd =>
            {
                dd.ListBox.ClampMouseRectToParent = false;
                dd.ListBox.Content.ClampMouseRectToParent = false;
            });
            SetPage(0);
        }

        private void CreateFirstPage(GUILayoutGroup firstPageLayout, IEnumerable<SubmarineInfo> submarines)
        {
            firstPageLayout.RelativeSpacing = 0.02f;
            
            var columnContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), firstPageLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var leftColumn = new GUILayoutGroup(new RectTransform(Vector2.One, columnContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(1.5f, 1.0f), columnContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            columnContainer.Recalculate();

            // New game left side
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("SaveName"), font: GUIStyle.SubHeadingFont);
            saveNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, string.Empty)
            {
                textFilterFunction = (string str) => { return ToolBox.RemoveInvalidFileNameChars(str); }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("MapSeed"), font: GUIStyle.SubHeadingFont);
            seedBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, ToolBox.RandomSeed(8));
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("SelectedSub"), font: GUIStyle.SubHeadingFont);

            var moddedDropdown = new GUIDropDown(new RectTransform(new Vector2(1f, 0.02f), leftColumn.RectTransform), "", 3);
            moddedDropdown.AddItem(TextManager.Get("clientpermission.all"), CategoryFilter.All);
            moddedDropdown.AddItem(TextManager.Get("servertag.modded.false"), CategoryFilter.Vanilla);
            moddedDropdown.AddItem(TextManager.Get("customrank"), CategoryFilter.Custom);
            moddedDropdown.Select(0);

            var filterContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            
            subList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.65f), leftColumn.RectTransform)) { ScrollBarVisible = true };

            var searchTitle = new GUITextBlock(new RectTransform(new Vector2(0.001f, 1.0f), filterContainer.RectTransform), TextManager.Get("serverlog.filter"), textAlignment: Alignment.CenterLeft, font: GUIStyle.Font);
            var searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 1.0f), filterContainer.RectTransform, Anchor.CenterRight), font: GUIStyle.Font, createClearButton: true);
            filterContainer.RectTransform.MinSize = searchBox.RectTransform.MinSize;
            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = true; };
            searchBox.OnTextChanged += (textBox, text) => { FilterSubs(subList, text); return true; };

            moddedDropdown.OnSelected = (component, data) =>
            {
                searchBox.Text = string.Empty;
                subFilter = (CategoryFilter)data;
                UpdateSubList(SubmarineInfo.SavedSubmarines);
                return true;
            };

            subList.OnSelected = OnSubSelected;

            // New game right side
            subPreviewContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), rightColumn.RectTransform))
            {
                Stretch = true
            };

            var firstPageButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.08f),
                firstPageLayout.RectTransform), childAnchor: Anchor.BottomLeft, isHorizontal: true)
            {
                RelativeSpacing = 0.025f
            };

            InitialMoneyText = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1f), firstPageButtonContainer.RectTransform), "", font: GUIStyle.Font, textColor: GUIStyle.Green, textAlignment: Alignment.CenterLeft)
            {
                TextGetter = () =>
                {
                    int initialMoney = CampaignMode.InitialMoney;
                    if (subList.SelectedData is SubmarineInfo subInfo)
                    {
                        initialMoney -= subInfo.Price;
                    }
                    initialMoney = Math.Max(initialMoney, 0);
                    return TextManager.GetWithVariable("campaignstartingmoney", "[money]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", initialMoney));
                }
            };
            
            CampaignCustomizeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1f), firstPageButtonContainer.RectTransform, Anchor.CenterLeft), TextManager.Get("SettingsButton"))
            {
                OnClicked = (tb, userdata) =>
                {
                    CreateCustomizeWindow();
                    return true;
                }
            };

            nextButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1f), firstPageButtonContainer.RectTransform, Anchor.BottomRight), TextManager.Get("Next"))
            {
                OnClicked = (GUIButton btn, object userData) =>
                {
                    SetPage(1);
                    return false;
                }
            };
            
            var disclaimerBtn = new GUIButton(new RectTransform(new Vector2(1.0f, 0.8f), rightColumn.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(5) }, style: "GUINotificationButton")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (btn, userdata) => { GameMain.Instance.ShowCampaignDisclaimer(); return true; }
            };
            disclaimerBtn.RectTransform.MaxSize = new Point((int)(30 * GUI.Scale));

            columnContainer.Recalculate();
            leftColumn.Recalculate();
            rightColumn.Recalculate();

            if (submarines != null) { UpdateSubList(submarines); }
        }
        
        private void CreateSecondPage(GUILayoutGroup secondPageLayout)
        {
            secondPageLayout.RelativeSpacing = 0.01f;
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.04f), secondPageLayout.RectTransform),
                TextManager.Get("Crew"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.TopLeft);
            
            characterInfoColumns = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.86f), secondPageLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            var secondPageButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.08f),
                secondPageLayout.RectTransform), childAnchor: Anchor.BottomLeft, isHorizontal: true)
            {
                RelativeSpacing = 0.2f
            };

            var backButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1f), secondPageButtonContainer.RectTransform, Anchor.BottomRight), TextManager.Get("Back"))
            {
                OnClicked = (GUIButton btn, object userData) =>
                {
                    SetPage(0);
                    return false;
                }
            };
            
            StartButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1f), secondPageButtonContainer.RectTransform, Anchor.BottomRight), TextManager.Get("StartCampaignButton"))
            {
                OnClicked = FinishSetup
            };
        }
        
        public void RandomizeCrew()
        {
            var characterInfos = new List<(CharacterInfo Info, JobPrefab Job)>();
            foreach (JobPrefab jobPrefab in JobPrefab.Prefabs)
            {
                for (int i = 0; i < jobPrefab.InitialCount; i++)
                {
                    var variant = Rand.Range(0, jobPrefab.Variants);
                    characterInfos.Add((new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: jobPrefab, variant: variant), jobPrefab));
                }
            }
            if (characterInfos.Count == 0)
            {
                DebugConsole.ThrowError($"No starting crew found! If you're using mods, it may be that the mods have overridden the vanilla jobs without specifying which types of characters the starting crew should consist of. If you're the developer of the mod, ensure that you've set the {nameof(JobPrefab.InitialCount)} properties for the custom jobs.");
                DebugConsole.AddWarning("Choosing the first available jobs as the starting crew...");
                foreach (JobPrefab jobPrefab in JobPrefab.Prefabs)
                {
                    var variant = Rand.Range(0, jobPrefab.Variants);
                    characterInfos.Add((new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: jobPrefab, variant: variant), jobPrefab));
                    if (characterInfos.Count >= 3) { break; }
                }
            }
            characterInfos.Sort((a, b) => Math.Sign(b.Job.MinKarma - a.Job.MinKarma));

            characterInfoColumns.ClearChildren();
            CharacterMenus?.ForEach(m => m.Dispose());
            CharacterMenus = new CharacterInfo.AppearanceCustomizationMenu[characterInfos.Count];
            
            for (int i = 0; i < characterInfos.Count; i++)
            {
                var subLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f / characterInfos.Count, 1.0f),
                    characterInfoColumns.RectTransform));

                var (characterInfo, job) = characterInfos[i];

                characterInfo.CreateIcon(new RectTransform(new Vector2(1.0f, 0.275f), subLayout.RectTransform));

                var jobTextContainer =
                    new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), subLayout.RectTransform), style: null);
                var jobText = new GUITextBlock(new RectTransform(Vector2.One, jobTextContainer.RectTransform), job.Name, job.UIColor);

                var characterName = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.1f), subLayout.RectTransform))
                {
                    Text = characterInfo.Name,
                    UserData = "random"
                };
                characterName.OnDeselected += (sender, key) =>
                {
                    if (string.IsNullOrWhiteSpace(sender.Text))
                    {
                        characterInfo.Name = characterInfo.GetRandomName(Rand.RandSync.Unsynced);
                        sender.Text = characterInfo.Name;
                        sender.UserData = "random";
                    }
                    else
                    {
                        characterInfo.Name = sender.Text;
                        sender.UserData = "user";
                    }
                };
                characterName.OnEnterPressed += (sender, text) =>
                {
                    sender.Deselect();
                    return false;
                };
                
                var customizationFrame =
                    new GUIFrame(new RectTransform(new Vector2(1.0f, 0.6f), subLayout.RectTransform), style: null);
                CharacterMenus[i] =
                    new CharacterInfo.AppearanceCustomizationMenu(characterInfo, customizationFrame, hasIcon: false)
                    {
                        OnHeadSwitch = menu =>
                        {
                            if (characterName.UserData is string ud && ud == "random")
                            {
                                characterInfo.Name = characterInfo.GetRandomName(Rand.RandSync.Unsynced);
                                characterName.Text = characterInfo.Name;
                                characterName.UserData = "random";
                            }

                            StealRandomizeButton(menu, jobTextContainer);
                        }
                    };
                StealRandomizeButton(CharacterMenus[i], jobTextContainer);
            }
        }
        
        private void CreateCustomizeWindow()
        {
            CampaignCustomizeSettings = new GUIMessageBox("", "", new LocalizedString[] { TextManager.Get("OK") }, new Vector2(0.2f, 0.2f));
            CampaignCustomizeSettings.Buttons[0].OnClicked += CampaignCustomizeSettings.Close;

            CampaignSettingsContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), CampaignCustomizeSettings.Content.RectTransform, Anchor.TopCenter))
            {
                RelativeSpacing = 0.1f
            };

            if (MapGenerationParams.Instance.RadiationParams != null)
            {
                bool prevRadiationToggleEnabled = EnableRadiationToggle?.Selected ?? true;
                EnableRadiationToggle = new GUITickBox(new RectTransform(new Vector2(0.3f, 0.3f), CampaignSettingsContent.RectTransform), TextManager.Get("CampaignOption.EnableRadiation"), font: GUIStyle.Font)
                {
                    Selected = prevRadiationToggleEnabled,
                    ToolTip = TextManager.Get("campaignoption.enableradiation.tooltip")
                };
            }
            var maxMissionCountSettingHolder = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.3f), CampaignSettingsContent.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft) 
            { 
                Stretch = true,
                ToolTip = TextManager.Get("maxmissioncounttooltip")
            };
            var maxMissionCountDescription = new GUITextBlock(new RectTransform(new Vector2(0.7f, 0.0f), maxMissionCountSettingHolder.RectTransform), TextManager.Get("maxmissioncount", "missions"), wrap: true);
            var maxMissionCountContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), maxMissionCountSettingHolder.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft) { RelativeSpacing = 0.05f, Stretch = true };
            var maxMissionCountButtons = new GUIButton[2];
            maxMissionCountButtons[0] = new GUIButton(new RectTransform(new Vector2(0.15f, 0.8f), maxMissionCountContainer.RectTransform), style: "GUIButtonToggleLeft")
            {
                OnClicked = (button, obj) =>
                {
                    MaxMissionCountText.Text = Math.Clamp(Int32.Parse(MaxMissionCountText.Text.SanitizedValue) - 1, CampaignSettings.MinMissionCountLimit, CampaignSettings.MaxMissionCountLimit).ToString();
                    return true;
                }
            };

            RichString prevMaxMissionCountText = MaxMissionCountText?.Text ?? CampaignSettings.DefaultMaxMissionCount.ToString();
            MaxMissionCountText = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), maxMissionCountContainer.RectTransform), prevMaxMissionCountText, textAlignment: Alignment.Center, style: "GUITextBox");
            maxMissionCountButtons[1] = new GUIButton(new RectTransform(new Vector2(0.15f, 0.8f), maxMissionCountContainer.RectTransform), style: "GUIButtonToggleRight")
            {
                OnClicked = (button, obj) =>
                {
                    MaxMissionCountText.Text = Math.Clamp(Int32.Parse(MaxMissionCountText.Text.SanitizedValue) + 1, CampaignSettings.MinMissionCountLimit, CampaignSettings.MaxMissionCountLimit).ToString();
                    return true;
                }
            };
            maxMissionCountContainer.Children.ForEach(c => c.ToolTip = maxMissionCountSettingHolder.ToolTip);
        }

        private static void StealRandomizeButton(CharacterInfo.AppearanceCustomizationMenu menu, GUIComponent parent)
        {
            //This is just stupid
            var randomizeButton = menu.RandomizeButton;
            var oldButton = parent.GetChild<GUIButton>();
            parent.RemoveChild(oldButton);
            randomizeButton.RectTransform.Parent = parent.RectTransform;
            randomizeButton.RectTransform.RelativeSize = Vector2.One * 1.3f;
        }
        
        private bool FinishSetup(GUIButton btn, object userdata)
        {
            if (string.IsNullOrWhiteSpace(saveNameBox.Text))
            {
                saveNameBox.Flash(GUIStyle.Red);
                return false;
            }
            
            SubmarineInfo selectedSub = null;

            if (!(subList.SelectedData is SubmarineInfo)) { return false; }
            selectedSub = subList.SelectedData as SubmarineInfo;
            
            if (selectedSub.SubmarineClass == SubmarineClass.Undefined)
            {
                new GUIMessageBox(TextManager.Get("error"), TextManager.Get("undefinedsubmarineselected"));
                return false;
            }

            if (string.IsNullOrEmpty(selectedSub.MD5Hash.StringRepresentation))
            {
                ((GUITextBlock)subList.SelectedComponent).TextColor = Color.DarkRed * 0.8f;
                subList.SelectedComponent.CanBeFocused = false;
                subList.Deselect();
                return false;
            }

            string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Singleplayer, saveNameBox.Text);
            bool hasRequiredContentPackages = selectedSub.RequiredContentPackagesInstalled;

            CampaignSettings settings = new CampaignSettings();
            settings.RadiationEnabled = EnableRadiationToggle?.Selected ?? false;
            if (MaxMissionCountText != null && Int32.TryParse(MaxMissionCountText.Text.SanitizedValue, out int missionCount))
            {
                settings.MaxMissionCount = missionCount;
            }
            else
            {
                settings.MaxMissionCount = CampaignSettings.DefaultMaxMissionCount;
            }

            if (selectedSub.HasTag(SubmarineTag.Shuttle) || !hasRequiredContentPackages)
            {
                if (!hasRequiredContentPackages)
                {
                    var msgBox = new GUIMessageBox(TextManager.Get("ContentPackageMismatch"),
                        TextManager.GetWithVariable("ContentPackageMismatchWarning", "[requiredcontentpackages]", string.Join(", ", selectedSub.RequiredContentPackages)),
                        new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });
                    
                    msgBox.Buttons[0].OnClicked = msgBox.Close;
                    msgBox.Buttons[0].OnClicked += (button, obj) =>
                    {
                        if (GUIMessageBox.MessageBoxes.Count == 0)
                        {
                            StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text, settings);
                        }
                        return true;
                    };

                    msgBox.Buttons[1].OnClicked = msgBox.Close;
                }

                if (selectedSub.HasTag(SubmarineTag.Shuttle))
                {
                    var msgBox = new GUIMessageBox(TextManager.Get("ShuttleSelected"),
                        TextManager.Get("ShuttleWarning"),
                        new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });

                    msgBox.Buttons[0].OnClicked = (button, obj) => 
                    {
                        StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text, settings);
                        return true;
                    };
                    msgBox.Buttons[0].OnClicked += msgBox.Close;

                    msgBox.Buttons[1].OnClicked = msgBox.Close;
                    return false;
                }
            }
            else
            {
                StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text, settings);
            }

            return true;
        }
        
        public void RandomizeSeed()
        {
            seedBox.Text = ToolBox.RandomSeed(8);
        }

        private void FilterSubs(GUIListBox subList, string filter)
        {
            foreach (GUIComponent child in subList.Content.Children)
            {
                var sub = child.UserData as SubmarineInfo;
                if (sub == null) { return; }
                child.Visible = string.IsNullOrEmpty(filter) || sub.DisplayName.Contains(filter.ToLower(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private bool OnSubSelected(GUIComponent component, object obj)
        {
            if (subPreviewContainer == null) { return false; }
            (subPreviewContainer.Parent as GUILayoutGroup)?.Recalculate();
            subPreviewContainer.ClearChildren();

            if (!(obj is SubmarineInfo sub)) { return true; }
#if !DEBUG
            if (sub.Price > CampaignMode.InitialMoney && !GameMain.DebugDraw) 
            {
                SetPage(0);
                nextButton.Enabled = false;
                return false; 
            }
#endif
            nextButton.Enabled = true;
            sub.CreatePreviewWindow(subPreviewContainer);
            return true;
        }
        
        public void CreateDefaultSaveName()
        {
            string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Singleplayer);
            saveNameBox.Text = Path.GetFileNameWithoutExtension(savePath);
        }

        public void UpdateSubList(IEnumerable<SubmarineInfo> submarines)
        {
            List<SubmarineInfo> subsToShow;
            if (subFilter != CategoryFilter.All)
            {
                subsToShow = submarines.Where(s => s.IsCampaignCompatibleIgnoreClass && s.IsVanillaSubmarine() == (subFilter == CategoryFilter.Vanilla)).ToList();
            }
            else
            {
                string downloadFolder = Path.GetFullPath(SaveUtil.SubmarineDownloadFolder);
                subsToShow = submarines.Where(s => s.IsCampaignCompatibleIgnoreClass && Path.GetDirectoryName(Path.GetFullPath(s.FilePath)) != downloadFolder).ToList();
            }

            subsToShow.Sort((s1, s2) => 
            {
                int p1 = s1.Price > CampaignMode.InitialMoney ? 10 : 0;
                int p2 = s2.Price > CampaignMode.InitialMoney ? 10 : 0;
                return p1.CompareTo(p2) * 100 + s1.Name.CompareTo(s2.Name); 
            });

            subList.ClearChildren();

            foreach (SubmarineInfo sub in subsToShow)
            {
                var textBlock = new GUITextBlock(
                    new RectTransform(new Vector2(1, 0.1f), subList.Content.RectTransform) { MinSize = new Point(0, 30) },
                    ToolBox.LimitString(sub.DisplayName.Value, GUIStyle.Font, subList.Rect.Width - 65), style: "ListBoxElement")
                    {
                        ToolTip = sub.Description,
                        UserData = sub
                    };
                               
                if (!sub.RequiredContentPackagesInstalled)
                {
                    textBlock.TextColor = Color.Lerp(textBlock.TextColor, Color.DarkRed, .5f);
                    textBlock.ToolTip = TextManager.Get("ContentPackageMismatch") + "\n\n" + textBlock.ToolTip.SanitizedString;
                }

                var priceText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), textBlock.RectTransform, Anchor.CenterRight),
                    TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", sub.Price)), textAlignment: Alignment.CenterRight, font: GUIStyle.SmallFont)
                {
                    TextColor = sub.Price > CampaignMode.InitialMoney ? GUIStyle.Red : textBlock.TextColor * 0.8f,
                    ToolTip = textBlock.ToolTip
                };
#if !DEBUG
                if (!GameMain.DebugDraw)
                {
                    if (sub.Price > CampaignMode.InitialMoney || !sub.IsCampaignCompatible)
                    {
                        textBlock.CanBeFocused = false;
                        textBlock.TextColor *= 0.5f;
                    }
                }
#endif
            }
            if (SubmarineInfo.SavedSubmarines.Any())
            {
                var validSubs = subsToShow.Where(s => s.IsCampaignCompatible && s.Price <= CampaignMode.InitialMoney).ToList();
                if (validSubs.Count > 0)
                {
                    subList.Select(validSubs[Rand.Int(validSubs.Count)]);
                }
            }
        }

        private List<string> prevSaveFiles;
        public void UpdateLoadMenu(IEnumerable<string> saveFiles = null)
        {
            prevSaveFiles?.Clear();
            prevSaveFiles = null;
            loadGameContainer.ClearChildren();

            if (saveFiles == null)
            {
                saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Singleplayer);
            }

            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), loadGameContainer.RectTransform), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            saveList = new GUIListBox(new RectTransform(Vector2.One, leftColumn.RectTransform))
            {
                OnSelected = SelectSaveFile
            };

            new GUIButton(new RectTransform(new Vector2(0.6f, 0.08f), leftColumn.RectTransform), TextManager.Get("showinfolder"))
            {
                OnClicked = (btn, userdata) =>
                {
                    try
                    {
                        ToolBox.OpenFileWithShell(SaveUtil.SaveFolder);
                    }
                    catch (Exception e)
                    {
                        new GUIMessageBox(
                            TextManager.Get("error"), 
                            TextManager.GetWithVariables("showinfoldererror", ("[folder]", SaveUtil.SaveFolder), ("[errormessage]", e.Message)));
                    }
                    return true;
                }
            };

            foreach (string saveFile in saveFiles)
            {
                string fileName = saveFile;
                string subName = "";
                string saveTime = "";
                string contentPackageStr = "";
                var saveFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), saveList.Content.RectTransform) { MinSize = new Point(0, 45) }, style: "ListBoxElement")
                {
                    UserData = saveFile
                };

                var nameText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform), "")
                {
                    CanBeFocused = false
                };

                bool isCompatible = true;
                prevSaveFiles ??= new List<string>();
                
                nameText.Text = Path.GetFileNameWithoutExtension(saveFile);
                XDocument doc = SaveUtil.LoadGameSessionDoc(saveFile);

                if (doc?.Root == null)
                {
                    DebugConsole.ThrowError("Error loading save file \"" + saveFile + "\". The file may be corrupted.");
                    nameText.TextColor = GUIStyle.Red;
                    continue;
                }
                if (doc.Root.GetChildElement("multiplayercampaign") != null)
                {
                    //multiplayer campaign save in the wrong folder -> don't show the save
                    saveList.Content.RemoveChild(saveFrame);
                    continue;
                }
                subName             = doc.Root.GetAttributeString("submarine", "");
                saveTime            = doc.Root.GetAttributeString("savetime", "");
                isCompatible        = SaveUtil.IsSaveFileCompatible(doc);
                contentPackageStr   = doc.Root.GetAttributeStringUnrestricted("selectedcontentpackages", "");
                prevSaveFiles?.Add(saveFile);
                if (!string.IsNullOrEmpty(saveTime) && long.TryParse(saveTime, out long unixTime))
                {
                    DateTime time = ToolBox.Epoch.ToDateTime(unixTime);
                    saveTime = time.ToString();
                }
                if (!string.IsNullOrEmpty(contentPackageStr))
                {
                    List<string> contentPackagePaths = contentPackageStr.Split('|').ToList();
                    if (!GameSession.IsCompatibleWithEnabledContentPackages(contentPackagePaths, out LocalizedString errorMsg))
                    {
                        nameText.TextColor = GUIStyle.Red;
                        saveFrame.ToolTip = string.Join("\n", errorMsg, TextManager.Get("campaignmode.contentpackagemismatchwarning"));
                    }
                }
                if (!isCompatible)
                {
                    nameText.TextColor = GUIStyle.Red;
                    saveFrame.ToolTip = TextManager.Get("campaignmode.incompatiblesave");
                }

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform, Anchor.BottomLeft),
                    text: subName, font: GUIStyle.SmallFont)
                {
                    CanBeFocused = false,
                    UserData = fileName
                };

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), saveFrame.RectTransform),
                    text: saveTime, textAlignment: Alignment.Right, font: GUIStyle.SmallFont)
                {
                    CanBeFocused = false,
                    UserData = fileName
                };
            }

            saveList.Content.RectTransform.SortChildren((c1, c2) =>
            {
                string file1 = c1.GUIComponent.UserData as string;
                string file2 = c2.GUIComponent.UserData as string;
                DateTime file1WriteTime = DateTime.MinValue;
                DateTime file2WriteTime = DateTime.MinValue;
                try
                {
                    file1WriteTime = File.GetLastWriteTime(file1);
                }
                catch
                { 
                    //do nothing - DateTime.MinValue will be used and the element will get sorted at the bottom of the list 
                };
                try
                {
                    file2WriteTime = File.GetLastWriteTime(file2);
                }
                catch
                {
                    //do nothing - DateTime.MinValue will be used and the element will get sorted at the bottom of the list 
                };
                return file2WriteTime.CompareTo(file1WriteTime);
            });

            loadGameButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.12f), loadGameContainer.RectTransform, Anchor.BottomRight), TextManager.Get("LoadButton"))
            {
                OnClicked = (btn, obj) =>
                {
                    if (string.IsNullOrWhiteSpace(saveList.SelectedData as string)) { return false; }
                    LoadGame?.Invoke(saveList.SelectedData as string);
                    return true;
                },
                Enabled = false
            };
        }       
        
        private bool SelectSaveFile(GUIComponent component, object obj)
        {
            string fileName = (string)obj;

            XDocument doc = SaveUtil.LoadGameSessionDoc(fileName);
            if (doc?.Root == null)
            {
                DebugConsole.ThrowError("Error loading save file \"" + fileName + "\". The file may be corrupted.");
                return false;
            }

            loadGameButton.Enabled = SaveUtil.IsSaveFileCompatible(doc);

            RemoveSaveFrame();

            string subName = doc.Root.GetAttributeString("submarine", "");
            string saveTime = doc.Root.GetAttributeString("savetime", "unknown");
            if (long.TryParse(saveTime, out long unixTime))
            {
                DateTime time = ToolBox.Epoch.ToDateTime(unixTime);
                saveTime = time.ToString();
            }

            string mapseed = doc.Root.GetAttributeString("mapseed", "unknown");

            var saveFileFrame = new GUIFrame(new RectTransform(new Vector2(0.45f, 0.6f), loadGameContainer.RectTransform, Anchor.TopRight)
            {
                RelativeOffset = new Vector2(0.0f, 0.1f)
            }, style: "InnerFrame")
            {
                UserData = "savefileframe"
            };

            var titleText = new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.2f), saveFileFrame.RectTransform, Anchor.TopCenter)
            {
                RelativeOffset = new Vector2(0, 0.05f)
            },
                Path.GetFileNameWithoutExtension(fileName), font: GUIStyle.LargeFont, textAlignment: Alignment.Center);
            titleText.Text = ToolBox.LimitString(titleText.Text, titleText.Font, titleText.Rect.Width);

            var layoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.5f), saveFileFrame.RectTransform, Anchor.Center)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            });

            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("Submarine")} : {subName}", font: GUIStyle.SmallFont);
            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("LastSaved")} : {saveTime}", font: GUIStyle.SmallFont);
            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("MapSeed")} : {mapseed}", font: GUIStyle.SmallFont);

            new GUIButton(new RectTransform(new Vector2(0.4f, 0.15f), saveFileFrame.RectTransform, Anchor.BottomCenter)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, TextManager.Get("Delete"), style: "GUIButtonSmall")
            {
                UserData = fileName,
                OnClicked = DeleteSave
            };

            return true;
        }

        private bool DeleteSave(GUIButton button, object obj)
        {
            string saveFile = obj as string;
            if (obj == null) { return false; }

            LocalizedString header = TextManager.Get("deletedialoglabel");
            LocalizedString body = TextManager.GetWithVariable("deletedialogquestion", "[file]", Path.GetFileNameWithoutExtension(saveFile));

            EventEditorScreen.AskForConfirmation(header, body, () =>
            {
                SaveUtil.DeleteSave(saveFile);
                prevSaveFiles?.RemoveAll(s => s.StartsWith(saveFile));
                UpdateLoadMenu(prevSaveFiles.ToList());
                return true;
            });

            return true;
        }

        private void RemoveSaveFrame()
        {
            GUIComponent prevFrame = null;
            foreach (GUIComponent child in loadGameContainer.Children)
            {
                if (child.UserData as string != "savefileframe") continue;

                prevFrame = child;
                break;
            }
            loadGameContainer.RemoveChild(prevFrame);
        }
    }
}
