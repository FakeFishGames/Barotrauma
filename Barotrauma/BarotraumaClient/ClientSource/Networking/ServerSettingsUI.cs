using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class ServerSettings : ISerializableEntity
    {
        //GUI stuff
        private GUIFrame settingsFrame;
        private readonly Dictionary<SettingsTab, GUIComponent> settingsTabs = new Dictionary<SettingsTab, GUIComponent>();
        private readonly Dictionary<SettingsTab, GUIButton> tabButtons = new Dictionary<SettingsTab, GUIButton>();
        private SettingsTab selectedTab;

        //UI elements relating to karma, hidden when karma is disabled
        private readonly List<GUIComponent> karmaElements = new List<GUIComponent>();
        private GUIDropDown karmaPresetDD;
        private GUIListBox karmaSettingsList;

        private GUIComponent extraCargoPanel, monstersEnabledPanel;
        private GUIButton extraCargoButton, monstersEnabledButton;

        enum SettingsTab
        {
            ServerIdentity,
            General,
            Antigriefing,
            Banlist
        }

        public void AssignGUIComponent(string propertyName, GUIComponent component)
        {
            GetPropertyData(propertyName).AssignGUIComponent(component);
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) { return; }

            settingsFrame?.AddToGUIUpdateList();
        }

        private void CreateSettingsFrame()
        {
            foreach (NetPropertyData prop in netProperties.Values)
            {
                prop.TempValue = prop.Value;
            }

            //background frame
            settingsFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null);
            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, settingsFrame.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

            new GUIButton(new RectTransform(Vector2.One, settingsFrame.RectTransform), "", style: null).OnClicked += (btn, userData) =>
            {
                if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) { ToggleSettingsFrame(btn, userData); }
                return true;
            };

            new GUIButton(new RectTransform(Vector2.One, settingsFrame.RectTransform), "", style: null)
            {
                OnClicked = ToggleSettingsFrame
            };

            //center frames
            GUIFrame innerFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.85f), settingsFrame.RectTransform, Anchor.Center) { MinSize = new Point(400, 430) });
            GUILayoutGroup paddedFrame = new GUILayoutGroup(new RectTransform(innerFrame.Rect.Size - new Point(GUI.IntScale(20)), innerFrame.RectTransform, Anchor.Center), 
                childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform), TextManager.Get("serversettingsbutton"), font: GUIStyle.LargeFont);

            var buttonArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.04f), paddedFrame.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            var tabContent = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.85f), paddedFrame.RectTransform), style: "InnerFrame");

            //tabs
            var settingsTabTypes = Enum.GetValues(typeof(SettingsTab)).Cast<SettingsTab>();
            foreach (var settingsTab in settingsTabTypes)
            {
                settingsTabs[settingsTab] = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), tabContent.RectTransform, Anchor.Center));
                tabButtons[settingsTab] = new GUIButton(new RectTransform(new Vector2(0.2f, 1.2f), buttonArea.RectTransform), TextManager.Get($"ServerSettings{settingsTab}Tab"), style: "GUITabButton")
                {
                    UserData = settingsTab,
                    OnClicked = SelectSettingsTab
                };
            }
            GUITextBlock.AutoScaleAndNormalize(tabButtons.Values.Select(b => b.TextBlock));
            SelectSettingsTab(tabButtons[0], 0);
            tabButtons[SettingsTab.Banlist].Enabled = 
                GameMain.Client.HasPermission(Networking.ClientPermissions.Ban) ||
                GameMain.Client.HasPermission(Networking.ClientPermissions.Unban);

            //"Close"
            var buttonContainer = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.05f), paddedFrame.RectTransform), style: null);
            var closeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonContainer.RectTransform, Anchor.BottomRight), TextManager.Get("Close"))
            {
                OnClicked = ToggleSettingsFrame
            };

            CreateServerIdentityTab(settingsTabs[SettingsTab.ServerIdentity]);
            CreateGeneralTab(settingsTabs[SettingsTab.General]);
            CreateAntigriefingTab(settingsTabs[SettingsTab.Antigriefing]);
            CreateBanlistTab(settingsTabs[SettingsTab.Banlist]);

            if (GameMain.Client == null || 
                !GameMain.Client.HasPermission(Networking.ClientPermissions.ManageSettings))
            {
                //block all settings if the client doesn't have permission to edit them
                foreach (var settingsTab in settingsTabs)
                {
                    SetElementInteractability(settingsTab.Value, false);
                }
            }
            //keep these enabled, so clients can open the panels and see what's enabled even if they can't edit them
            extraCargoButton.Enabled = monstersEnabledButton.Enabled = true;
        }

        private void SetElementInteractability(GUIComponent parent, bool interactable)
        {
            foreach (var child in parent.GetAllChildren<GUIComponent>())
            {
                child.Enabled = interactable;
                //make the disabled color slightly less dim (these should be readable, despite being non-interactable)
                child.DisabledColor = new Color(child.Color, child.Color.A / 255.0f * 0.8f);
                if (child is GUITextBlock textBlock)
                {
                    textBlock.DisabledTextColor = new Color(textBlock.TextColor, textBlock.TextColor.A / 255.0f * 0.8f);
                }
            }
        }

        private void CreateServerIdentityTab(GUIComponent parent)
        {
            //changing server visibility on the fly is not supported in dedicated servers
            if (GameMain.Client?.ClientPeer is not LidgrenClientPeer)
            {
                var isPublic = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform),
                    TextManager.Get("publicserver"))
                {
                    ToolTip = TextManager.Get("publicservertooltip")
                };
                AssignGUIComponent(nameof(IsPublic), isPublic);
            }

            var serverNameLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform), TextManager.Get("ServerName"));
            var serverNameBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), serverNameLabel.RectTransform, Anchor.CenterRight),
                GameMain.Client.ServerSettings.ServerName)
            {
                OverflowClip = true,
                MaxTextLength = NetConfig.ServerNameMaxLength
            };
            serverNameBox.OnDeselected += (textBox, key) =>
            {
                if (textBox.Text.IsNullOrWhiteSpace())
                {
                    textBox.Flash(GUIStyle.Red);
                    if (GameMain.Client != null)
                    {
                        textBox.Text = GameMain.Client.ServerSettings.ServerName;
                    }
                }
                GameMain.Client?.ServerSettings.ClientAdminWrite(NetFlags.Properties);
            };
            AssignGUIComponent(nameof(ServerName), serverNameBox);

            // server message *************************************************************************

            var motdHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform), TextManager.Get("ServerMOTD"));
            var motdCharacterCount = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), motdHeader.RectTransform, Anchor.CenterRight), string.Empty, textAlignment: Alignment.CenterRight);
            var serverMessageContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.2f), parent.RectTransform))
            {
                Visible = true
            };
            var serverMessageBox = new GUITextBox(new RectTransform(Vector2.One, serverMessageContainer.Content.RectTransform),
                style: "GUITextBoxNoBorder", wrap: true, textAlignment: Alignment.TopLeft)
            {
                MaxTextLength = NetConfig.ServerMessageMaxLength
            };
            var serverMessageHint = new GUITextBlock(new RectTransform(Vector2.One, serverMessageBox.RectTransform),
                textColor: Color.DarkGray * 0.6f, textAlignment: Alignment.TopLeft, font: GUIStyle.Font, text: TextManager.Get("ClickToWriteServerMessage"));
            AssignGUIComponent(nameof(ServerMessageText), serverMessageBox);

            void updateServerMessageScrollBasedOnCaret()
            {
                float caretY = serverMessageBox.CaretScreenPos.Y;
                float bottomCaretExtent = serverMessageBox.Font.LineHeight * 1.5f;
                float topCaretExtent = -serverMessageBox.Font.LineHeight * 0.5f;
                if (caretY + bottomCaretExtent > serverMessageContainer.Rect.Bottom)
                {
                    serverMessageContainer.ScrollBar.BarScroll
                        = (caretY - serverMessageBox.Rect.Top - serverMessageContainer.Rect.Height + bottomCaretExtent)
                          / (serverMessageBox.Rect.Height - serverMessageContainer.Rect.Height);
                }
                else if (caretY + topCaretExtent < serverMessageContainer.Rect.Top)
                {
                    serverMessageContainer.ScrollBar.BarScroll
                        = (caretY - serverMessageBox.Rect.Top + topCaretExtent)
                          / (serverMessageBox.Rect.Height - serverMessageContainer.Rect.Height);
                }
            }
            serverMessageBox.OnSelected += (textBox, key) =>
            {
                serverMessageHint.Visible = false;
                updateServerMessageScrollBasedOnCaret();
            };
            serverMessageBox.OnTextChanged += (textBox, text) =>
            {
                serverMessageHint.Visible = !textBox.Selected && !textBox.Readonly && string.IsNullOrWhiteSpace(textBox.Text);
                RefreshServerInfoSize();
                return true;
            };
            serverMessageBox.RectTransform.SizeChanged += RefreshServerInfoSize;
            motdCharacterCount.TextGetter += () => { return serverMessageBox.Text.Length + " / " + NetConfig.ServerMessageMaxLength; };

            void RefreshServerInfoSize()
            {
                serverMessageHint.Visible = !serverMessageBox.Selected && !serverMessageBox.Readonly && string.IsNullOrWhiteSpace(serverMessageBox.Text);
                Vector2 textSize = serverMessageBox.Font.MeasureString(serverMessageBox.WrappedText);
                serverMessageBox.RectTransform.NonScaledSize = new Point(serverMessageBox.RectTransform.NonScaledSize.X, Math.Max(serverMessageContainer.Content.Rect.Height, (int)textSize.Y + 10));
                serverMessageContainer.UpdateScrollBarSize();
            }

            serverMessageBox.OnEnterPressed += (textBox, text) =>
            {
                string str = textBox.Text;
                int caretIndex = textBox.CaretIndex;
                textBox.Text = $"{str[..caretIndex]}\n{str[caretIndex..]}";
                textBox.CaretIndex = caretIndex + 1;

                return true;
            };
            serverMessageBox.OnDeselected += (textBox, key) =>
            {
                if (!textBox.Readonly)
                {
                    GameMain.Client?.ServerSettings?.ClientAdminWrite(NetFlags.Properties);
                }
                serverMessageHint.Visible = !textBox.Readonly && string.IsNullOrWhiteSpace(textBox.Text);
            };
            serverMessageBox.OnKeyHit += (sender, key) => updateServerMessageScrollBasedOnCaret();

            // *************************************************************************

            var playStyleLayoutLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform),
                TextManager.Get("ServerSettingsPlayStyle"));
            var playStyleSelection = new GUISelectionCarousel<PlayStyle>(new RectTransform(new Vector2(0.5f, 1.0f), playStyleLayoutLabel.RectTransform, Anchor.CenterRight));
            foreach (PlayStyle playStyle in Enum.GetValues(typeof(PlayStyle)))
            {
                playStyleSelection.AddElement(playStyle, TextManager.Get("servertag." + playStyle), TextManager.Get("servertagdescription." + playStyle));
            }
            AssignGUIComponent(nameof(PlayStyle), playStyleSelection);

            var passwordLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform), TextManager.Get("Password"));
            new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), passwordLabel.RectTransform, Anchor.CenterRight),
                TextManager.Get("ServerSettingsSetPassword"), style: "GUIButtonSmall")
            {
                OnClicked = (btn, userdata) => { CreateChangePasswordPrompt(); return true; }
            };

            var wrongPasswordBanBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform), TextManager.Get("ServerSettingsBanAfterWrongPassword"));
            AssignGUIComponent(nameof(BanAfterWrongPassword), wrongPasswordBanBox); 
            var allowedPasswordRetries = NetLobbyScreen.CreateLabeledNumberInput(parent, "ServerSettingsPasswordRetriesBeforeBan", 0, 10);
            AssignGUIComponent(nameof(MaxPasswordRetriesBeforeBan), allowedPasswordRetries);

            var maxPlayers = NetLobbyScreen.CreateLabeledNumberInput(parent, "MaxPlayers", 0, NetConfig.MaxPlayers);
            AssignGUIComponent(nameof(MaxPlayers), maxPlayers);

            // Language
            var languageLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), parent.RectTransform), TextManager.Get("Language"));
            var languageDD = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1.0f), languageLabel.RectTransform, Anchor.CenterRight));
            foreach (var language in ServerLanguageOptions.Options)
            {
                languageDD.AddItem(language.Label, language.Identifier);
            }
            languageLabel.InheritTotalChildrenMinHeight();
            AssignGUIComponent(nameof(Language), languageDD);

        }

        private static void CreateChangePasswordPrompt()
        {
            var passwordMsgBox = new GUIMessageBox(
                TextManager.Get("ServerSettingsSetPassword"), 
                "", new LocalizedString[] { TextManager.Get("OK"), TextManager.Get("Cancel") },
                relativeSize: new Vector2(0.25f, 0.1f), minSize: new Point(400, GUI.IntScale(170)));
            var passwordHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), passwordMsgBox.Content.RectTransform), childAnchor: Anchor.TopCenter);
            var passwordBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1f), passwordHolder.RectTransform))
            {
                Censor = true
            };

            passwordMsgBox.Content.Recalculate();
            passwordMsgBox.Content.InheritTotalChildrenHeight();
            passwordMsgBox.Content.Parent.RectTransform.MinSize = new Point(0, (int)(passwordMsgBox.Content.RectTransform.MinSize.Y / passwordMsgBox.Content.RectTransform.RelativeSize.Y));

            var okButton = passwordMsgBox.Buttons[0];
            okButton.OnClicked += (_, __) =>
            {
                DebugConsole.ExecuteCommand($"setpassword \"{passwordBox.Text}\"");
                return true;
            };
            okButton.OnClicked += passwordMsgBox.Close;

            var cancelButton = passwordMsgBox.Buttons[1];
            cancelButton.OnClicked = (_, __) =>
            {
                passwordMsgBox?.Close(); 
                passwordMsgBox = null;
                return true;
            };
            passwordBox.OnEnterPressed += (_, __) =>
            {
                okButton.OnClicked.Invoke(okButton, okButton.UserData);
                return true;
            };

            passwordBox.Select();
        }

        private void CreateGeneralTab(GUIComponent parent)
        {
            var listBox = new GUIListBox(new RectTransform(Vector2.One, parent.RectTransform), style: "GUIListBoxNoBorder")
            {
                AutoHideScrollBar = true,
                CurrentSelectMode = GUIListBox.SelectMode.None
            };

            NetLobbyScreen.CreateSubHeader("serversettingscategory.roundmanagement", listBox.Content);

            var endVoteBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform),
                TextManager.Get("ServerSettingsEndRoundVoting"));
            AssignGUIComponent(nameof(AllowEndVoting), endVoteBox);

            NetLobbyScreen.CreateLabeledSlider(listBox.Content, headerTag: string.Empty, valueLabelTag: "ServerSettingsEndRoundVotesRequired", tooltipTag: string.Empty, out var slider, out var sliderLabel);

            LocalizedString endRoundLabel = sliderLabel.Text;
            slider.Step = 0.2f;
            slider.Range = new Vector2(0.5f, 1.0f);
            AssignGUIComponent(nameof(EndVoteRequiredRatio), slider);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = endRoundLabel + " " + (int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 10.0f) + " %";
                return true;
            };
            slider.OnMoved(slider, slider.BarScroll);

            //***********************************************

            // Sub Selection

            var subSelectionLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform),
                TextManager.Get("ServerSettingsSubSelection"));
            var subSelection = new GUISelectionCarousel<SelectionMode>(new RectTransform(new Vector2(0.5f, 1.0f), subSelectionLabel.RectTransform, Anchor.CenterRight));
            foreach (SelectionMode selectionMode in Enum.GetValues(typeof(SelectionMode)))
            {
                subSelection.AddElement(selectionMode, TextManager.Get(selectionMode.ToString()));
            }
            AssignGUIComponent(nameof(SubSelectionMode), subSelection);

            // Mode Selection
            var gameModeSelectionLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform),
                TextManager.Get("ServerSettingsModeSelection"));
            var gameModeSelection = new GUISelectionCarousel<SelectionMode>(new RectTransform(new Vector2(0.5f, 1.0f), gameModeSelectionLabel.RectTransform, Anchor.CenterRight));
            foreach (SelectionMode selectionMode in Enum.GetValues(typeof(SelectionMode)))
            {
                gameModeSelection.AddElement(selectionMode, TextManager.Get(selectionMode.ToString()));
            }
            AssignGUIComponent(nameof(ModeSelectionMode), gameModeSelection);

            //***********************************************

            LocalizedString autoRestartDelayLabel = TextManager.Get("ServerSettingsAutoRestartDelay") + " ";

            var autorestartBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform),
                TextManager.Get("AutoRestart"));
            AssignGUIComponent(nameof(AutoRestart), autorestartBox);
            NetLobbyScreen.CreateLabeledSlider(listBox.Content, headerTag: string.Empty, valueLabelTag: string.Empty, tooltipTag: string.Empty,
                out var startIntervalSlider, out var startIntervalSliderLabel, range: new Vector2(10.0f, 300.0f));
            startIntervalSlider.StepValue = 10.0f;
            startIntervalSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock text = scrollBar.UserData as GUITextBlock;
                text.Text = autoRestartDelayLabel + ToolBox.SecondsToReadableTime(scrollBar.BarScrollValue);
                return true;
            };
            AssignGUIComponent(nameof(AutoRestartInterval), startIntervalSlider);
            startIntervalSlider.OnMoved(startIntervalSlider, startIntervalSlider.BarScroll);

            //***********************************************

            var startWhenClientsReady = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform),
                TextManager.Get("ServerSettingsStartWhenClientsReady"));
            AssignGUIComponent(nameof(StartWhenClientsReady), startWhenClientsReady);

            NetLobbyScreen.CreateLabeledSlider(listBox.Content, headerTag: string.Empty, valueLabelTag: "ServerSettingsStartWhenClientsReadyRatio", tooltipTag: string.Empty,
                out slider, out sliderLabel);
            LocalizedString clientsReadyRequiredLabel = sliderLabel.Text;
            slider.Step = 0.2f;
            slider.Range = new Vector2(0.5f, 1.0f);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = clientsReadyRequiredLabel.Replace("[percentage]", ((int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 10.0f)).ToString());
                return true;
            };
            AssignGUIComponent(nameof(StartWhenClientsReadyRatio), slider);
            slider.OnMoved(slider, slider.BarScroll);

            var randomizeLevelBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform), TextManager.Get("ServerSettingsRandomizeSeed"));
            AssignGUIComponent(nameof(RandomizeSeed), randomizeLevelBox);

            //***********************************************

            NetLobbyScreen.CreateSubHeader("serversettingsroundstab", listBox.Content);

            var voiceChatEnabled = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform),
                TextManager.Get("ServerSettingsVoiceChatEnabled"));
            AssignGUIComponent(nameof(VoiceChatEnabled), voiceChatEnabled);

            var allowSpecBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform), TextManager.Get("ServerSettingsAllowSpectating"));
            AssignGUIComponent(nameof(AllowSpectating), allowSpecBox);

            var losModeLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform),
                TextManager.Get("LosEffect"));
            var losModeSelection = new GUISelectionCarousel<LosMode>(new RectTransform(new Vector2(0.5f, 0.6f), losModeLabel.RectTransform, Anchor.CenterRight));
            foreach (var losMode in Enum.GetValues(typeof(LosMode)).Cast<LosMode>())
            {
                losModeSelection.AddElement(losMode, TextManager.Get($"LosMode{losMode}"), TextManager.Get($"LosMode{losMode}.tooltip"));
            }
            AssignGUIComponent(nameof(LosMode), losModeSelection);

            var healthBarModeLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform),
                TextManager.Get("ShowEnemyHealthBars"));
            var healthBarModeSelection = new GUISelectionCarousel<EnemyHealthBarMode>(new RectTransform(new Vector2(0.5f, 0.6f), healthBarModeLabel.RectTransform, Anchor.CenterRight));
            foreach (var healthBarMode in Enum.GetValues(typeof(EnemyHealthBarMode)).Cast<EnemyHealthBarMode>())
            {
                healthBarModeSelection.AddElement(healthBarMode, TextManager.Get($"ShowEnemyHealthBars.{healthBarMode}"));
            }
            AssignGUIComponent(nameof(ShowEnemyHealthBars), healthBarModeSelection);

            var disableBotConversationsBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform), TextManager.Get("ServerSettingsDisableBotConversations"));
            AssignGUIComponent(nameof(DisableBotConversations), disableBotConversationsBox);

            //***********************************************

            NetLobbyScreen.CreateSubHeader("serversettingscategory.misc", listBox.Content);

            var shareSubsBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform), TextManager.Get("ServerSettingsShareSubFiles"));
            AssignGUIComponent(nameof(AllowFileTransfers), shareSubsBox);

            var saveLogsBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform), TextManager.Get("ServerSettingsSaveLogs"))
            {
                OnSelected = (GUITickBox) =>
                {
                    //TODO: fix?
                    //showLogButton.Visible = SaveServerLogs;
                    return true;
                }
            };
            AssignGUIComponent(nameof(SaveServerLogs), saveLogsBox);

            LocalizedString newCampaignDefaultSalaryLabel = TextManager.Get("ServerSettingsNewCampaignDefaultSalary");
            NetLobbyScreen.CreateLabeledSlider(listBox.Content, headerTag: "ServerSettingsNewCampaignDefaultSalary", valueLabelTag: "ServerSettingsKickVotesRequired", tooltipTag: "ServerSettingsNewCampaignDefaultSalaryToolTip", 
                out var defaultSalarySlider, out var defaultSalarySliderLabel);
            defaultSalarySlider.Range = new Vector2(0, 100);
            defaultSalarySlider.StepValue = 1;
            defaultSalarySlider.OnMoved = (scrollBar, _) =>
            {
                if (scrollBar.UserData is not GUITextBlock text) { return false; }
                text.Text = TextManager.AddPunctuation(
                    ':',
                    newCampaignDefaultSalaryLabel,
                    TextManager.GetWithVariable("percentageformat", "[value]", ((int)Math.Round(scrollBar.BarScrollValue, digits: 0)).ToString()));
                return true;
            };
            AssignGUIComponent(nameof(NewCampaignDefaultSalary), defaultSalarySlider);
            defaultSalarySlider.OnMoved(defaultSalarySlider, defaultSalarySlider.BarScroll);

            //--------------------------------------------------------------------------------
            //                              game settings 
            //--------------------------------------------------------------------------------

            GUILayoutGroup buttonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), listBox.Content.RectTransform), isHorizontal: true, childAnchor: Anchor.BottomLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            const string MonstersEnabledUserdata = "monstersenabled";
            const string ExtraCargoUserdata = "extracargo";

            monstersEnabledButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonHolder.RectTransform),
                TextManager.Get("ServerSettingsMonsterSpawns"), style: "GUIButtonSmall")
            {
                Enabled = !GameMain.NetworkMember.GameStarted
            };
            monstersEnabledPanel = CreateMonstersEnabledPanel();
            monstersEnabledButton.UserData = MonstersEnabledUserdata;
            monstersEnabledButton.OnClicked = ExtraSettingsButtonClicked;

            extraCargoButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonHolder.RectTransform),
                TextManager.Get("ServerSettingsAdditionalCargo"), style: "GUIButtonSmall")
            {
                Enabled = !GameMain.NetworkMember.GameStarted
            };

            extraCargoPanel = CreateExtraCargoPanel();
            extraCargoButton.UserData = ExtraCargoUserdata;
            extraCargoButton.OnClicked = ExtraSettingsButtonClicked;

            GUITextBlock.AutoScaleAndNormalize(buttonHolder.Children.Select(c => ((GUIButton)c).TextBlock));

            bool ExtraSettingsButtonClicked(GUIButton button, object obj)
            {
                //the extra settings buttons (monsters enabled, cargo) hold a reference to the panel they're supposed to toggle
                GUIComponent panel;
                switch (obj as string)
                {
                    case MonstersEnabledUserdata:
                        panel = monstersEnabledPanel;
                        break;
                    case ExtraCargoUserdata:
                        panel = extraCargoPanel;
                        break;
                    default:
                        throw new Exception("Unrecognized extra settings button");
                }
                if (GameMain.NetworkMember.GameStarted)
                {
                    panel.Visible = false;
                    button.Enabled = false;
                    return true;
                }
                panel.Visible = !panel.Visible;
                return true;
            }
        }

        private GUIComponent CreateMonstersEnabledPanel()
        {
            var monsterFrame = new GUIListBox(new RectTransform(new Vector2(0.5f, 0.7f), settingsTabs[SettingsTab.General].RectTransform, Anchor.BottomLeft, Pivot.BottomRight))
            {
                Visible = false,
                IgnoreLayoutGroups = true
            };

            InitMonstersEnabled();
            List<Identifier> monsterNames = MonsterEnabled.Keys.ToList();
            tempMonsterEnabled = new Dictionary<Identifier, bool>(MonsterEnabled);
            foreach (Identifier s in monsterNames)
            {
                LocalizedString translatedLabel = TextManager.Get($"Character.{s}").Fallback(s.Value);
                var monsterEnabledBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), monsterFrame.Content.RectTransform) { MinSize = new Point(0, 25) },
                    label: translatedLabel)
                {
                    Selected = tempMonsterEnabled[s],
                    OnSelected = (GUITickBox tb) =>
                    {
                        tempMonsterEnabled[s] = tb.Selected;
                        return true;
                    }
                };
            }
            monsterFrame.Content.RectTransform.SortChildren((c1, c2) =>
            {
                var name1 = (c1.GUIComponent as GUITickBox)?.Text ?? string.Empty;
                var name2 = (c2.GUIComponent as GUITickBox)?.Text ?? string.Empty;
                return name1.CompareTo(name2);
            });

            if (GameMain.Client == null ||
                !GameMain.Client.HasPermission(Networking.ClientPermissions.ManageSettings))
            {
                SetElementInteractability(monsterFrame.Content, false);                
            }

            return monsterFrame;
        }

        private GUIComponent CreateExtraCargoPanel()
        {
            var cargoFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.7f), settingsTabs[SettingsTab.General].RectTransform, Anchor.BottomRight, Pivot.BottomLeft))
            {
                Visible = false,
                IgnoreLayoutGroups = true
            };
            var cargoContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), cargoFrame.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            var filterText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), cargoContent.RectTransform), TextManager.Get("serverlog.filter"), font: GUIStyle.SubHeadingFont);
            var entityFilterBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), filterText.RectTransform, Anchor.CenterRight), font: GUIStyle.Font, createClearButton: true);
            filterText.RectTransform.MinSize = new Point(0, entityFilterBox.RectTransform.MinSize.Y);
            var cargoList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.8f), cargoContent.RectTransform));
            entityFilterBox.OnTextChanged += (textBox, text) =>
            {
                foreach (var child in cargoList.Content.Children)
                {
                    if (child.UserData is not ItemPrefab itemPrefab) { continue; }
                    child.Visible = string.IsNullOrEmpty(text) || itemPrefab.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
                }
                return true;
            };

            foreach (ItemPrefab ip in ItemPrefab.Prefabs.OrderBy(ip => ip.Name))
            {
                if (ip.AllowAsExtraCargo.HasValue)
                {
                    if (!ip.AllowAsExtraCargo.Value) { continue; }
                }
                else
                {
                    if (!ip.CanBeBought) { continue; }
                }

                var itemFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), cargoList.Content.RectTransform) { MinSize = new Point(0, 30) }, isHorizontal: true)
                {
                    Stretch = true,
                    UserData = ip,
                    RelativeSpacing = 0.05f
                };

                if (ip.InventoryIcon != null || ip.Sprite != null)
                {
                    GUIImage img = new GUIImage(new RectTransform(new Point(itemFrame.Rect.Height), itemFrame.RectTransform),
                        ip.InventoryIcon ?? ip.Sprite, scaleToFit: true)
                    {
                        CanBeFocused = false
                    };
                    img.Color = img.Sprite == ip.InventoryIcon ? ip.InventoryIconColor : ip.SpriteColor;
                }

                new GUITextBlock(new RectTransform(new Vector2(0.75f, 1.0f), itemFrame.RectTransform),
                    ip.Name, font: GUIStyle.SmallFont)
                {
                    Wrap = true,
                    CanBeFocused = false
                };

                ExtraCargo.TryGetValue(ip, out int cargoVal);
                var amountInput = new GUINumberInput(new RectTransform(new Vector2(0.35f, 1.0f), itemFrame.RectTransform),
                    NumberType.Int, textAlignment: Alignment.CenterLeft)
                {
                    MinValueInt = 0,
                    MaxValueInt = MaxExtraCargoItemsOfType,
                    IntValue = cargoVal
                };
                amountInput.OnValueChanged += (numberInput) =>
                {
                    if (ExtraCargo.ContainsKey(ip))
                    {
                        ExtraCargo[ip] = numberInput.IntValue;
                        if (numberInput.IntValue <= 0) { ExtraCargo.Remove(ip); }
                    }
                    else if (ExtraCargo.Keys.Count < MaxExtraCargoItemTypes)
                    {
                        ExtraCargo.Add(ip, numberInput.IntValue);
                    }
                    numberInput.IntValue = ExtraCargo.ContainsKey(ip) ? ExtraCargo[ip] : 0;
                    CoroutineManager.Invoke(() =>
                    {
                        foreach (var child in cargoList.Content.GetAllChildren())
                        {
                            if (child.GetChild<GUINumberInput>() is GUINumberInput otherNumberInput)
                            {
                                otherNumberInput.PlusButton.Enabled = ExtraCargo.Keys.Count < MaxExtraCargoItemTypes && otherNumberInput.IntValue < otherNumberInput.MaxValueInt;
                            }
                        }
                    }, 0.0f);
                };
            }
            if (GameMain.Client == null ||
                !GameMain.Client.HasPermission(Networking.ClientPermissions.ManageSettings))
            {
                SetElementInteractability(cargoList.Content, false);
            }

            return cargoFrame;
        }

        private void CreateAntigriefingTab(GUIComponent parent)
        {
            var listBox = new GUIListBox(new RectTransform(Vector2.One, parent.RectTransform), style: "GUIListBoxNoBorder")
            {
                AutoHideScrollBar = true,
                CurrentSelectMode = GUIListBox.SelectMode.None
            };

            //--------------------------------------------------------------------------------
            //                              antigriefing
            //--------------------------------------------------------------------------------

            var tickBoxContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.268f), listBox.Content.RectTransform))
            {
                AutoHideScrollBar = true,
                UseGridLayout = true
            };
            tickBoxContainer.Padding *= 2.0f;

            var allowFriendlyFire = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsAllowFriendlyFire"));
            AssignGUIComponent(nameof(AllowFriendlyFire), allowFriendlyFire);
            
            var allowDragAndDropGive = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsAllowDragAndDropGive"));
            AssignGUIComponent(nameof(AllowDragAndDropGive), allowDragAndDropGive);

            var killableNPCs = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsKillableNPCs"));
            AssignGUIComponent(nameof(KillableNPCs), killableNPCs);

            var destructibleOutposts = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsDestructibleOutposts"));
            AssignGUIComponent(nameof(DestructibleOutposts), destructibleOutposts);

            var lockAllDefaultWires = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsLockAllDefaultWires"));
            AssignGUIComponent(nameof(LockAllDefaultWires), lockAllDefaultWires);

            var allowRewiring = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsAllowRewiring"));
            AssignGUIComponent(nameof(AllowRewiring), allowRewiring);

            var allowWifiChatter = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsAllowWifiChat"));
            AssignGUIComponent(nameof(AllowLinkingWifiToChat), allowWifiChatter);

            var allowDisguises = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsAllowDisguises"));
            AssignGUIComponent(nameof(AllowDisguises), allowDisguises);

            var allowImmediateItemDeliveryBox = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsImmediateItemDelivery"));
            AssignGUIComponent(nameof(AllowImmediateItemDelivery), allowImmediateItemDeliveryBox);

            GUITextBlock.AutoScaleAndNormalize(tickBoxContainer.Content.Children.Select(c => ((GUITickBox)c).TextBlock));

            tickBoxContainer.RectTransform.MinSize = new Point(0, (int)(tickBoxContainer.Content.Children.First().Rect.Height * 2.0f + tickBoxContainer.Padding.Y + tickBoxContainer.Padding.W));

            tickBoxContainer.RectTransform.MinSize = new Point(0, (int)(tickBoxContainer.Content.Children.First().Rect.Height * 2.0f + tickBoxContainer.Padding.Y + tickBoxContainer.Padding.W));

            var voteKickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform),
                TextManager.Get("ServerSettingsAllowVoteKick"));
            AssignGUIComponent(nameof(AllowVoteKick), voteKickBox);

            NetLobbyScreen.CreateLabeledSlider(listBox.Content, headerTag: string.Empty, valueLabelTag: "ServerSettingsKickVotesRequired", tooltipTag: string.Empty, out var slider, out var sliderLabel);
            LocalizedString votesRequiredLabel = sliderLabel.Text + " ";
            slider.Step = 0.2f;
            slider.Range = new Vector2(0.5f, 1.0f);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = votesRequiredLabel + (int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 10.0f) + " %";
                return true;
            };
            AssignGUIComponent(nameof(KickVoteRequiredRatio), slider);
            slider.OnMoved(slider, slider.BarScroll);

            NetLobbyScreen.CreateLabeledSlider(listBox.Content, headerTag: string.Empty, valueLabelTag: "ServerSettingsAutobanTime", tooltipTag: "ServerSettingsAutobanTime.Tooltip", out slider, out sliderLabel);
            LocalizedString autobanLabel = sliderLabel.Text + " ";         
            slider.Range = new Vector2(0.0f, MaxAutoBanTime);  
            slider.StepValue = 60.0f * 15.0f; //15 minutes
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = autobanLabel + ToolBox.SecondsToReadableTime(scrollBar.BarScrollValue);
                return true;
            };
            AssignGUIComponent(nameof(AutoBanTime), slider);
            slider.OnMoved(slider, slider.BarScroll);

            var maximumTransferAmount = NetLobbyScreen.CreateLabeledNumberInput(listBox.Content, "serversettingsmaximumtransferrequest", 0, CampaignMode.MaxMoney, "serversettingsmaximumtransferrequesttooltip");
            AssignGUIComponent(nameof(MaximumMoneyTransferRequest), maximumTransferAmount);

            var lootedMoneyDestination = NetLobbyScreen.CreateLabeledDropdown(listBox.Content, "serversettingslootedmoneydestination", numElements: 2, "serversettingslootedmoneydestinationtooltip");
            lootedMoneyDestination.AddItem(TextManager.Get("lootedmoneydestination.bank"), LootedMoneyDestination.Bank);
            lootedMoneyDestination.AddItem(TextManager.Get("lootedmoneydestination.wallet"), LootedMoneyDestination.Wallet);
            AssignGUIComponent(nameof(LootedMoneyDestination), lootedMoneyDestination);

            var enableDosProtection = new GUITickBox(new RectTransform(new Vector2(0.5f, 0.0f), listBox.Content.RectTransform), TextManager.Get("ServerSettingsEnableDoSProtection"))
            {
                ToolTip = TextManager.Get("ServerSettingsEnableDoSProtectionTooltip")
            };
            AssignGUIComponent(nameof(EnableDoSProtection), enableDosProtection);

            NetLobbyScreen.CreateLabeledSlider(listBox.Content, headerTag: string.Empty, valueLabelTag: "ServerSettingsMaxPacketAmount", tooltipTag: string.Empty, out GUIScrollBar maxPacketSlider, out GUITextBlock maxPacketSliderLabel);
            LocalizedString maxPacketCountLabel = maxPacketSliderLabel.Text;
            maxPacketSlider.Step = 0.001f;
            maxPacketSlider.Range = new Vector2(PacketLimitMin, PacketLimitMax);
            maxPacketSlider.ToolTip = packetAmountTooltip;
            maxPacketSlider.OnMoved = (scrollBar, _) =>
            {
                GUITextBlock textBlock = (GUITextBlock)scrollBar.UserData;
                int value = (int)MathF.Floor(scrollBar.BarScrollValue);

                LocalizedString valueText = value > PacketLimitMin
                    ? value.ToString()
                    : TextManager.Get("ServerSettingsNoLimit");

                switch (value)
                {
                    case <= PacketLimitMin:
                        textBlock.TextColor = GUIStyle.Green;
                        scrollBar.ToolTip = packetAmountTooltip;
                        break;
                    case < PacketLimitWarning:
                        textBlock.TextColor = GUIStyle.Red;
                        scrollBar.ToolTip = packetAmountTooltipWarning;
                        break;
                    default:
                        textBlock.TextColor = GUIStyle.TextColorNormal;
                        scrollBar.ToolTip = packetAmountTooltip;
                        break;
                }

                textBlock.Text = $"{maxPacketCountLabel} {valueText}";
                return true;
            };
            AssignGUIComponent(nameof(MaxPacketAmount), maxPacketSlider);
            maxPacketSlider.OnMoved(maxPacketSlider, maxPacketSlider.BarScroll);

            // karma --------------------------------------------------------------------------

            NetLobbyScreen.CreateSubHeader("Karma", listBox.Content, toolTipTag: "KarmaExplanation");

            var karmaBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 1f), listBox.Content.RectTransform), TextManager.Get("ServerSettingsUseKarma"))
            {
                ToolTip = TextManager.Get("KarmaExplanation")
            };
            AssignGUIComponent(nameof(KarmaEnabled), karmaBox);

            karmaPresetDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), listBox.Content.RectTransform));
            foreach (string karmaPreset in GameMain.NetworkMember.KarmaManager.Presets.Keys)
            {
                karmaPresetDD.AddItem(TextManager.Get("KarmaPreset." + karmaPreset), karmaPreset);
            }
            karmaElements.Add(karmaPresetDD);

            var karmaSettingsContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), listBox.Content.RectTransform), style: null);
            karmaElements.Add(karmaSettingsContainer);
            karmaSettingsList = new GUIListBox(new RectTransform(Vector2.One, karmaSettingsContainer.RectTransform))
            {
                Spacing = (int)(8 * GUI.Scale)
            };
            karmaSettingsList.Padding *= 2.0f;

            karmaPresetDD.SelectItem(KarmaPreset);
            SetElementInteractability(karmaSettingsList.Content, !karmaBox.Selected || KarmaPreset != "custom");
            GameMain.NetworkMember.KarmaManager.CreateSettingsFrame(karmaSettingsList.Content);
            karmaPresetDD.OnSelected = (selected, obj) =>
            {
                string newKarmaPreset = obj as string;
                if (newKarmaPreset == KarmaPreset) { return true; }

                List<NetPropertyData> properties = netProperties.Values.ToList();
                List<object> prevValues = new List<object>();
                foreach (NetPropertyData prop in netProperties.Values)
                {
                    prevValues.Add(prop.TempValue);
                    if (prop.GUIComponent != null) { prop.Value = prop.GUIComponentValue; }
                }
                if (KarmaPreset == "custom")
                {
                    GameMain.NetworkMember?.KarmaManager?.SaveCustomPreset();
                    GameMain.NetworkMember?.KarmaManager?.Save();
                }
                KarmaPreset = newKarmaPreset;
                GameMain.NetworkMember.KarmaManager.SelectPreset(KarmaPreset);
                karmaSettingsList.Content.ClearChildren();
                GameMain.NetworkMember.KarmaManager.CreateSettingsFrame(karmaSettingsList.Content);
                SetElementInteractability(karmaSettingsList.Content, !karmaBox.Selected || KarmaPreset != "custom");
                for (int i = 0; i < netProperties.Count; i++)
                {
                    properties[i].TempValue = prevValues[i];
                }
                return true;
            };
            AssignGUIComponent(nameof(KarmaPreset), karmaPresetDD);
            karmaBox.OnSelected = (tb) =>
            {
                SetElementInteractability(karmaSettingsList.Content, !karmaBox.Selected || KarmaPreset != "custom");
                karmaElements.ForEach(e => e.Visible = tb.Selected);
                return true;
            };
            karmaElements.ForEach(e => e.Visible = KarmaEnabled);

            listBox.Content.InheritTotalChildrenMinHeight();
        }

        private void CreateBanlistTab(GUIComponent parent)
        {
            BanList.CreateBanFrame(parent);
        }

        private bool SelectSettingsTab(GUIButton button, object obj)
        {
            selectedTab = (SettingsTab)obj;
            foreach (var key in settingsTabs.Keys)
            {
                settingsTabs[key].Visible = key == selectedTab;
                tabButtons[key].Selected = key == selectedTab;
            }
            return true;
        }

        public void Close()
        {
            if (KarmaPreset == "custom")
            {
                GameMain.NetworkMember?.KarmaManager?.SaveCustomPreset();
                GameMain.NetworkMember?.KarmaManager?.Save();
            }
            ClientAdminWrite(NetFlags.Properties);
            foreach (NetPropertyData prop in netProperties.Values)
            {
                prop.GUIComponent = null;
            }
            settingsFrame = null;
            //give control of server settings back to elements in the lobby
            GameMain.NetLobbyScreen.AssignComponentsToServerSettings();
        }

        public bool ToggleSettingsFrame(GUIButton button, object obj)
        {
            if (GameMain.NetworkMember == null) { return false; }
            if (settingsFrame == null)
            {
                CreateSettingsFrame();
            }
            else
            {
                Close();
            }
            return false;
        }
    }
}
