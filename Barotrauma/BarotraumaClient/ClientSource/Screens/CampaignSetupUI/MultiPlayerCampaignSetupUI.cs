using Barotrauma.Extensions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    class MultiPlayerCampaignSetupUI : CampaignSetupUI
    {
        private GUIButton deleteMpSaveButton;
        
        public MultiPlayerCampaignSetupUI(GUIComponent newGameContainer, GUIComponent loadGameContainer, List<CampaignMode.SaveInfo> saveFiles = null)
            : base(newGameContainer, loadGameContainer)
        {
            var verticalLayout = new GUILayoutGroup(new RectTransform(Vector2.One, newGameContainer.RectTransform), isHorizontal: false)
            {
                Stretch = true,
                RelativeSpacing = 0.0f
            };

            // New game
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.03f), verticalLayout.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("SaveName"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.BottomLeft);
            saveNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.03f), verticalLayout.RectTransform) { MinSize = new Point(0, 20) }, string.Empty)
            {
                textFilterFunction = (string str) => { return ToolBox.RemoveInvalidFileNameChars(str); }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.03f), verticalLayout.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("MapSeed"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.BottomLeft);
            seedBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.03f), verticalLayout.RectTransform) { MinSize = new Point(0, 20) }, ToolBox.RandomSeed(8));

            GUIFrame radiationBoxContainer
                = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), verticalLayout.RectTransform), style: null);
            GUITickBox radiationEnabledTickBox = null;
            if (MapGenerationParams.Instance.RadiationParams != null)
            {
                radiationEnabledTickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.5f), radiationBoxContainer.RectTransform, Anchor.Center), TextManager.Get("CampaignOption.EnableRadiation"), font: GUIStyle.Font)
                {
                    Selected = true,
                    OnSelected = box => true
                };
            }

            var maxMissionCountSettingHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), verticalLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft) { Stretch = true };
            var maxMissionCountDescription = new GUITextBlock(new RectTransform(new Vector2(0.7f, 0.0f), maxMissionCountSettingHolder.RectTransform), TextManager.Get("maxmissioncount", "missions"), wrap: true)
                {
                ToolTip = TextManager.Get("maxmissioncounttooltip")
            };
            int maxMissionCount = GameMain.NetworkMember.ServerSettings.MaxMissionCount;
            var maxMissionCountContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), maxMissionCountSettingHolder.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft) { RelativeSpacing = 0.05f, Stretch = true };
            var maxMissionCountButtons = new GUIButton[2];
            maxMissionCountButtons[0]
                = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), maxMissionCountContainer.RectTransform),
                    style: "GUIButtonToggleLeft");
            var maxMissionCountText = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), maxMissionCountContainer.RectTransform), "0", textAlignment: Alignment.Center, style: "GUITextBox");

            void updateMissionCountText()
            {
                maxMissionCount = MathHelper.Clamp(maxMissionCount,
                    CampaignSettings.MinMissionCountLimit,
                    CampaignSettings.MaxMissionCountLimit);

                maxMissionCountText.Text = maxMissionCount.ToString(CultureInfo.InvariantCulture);
            }
            maxMissionCountButtons[1]
                = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), maxMissionCountContainer.RectTransform),
                    style: "GUIButtonToggleRight");
            maxMissionCountButtons[0].OnClicked = (button, o) =>
            {
                maxMissionCount--;
                updateMissionCountText();
                return false;
            };
            maxMissionCountButtons[1].OnClicked = (button, o) =>
            {
                maxMissionCount++;
                updateMissionCountText();
                return false;
            };
            updateMissionCountText();
            maxMissionCountSettingHolder.Children.ForEach(c => c.ToolTip = maxMissionCountSettingHolder.ToolTip);

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.04f),
                verticalLayout.RectTransform) { MaxSize = new Point(int.MaxValue, 60) }, childAnchor: Anchor.BottomRight, isHorizontal: true);

            StartButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1f), buttonContainer.RectTransform, Anchor.BottomRight), TextManager.Get("StartCampaignButton"))
            {
                OnClicked = (GUIButton btn, object userData) =>
                {
                    if (string.IsNullOrWhiteSpace(saveNameBox.Text))
                    {
                        saveNameBox.Flash(GUIStyle.Red);
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

                    if (string.IsNullOrEmpty(selectedSub.MD5Hash.StringRepresentation))
                    {
                        new GUIMessageBox(TextManager.Get("error"), TextManager.Get("nohashsubmarineselected"));
                        return false;
                    }

                    string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer, saveNameBox.Text);
                    bool hasRequiredContentPackages = selectedSub.RequiredContentPackagesInstalled;

                    CampaignSettings settings = new CampaignSettings
                    {
                        RadiationEnabled = radiationEnabledTickBox?.Selected ?? GameMain.NetworkMember.ServerSettings.RadiationEnabled,
                        MaxMissionCount = maxMissionCount
                    };

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
                                new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });

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
            StartButton.RectTransform.MaxSize = RectTransform.MaxPoint;
            StartButton.Children.ForEach(c => c.RectTransform.MaxSize = RectTransform.MaxPoint);
            
            InitialMoneyText = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1f), buttonContainer.RectTransform), "", font: GUIStyle.SmallFont, textColor: GUIStyle.Green)
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

            verticalLayout.Recalculate();

            UpdateLoadMenu(saveFiles);
        }

        private IEnumerable<CoroutineStatus> WaitForCampaignSetup()
        {
            GUI.SetCursorWaiting();
            var headerText = TextManager.Get("CampaignStartingPleaseWait");
            var msgBox = new GUIMessageBox(headerText, TextManager.Get("CampaignStarting"), new LocalizedString[] { TextManager.Get("Cancel") });

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

        public void UpdateLoadMenu(IEnumerable<CampaignMode.SaveInfo> saveFiles = null)
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

            foreach (CampaignMode.SaveInfo saveInfo in saveFiles)
            {
                CreateSaveElement(saveInfo);
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

            var header = TextManager.Get("deletedialoglabel");
            var body = TextManager.GetWithVariable("deletedialogquestion", "[file]", Path.GetFileNameWithoutExtension(saveFile));

            EventEditorScreen.AskForConfirmation(header, body, () =>
            {
                SaveUtil.DeleteSave(saveFile);
                prevSaveFiles?.RemoveAll(s => s.FilePath == saveFile);
                UpdateLoadMenu(prevSaveFiles.ToList());
                return true;
            });

            return true;
        }
    }
}
