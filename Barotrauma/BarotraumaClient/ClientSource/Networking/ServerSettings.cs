using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Barotrauma.Steam;

namespace Barotrauma.Networking
{
    partial class ServerSettings : ISerializableEntity
    {
        partial class NetPropertyData
        {
            public GUIComponent GUIComponent;
            public object TempValue;

            public void AssignGUIComponent(GUIComponent component)
            {
                GUIComponent = component;
                GUIComponentValue = property.GetValue(parentObject);
                TempValue = GUIComponentValue;
            }

            public object GUIComponentValue
            {
                get
                {
                    if (GUIComponent == null) return null;
                    else if (GUIComponent is GUITickBox tickBox) return tickBox.Selected;
                    else if (GUIComponent is GUITextBox textBox) return textBox.Text;
                    else if (GUIComponent is GUIScrollBar scrollBar) return scrollBar.BarScrollValue;
                    else if (GUIComponent is GUIRadioButtonGroup radioButtonGroup) return radioButtonGroup.Selected;
                    else if (GUIComponent is GUIDropDown dropdown) return dropdown.SelectedData;
                    else if (GUIComponent is GUINumberInput numInput)
                    {
                        if (numInput.InputType == NumberType.Int) { return numInput.IntValue; } else { return numInput.FloatValue; }
                    }
                    return null;
                }
                set
                {
                    if (GUIComponent == null) return;
                    else if (GUIComponent is GUITickBox tickBox) tickBox.Selected = (bool)value;
                    else if (GUIComponent is GUITextBox textBox) textBox.Text = (string)value;
                    else if (GUIComponent is GUIScrollBar scrollBar)
                    {
                        if (value.GetType() == typeof(int))
                        {
                            scrollBar.BarScrollValue = (int)value;
                        }
                        else
                        {
                            scrollBar.BarScrollValue = (float)value;
                        }
                    }
                    else if (GUIComponent is GUIRadioButtonGroup radioButtonGroup) radioButtonGroup.Selected = (int)value;
                    else if (GUIComponent is GUIDropDown dropdown) dropdown.SelectItem(value);
                    else if (GUIComponent is GUINumberInput numInput)
                    {
                        if (numInput.InputType == NumberType.Int)
                        {
                            numInput.IntValue = (int)value;
                        }
                        else
                        {
                            numInput.FloatValue = (float)value;
                        }
                    }
                }
            }

            public bool ChangedLocally
            {
                get
                {
                    if (GUIComponent == null) return false;
                    return !PropEquals(TempValue, GUIComponentValue);
                }
            }
        }
        private Dictionary<Identifier, bool> tempMonsterEnabled;

        partial void InitProjSpecific()
        {
            var properties = TypeDescriptor.GetProperties(GetType()).Cast<PropertyDescriptor>();

            SerializableProperties = new Dictionary<Identifier, SerializableProperty>();

            foreach (var property in properties)
            {
                SerializableProperty objProperty = new SerializableProperty(property);
                SerializableProperties.Add(property.Name.ToIdentifier(), objProperty);
            }
        }

        public void ClientAdminRead(IReadMessage incMsg)
        {
            while (true)
            {
                UInt32 key = incMsg.ReadUInt32();
                if (key == 0) { break; }
                if (netProperties.ContainsKey(key))
                {
                    bool changedLocally = netProperties[key].ChangedLocally;
                    netProperties[key].Read(incMsg);
                    netProperties[key].TempValue = netProperties[key].Value;

                    if (netProperties[key].GUIComponent != null)
                    {
                        if (!changedLocally)
                        {
                            netProperties[key].GUIComponentValue = netProperties[key].Value;
                        }
                    }
                }
                else
                {
                    UInt32 size = incMsg.ReadVariableUInt32();
                    incMsg.BitPosition += (int)(8 * size);
                }
            }

            ReadMonsterEnabled(incMsg);
            BanList.ClientAdminRead(incMsg);
        }

        public void ClientRead(IReadMessage incMsg)
        {
            NetFlags requiredFlags = (NetFlags)incMsg.ReadByte();

            if (requiredFlags.HasFlag(NetFlags.Name))
            {
                ServerName = incMsg.ReadString();
            }

            if (requiredFlags.HasFlag(NetFlags.Message))
            {
                ServerMessageText = incMsg.ReadString();
            }
            PlayStyle = (PlayStyle)incMsg.ReadByte();
            MaxPlayers = incMsg.ReadByte();
            HasPassword = incMsg.ReadBoolean();
            IsPublic = incMsg.ReadBoolean();
            GameMain.Client?.SetLobbyPublic(IsPublic);
            AllowFileTransfers = incMsg.ReadBoolean();
            incMsg.ReadPadBits();
            TickRate = incMsg.ReadRangedInteger(1, 60);

            if (requiredFlags.HasFlag(NetFlags.Properties))
            {
                ReadExtraCargo(incMsg);
            }

            if (requiredFlags.HasFlag(NetFlags.HiddenSubs))
            {
                ReadHiddenSubs(incMsg);
            }
            GameMain.NetLobbyScreen.UpdateSubVisibility();

            bool isAdmin = incMsg.ReadBoolean();
            incMsg.ReadPadBits();
            if (isAdmin)
            {
                ClientAdminRead(incMsg);
            }
        }

