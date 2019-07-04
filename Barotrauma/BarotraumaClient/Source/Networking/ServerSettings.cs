using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

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
                GUIComponentValue = property.GetValue(serverSettings);
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
                    return null;
                }
                set
                {
                    if (GUIComponent == null) return;
                    else if (GUIComponent is GUITickBox tickBox) tickBox.Selected = (bool)value;
                    else if (GUIComponent is GUITextBox textBox) textBox.Text = (string)value;
                    else if (GUIComponent is GUIScrollBar scrollBar) scrollBar.BarScrollValue = (float)value;
                    else if (GUIComponent is GUIRadioButtonGroup radioButtonGroup) radioButtonGroup.Selected = (Enum)value;
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

            public bool PropEquals(object a,object b)
            {
                switch (typeString)
                {
                    case "float":
                        if (!(a is float?)) return false;
                        if (!(b is float?)) return false;
                        return (float)a == (float)b;
                    case "int":
                        if (!(a is int?)) return false;
                        if (!(b is int?)) return false;
                        return (int)a == (int)b;
                    case "bool":
                        if (!(a is bool?)) return false;
                        if (!(b is bool?)) return false;
                        return (bool)a == (bool)b;
                    case "Enum":
                        if (!(a is Enum)) return false;
                        if (!(b is Enum)) return false;
                        return ((Enum)a).Equals((Enum)b);
                    default:
                        return a.ToString().Equals(b.ToString(),StringComparison.InvariantCulture);
                }
            }
        }
        private Dictionary<string, bool> tempMonsterEnabled;
        
        partial void InitProjSpecific()
        {
            var properties = TypeDescriptor.GetProperties(GetType()).Cast<PropertyDescriptor>();

            SerializableProperties = new Dictionary<string, SerializableProperty>();

            foreach (var property in properties)
            {
                SerializableProperty objProperty = new SerializableProperty(property, this);
                SerializableProperties.Add(property.Name.ToLowerInvariant(), objProperty);
            }
        }

        public void ClientAdminRead(NetBuffer incMsg)
        {
            int count = incMsg.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                UInt32 key = incMsg.ReadUInt32();
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
                    incMsg.Position += 8 * size;
                }
            }

            ReadMonsterEnabled(incMsg);
            BanList.ClientAdminRead(incMsg);
            Whitelist.ClientAdminRead(incMsg);
        }

        public void ClientRead(NetBuffer incMsg)
        {
            ServerName = incMsg.ReadString();
            ServerMessageText = incMsg.ReadString();
            TickRate = incMsg.ReadRangedInteger(1, 60);
            GameMain.NetworkMember.TickRate = TickRate;

            ReadExtraCargo(incMsg);

            Voting.ClientRead(incMsg);

            bool isAdmin = incMsg.ReadBoolean();
            incMsg.ReadPadBits();
            if (isAdmin)
            {
                ClientAdminRead(incMsg);
            }
        }

        public void ClientAdminWrite(NetFlags dataToSend, int missionType = 0, float? levelDifficulty = null, bool? autoRestart = null, int traitorSetting = 0, int botCount = 0, int botSpawnMode = 0, bool? useRespawnShuttle = null)
        {
            if (!GameMain.Client.HasPermission(Networking.ClientPermissions.ManageSettings)) return;

            NetOutgoingMessage outMsg = GameMain.NetworkMember.NetPeer.CreateMessage();

            outMsg.Write((byte)ClientPacketHeader.SERVER_SETTINGS);

            outMsg.Write((byte)dataToSend);

            if (dataToSend.HasFlag(NetFlags.Name))
            {
                if (GameMain.NetLobbyScreen.ServerName.Text != ServerName)
                {
                    ServerName = GameMain.NetLobbyScreen.ServerName.Text;
                }
                outMsg.Write(ServerName);
            }

            if (dataToSend.HasFlag(NetFlags.Message))
            {
                if (GameMain.NetLobbyScreen.ServerMessage.Text != ServerMessageText)
                {
                    ServerMessageText = GameMain.NetLobbyScreen.ServerMessage.Text;
                }
                outMsg.Write(ServerMessageText);
            }

            if (dataToSend.HasFlag(NetFlags.Properties))
            {
                //TODO: split this up?
                WriteExtraCargo(outMsg);

                IEnumerable<KeyValuePair<UInt32, NetPropertyData>> changedProperties = netProperties.Where(kvp => kvp.Value.ChangedLocally);
                UInt32 count = (UInt32)changedProperties.Count();
                bool changedMonsterSettings = tempMonsterEnabled != null && tempMonsterEnabled.Any(p => p.Value != MonsterEnabled[p.Key]);

                outMsg.Write(count);
                foreach (KeyValuePair<UInt32, NetPropertyData> prop in changedProperties)
                {
                    DebugConsole.NewMessage(prop.Value.Name, Color.Lime);
                    outMsg.Write(prop.Key);
                    prop.Value.Write(outMsg, prop.Value.GUIComponentValue);
                }

                outMsg.Write(changedMonsterSettings); outMsg.WritePadBits();
                if (changedMonsterSettings) WriteMonsterEnabled(outMsg, tempMonsterEnabled);
                BanList.ClientAdminWrite(outMsg);
                Whitelist.ClientAdminWrite(outMsg);
            }

            if (dataToSend.HasFlag(NetFlags.Misc))
            {
                outMsg.Write((byte)(missionType + 1));
                outMsg.Write((byte)(traitorSetting + 1));
                outMsg.Write((byte)(botCount + 1));
                outMsg.Write((byte)(botSpawnMode + 1));

                outMsg.Write(levelDifficulty ?? -1000.0f);

                outMsg.Write(useRespawnShuttle ?? UseRespawnShuttle);

                outMsg.Write(autoRestart != null);
                outMsg.Write(autoRestart ?? false);
                outMsg.WritePadBits();
            }

            if (dataToSend.HasFlag(NetFlags.LevelSeed))
            {
                outMsg.Write(GameMain.NetLobbyScreen.SeedBox.Text);
            }

            (GameMain.NetworkMember.NetPeer as NetClient).SendMessage(outMsg, NetDeliveryMethod.ReliableOrdered);
        }

        //GUI stuff
        private GUIFrame settingsFrame;
        private GUIFrame[] settingsTabs;
        private GUIButton[] tabButtons;
        private int settingsTabIndex;
        
        enum SettingsTab
        {
            Rounds,
            Server,
            Banlist,
            Whitelist
        }

        private NetPropertyData GetPropertyData(string name)
        {
            return netProperties.First(p => p.Value.Name == name).Value;
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
            settingsFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null, color: Color.Black * 0.5f);
            new GUIButton(new RectTransform(Vector2.One, settingsFrame.RectTransform), "", style: null).OnClicked += (btn, userData) =>
            {
                if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) ToggleSettingsFrame(btn, userData);
                return true;
            };
            
            new GUIButton(new RectTransform(Vector2.One, settingsFrame.RectTransform), "", style: null)
            {
                OnClicked = ToggleSettingsFrame
            };

            //center frames
            GUIFrame innerFrame = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.7f), settingsFrame.RectTransform, Anchor.Center) { MinSize = new Point(400, 430) });
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), innerFrame.RectTransform, Anchor.Center), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform), TextManager.Get("Settings"), font: GUI.LargeFont);

            var buttonArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.07f), paddedFrame.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.1f) }, isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            //tabs
            var tabValues = Enum.GetValues(typeof(SettingsTab)).Cast<SettingsTab>().ToArray();
            string[] tabNames = new string[tabValues.Count()];
            for (int i = 0; i < tabNames.Length; i++)
            {
                tabNames[i] = TextManager.Get("ServerSettings" + tabValues[i] + "Tab");
            }
            settingsTabs = new GUIFrame[tabNames.Length];
            tabButtons = new GUIButton[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
            {
                settingsTabs[i] = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.79f), paddedFrame.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.0f, 0.05f) },
                    style: "InnerFrame");

                tabButtons[i] = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform), tabNames[i], style: "GUITabButton")
                {
                    UserData = i,
                    OnClicked = SelectSettingsTab
                };
            }
            GUITextBlock.AutoScaleAndNormalize(tabButtons.Select(b => b.TextBlock));
            SelectSettingsTab(tabButtons[0], 0);

            //"Close"
            var closeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.05f), paddedFrame.RectTransform, Anchor.BottomRight), TextManager.Get("Close"))
            {
                OnClicked = ToggleSettingsFrame
            };

            //--------------------------------------------------------------------------------
            //                              game settings 
            //--------------------------------------------------------------------------------

            var roundsTab = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), settingsTabs[(int)SettingsTab.Rounds].RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), roundsTab.RectTransform), TextManager.Get("ServerSettingsSubSelection"));
            var selectionFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), roundsTab.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            GUIRadioButtonGroup selectionMode = new GUIRadioButtonGroup();
            for (int i = 0; i < 3; i++)
            {
                var selectionTick = new GUITickBox(new RectTransform(new Vector2(0.3f, 1.0f), selectionFrame.RectTransform), TextManager.Get(((SelectionMode)i).ToString()), font: GUI.SmallFont);
                selectionMode.AddRadioButton((SelectionMode)i, selectionTick);
            }
            DebugConsole.NewMessage(SubSelectionMode.ToString(),Color.White);
            GetPropertyData("SubSelectionMode").AssignGUIComponent(selectionMode);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), roundsTab.RectTransform), TextManager.Get("ServerSettingsModeSelection"));
            selectionFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), roundsTab.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            selectionMode = new GUIRadioButtonGroup();
            for (int i = 0; i < 3; i++)
            {
                var selectionTick = new GUITickBox(new RectTransform(new Vector2(0.3f, 1.0f), selectionFrame.RectTransform), TextManager.Get(((SelectionMode)i).ToString()), font: GUI.SmallFont);
                selectionMode.AddRadioButton((SelectionMode)i, selectionTick);
            }
            GetPropertyData("ModeSelectionMode").AssignGUIComponent(selectionMode);

            var endBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), roundsTab.RectTransform),
                TextManager.Get("ServerSettingsEndRoundWhenDestReached"));
            GetPropertyData("EndRoundAtLevelEnd").AssignGUIComponent(endBox);
            
            var endVoteBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), roundsTab.RectTransform),
                TextManager.Get("ServerSettingsEndRoundVoting"));
            GetPropertyData("AllowEndVoting").AssignGUIComponent(endVoteBox);

            CreateLabeledSlider(roundsTab, "ServerSettingsEndRoundVotesRequired", out GUIScrollBar slider, out GUITextBlock sliderLabel);

            string endRoundLabel = sliderLabel.Text;
            slider.Step = 0.2f;
            slider.Range = new Vector2(0.5f, 1.0f);
            GetPropertyData("EndVoteRequiredRatio").AssignGUIComponent(slider);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = endRoundLabel + " " + (int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 10.0f) + " %";
                return true;
            };
            slider.OnMoved(slider, slider.BarScroll);

            var respawnBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), roundsTab.RectTransform),
                TextManager.Get("ServerSettingsAllowRespawning"));
            GetPropertyData("AllowRespawn").AssignGUIComponent(respawnBox);

            CreateLabeledSlider(roundsTab, "ServerSettingsRespawnInterval", out slider, out sliderLabel);
            string intervalLabel = sliderLabel.Text;
            slider.Step = 0.05f;
            slider.Range = new Vector2(10.0f, 600.0f);
            GetPropertyData("RespawnInterval").AssignGUIComponent(slider);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock text = scrollBar.UserData as GUITextBlock;
                text.Text = intervalLabel + " " + ToolBox.SecondsToReadableTime(scrollBar.BarScrollValue);
                return true;
            };
            slider.OnMoved(slider, slider.BarScroll);

            var minRespawnText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), roundsTab.RectTransform), "")
            {
                ToolTip = TextManager.Get("ServerSettingsMinRespawnToolTip")
            };

            string minRespawnLabel = TextManager.Get("ServerSettingsMinRespawn") + " ";
            CreateLabeledSlider(roundsTab, "", out slider, out sliderLabel);
            slider.ToolTip = minRespawnText.ToolTip;
            slider.UserData = minRespawnText;
            slider.Step = 0.1f;
            slider.Range = new Vector2(0.0f, 1.0f);
            GetPropertyData("MinRespawnRatio").AssignGUIComponent(slider);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = minRespawnLabel + (int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 10.0f) + " %";
                return true;
            };
            slider.OnMoved(slider, MinRespawnRatio);

            var respawnDurationText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), roundsTab.RectTransform), "")
            {
                ToolTip = TextManager.Get("ServerSettingsRespawnDurationToolTip")
            };

            string respawnDurationLabel = TextManager.Get("ServerSettingsRespawnDuration") + " ";
            CreateLabeledSlider(roundsTab, "", out slider, out sliderLabel);
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
            GetPropertyData("MaxTransportTime").AssignGUIComponent(slider);
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

            var buttonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.07f), roundsTab.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            var monsterButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonHolder.RectTransform),
                TextManager.Get("ServerSettingsMonsterSpawns"))
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
            List<string> monsterNames = MonsterEnabled.Keys.ToList();
            tempMonsterEnabled = new Dictionary<string, bool>(MonsterEnabled);
            foreach (string s in monsterNames)
            {
                string translatedLabel = TextManager.Get($"Character.{s}", true);
                var monsterEnabledBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), monsterFrame.Content.RectTransform) { MinSize = new Point(0, 25) },
                    label: translatedLabel ?? s)
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
                TextManager.Get("ServerSettingsAdditionalCargo"))
            {
                Enabled = !GameMain.NetworkMember.GameStarted
            };
            var cargoFrame = new GUIListBox(new RectTransform(new Vector2(0.6f, 0.7f), settingsTabs[(int)SettingsTab.Rounds].RectTransform, Anchor.BottomRight, Pivot.BottomLeft))
            {
                Visible = false
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

            foreach (ItemPrefab ip in MapEntityPrefab.List.Where(p => p is ItemPrefab).Select(p => p as ItemPrefab))
            {
                if (!ip.CanBeBought && !ip.Tags.Contains("smallitem")) continue;

                var itemFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), cargoFrame.Content.RectTransform) { MinSize = new Point(0, 30) }, isHorizontal: true)
                {
                    Stretch = true,
                    UserData = cargoFrame,
                    RelativeSpacing = 0.05f
                };


                if (ip.InventoryIcon != null || ip.sprite != null)
                {
                    GUIImage img = new GUIImage(new RectTransform(new Point(itemFrame.Rect.Height), itemFrame.RectTransform),
                        ip.InventoryIcon ?? ip.sprite, scaleToFit: true)
                    {
                        CanBeFocused = false
                    };
                    img.Color = img.Sprite == ip.InventoryIcon ? ip.InventoryIconColor : ip.SpriteColor;
                }

                new GUITextBlock(new RectTransform(new Vector2(0.75f, 1.0f), itemFrame.RectTransform),
                    ip.Name, font: GUI.SmallFont)
                {
                    Wrap = true,
                    CanBeFocused = false
                };

                ExtraCargo.TryGetValue(ip, out int cargoVal);
                var amountInput = new GUINumberInput(new RectTransform(new Vector2(0.35f, 1.0f), itemFrame.RectTransform),
                    GUINumberInput.NumberType.Int, textAlignment: Alignment.CenterLeft)
                {
                    MinValueInt = 0,
                    MaxValueInt = 100,
                    IntValue = cargoVal
                };
                amountInput.OnValueChanged += (numberInput) =>
                {
                    if (ExtraCargo.ContainsKey(ip))
                    {
                        ExtraCargo[ip] = numberInput.IntValue;
                        if (numberInput.IntValue <= 0) ExtraCargo.Remove(ip);
                    }
                    else
                    {
                        ExtraCargo.Add(ip, numberInput.IntValue);
                    }
                };
            }

            //--------------------------------------------------------------------------------
            //                              server settings 
            //--------------------------------------------------------------------------------

            var serverTab = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), settingsTabs[(int)SettingsTab.Server].RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            //***********************************************

            var voiceChatEnabled = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform),
                TextManager.Get("ServerSettingsVoiceChatEnabled"));
            GetPropertyData("VoiceChatEnabled").AssignGUIComponent(voiceChatEnabled);

            //***********************************************

            string autoRestartDelayLabel = TextManager.Get("ServerSettingsAutoRestartDelay") + " ";
            var startIntervalText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), autoRestartDelayLabel);
            var startIntervalSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), barSize: 0.1f)
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
            GetPropertyData("AutoRestartInterval").AssignGUIComponent(startIntervalSlider);
            startIntervalSlider.OnMoved(startIntervalSlider, startIntervalSlider.BarScroll);

            //***********************************************

            var startWhenClientsReady = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform),
                TextManager.Get("ServerSettingsStartWhenClientsReady"));
            GetPropertyData("StartWhenClientsReady").AssignGUIComponent(startWhenClientsReady);

            CreateLabeledSlider(serverTab, "ServerSettingsStartWhenClientsReadyRatio", out slider, out sliderLabel);
            string clientsReadyRequiredLabel = sliderLabel.Text;
            slider.Step = 0.2f;
            slider.Range = new Vector2(0.5f, 1.0f);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = clientsReadyRequiredLabel.Replace("[percentage]", ((int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 10.0f)).ToString());
                return true;
            };
            GetPropertyData("StartWhenClientsReadyRatio").AssignGUIComponent(slider);
            slider.OnMoved(slider, slider.BarScroll);

            //***********************************************

            var allowSpecBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsAllowSpectating"));
            GetPropertyData("AllowSpectating").AssignGUIComponent(allowSpecBox);

            var voteKickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsAllowVoteKick"));
            GetPropertyData("AllowVoteKick").AssignGUIComponent(voteKickBox);

            CreateLabeledSlider(serverTab, "ServerSettingsKickVotesRequired", out slider, out sliderLabel);
            string votesRequiredLabel = sliderLabel.Text + " ";
            slider.Step = 0.2f;
            slider.Range = new Vector2(0.5f, 1.0f);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = votesRequiredLabel + (int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 10.0f) + " %";
                return true;
            };
            GetPropertyData("KickVoteRequiredRatio").AssignGUIComponent(slider);
            slider.OnMoved(slider, slider.BarScroll);

            CreateLabeledSlider(serverTab, "ServerSettingsAutobanTime", out slider, out sliderLabel);
            string autobanLabel = sliderLabel.Text + " ";
            slider.Step = 0.05f;
            slider.Range = new Vector2(0.0f, MaxAutoBanTime);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = autobanLabel + ToolBox.SecondsToReadableTime(scrollBar.BarScrollValue);
                return true;
            };
            GetPropertyData("AutoBanTime").AssignGUIComponent(slider);
            slider.OnMoved(slider, slider.BarScroll);

            var shareSubsBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsShareSubFiles"));
            GetPropertyData("AllowFileTransfers").AssignGUIComponent(shareSubsBox);

            var randomizeLevelBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsRandomizeSeed"));
            GetPropertyData("RandomizeSeed").AssignGUIComponent(randomizeLevelBox);

            var saveLogsBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsSaveLogs"))
            {
                OnSelected = (GUITickBox) =>
                {
                    //TODO: fix?
                    //showLogButton.Visible = SaveServerLogs;
                    return true;
                }
            };
            GetPropertyData("SaveServerLogs").AssignGUIComponent(saveLogsBox);

            var ragdollButtonBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsAllowRagdollButton"));
            GetPropertyData("AllowRagdollButton").AssignGUIComponent(ragdollButtonBox);

            var traitorRatioBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsUseTraitorRatio"));

            CreateLabeledSlider(serverTab, "", out slider, out sliderLabel);
            /*var traitorRatioText = new GUITextBlock(new Rectangle(20, y + 20, 20, 20), "Traitor ratio: 20 %", "", settingsTabs[1], GUI.SmallFont);
            var traitorRatioSlider = new GUIScrollBar(new Rectangle(150, y + 22, 100, 15), "", 0.1f, settingsTabs[1]);*/
            var traitorRatioSlider = slider;
            traitorRatioBox.OnSelected = (GUITickBox) =>
            {
                traitorRatioSlider.OnMoved(traitorRatioSlider, traitorRatioSlider.BarScroll);
                return true;
            };
            
            if (TraitorUseRatio)
            {
                traitorRatioSlider.Range = new Vector2(0.1f, 1.0f);
            }
            else
            {
                traitorRatioSlider.Range = new Vector2(1.0f, maxPlayers);
            }

            string traitorRatioLabel = TextManager.Get("ServerSettingsTraitorRatio") + " ";
            string traitorCountLabel = TextManager.Get("ServerSettingsTraitorCount") + " ";

            traitorRatioSlider.Range = new Vector2(0.1f, 1.0f);
            traitorRatioSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock traitorText = scrollBar.UserData as GUITextBlock;
                if (traitorRatioBox.Selected)
                {
                    scrollBar.Step = 0.01f;
                    scrollBar.Range = new Vector2(0.1f, 1.0f);
                    traitorText.Text = traitorRatioLabel + (int)MathUtils.Round(scrollBar.BarScrollValue * 100.0f, 1.0f) + " %";
                }
                else
                {
                    scrollBar.Step = 1f / (maxPlayers - 1);
                    scrollBar.Range = new Vector2(1.0f, maxPlayers);
                    traitorText.Text = traitorCountLabel + scrollBar.BarScrollValue;
                }
                return true;
            };
            
            GetPropertyData("TraitorUseRatio").AssignGUIComponent(traitorRatioBox);
            GetPropertyData("TraitorRatio").AssignGUIComponent(traitorRatioSlider);

            traitorRatioSlider.OnMoved(traitorRatioSlider, traitorRatioSlider.BarScroll);
            traitorRatioBox.OnSelected(traitorRatioBox);


            var karmaBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), serverTab.RectTransform), TextManager.Get("ServerSettingsUseKarma"));
            GetPropertyData("KarmaEnabled").AssignGUIComponent(karmaBox);

            //--------------------------------------------------------------------------------
            //                              banlist
            //--------------------------------------------------------------------------------

            BanList.CreateBanFrame(settingsTabs[2]);

            //--------------------------------------------------------------------------------
            //                              whitelist
            //--------------------------------------------------------------------------------

            Whitelist.CreateWhiteListFrame(settingsTabs[3]);

        }

        private void CreateLabeledSlider(GUIComponent parent, string labelTag, out GUIScrollBar slider, out GUITextBlock label)
        {
            var container = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            slider = new GUIScrollBar(new RectTransform(new Vector2(0.5f, 0.8f), container.RectTransform), barSize: 0.1f);
            label = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.8f), container.RectTransform),
                string.IsNullOrEmpty(labelTag) ? "" : TextManager.Get(labelTag), font: GUI.SmallFont);

            //slider has a reference to the label to change the text when it's used
            slider.UserData = label;
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
            if (settingsFrame == null)
            {
                CreateSettingsFrame();
            }
            else
            {
                ClientAdminWrite(NetFlags.Properties);
                foreach (NetPropertyData prop in netProperties.Values)
                {
                    prop.GUIComponent = null;
                }
                settingsFrame = null;
            }

            return false;
        }

        public void ManagePlayersFrame(GUIFrame infoFrame)
        {
            GUIListBox cList = new GUIListBox(new RectTransform(Vector2.One, infoFrame.RectTransform));
            /*foreach (Client c in ConnectedClients)
            {
                var frame = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), cList.Content.RectTransform),
                    c.Name + " (" + c.Connection.RemoteEndPoint.Address.ToString() + ")", style: "ListBoxElement")
                {
                    Color = (c.InGame && c.Character != null && !c.Character.IsDead) ? Color.Gold * 0.2f : Color.Transparent,
                    HoverColor = Color.LightGray * 0.5f,
                    SelectedColor = Color.Gold * 0.5f
                };

                var buttonArea = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 0.85f), frame.RectTransform, Anchor.CenterRight) { RelativeOffset = new Vector2(0.05f, 0.0f) },
                    isHorizontal: true);

                var kickButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonArea.RectTransform),
                    TextManager.Get("Kick"))
                {
                    UserData = c.Name,
                    OnClicked = GameMain.NetLobbyScreen.KickPlayer
                };

                var banButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonArea.RectTransform),
                    TextManager.Get("Ban"))
                {
                    UserData = c.Name,
                    OnClicked = GameMain.NetLobbyScreen.BanPlayer
                };

                var rangebanButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonArea.RectTransform),
                    TextManager.Get("BanRange"))
                {
                    UserData = c.Name,
                    OnClicked = GameMain.NetLobbyScreen.BanPlayerRange
                };
            }*/ //TODO: reimplement
        }
    }
}