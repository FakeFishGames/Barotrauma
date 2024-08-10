using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Barotrauma
{
    class MultiPlayerCampaignSetupUI : CampaignSetupUI
    {
        private GUIButton deleteMpSaveButton;

        private int prevInitialMoney;

        private CampaignSettingElements campaignSettingElements;

        public bool LoadGameMenuVisible => loadGameContainer is { Visible: true };

        public MultiPlayerCampaignSetupUI(GUIComponent newGameContainer, GUIComponent loadGameContainer, List<CampaignMode.SaveInfo> saveFiles = null)
            : base(newGameContainer, loadGameContainer)
        {
            var verticalLayout = new GUILayoutGroup(new RectTransform(Vector2.One, newGameContainer.RectTransform), isHorizontal: false)
            {
                Stretch = true,
                RelativeSpacing = 0.025f
            };

            GUILayoutGroup nameSeedLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), verticalLayout.RectTransform), isHorizontal: false)
            {
                Stretch = true
            };

            GUILayoutGroup campaignSettingLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.6f), verticalLayout.RectTransform), isHorizontal: false)
            {
                Stretch = true,
                RelativeSpacing = 0.0f
            };

            // New game
            var saveLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.03f), nameSeedLayout.RectTransform) { MinSize = new Point(0, GUI.IntScale(24)) }, TextManager.Get("SaveName"), textAlignment: Alignment.CenterLeft);
            saveNameBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), saveLabel.RectTransform, Anchor.CenterRight), string.Empty)
            {
                textFilterFunction = ToolBox.RemoveInvalidFileNameChars
            };
            saveLabel.InheritTotalChildrenMinHeight();

            var seedLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.03f), nameSeedLayout.RectTransform) { MinSize = new Point(0, GUI.IntScale(24)) }, TextManager.Get("MapSeed"), textAlignment: Alignment.CenterLeft);
            seedBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), seedLabel.RectTransform, Anchor.CenterRight), ToolBox.RandomSeed(8));
            seedLabel.InheritTotalChildrenMinHeight();

            nameSeedLayout.InheritTotalChildrenMinHeight();

            campaignSettingElements = CreateCampaignSettingList(campaignSettingLayout, CampaignSettings.Empty, false);

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f),
                verticalLayout.RectTransform) { MaxSize = new Point(int.MaxValue, GUI.IntScale(30)) }, childAnchor: Anchor.BottomRight, isHorizontal: true);

            prevInitialMoney = CampaignSettings.DefaultInitialMoney;
            InitialMoneyText = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1f), buttonContainer.RectTransform), "", font: GUIStyle.SmallFont, textColor: GUIStyle.Green, textAlignment: Alignment.CenterRight)
            {
                TextGetter = () =>
                {
                    int defaultInitialMoney = CampaignSettings.DefaultInitialMoney;
                    int initialMoney = defaultInitialMoney;
                    if (CampaignModePresets.TryGetAttribute(
                        nameof(CampaignSettings.StartingBalanceAmount).ToIdentifier(),
                        campaignSettingElements.StartingFunds.GetValue().ToIdentifier(),
                        out var attribute))
                    {
                        initialMoney = attribute.GetAttributeInt(defaultInitialMoney);
                    }
                    if (prevInitialMoney != initialMoney)
                    {
                        GameMain.NetLobbyScreen.RefreshEnabledElements();
                        prevInitialMoney = initialMoney;
                    }
                    if (GameMain.NetLobbyScreen.SelectedSub != null)
                    {
                        initialMoney -= GameMain.NetLobbyScreen.SelectedSub.Price;
                    }
                    initialMoney = Math.Max(initialMoney, 0);

                    return TextManager.GetWithVariable("campaignstartingmoney", "[money]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", initialMoney));
                }
            };

            verticalLayout.Recalculate();

            CreateLoadMenu(saveFiles);
        }

        public bool StartGameClicked(GUIButton button, object userdata)
        {
            if (string.IsNullOrWhiteSpace(saveNameBox.Text))
            {
                saveNameBox.Flash(GUIStyle.Red, flashDuration: 5.0f);
                saveNameBox.Pulsate(Vector2.One, Vector2.One * 1.2f, duration: 2.0f);
                newGameContainer?.Flash(GUIStyle.Red, flashDuration: 0.5f);
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

            CampaignSettings settings = campaignSettingElements.CreateSettings();

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

        public override void CreateLoadMenu(IEnumerable<CampaignMode.SaveInfo> saveFiles = null)
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
                PlaySoundOnSelect = true,
                OnSelected = SelectSaveFile
            };

            foreach (CampaignMode.SaveInfo saveInfo in saveFiles)
            {
                CreateSaveElement(saveInfo);
            }

            SortSaveList();

            loadGameButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.12f), loadGameContainer.RectTransform, Anchor.BottomRight), TextManager.Get("LoadButton"))
            {
                OnClicked = (btn, obj) =>
                {
                    if (saveList.SelectedData is not CampaignMode.SaveInfo saveInfo) { return false; }
                    if (string.IsNullOrWhiteSpace(saveInfo.FilePath)) { return false; }
                    LoadGame?.Invoke(saveInfo.FilePath);
                    
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
            if (obj is not CampaignMode.SaveInfo saveInfo) { return true; }
            string fileName = saveInfo.FilePath;

            loadGameButton.Enabled = true;
            deleteMpSaveButton.Visible = deleteMpSaveButton.Enabled = GameMain.Client.IsServerOwner;
            deleteMpSaveButton.Enabled = GameMain.GameSession?.SavePath != fileName;
            if (deleteMpSaveButton.Visible)
            {
                deleteMpSaveButton.UserData = saveInfo;
            }
            return true;
        }
    }
}