        public void ClientAdminWrite(
                NetFlags dataToSend,
                int? missionTypeOr = null,
                int? missionTypeAnd = null,
                float? levelDifficulty = null,
                bool? autoRestart = null,
                int traitorSetting = 0,
                int botCount = 0,
                int botSpawnMode = 0,
                bool? useRespawnShuttle = null)
        {
            if (!GameMain.Client.HasPermission(Networking.ClientPermissions.ManageSettings)) return;

            IWriteMessage outMsg = new WriteOnlyMessage();

            outMsg.WriteByte((byte)ClientPacketHeader.SERVER_SETTINGS);

            outMsg.WriteByte((byte)dataToSend);

            if (dataToSend.HasFlag(NetFlags.Name))
            {
                if (GameMain.NetLobbyScreen.ServerName.Text != ServerName)
                {
                    ServerName = GameMain.NetLobbyScreen.ServerName.Text;
                }
                outMsg.WriteString(ServerName);
            }

            if (dataToSend.HasFlag(NetFlags.Message))
            {
                if (GameMain.NetLobbyScreen.ServerMessage.Text != ServerMessageText)
                {
                    ServerMessageText = GameMain.NetLobbyScreen.ServerMessage.Text;
                }
                outMsg.WriteString(ServerMessageText);
            }

            if (dataToSend.HasFlag(NetFlags.Properties))
            {
                //TODO: split this up?
                WriteExtraCargo(outMsg);

                IEnumerable<KeyValuePair<UInt32, NetPropertyData>> changedProperties = netProperties.Where(kvp => kvp.Value.ChangedLocally);
                UInt32 count = (UInt32)changedProperties.Count();
                bool changedMonsterSettings = tempMonsterEnabled != null && tempMonsterEnabled.Any(p => p.Value != MonsterEnabled[p.Key]);

                outMsg.WriteUInt32(count);
                foreach (KeyValuePair<UInt32, NetPropertyData> prop in changedProperties)
                {
                    DebugConsole.NewMessage(prop.Value.Name.Value, Color.Lime);
                    outMsg.WriteUInt32(prop.Key);
                    prop.Value.Write(outMsg, prop.Value.GUIComponentValue);
                }

                outMsg.WriteBoolean(changedMonsterSettings); outMsg.WritePadBits();
                if (changedMonsterSettings) WriteMonsterEnabled(outMsg, tempMonsterEnabled);
                BanList.ClientAdminWrite(outMsg);
            }

            if (dataToSend.HasFlag(NetFlags.HiddenSubs))
            {
                WriteHiddenSubs(outMsg);
            }
            
            if (dataToSend.HasFlag(NetFlags.Misc))
            {
                outMsg.WriteRangedInteger(missionTypeOr ?? (int)Barotrauma.MissionType.None, 0, (int)Barotrauma.MissionType.All);
                outMsg.WriteRangedInteger(missionTypeAnd ?? (int)Barotrauma.MissionType.All, 0, (int)Barotrauma.MissionType.All);
                outMsg.WriteByte((byte)(traitorSetting + 1));
                outMsg.WriteByte((byte)(botCount + 1));
                outMsg.WriteByte((byte)(botSpawnMode + 1));

                outMsg.WriteSingle(levelDifficulty ?? -1000.0f);

                outMsg.WriteBoolean(useRespawnShuttle != null);
                outMsg.WriteBoolean(useRespawnShuttle ?? false);

                outMsg.WriteBoolean(autoRestart != null);
                outMsg.WriteBoolean(autoRestart ?? false);

                outMsg.WritePadBits();
            }

            if (dataToSend.HasFlag(NetFlags.LevelSeed))
            {
                outMsg.WriteString(GameMain.NetLobbyScreen.SeedBox.Text);
            }

            GameMain.Client.ClientPeer.Send(outMsg, DeliveryMethod.Reliable);
        }

        //GUI stuff
        private GUIFrame settingsFrame;
        private GUIFrame[] settingsTabs;
        private GUIButton[] tabButtons;
        private int settingsTabIndex;

        private GUIDropDown karmaPresetDD;
        private GUIComponent karmaSettingsBlocker;
        
        enum SettingsTab
        {
            General,
            Rounds,
            Antigriefing,
            Banlist
        }

        private NetPropertyData GetPropertyData(string name)
        {
            return netProperties.First(p => p.Value.Name == name).Value;
        }

