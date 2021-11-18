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
    class MultiPlayerCampaignSetupUI : CampaignSetupUI
    {
        private GUIButton deleteMpSaveButton;
        
        public MultiPlayerCampaignSetupUI(GUIComponent newGameContainer, GUIComponent loadGameContainer, IEnumerable<SubmarineInfo> submarines, IEnumerable<string> saveFiles = null)
            : base(newGameContainer, loadGameContainer)
        {
            var columnContainer = new GUILayoutGroup(new RectTransform(Vector2.One, newGameContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.0f
            };

            var leftColumn = new GUILayoutGroup(new RectTransform(Vector2.One, columnContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            var rightColumn = new GUILayoutGroup(new RectTransform(Vector2.Zero, columnContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            columnContainer.Recalculate();

            // New game left side
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("SaveName"), font: GUI.SubHeadingFont);
            saveNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, string.Empty)
            {
                textFilterFunction = (string str) => { return ToolBox.RemoveInvalidFileNameChars(str); }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("MapSeed"), font: GUI.SubHeadingFont);
            seedBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, ToolBox.RandomSeed(8));

            // Spacing to fix the multiplayer campaign setup layout
            CreateMultiplayerCampaignSubList(leftColumn.RectTransform);

            //spacing
            //new GUIFrame(new RectTransform(new Vector2(1.0f, 0.25f), leftColumn.RectTransform), style: null);

            // New game right side
            subPreviewContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), rightColumn.RectTransform))
            {
                Stretch = true
            };

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.12f),
                leftColumn.RectTransform) { MaxSize = new Point(int.MaxValue, 60) }, childAnchor: Anchor.BottomRight, isHorizontal: true);

            StartButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1f), buttonContainer.RectTransform, Anchor.BottomRight) { MaxSize = new Point(350, 60) }, TextManager.Get("StartCampaignButton"))
            {
                OnClicked = (GUIButton btn, object userData) =>
                {
                    if (string.IsNullOrWhiteSpace(saveNameBox.Text))
                    {
                        saveNameBox.Flash(GUI.Style.Red);
                        return false;
                    }

                    SubmarineInfo selectedSub = null;

                    if (GameMain.NetLobbyScreen.SelectedSub == null) { return false; }
                    selectedSub = GameMain.NetLobbyScreen.SelectedSub;
                    
                    if (selectedSub.SubmarineClass == SubmarineClass.Undefined)
                    {
                        new GUIMessageBox(TextManager.Get("error"), TextManager.Get("undefinedsubmarineselected"));
                        return false;
                    }

                    if (string.IsNullOrEmpty(selectedSub.MD5Hash.Hash))
                    {
                        ((GUITextBlock)subList.SelectedComponent).TextColor = Color.DarkRed * 0.8f;
                        subList.SelectedComponent.CanBeFocused = false;
                        subList.Deselect();
                        return false;
                    }

                    string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer, saveNameBox.Text);
                    bool hasRequiredContentPackages = selectedSub.RequiredContentPackagesInstalled;

                    CampaignSettings settings = new CampaignSettings();

                    settings.RadiationEnabled = GameMain.NetLobbyScreen.IsRadiationEnabled();
                    settings.MaxMissionCount = GameMain.NetLobbyScreen.GetMaxMissionCount();
                    
                    if (selectedSub.HasTag(SubmarineTag.Shuttle) || !hasRequiredContentPackages)
                    {
                        if (!hasRequiredContentPackages)
                        {
                            var msgBox = new GUIMessageBox(TextManager.Get("ContentPackageMismatch"),
                                TextManager.GetWithVariable("ContentPackageMismatchWarning", "[requiredcontentpackages]", string.Join(", ", selectedSub.RequiredContentPackages)),
                                new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                            msgBox.Buttons[0].OnClicked = msgBox.Close;
                            msgBox.Buttons[0].OnClicked += (button, obj) =>
                            {
                                if (GUIMessageBox.MessageBoxes.Count == 0)
                                {
                                    StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text, settings);
                                    CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
                                }
                                return true;
                            };

                            msgBox.Buttons[1].OnClicked = msgBox.Close;
                        }

                        if (selectedSub.HasTag(SubmarineTag.Shuttle))
                        {
                            var msgBox = new GUIMessageBox(TextManager.Get("ShuttleSelected"),
                                TextManager.Get("ShuttleWarning"),
                                new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                            msgBox.Buttons[0].OnClicked = (button, obj) => 
                            {
                                StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text, settings);
                                CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
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
                        CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
                    }

                    return true;
                }
            };

            InitialMoneyText = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1f), buttonContainer.RectTransform), "", font: GUI.Style.SmallFont, textColor: GUI.Style.Green)
            {
                TextGetter = () =>
                {
                    int initialMoney = CampaignMode.InitialMoney;
                    if (GameMain.NetLobbyScreen.SelectedSub != null)
                    {
                        initialMoney -= GameMain.NetLobbyScreen.SelectedSub.Price;
                    }
                    initialMoney = Math.Max(initialMoney, MultiPlayerCampaign.MinimumInitialMoney);
                    return TextManager.GetWithVariable("campaignstartingmoney", "[money]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", initialMoney));
                }
            };

            columnContainer.Recalculate();
            leftColumn.Recalculate();
            rightColumn.Recalculate();

            if (submarines != null) { UpdateSubList(submarines); }
            UpdateLoadMenu(saveFiles);
        }

        private void CreateMultiplayerCampaignSubList(RectTransform parent)
        {
            GUILayoutGroup subHolder = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.725f), parent))
            {
                RelativeSpacing = 0.005f,
                Stretch = true
            };

            var subLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.055f), subHolder.RectTransform) { MinSize = new Point(0, 25) }, TextManager.Get("purchasablesubmarines", fallBackTag: "workshoplabelsubmarines"), font: GUI.SubHeadingFont);

            var filterContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), subHolder.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            var searchTitle = new GUITextBlock(new RectTransform(new Vector2(0.001f, 1.0f), filterContainer.RectTransform), TextManager.Get("serverlog.filter"), textAlignment: Alignment.CenterLeft, font: GUI.Font);
            var searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 1.0f), filterContainer.RectTransform, Anchor.CenterRight), font: GUI.Font, createClearButton: true);
            filterContainer.RectTransform.MinSize = searchBox.RectTransform.MinSize;
            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = true; };
            searchBox.OnTextChanged += (textBox, text) =>
            {
                foreach (GUIComponent child in subList.Content.Children)
                {
                    if (!(child.UserData is SubmarineInfo sub)) { continue; }
                    child.Visible = string.IsNullOrEmpty(text) ? true : sub.DisplayName.ToLower().Contains(text.ToLower());
                }
                return true;
            };

            subList = new GUIListBox(new RectTransform(Vector2.One, subHolder.RectTransform));
            subTickBoxes = new List<GUITickBox>();

            for (int i = 0; i < GameMain.Client.ServerSubmarines.Count; i++)
            {
                SubmarineInfo sub = GameMain.Client.ServerSubmarines[i];

                if (!sub.IsCampaignCompatible) continue;

                var frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), subList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    style: "ListBoxElement")
                {
                    ToolTip = sub.Description,
                    UserData = sub
                };

                int buttonSize = (int)(frame.Rect.Height * 0.8f);

                GUITickBox tickBox = new GUITickBox(new RectTransform(new Vector2(0.8f, 1.0f), frame.RectTransform, Anchor.CenterLeft), ToolBox.LimitString(sub.DisplayName, GUI.Font, subList.Content.Rect.Width - 65))
                {
                    UserData = sub,
                    OnSelected = (GUITickBox box) =>
                    {
                        GameMain.Client.RequestCampaignSub(box.UserData as SubmarineInfo, box.Selected);
                        return true;
                    }
                };
                subTickBoxes.Add(tickBox);
                tickBox.Selected = GameMain.NetLobbyScreen.CampaignSubmarines.Contains(sub);

                frame.RectTransform.MinSize = new Point(0, tickBox.RectTransform.MinSize.Y);

                var subTextBlock = tickBox.TextBlock;

                var matchingSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == sub.Name && s.MD5Hash?.Hash == sub.MD5Hash?.Hash);
                if (matchingSub == null) matchingSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == sub.Name);

                if (matchingSub == null)
                {
                    subTextBlock.TextColor = new Color(subTextBlock.TextColor, 0.5f);
                    frame.ToolTip = TextManager.Get("SubNotFound");
                }
                else if (matchingSub?.MD5Hash == null || matchingSub.MD5Hash?.Hash != sub.MD5Hash?.Hash)
                {
                    subTextBlock.TextColor = new Color(subTextBlock.TextColor, 0.5f);
                    frame.ToolTip = TextManager.Get("SubDoesntMatch");
                }

                if (!sub.RequiredContentPackagesInstalled)
                {
                    subTextBlock.TextColor = Color.Lerp(subTextBlock.TextColor, Color.DarkRed, 0.5f);
                    frame.ToolTip = TextManager.Get("ContentPackageMismatch") + "\n\n" + frame.RawToolTip;
                }

                var classText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), frame.RectTransform, Anchor.CenterRight),
                TextManager.Get($"submarineclass.{sub.SubmarineClass}"), textAlignment: Alignment.CenterRight, font: GUI.SmallFont)
                {
                    TextColor = subTextBlock.TextColor * 0.8f,
                    ToolTip = subTextBlock.RawToolTip
                };
            }
        }

        public void RefreshMultiplayerCampaignSubUI(List<SubmarineInfo> campaignSubs)
        {
            for (int i = 0; i < subTickBoxes.Count; i++)
            {
                subTickBoxes[i].Selected = campaignSubs.Contains(subTickBoxes[i].UserData as SubmarineInfo);
            }
        }

        private IEnumerable<CoroutineStatus> WaitForCampaignSetup()
        {
            GUI.SetCursorWaiting();
            string headerText = TextManager.Get("CampaignStartingPleaseWait");
            var msgBox = new GUIMessageBox(headerText, TextManager.Get("CampaignStarting"), new string[] { TextManager.Get("Cancel") });

            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                GameMain.NetLobbyScreen.HighlightMode(GameMain.NetLobbyScreen.SelectedModeIndex);
                GameMain.NetLobbyScreen.SelectMode(GameMain.NetLobbyScreen.SelectedModeIndex);
                GUI.ClearCursorWait();
                CoroutineManager.StopCoroutines("WaitForCampaignSetup");
                return true;
            };
            msgBox.Buttons[0].OnClicked += msgBox.Close;

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 20);
            while (Screen.Selected != GameMain.GameScreen && DateTime.Now < timeOut)
            {
                msgBox.Header.Text = headerText + new string('.', (int)Timing.TotalTime % 3 + 1);
                yield return CoroutineStatus.Running;
            }
            msgBox.Close();
            GUI.ClearCursorWait();
            yield return CoroutineStatus.Success;
        }

        public void UpdateSubList(IEnumerable<SubmarineInfo> submarines)
        {
            List<SubmarineInfo> subsToShow;
            string downloadFolder = Path.GetFullPath(SaveUtil.SubmarineDownloadFolder);
            subsToShow = submarines.Where(s => s.IsCampaignCompatibleIgnoreClass && Path.GetDirectoryName(Path.GetFullPath(s.FilePath)) != downloadFolder).ToList();

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
                    ToolBox.LimitString(sub.DisplayName, GUI.Font, subList.Rect.Width - 65), style: "ListBoxElement")
                    {
                        ToolTip = sub.Description,
                        UserData = sub
                    };
                               
                if (!sub.RequiredContentPackagesInstalled)
                {
                    textBlock.TextColor = Color.Lerp(textBlock.TextColor, Color.DarkRed, .5f);
                    textBlock.ToolTip = TextManager.Get("ContentPackageMismatch") + "\n\n" + textBlock.RawToolTip;
                }

                var priceText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), textBlock.RectTransform, Anchor.CenterRight),
                    TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", sub.Price)), textAlignment: Alignment.CenterRight, font: GUI.SmallFont)
                {
                    TextColor = sub.Price > CampaignMode.InitialMoney ? GUI.Style.Red : textBlock.TextColor * 0.8f,
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
                saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Multiplayer);
            }

            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.85f), loadGameContainer.RectTransform), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            saveList = new GUIListBox(new RectTransform(Vector2.One, leftColumn.RectTransform))
            {
                OnSelected = SelectSaveFile
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

                prevSaveFiles?.Add(saveFile);
                string[] splitSaveFile = saveFile.Split(';');
                saveFrame.UserData = splitSaveFile[0];
                fileName = nameText.Text = Path.GetFileNameWithoutExtension(splitSaveFile[0]);
                if (splitSaveFile.Length > 1) { subName = splitSaveFile[1]; }
                if (splitSaveFile.Length > 2) { saveTime = splitSaveFile[2]; }
                if (splitSaveFile.Length > 3) { contentPackageStr = splitSaveFile[3]; }
                
                if (!string.IsNullOrEmpty(saveTime) && long.TryParse(saveTime, out long unixTime))
                {
                    DateTime time = ToolBox.Epoch.ToDateTime(unixTime);
                    saveTime = time.ToString();
                }
                if (!string.IsNullOrEmpty(contentPackageStr))
                {
                    List<string> contentPackagePaths = contentPackageStr.Split('|').ToList();
                    if (!GameSession.IsCompatibleWithEnabledContentPackages(contentPackagePaths, out string errorMsg))
                    {
                        nameText.TextColor = GUI.Style.Red;
                        saveFrame.ToolTip = string.Join("\n", errorMsg, TextManager.Get("campaignmode.contentpackagemismatchwarning"));
                    }
                }
                if (!isCompatible)
                {
                    nameText.TextColor = GUI.Style.Red;
                    saveFrame.ToolTip = TextManager.Get("campaignmode.incompatiblesave");
                }

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform, Anchor.BottomLeft),
                    text: subName, font: GUI.SmallFont)
                {
                    CanBeFocused = false,
                    UserData = fileName
                };

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), saveFrame.RectTransform),
                    text: saveTime, textAlignment: Alignment.Right, font: GUI.SmallFont)
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
                    CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
                    return true;
                },
                Enabled = false
            };
            deleteMpSaveButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.12f), loadGameContainer.RectTransform, Anchor.BottomLeft), 
                TextManager.Get("Delete"), style: "GUIButtonSmall")
            {
                OnClicked = DeleteSave,
                Visible = false
            };
        }       
        
        private bool SelectSaveFile(GUIComponent component, object obj)
        {
            string fileName = (string)obj;

            loadGameButton.Enabled = true;
            deleteMpSaveButton.Visible = deleteMpSaveButton.Enabled = GameMain.Client.IsServerOwner;
            deleteMpSaveButton.Enabled = GameMain.GameSession?.SavePath != fileName;
            if (deleteMpSaveButton.Visible)
            {
                deleteMpSaveButton.UserData = obj as string;
            }
            return true;
        }

        private bool DeleteSave(GUIButton button, object obj)
        {
            string saveFile = obj as string;
            if (obj == null) { return false; }

            string header = TextManager.Get("deletedialoglabel");
            string body = TextManager.GetWithVariable("deletedialogquestion", "[file]", Path.GetFileNameWithoutExtension(saveFile));

            EventEditorScreen.AskForConfirmation(header, body, () =>
            {
                SaveUtil.DeleteSave(saveFile);
                prevSaveFiles?.RemoveAll(s => s.StartsWith(saveFile));
                UpdateLoadMenu(prevSaveFiles.ToList());
                return true;
            });

            return true;
        }
    }
}
