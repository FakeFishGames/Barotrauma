using Barotrauma.Extensions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    internal class SinglePlayerMissionSetupUI
    {
        private readonly GUIComponent newGameContainer;
        private GUITextBox seedBox;
        private GUIListBox subList;
        private GUILayoutGroup subPreviewContainer;
        private GUIButton startButton;

        public Action<SubmarineInfo, string, float, List<MissionType>> StartNewGame;

        private enum CategoryFilter
        {
            All = 0,
            Vanilla = 1,
            Custom = 2
        }

        private CategoryFilter subFilter = CategoryFilter.All;

        public SinglePlayerMissionSetupUI(GUIComponent newGameContainer)
        {
            this.newGameContainer = newGameContainer;
            CreateNewGameMenu();
        }

        private float difficulty;
        private readonly List<MissionType> missions = new List<MissionType>();

        private void CreateNewGameMenu()
        {
            GUIListBox pageContainer = new(new(Vector2.One, newGameContainer.RectTransform), true, style: null)
            {
                ScrollBarEnabled = false,
                ScrollBarVisible = false,
                AllowArrowKeyScroll = false,
                HoverCursor = CursorState.Default
            };

            GUIFrame containerItem = new(new(Vector2.One, pageContainer.Content.RectTransform), null);
            GUILayoutGroup pageLayout = new(new(Vector2.One * 0.95f, containerItem.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.02f
            };

            GUILayoutGroup columnContainer = new(new(new Vector2(1, 0.9f), pageLayout.RectTransform), true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            GUILayoutGroup leftColumn = new(new(Vector2.One, columnContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            GUILayoutGroup rightColumn = new(new(new Vector2(1.5f, 1), columnContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            columnContainer.Recalculate();

            // Left Side
            // Seed
            new GUITextBlock(new(new Vector2(1, 0.02f), leftColumn.RectTransform, minSize: new(0, 20)), TextManager.Get("LevelSeed"), font: GUIStyle.SubHeadingFont);
            seedBox = new(new(new Vector2(1, 0.05f), leftColumn.RectTransform, minSize: new(0, 20)), ToolBox.RandomSeed(8));

            // Difficulty
            GUITextBlock difficultyLabel = new(new(new Vector2(1, 0.02f), leftColumn.RectTransform, minSize: new(0, 20)), TextManager.Get("LevelDifficulty"), font: GUIStyle.SubHeadingFont);
            GUITextBlock difficultyName = new(new(Vector2.One, difficultyLabel.RectTransform), EventManagerSettings.GetByDifficultyPercentile(0).Name + $" ({TextManager.GetWithVariable("percentageformat", "[value]", "0")})", GUIStyle.Green, GUIStyle.SubHeadingFont, Alignment.CenterRight);
            GUIScrollBar levelDifficultyScrollBar = new(new(new Vector2(1, 0.02f), leftColumn.RectTransform, Anchor.BottomCenter), 0.2f, style: "GUISlider")
            {
                Step = 0.01f,
                Range = new(0, 100),
                ToolTip = TextManager.Get("leveldifficultyexplanation"),
                OnReleased = (scrollbar, value) =>
                {
                    difficulty = scrollbar.BarScrollValue;
                    return true;
                },
                OnMoved = (scrollbar, value) =>
                {
                    if (!EventManagerSettings.Prefabs.Any()) return true;

                    difficultyName.Text = EventManagerSettings.GetByDifficultyPercentile(value).Name + $" ({TextManager.GetWithVariable("percentageformat", "[value]", ((int)MathF.Round(scrollbar.BarScrollValue)).ToString())})";
                    difficultyName.TextColor = ToolBox.GradientLerp(scrollbar.BarScroll, GUIStyle.Green, GUIStyle.Orange, GUIStyle.Red);
                    return true;
                }
            };

            // Missions
            new GUITextBlock(new(new Vector2(1, 0.02f), leftColumn.RectTransform, minSize: new(0, 20)), TextManager.Get("Missions"), font: GUIStyle.SubHeadingFont);

            MissionType[] missionTypes = MissionPrefab.CoOpMissionClasses.Keys.Where(type => !MissionPrefab.HiddenMissionClasses.Contains(type)).ToArray();

            GUIDropDown missionsDropdown = new(new(new Vector2(1, 0.02f), leftColumn.RectTransform), TextManager.Get("MissionType.None"), missionTypes.Length, selectMultiple: true);

            foreach (MissionType missionType in missionTypes)
            {
                GUITickBox tickBox = missionsDropdown.AddItem(TextManager.Get("MissionType." + missionType), missionType, TextManager.Get("MissionTypeDescription." + missionType)).GetChild<GUITickBox>();
                tickBox.OnSelected += obj =>
                {
                    if (obj.Selected)
                    {
                        missions.Add(missionType);
                        if (missionTypes.All(m => missions.Contains(m)))
                        {
                            missionsDropdown.Text = TextManager.Get("AllLanguages");
                        }
                    }
                    else
                    {
                        missions.Remove(missionType);
                        if (!missions.Any())
                        {
                            missionsDropdown.Text = TextManager.Get("MissionType.None");
                        }
                    }
                    return true;
                };
            }

            // Submarines
            new GUITextBlock(new(new Vector2(1, 0.02f), leftColumn.RectTransform, minSize: new(0, 20)), TextManager.Get("SelectedSub"), font: GUIStyle.SubHeadingFont);

            GUIDropDown moddedDropdown = new GUIDropDown(new(new Vector2(1, 0.02f), leftColumn.RectTransform), "", 3);
            moddedDropdown.AddItem(TextManager.Get("clientpermission.all"), CategoryFilter.All);
            moddedDropdown.AddItem(TextManager.Get("servertag.modded.false"), CategoryFilter.Vanilla);
            moddedDropdown.AddItem(TextManager.Get("customrank"), CategoryFilter.Custom);
            moddedDropdown.Select(0);

            GUILayoutGroup filterContainer = new GUILayoutGroup(new(new Vector2(1, 0.05f), leftColumn.RectTransform), true)
            {
                Stretch = true
            };

            subList = new GUIListBox(new(new Vector2(1, 0.65f), leftColumn.RectTransform))
            {
                PlaySoundOnSelect = true,
                ScrollBarVisible = true,
                OnSelected = (component, obj) =>
                {
                    if (subPreviewContainer == null || obj is not SubmarineInfo sub) return false;

                    (subPreviewContainer.Parent as GUILayoutGroup)?.Recalculate();
                    subPreviewContainer.ClearChildren();

                    startButton.Enabled = true;
                    sub.CreatePreviewWindow(subPreviewContainer);
                    return true;
                }
            };

            GUITextBlock searchTitle = new(new(new Vector2(0.001f, 1), filterContainer.RectTransform), TextManager.Get("serverlog.filter"), font: GUIStyle.Font, textAlignment: Alignment.CenterLeft);
            GUITextBox searchBox = new(new(Vector2.One, filterContainer.RectTransform, Anchor.CenterRight), font: GUIStyle.Font, createClearButton: true);
            filterContainer.RectTransform.MinSize = searchBox.RectTransform.MinSize;
            searchBox.OnSelected += (sender, userdata) => searchTitle.Visible = false;
            searchBox.OnDeselected += (sender, userdata) => searchTitle.Visible = true;
            searchBox.OnTextChanged += (textBox, filter) =>
            {
                foreach (GUIComponent child in subList.Content.Children)
                {
                    if (child.UserData is not SubmarineInfo sub) continue;
                    child.Visible = string.IsNullOrEmpty(filter) || sub.DisplayName.Contains(filter.ToLower(), StringComparison.OrdinalIgnoreCase);
                }
                return true;
            };

            moddedDropdown.OnSelected = (component, data) =>
            {
                searchBox.Text = string.Empty;
                subFilter = (CategoryFilter)data;
                UpdateSubList(SubmarineInfo.SavedSubmarines);
                return true;
            };

            // Right Side
            // Submarine Preview
            subPreviewContainer = new(new(Vector2.One, rightColumn.RectTransform))
            {
                Stretch = true
            };

            // Bottom
            // Start Button
            GUILayoutGroup pageButtonContainer = new(new(new Vector2(1, 0.08f), pageLayout.RectTransform), true, Anchor.BottomLeft)
            {
                RelativeSpacing = 0.025f
            };

            startButton = new(new(Vector2.One, pageButtonContainer.RectTransform, Anchor.BottomRight), TextManager.Get("StartCampaignButton"))
            {
                OnClicked = (btn, userData) =>
                {
                    if (subList.SelectedData is not SubmarineInfo selectedSub)
                    {
                        GUIMessageBox msgBox = new(TextManager.Get("SubNotSelected"), TextManager.Get("SelectSubRequest"));
                        return false;
                    }

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

                    if (!selectedSub.RequiredContentPackagesInstalled)
                    {
                        GUIMessageBox msgBox = new GUIMessageBox(TextManager.Get("ContentPackageMismatch"), TextManager.GetWithVariable("ContentPackageMismatchWarning", "[requiredcontentpackages]", string.Join(", ", selectedSub.RequiredContentPackages)), new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });

                        msgBox.Buttons[1].OnClicked = (button, obj) =>
                        {
                            msgBox.Close();
                            return false;
                        };
                    }

                    if (selectedSub.HasTag(SubmarineTag.Shuttle))
                    {
                        GUIMessageBox msgBox = new GUIMessageBox(TextManager.Get("ShuttleSelected"), TextManager.Get("ShuttleWarning"), new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });

                        msgBox.Buttons[1].OnClicked = (button, obj) =>
                        {
                            msgBox.Close();
                            return false;
                        };
                    }

                    StartNewGame?.Invoke(selectedSub, seedBox.Text, difficulty, missions);
                    return true;
                }
            };

            columnContainer.Recalculate();
            leftColumn.Recalculate();
            rightColumn.Recalculate();

            pageContainer.RecalculateChildren();
            pageContainer.GetAllChildren().ForEach(c => c.ClampMouseRectToParent = true);
            pageContainer.GetAllChildren<GUIDropDown>().ForEach(dd =>
            {
                dd.ListBox.ClampMouseRectToParent = false;
                dd.ListBox.Content.ClampMouseRectToParent = false;
            });
        }

        public void RandomizeSeed() => seedBox.Text = ToolBox.RandomSeed(8);

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

            subsToShow.Sort((sub1, sub2) => sub1.SubmarineClass.CompareTo(sub2.SubmarineClass) * 100 + sub1.Name.CompareTo(sub2.Name));

            subList.ClearChildren();

            foreach (SubmarineInfo sub in subsToShow)
            {
                GUITextBlock textBlock = new(new(new Vector2(1, 0.15f), subList.Content.RectTransform, minSize: new(0, 30)), ToolBox.LimitString(sub.DisplayName.Value, GUIStyle.Font, subList.Rect.Width - 65), style: "ListBoxElement")
                {
                    ToolTip = sub.Description,
                    UserData = sub
                };

                if (!sub.RequiredContentPackagesInstalled)
                {
                    textBlock.TextColor = Color.Lerp(textBlock.TextColor, Color.DarkRed, 0.5f);
                    textBlock.ToolTip = TextManager.Get("ContentPackageMismatch") + "\n\n" + textBlock.ToolTip.SanitizedString;
                }

                new GUITextBlock(new(new Vector2(0.5f, 0.5f), textBlock.RectTransform, Anchor.CenterRight), TextManager.Get($"submarineclass.{sub.SubmarineClass}"), font: GUIStyle.SmallFont, textAlignment: Alignment.CenterRight)
                {
                    TextColor = textBlock.TextColor * 0.8f,
                    ToolTip = textBlock.ToolTip
                };
#if !DEBUG
                if (!GameMain.DebugDraw && !sub.IsCampaignCompatible)
                {
                    textBlock.CanBeFocused = false;
                    textBlock.TextColor *= 0.5f;
                }
#endif
            }

            if (subsToShow.Where(s => s.IsCampaignCompatible).ToList() is { Count: > 0 } validSubs)
            {
                subList.Select(validSubs.GetRandomUnsynced());
            }
        }
    }
}