        public void AssignGUIComponent(string propertyName, GUIComponent component)
        {
            GetPropertyData(propertyName).AssignGUIComponent(component);
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) return;

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
            GUILayoutGroup paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), innerFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform), TextManager.Get("Settings"), font: GUIStyle.LargeFont);

            var buttonArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.04f), paddedFrame.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            var tabContent = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.85f), paddedFrame.RectTransform), style: "InnerFrame");

            //tabs
            LocalizedString[] tabNames =
                Enum.GetValues(typeof(SettingsTab)).Cast<SettingsTab>()
                    .Select(tv => TextManager.Get("ServerSettings" + tv + "Tab")).ToArray();
            settingsTabs = new GUIFrame[tabNames.Length];
            tabButtons = new GUIButton[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
            {
                settingsTabs[i] = new GUIFrame(new RectTransform(Vector2.One, tabContent.RectTransform, Anchor.Center), style: null);
                tabButtons[i] = new GUIButton(new RectTransform(new Vector2(0.2f, 1.2f), buttonArea.RectTransform), tabNames[i], style: "GUITabButton")
                {
                    UserData = i,
                    OnClicked = SelectSettingsTab
                };
            }
            GUITextBlock.AutoScaleAndNormalize(tabButtons.Select(b => b.TextBlock));
            SelectSettingsTab(tabButtons[0], 0);

            //"Close"
            var buttonContainer = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.05f), paddedFrame.RectTransform), style: null);
            var closeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonContainer.RectTransform, Anchor.BottomRight), TextManager.Get("Close"))
            {
                OnClicked = ToggleSettingsFrame
            };


            //--------------------------------------------------------------------------------
            //                              server settings 
            //--------------------------------------------------------------------------------

            var serverTab = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), settingsTabs[(int)SettingsTab.General].RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            //***********************************************

            // Language
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), serverTab.RectTransform), TextManager.Get("Language"), font: GUIStyle.SubHeadingFont);
            var languageDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.02f), serverTab.RectTransform));
            foreach (var language in ServerLanguageOptions.Options)
            {
                languageDD.AddItem(language.Label, language.Identifier);
            }
            GetPropertyData(nameof(Language)).AssignGUIComponent(languageDD);

            //changing server visibility on the fly is not supported in dedicated servers
            if (GameMain.Client?.ClientPeer is not LidgrenClientPeer)
            {
                var isPublic = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform),
                    TextManager.Get("publicserver"))
                {
                    ToolTip = TextManager.Get("publicservertooltip")
                };
                GetPropertyData(nameof(IsPublic)).AssignGUIComponent(isPublic);
            }

            // Sub Selection
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), serverTab.RectTransform), TextManager.Get("ServerSettingsSubSelection"), font: GUIStyle.SubHeadingFont);
            var selectionFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.02f), serverTab.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            GUIRadioButtonGroup selectionMode = new GUIRadioButtonGroup();
            for (int i = 0; i < 3; i++)
            {
                var selectionTick = new GUITickBox(new RectTransform(new Vector2(0.3f, 1.0f), selectionFrame.RectTransform), TextManager.Get(((SelectionMode)i).ToString()), font: GUIStyle.SmallFont, style: "GUIRadioButton");
                selectionMode.AddRadioButton(i, selectionTick);
            }
            selectionFrame.RectTransform.NonScaledSize = new Point(selectionFrame.Rect.Width, selectionFrame.Children.First().Rect.Height);
            selectionFrame.RectTransform.IsFixedSize = true;

            GetPropertyData(nameof(SubSelectionMode)).AssignGUIComponent(selectionMode);

            // Mode Selection
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), serverTab.RectTransform), TextManager.Get("ServerSettingsModeSelection"), font: GUIStyle.SubHeadingFont);
            selectionFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.02f), serverTab.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            selectionMode = new GUIRadioButtonGroup();
            for (int i = 0; i < 3; i++)
            {
                var selectionTick = new GUITickBox(new RectTransform(new Vector2(0.3f, 1.0f), selectionFrame.RectTransform), TextManager.Get(((SelectionMode)i).ToString()), font: GUIStyle.SmallFont, style: "GUIRadioButton");
                selectionMode.AddRadioButton(i, selectionTick);
            }
            selectionFrame.RectTransform.NonScaledSize = new Point(selectionFrame.Rect.Width, selectionFrame.Children.First().Rect.Height);
            selectionFrame.RectTransform.IsFixedSize = true;
            GetPropertyData(nameof(ModeSelectionMode)).AssignGUIComponent(selectionMode);

            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), serverTab.RectTransform), style: "HorizontalLine");

            //***********************************************

            var voiceChatEnabled = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform),
                TextManager.Get("ServerSettingsVoiceChatEnabled"));
            GetPropertyData(nameof(VoiceChatEnabled)).AssignGUIComponent(voiceChatEnabled);

            //***********************************************

            LocalizedString autoRestartDelayLabel = TextManager.Get("ServerSettingsAutoRestartDelay") + " ";
            var startIntervalText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), serverTab.RectTransform), autoRestartDelayLabel);
            var startIntervalSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), barSize: 0.1f, style: "GUISlider")
            {
                UserData = startIntervalText,
                Step = 0.05f,
                OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                {
                    GUITextBlock text = scrollBar.UserData as GUITextBlock;
                    text.Text = autoRestartDelayLabel + ToolBox.SecondsToReadableTime(scrollBar.BarScrollValue);
                    return true;
                }
            };
            startIntervalSlider.Range = new Vector2(10.0f, 300.0f);
            GetPropertyData(nameof(AutoRestartInterval)).AssignGUIComponent(startIntervalSlider);
            startIntervalSlider.OnMoved(startIntervalSlider, startIntervalSlider.BarScroll);

            //***********************************************

            var startWhenClientsReady = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform),
                TextManager.Get("ServerSettingsStartWhenClientsReady"));
            GetPropertyData(nameof(StartWhenClientsReady)).AssignGUIComponent(startWhenClientsReady);

            CreateLabeledSlider(serverTab, "ServerSettingsStartWhenClientsReadyRatio", out GUIScrollBar slider, out GUITextBlock sliderLabel);
            LocalizedString clientsReadyRequiredLabel = sliderLabel.Text;
            slider.Step = 0.2f;
            slider.Range = new Vector2(0.5f, 1.0f);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = clientsReadyRequiredLabel.Replace("[percentage]", ((int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 10.0f)).ToString());
                return true;
            };
            GetPropertyData(nameof(StartWhenClientsReadyRatio)).AssignGUIComponent(slider);
            slider.OnMoved(slider, slider.BarScroll);

            //***********************************************

            var allowSpecBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsAllowSpectating"));
            GetPropertyData(nameof(AllowSpectating)).AssignGUIComponent(allowSpecBox);

            var shareSubsBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsShareSubFiles"));
            GetPropertyData(nameof(AllowFileTransfers)).AssignGUIComponent(shareSubsBox);

            var randomizeLevelBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsRandomizeSeed"));
            GetPropertyData(nameof(RandomizeSeed)).AssignGUIComponent(randomizeLevelBox);

            var saveLogsBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsSaveLogs"))
            {
                OnSelected = (GUITickBox) =>
                {
                    //TODO: fix?
                    //showLogButton.Visible = SaveServerLogs;
                    return true;
                }
            };
            GetPropertyData(nameof(SaveServerLogs)).AssignGUIComponent(saveLogsBox);

            //--------------------------------------------------------------------------------
            //                              game settings 
            //--------------------------------------------------------------------------------

            var roundsTab = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), settingsTabs[(int)SettingsTab.Rounds].RectTransform, Anchor.Center));
            var roundsContent = new GUIListBox(new RectTransform(Vector2.One, roundsTab.RectTransform, Anchor.Center), style: "GUIListBoxNoBorder").Content;

            GUILayoutGroup playStyleLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.2f), roundsContent.RectTransform));
            // Play Style Selection
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), playStyleLayout.RectTransform), TextManager.Get("ServerSettingsPlayStyle"), font: GUIStyle.SubHeadingFont);
            var playstyleList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.7f), playStyleLayout.RectTransform))
            {                
                AutoHideScrollBar = true,
                UseGridLayout = true
            };
            playstyleList.Padding *= 2.0f;

            List<GUITickBox> playStyleTickBoxes = new List<GUITickBox>();
            GUIRadioButtonGroup selectionPlayStyle = new GUIRadioButtonGroup();
            foreach (PlayStyle playStyle in Enum.GetValues(typeof(PlayStyle)))
            {
                var selectionTick = new GUITickBox(new RectTransform(new Vector2(0.32f, 0.49f), playstyleList.Content.RectTransform), TextManager.Get("servertag." + playStyle), font: GUIStyle.SmallFont, style: "GUIRadioButton")
                {
                    ToolTip = TextManager.Get("servertagdescription." + playStyle)
                };
                selectionPlayStyle.AddRadioButton((int)playStyle, selectionTick);
                playStyleTickBoxes.Add(selectionTick);
            }
            GetPropertyData(nameof(PlayStyle)).AssignGUIComponent(selectionPlayStyle);
            GUITextBlock.AutoScaleAndNormalize(playStyleTickBoxes.Select(t => t.TextBlock));
            playstyleList.RectTransform.MinSize = new Point(0, (int)(playstyleList.Content.Children.First().Rect.Height * 2.0f + playstyleList.Padding.Y + playstyleList.Padding.W));

            GUILayoutGroup sliderLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.35f), roundsContent.RectTransform))
            {
                Stretch = true
            };

            var endVoteBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), sliderLayout.RectTransform),
                TextManager.Get("ServerSettingsEndRoundVoting"));
            GetPropertyData(nameof(AllowEndVoting)).AssignGUIComponent(endVoteBox);

            CreateLabeledSlider(sliderLayout, "ServerSettingsEndRoundVotesRequired", out slider, out sliderLabel);

            LocalizedString endRoundLabel = sliderLabel.Text;
            slider.Step = 0.2f;
            slider.Range = new Vector2(0.5f, 1.0f);
            GetPropertyData(nameof(EndVoteRequiredRatio)).AssignGUIComponent(slider);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = endRoundLabel + " " + (int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 10.0f) + " %";
                return true;
            };
            slider.OnMoved(slider, slider.BarScroll);

            var respawnBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), sliderLayout.RectTransform),
                TextManager.Get("ServerSettingsAllowRespawning"));
            GetPropertyData(nameof(AllowRespawn)).AssignGUIComponent(respawnBox);

            CreateLabeledSlider(sliderLayout, "ServerSettingsRespawnInterval", out slider, out sliderLabel);
            LocalizedString intervalLabel = sliderLabel.Text;
            slider.Range = new Vector2(10.0f, 600.0f);
            slider.StepValue = 10.0f;
            GetPropertyData(nameof(RespawnInterval)).AssignGUIComponent(slider);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock text = scrollBar.UserData as GUITextBlock;
                text.Text = intervalLabel + " " + ToolBox.SecondsToReadableTime(scrollBar.BarScrollValue);
                return true;
            };
            slider.OnMoved(slider, slider.BarScroll);

            var respawnLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), sliderLayout.RectTransform),
                isHorizontal: true);

            var minRespawnLayout
                = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), respawnLayout.RectTransform));

            var minRespawnText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), minRespawnLayout.RectTransform), "")
            {
                ToolTip = TextManager.Get("ServerSettingsMinRespawnToolTip")
            };

            LocalizedString minRespawnLabel = TextManager.Get("ServerSettingsMinRespawn") + " ";
            CreateLabeledSlider(minRespawnLayout, "", out slider, out sliderLabel);
            sliderLabel.RectTransform.RelativeSize = Vector2.Zero;
            slider.RectTransform.RelativeSize = new Vector2(1.0f, 0.5f);
            slider.ToolTip = minRespawnText.ToolTip;
            slider.UserData = minRespawnText;
            slider.Step = 0.1f;
            slider.Range = new Vector2(0.0f, 1.0f);
            GetPropertyData(nameof(MinRespawnRatio)).AssignGUIComponent(slider);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = minRespawnLabel + (int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 10.0f) + " %";
                return true;
            };
            slider.OnMoved(slider, MinRespawnRatio);

            var respawnDurationLayout
                = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), respawnLayout.RectTransform));

            var respawnDurationText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), respawnDurationLayout.RectTransform), "")
            {
                ToolTip = TextManager.Get("ServerSettingsRespawnDurationToolTip")
            };

            LocalizedString respawnDurationLabel = TextManager.Get("ServerSettingsRespawnDuration") + " ";
            CreateLabeledSlider(respawnDurationLayout, "", out slider, out sliderLabel);
            sliderLabel.RectTransform.RelativeSize = Vector2.Zero;
            slider.RectTransform.RelativeSize = new Vector2(1.0f, 0.5f);
            slider.ToolTip = respawnDurationText.ToolTip;
            slider.UserData = respawnDurationText;
            slider.Step = 0.1f;
            slider.Range = new Vector2(60.0f, 660.0f);
            slider.ScrollToValue = (GUIScrollBar scrollBar, float barScroll) =>
            {
                return barScroll >= 1.0f ? 0.0f : barScroll * (scrollBar.Range.Y - scrollBar.Range.X) + scrollBar.Range.X;
            };
            slider.ValueToScroll = (GUIScrollBar scrollBar, float value) =>
            {
                return value <= 0.0f ? 1.0f : (value - scrollBar.Range.X) / (scrollBar.Range.Y - scrollBar.Range.X);
            };
            GetPropertyData(nameof(MaxTransportTime)).AssignGUIComponent(slider);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                if (barScroll == 1.0f)
                {
                    ((GUITextBlock)scrollBar.UserData).Text = respawnDurationLabel + TextManager.Get("Unlimited");
                }
                else
                {
                    ((GUITextBlock)scrollBar.UserData).Text = respawnDurationLabel + ToolBox.SecondsToReadableTime(scrollBar.BarScrollValue);
                }

                return true;
            };
            slider.OnMoved(slider, slider.BarScroll);

            GUILayoutGroup losModeLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.14f), roundsContent.RectTransform));

            var losModeLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), losModeLayout.RectTransform),
                TextManager.Get("LosEffect"));

            var losModeRadioButtonLayout
                = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.6f), losModeLayout.RectTransform),
                    isHorizontal: true)
                {
                    Stretch = true
                };

            var losModeRadioButtonGroup = new GUIRadioButtonGroup();
            LosMode[] losModes = (LosMode[])Enum.GetValues(typeof(LosMode));
            for (int i = 0; i < losModes.Length; i++)
            {
                var losTick = new GUITickBox(new RectTransform(new Vector2(0.3f, 1.0f), losModeRadioButtonLayout.RectTransform), TextManager.Get($"LosMode{losModes[i]}"), font: GUIStyle.SmallFont, style: "GUIRadioButton");
                losModeRadioButtonGroup.AddRadioButton(i, losTick);
            }
            GetPropertyData(nameof(LosMode)).AssignGUIComponent(losModeRadioButtonGroup);

            GUILayoutGroup healthBarModeLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.14f), roundsContent.RectTransform));

            var healthBarModeLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), healthBarModeLayout.RectTransform),
                TextManager.Get("ShowEnemyHealthBars"));

            var healthBarModeRadioButtonLayout
                = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.6f), healthBarModeLayout.RectTransform),
                    isHorizontal: true)
                {
                    Stretch = true
                };

            var healthBarModeRadioButtonGroup = new GUIRadioButtonGroup();
            EnemyHealthBarMode[] healthBarModeModes = Enum.GetValues<EnemyHealthBarMode>();
            for (int i = 0; i < healthBarModeModes.Length; i++)
            {
                var losTick = new GUITickBox(new RectTransform(new Vector2(0.3f, 1.0f), healthBarModeRadioButtonLayout.RectTransform), 
                    TextManager.Get($"ShowEnemyHealthBars.{healthBarModeModes[i]}"), 
                    font: GUIStyle.SmallFont, style: "GUIRadioButton");
                healthBarModeRadioButtonGroup.AddRadioButton(i, losTick);
            }
            GetPropertyData(nameof(ShowEnemyHealthBars)).AssignGUIComponent(healthBarModeRadioButtonGroup);

            GUILayoutGroup numberLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.3f), roundsContent.RectTransform))
            {
                Stretch = true
            };

            var traitorsMinPlayerCount = CreateLabeledNumberInput(numberLayout, "ServerSettingsTraitorsMinPlayerCount", 1, 16, "ServerSettingsTraitorsMinPlayerCountToolTip");
            GetPropertyData(nameof(TraitorsMinPlayerCount)).AssignGUIComponent(traitorsMinPlayerCount);

            var maximumTransferAmount = CreateLabeledNumberInput(numberLayout, "serversettingsmaximumtransferrequest", 0, CampaignMode.MaxMoney, "serversettingsmaximumtransferrequesttooltip");
            GetPropertyData(nameof(MaximumMoneyTransferRequest)).AssignGUIComponent(maximumTransferAmount);

            var lootedMoneyDestination = CreateLabeledDropdown(numberLayout, "serversettingslootedmoneydestination", numElements: 2, "serversettingslootedmoneydestinationtooltip");
            lootedMoneyDestination.AddItem(TextManager.Get("lootedmoneydestination.bank"), LootedMoneyDestination.Bank);
            lootedMoneyDestination.AddItem(TextManager.Get("lootedmoneydestination.wallet"), LootedMoneyDestination.Wallet);
            GetPropertyData(nameof(LootedMoneyDestination)).AssignGUIComponent(lootedMoneyDestination);

            var ragdollButtonBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), numberLayout.RectTransform), TextManager.Get("ServerSettingsAllowRagdollButton"));
            GetPropertyData(nameof(AllowRagdollButton)).AssignGUIComponent(ragdollButtonBox);

            var disableBotConversationsBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), numberLayout.RectTransform), TextManager.Get("ServerSettingsDisableBotConversations"));
            GetPropertyData(nameof(DisableBotConversations)).AssignGUIComponent(disableBotConversationsBox);

            GUILayoutGroup buttonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), roundsContent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            var monsterButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonHolder.RectTransform),
                TextManager.Get("ServerSettingsMonsterSpawns"), style: "GUIButtonSmall")
            {
                Enabled = !GameMain.NetworkMember.GameStarted
            };
            var monsterFrame = new GUIListBox(new RectTransform(new Vector2(0.6f, 0.7f), settingsTabs[(int)SettingsTab.Rounds].RectTransform, Anchor.BottomLeft, Pivot.BottomRight))
            {
                Visible = false
            };
            monsterButton.UserData = monsterFrame;
            monsterButton.OnClicked = (button, obj) =>
            {
                if (GameMain.NetworkMember.GameStarted)
                {
                    ((GUIComponent)obj).Visible = false;
                    button.Enabled = false;
                    return true;
                }
                ((GUIComponent)obj).Visible = !((GUIComponent)obj).Visible;
                return true;
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

            var cargoButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonHolder.RectTransform),
                TextManager.Get("ServerSettingsAdditionalCargo"), style: "GUIButtonSmall")
            {
                Enabled = !GameMain.NetworkMember.GameStarted
            };

            var cargoFrame = new GUIFrame(new RectTransform(new Vector2(0.6f, 0.7f), settingsTabs[(int)SettingsTab.Rounds].RectTransform, Anchor.BottomRight, Pivot.BottomLeft))
            {
                Visible = false
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

            cargoButton.UserData = cargoFrame;
            cargoButton.OnClicked = (button, obj) =>
            {
                if (GameMain.NetworkMember.GameStarted)
                {
                    ((GUIComponent)obj).Visible = false;
                    button.Enabled = false;
                    return true;
                }
                ((GUIComponent)obj).Visible = !((GUIComponent)obj).Visible;
                return true;
            };

            GUITextBlock.AutoScaleAndNormalize(buttonHolder.Children.Select(c => ((GUIButton)c).TextBlock));

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
                    else if (ExtraCargo.Keys.Count() < MaxExtraCargoItemTypes)
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
                                otherNumberInput.PlusButton.Enabled = ExtraCargo.Keys.Count() < MaxExtraCargoItemTypes && otherNumberInput.IntValue < otherNumberInput.MaxValueInt;
                            }
                        }
                    }, 0.0f);
                };
            }

            //--------------------------------------------------------------------------------
            //                              antigriefing
            //--------------------------------------------------------------------------------

            var antigriefingTab = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), settingsTabs[(int)SettingsTab.Antigriefing].RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var tickBoxContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.16f), antigriefingTab.RectTransform))
            {
                AutoHideScrollBar = true,
                UseGridLayout = true
            };
            tickBoxContainer.Padding *= 2.0f;

            var allowFriendlyFire = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsAllowFriendlyFire"));
            GetPropertyData(nameof(AllowFriendlyFire)).AssignGUIComponent(allowFriendlyFire);

            var killableNPCs = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsKillableNPCs"));
            GetPropertyData(nameof(KillableNPCs)).AssignGUIComponent(killableNPCs);

            var destructibleOutposts = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsDestructibleOutposts"));
            GetPropertyData(nameof(DestructibleOutposts)).AssignGUIComponent(destructibleOutposts);

            var lockAllDefaultWires = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsLockAllDefaultWires"));
            GetPropertyData(nameof(LockAllDefaultWires)).AssignGUIComponent(lockAllDefaultWires);

            var allowRewiring = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsAllowRewiring"));
            GetPropertyData(nameof(AllowRewiring)).AssignGUIComponent(allowRewiring);

            var allowWifiChatter = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform), 
                TextManager.Get("ServerSettingsAllowWifiChat"));
            GetPropertyData(nameof(AllowLinkingWifiToChat)).AssignGUIComponent(allowWifiChatter);

            var allowDisguises = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform),
                TextManager.Get("ServerSettingsAllowDisguises"));
            GetPropertyData(nameof(AllowDisguises)).AssignGUIComponent(allowDisguises);

            var voteKickBox = new GUITickBox(new RectTransform(new Vector2(0.48f, 0.05f), tickBoxContainer.Content.RectTransform), 
                TextManager.Get("ServerSettingsAllowVoteKick"));
            GetPropertyData(nameof(AllowVoteKick)).AssignGUIComponent(voteKickBox);

            GUITextBlock.AutoScaleAndNormalize(tickBoxContainer.Content.Children.Select(c => ((GUITickBox)c).TextBlock));

            tickBoxContainer.RectTransform.MinSize = new Point(0, (int)(tickBoxContainer.Content.Children.First().Rect.Height * 2.0f + tickBoxContainer.Padding.Y + tickBoxContainer.Padding.W));

            CreateLabeledSlider(antigriefingTab, "ServerSettingsKickVotesRequired", out slider, out sliderLabel);
            LocalizedString votesRequiredLabel = sliderLabel.Text + " ";
            slider.Step = 0.2f;
            slider.Range = new Vector2(0.5f, 1.0f);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = votesRequiredLabel + (int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 10.0f) + " %";
                return true;
            };
            GetPropertyData(nameof(KickVoteRequiredRatio)).AssignGUIComponent(slider);
            slider.OnMoved(slider, slider.BarScroll);
            
            CreateLabeledSlider(antigriefingTab, "ServerSettingsAutobanTime", out slider, out sliderLabel);
            LocalizedString autobanLabel = sliderLabel.Text + " ";
            slider.Step = 0.01f;
            slider.Range = new Vector2(0.0f, MaxAutoBanTime);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = autobanLabel + ToolBox.SecondsToReadableTime(scrollBar.BarScrollValue);
                return true;
            };
            GetPropertyData(nameof(AutoBanTime)).AssignGUIComponent(slider);
            slider.OnMoved(slider, slider.BarScroll);

            var wrongPasswordBanBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), antigriefingTab.RectTransform), TextManager.Get("ServerSettingsBanAfterWrongPassword"));
            GetPropertyData(nameof(BanAfterWrongPassword)).AssignGUIComponent(wrongPasswordBanBox);
            var allowedPasswordRetries = CreateLabeledNumberInput(antigriefingTab, "ServerSettingsPasswordRetriesBeforeBan", 0, 10);
            GetPropertyData(nameof(MaxPasswordRetriesBeforeBan)).AssignGUIComponent(allowedPasswordRetries);
            wrongPasswordBanBox.OnSelected += (tb) =>
            {
                allowedPasswordRetries.Enabled = tb.Selected;
                return true;
            };

            // karma --------------------------------------------------------------------------

            var karmaBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), antigriefingTab.RectTransform), TextManager.Get("ServerSettingsUseKarma"));
            GetPropertyData(nameof(KarmaEnabled)).AssignGUIComponent(karmaBox);

            karmaPresetDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), antigriefingTab.RectTransform));
            foreach (string karmaPreset in GameMain.NetworkMember.KarmaManager.Presets.Keys)
            {
                karmaPresetDD.AddItem(TextManager.Get("KarmaPreset." + karmaPreset), karmaPreset);
            }

            var karmaSettingsContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), antigriefingTab.RectTransform), style: null);
            var karmaSettingsList = new GUIListBox(new RectTransform(Vector2.One, karmaSettingsContainer.RectTransform))
            {
                Spacing = (int)(8 * GUI.Scale)
            };
            karmaSettingsList.Padding *= 2.0f;

            karmaSettingsBlocker = new GUIFrame(new RectTransform(Vector2.One, karmaSettingsContainer.RectTransform, Anchor.CenterLeft) 
            { MaxSize = new Point(karmaSettingsList.ContentBackground.Rect.Width, int.MaxValue) }, style: null)
            {
                UserData = "karmasettingsblocker",
                Color = Color.Black * 0.95f
            };
            karmaSettingsBlocker.Color *= 0.5f;
            karmaPresetDD.SelectItem(KarmaPreset);
            karmaSettingsBlocker.Visible = !karmaBox.Selected || KarmaPreset != "custom";
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
                karmaSettingsBlocker.Visible = !karmaBox.Selected || KarmaPreset != "custom";
                GameMain.NetworkMember.KarmaManager.CreateSettingsFrame(karmaSettingsList.Content);
                for (int i = 0; i < netProperties.Count; i++)
                {
                    properties[i].TempValue = prevValues[i];
                }
                return true;
            };
            AssignGUIComponent("KarmaPreset", karmaPresetDD);
            karmaBox.OnSelected = (tb) =>
            {
                karmaSettingsBlocker.Visible = !tb.Selected || KarmaPreset != "custom";
                return true;
            };

            //--------------------------------------------------------------------------------
            //                              banlist
            //--------------------------------------------------------------------------------

            BanList.CreateBanFrame(settingsTabs[(int)SettingsTab.Banlist]);
        }

        private void CreateLabeledSlider(GUIComponent parent, string labelTag, out GUIScrollBar slider, out GUITextBlock label)
        {
            var container = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            slider = new GUIScrollBar(new RectTransform(new Vector2(0.5f, 1.0f), container.RectTransform), barSize: 0.1f, style: "GUISlider");
            label = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), container.RectTransform),
                string.IsNullOrEmpty(labelTag) ? "" : TextManager.Get(labelTag), textAlignment: Alignment.CenterLeft, font: GUIStyle.SmallFont);

            container.RectTransform.MinSize = new Point(0, slider.RectTransform.MinSize.Y);
            container.RectTransform.MaxSize = new Point(int.MaxValue, slider.RectTransform.MaxSize.Y);

            //slider has a reference to the label to change the text when it's used
            slider.UserData = label;
        }

        private GUINumberInput CreateLabeledNumberInput(GUIComponent parent, string labelTag, int min, int max, string toolTipTag = null)
        {
            var container = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), parent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f,
                ToolTip = TextManager.Get(labelTag)
            };

            var label = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), container.RectTransform),
                TextManager.Get(labelTag), textAlignment: Alignment.CenterLeft, font: GUIStyle.SmallFont)
            {
                AutoScaleHorizontal = true
            };
            if (!string.IsNullOrEmpty(toolTipTag))
            {
                label.ToolTip = TextManager.Get(toolTipTag);
            }
            var input = new GUINumberInput(new RectTransform(new Vector2(0.3f, 1.0f), container.RectTransform), NumberType.Int)
            {
                MinValueInt = min,
                MaxValueInt = max
            };

            container.RectTransform.MinSize = new Point(0, input.RectTransform.MinSize.Y);
            container.RectTransform.MaxSize = new Point(int.MaxValue, input.RectTransform.MaxSize.Y);

            return input;
        }

        private GUIDropDown CreateLabeledDropdown(GUIComponent parent, string labelTag, int numElements, string toolTipTag = null)
        {
            var container = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), parent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f,
                ToolTip = TextManager.Get(labelTag)
            };

            var label = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), container.RectTransform),
                TextManager.Get(labelTag), textAlignment: Alignment.CenterLeft, font: GUIStyle.SmallFont)
            {
                AutoScaleHorizontal = true
            };
            if (!string.IsNullOrEmpty(toolTipTag))
            {
                label.ToolTip = TextManager.Get(toolTipTag);
            }
            var input = new GUIDropDown(new RectTransform(new Vector2(0.3f, 1.0f), container.RectTransform), elementCount: numElements);

            container.RectTransform.MinSize = new Point(0, input.RectTransform.MinSize.Y);
            container.RectTransform.MaxSize = new Point(int.MaxValue, input.RectTransform.MaxSize.Y);

            return input;
        }

        private bool SelectSettingsTab(GUIButton button, object obj)
        {
            settingsTabIndex = (int)obj;

            for (int i = 0; i < settingsTabs.Length; i++)
            {
                settingsTabs[i].Visible = i == settingsTabIndex;
                tabButtons[i].Selected = i == settingsTabIndex;
            }

            return true;
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
            }
            return false;
        }
    }
}
