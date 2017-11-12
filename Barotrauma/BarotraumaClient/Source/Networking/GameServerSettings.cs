using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class GameServer : NetworkMember, ISerializableEntity
    {
        private GUIFrame settingsFrame;
        private GUIFrame[] settingsTabs;
        private int settingsTabIndex;


        private void CreateSettingsFrame()
        {
            settingsFrame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.5f, null);

            GUIFrame innerFrame = new GUIFrame(new Rectangle(0, 0, 400, 430), null, Alignment.Center, "", settingsFrame);
            innerFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            new GUITextBlock(new Rectangle(0, -5, 0, 20), "Settings", "", innerFrame, GUI.LargeFont);

            string[] tabNames = { "Rounds", "Server", "Banlist", "Whitelist" };
            settingsTabs = new GUIFrame[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
            {
                settingsTabs[i] = new GUIFrame(new Rectangle(0, 15, 0, innerFrame.Rect.Height - 120), null, Alignment.Center, "InnerFrame", innerFrame);
                settingsTabs[i].Padding = new Vector4(40.0f, 20.0f, 40.0f, 40.0f);

                var tabButton = new GUIButton(new Rectangle(85 * i, 35, 80, 20), tabNames[i], "", innerFrame);
                tabButton.UserData = i;
                tabButton.OnClicked = SelectSettingsTab;
            }

            settingsTabs[2].Padding = Vector4.Zero;

            SelectSettingsTab(null, 0);

            var closeButton = new GUIButton(new Rectangle(10, 0, 100, 20), "Close", Alignment.BottomRight, "", innerFrame);
            closeButton.OnClicked = ToggleSettingsFrame;

            //--------------------------------------------------------------------------------
            //                              game settings 
            //--------------------------------------------------------------------------------

            int y = 0;

            settingsTabs[0].Padding = new Vector4(40.0f, 5.0f, 40.0f, 40.0f);

            new GUITextBlock(new Rectangle(0, y, 100, 20), "Submarine selection:", "", settingsTabs[0]);
            var selectionFrame = new GUIFrame(new Rectangle(0, y + 20, 300, 20), null, settingsTabs[0]);
            for (int i = 0; i < 3; i++)
            {
                var selectionTick = new GUITickBox(new Rectangle(i * 100, 0, 20, 20), ((SelectionMode)i).ToString(), Alignment.Left, selectionFrame);
                selectionTick.Selected = i == (int)subSelectionMode;
                selectionTick.OnSelected = SwitchSubSelection;
                selectionTick.UserData = (SelectionMode)i;
            }

            y += 45;

            new GUITextBlock(new Rectangle(0, y, 100, 20), "Mode selection:", "", settingsTabs[0]);
            selectionFrame = new GUIFrame(new Rectangle(0, y + 20, 300, 20), null, settingsTabs[0]);
            for (int i = 0; i < 3; i++)
            {
                var selectionTick = new GUITickBox(new Rectangle(i * 100, 0, 20, 20), ((SelectionMode)i).ToString(), Alignment.Left, selectionFrame);
                selectionTick.Selected = i == (int)modeSelectionMode;
                selectionTick.OnSelected = SwitchModeSelection;
                selectionTick.UserData = (SelectionMode)i;
            }

            y += 60;

            var endBox = new GUITickBox(new Rectangle(0, y, 20, 20), "End round when destination reached", Alignment.Left, settingsTabs[0]);
            endBox.Selected = EndRoundAtLevelEnd;
            endBox.OnSelected = (GUITickBox) => { EndRoundAtLevelEnd = GUITickBox.Selected; return true; };

            y += 25;

            var endVoteBox = new GUITickBox(new Rectangle(0, y, 20, 20), "End round by voting", Alignment.Left, settingsTabs[0]);
            endVoteBox.Selected = Voting.AllowEndVoting;
            endVoteBox.OnSelected = (GUITickBox) =>
            {
                Voting.AllowEndVoting = !Voting.AllowEndVoting;
                GameMain.Server.UpdateVoteStatus();
                return true;
            };


            var votesRequiredText = new GUITextBlock(new Rectangle(20, y + 15, 20, 20), "Votes required: 50 %", "", settingsTabs[0], GUI.SmallFont);

            var votesRequiredSlider = new GUIScrollBar(new Rectangle(150, y + 22, 100, 15), "", 0.1f, settingsTabs[0]);
            votesRequiredSlider.UserData = votesRequiredText;
            votesRequiredSlider.Step = 0.2f;
            votesRequiredSlider.BarScroll = (EndVoteRequiredRatio - 0.5f) * 2.0f;
            votesRequiredSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock voteText = scrollBar.UserData as GUITextBlock;

                EndVoteRequiredRatio = barScroll / 2.0f + 0.5f;
                voteText.Text = "Votes required: " + (int)MathUtils.Round(EndVoteRequiredRatio * 100.0f, 10.0f) + " %";
                return true;
            };
            votesRequiredSlider.OnMoved(votesRequiredSlider, votesRequiredSlider.BarScroll);

            y += 35;

            var respawnBox = new GUITickBox(new Rectangle(0, y, 20, 20), "Allow respawning", Alignment.Left, settingsTabs[0]);
            respawnBox.Selected = AllowRespawn;
            respawnBox.OnSelected = (GUITickBox) =>
            {
                AllowRespawn = !AllowRespawn;
                return true;
            };


            var respawnIntervalText = new GUITextBlock(new Rectangle(20, y + 13, 20, 20), "Respawn interval", "", settingsTabs[0], GUI.SmallFont);

            var respawnIntervalSlider = new GUIScrollBar(new Rectangle(150, y + 20, 100, 15), "", 0.1f, settingsTabs[0]);
            respawnIntervalSlider.UserData = respawnIntervalText;
            respawnIntervalSlider.Step = 0.05f;
            respawnIntervalSlider.BarScroll = RespawnInterval / 600.0f;
            respawnIntervalSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock text = scrollBar.UserData as GUITextBlock;

                RespawnInterval = Math.Max(barScroll * 600.0f, 10.0f);
                text.Text = "Interval: " + ToolBox.SecondsToReadableTime(RespawnInterval);
                return true;
            };
            respawnIntervalSlider.OnMoved(respawnIntervalSlider, respawnIntervalSlider.BarScroll);

            y += 35;

            var minRespawnText = new GUITextBlock(new Rectangle(0, y, 200, 20), "Minimum players to respawn", "", settingsTabs[0]);
            minRespawnText.ToolTip = "What percentage of players has to be dead/spectating until a respawn shuttle is dispatched";

            var minRespawnSlider = new GUIScrollBar(new Rectangle(150, y + 20, 100, 15), "", 0.1f, settingsTabs[0]);
            minRespawnSlider.ToolTip = minRespawnText.ToolTip;
            minRespawnSlider.UserData = minRespawnText;
            minRespawnSlider.Step = 0.1f;
            minRespawnSlider.BarScroll = MinRespawnRatio;
            minRespawnSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock txt = scrollBar.UserData as GUITextBlock;

                MinRespawnRatio = barScroll;
                txt.Text = "Minimum players to respawn: " + (int)MathUtils.Round(MinRespawnRatio * 100.0f, 10.0f) + " %";
                return true;
            };
            minRespawnSlider.OnMoved(minRespawnSlider, MinRespawnRatio);

            y += 30;

            var respawnDurationText = new GUITextBlock(new Rectangle(0, y, 200, 20), "Duration of respawn transport", "", settingsTabs[0]);
            respawnDurationText.ToolTip = "The amount of time respawned players have to navigate the respawn shuttle to the main submarine. " +
                "After the duration expires, the shuttle will automatically head back out of the level.";

            var respawnDurationSlider = new GUIScrollBar(new Rectangle(150, y + 20, 100, 15), "", 0.1f, settingsTabs[0]);
            respawnDurationSlider.ToolTip = minRespawnText.ToolTip;
            respawnDurationSlider.UserData = respawnDurationText;
            respawnDurationSlider.Step = 0.1f;
            respawnDurationSlider.BarScroll = MaxTransportTime <= 0.0f ? 1.0f : (MaxTransportTime - 60.0f) / 600.0f;
            respawnDurationSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock txt = scrollBar.UserData as GUITextBlock;

                if (barScroll == 1.0f)
                {
                    MaxTransportTime = 0;
                    txt.Text = "Duration of respawn transport: unlimited";
                }
                else
                {
                    MaxTransportTime = barScroll * 600.0f + 60.0f;
                    txt.Text = "Duration of respawn transport: " + ToolBox.SecondsToReadableTime(MaxTransportTime);
                }

                return true;
            };
            respawnDurationSlider.OnMoved(respawnDurationSlider, respawnDurationSlider.BarScroll);

            y += 35;

            var monsterButton = new GUIButton(new Rectangle(0, y, 130, 20), "Monster Spawns", "", settingsTabs[0]);
            monsterButton.Enabled = !GameStarted;
            var monsterFrame = new GUIListBox(new Rectangle(-290, 60, 280, 250), "", settingsTabs[0]);
            monsterFrame.Visible = false;
            monsterButton.UserData = monsterFrame;
            monsterButton.OnClicked = (button, obj) =>
            {
                if (gameStarted)
                {
                    ((GUIComponent)obj).Visible = false;
                    button.Enabled = false;
                    return true;
                }
                ((GUIComponent)obj).Visible = !((GUIComponent)obj).Visible;
                return true;
            };
            List<string> monsterNames = monsterEnabled.Keys.ToList();
            foreach (string s in monsterNames)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 260, 25),
                    s,
                    "",
                    Alignment.Left, Alignment.Left, monsterFrame);
                textBlock.Padding = new Vector4(35.0f, 3.0f, 0.0f, 0.0f);
                textBlock.UserData = monsterFrame;
                textBlock.CanBeFocused = false;

                var monsterEnabledBox = new GUITickBox(new Rectangle(-25, 0, 20, 20), "", Alignment.Left, textBlock);
                monsterEnabledBox.Selected = monsterEnabled[s];
                monsterEnabledBox.OnSelected = (GUITickBox) =>
                {
                    if (gameStarted)
                    {
                        monsterFrame.Visible = false;
                        monsterButton.Enabled = false;
                        return true;
                    }
                    monsterEnabled[s] = !monsterEnabled[s];
                    return true;
                };
            }

            var cargoButton = new GUIButton(new Rectangle(160, y, 130, 20), "Additional Cargo", "", settingsTabs[0]);
            cargoButton.Enabled = !GameStarted;

            var cargoFrame = new GUIListBox(new Rectangle(300, 60, 280, 250), "", settingsTabs[0]);
            cargoFrame.Visible = false;
            cargoButton.UserData = cargoFrame;
            cargoButton.OnClicked = (button, obj) =>
            {
                if (gameStarted)
                {
                    ((GUIComponent)obj).Visible = false;
                    button.Enabled = false;
                    return true;
                }
                ((GUIComponent)obj).Visible = !((GUIComponent)obj).Visible;
                return true;
            };


            foreach (MapEntityPrefab pf in MapEntityPrefab.list)
            {
                if (!(pf is ItemPrefab) || (pf.Price <= 0.0f && !pf.tags.Contains("smallitem"))) continue;

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 260, 25),
                    pf.Name, "",
                    Alignment.Left, Alignment.CenterLeft, cargoFrame, false, GUI.SmallFont);
                textBlock.Padding = new Vector4(40.0f, 3.0f, 0.0f, 0.0f);
                textBlock.UserData = cargoFrame;
                textBlock.CanBeFocused = false;

                if (pf.sprite != null)
                {
                    float scale = Math.Min(Math.Min(30.0f / pf.sprite.SourceRect.Width, 30.0f / pf.sprite.SourceRect.Height), 1.0f);
                    GUIImage img = new GUIImage(new Rectangle(-20 - (int)(pf.sprite.SourceRect.Width * scale * 0.5f), 12 - (int)(pf.sprite.SourceRect.Height * scale * 0.5f), 40, 40), pf.sprite, Alignment.Left, textBlock);
                    img.Color = pf.SpriteColor;
                    img.Scale = scale;
                }

                int cargoVal = 0;
                extraCargo.TryGetValue(pf.Name, out cargoVal);
                var amountInput = new GUINumberInput(new Rectangle(160, 0, 50, 20), "", GUINumberInput.NumberType.Int, textBlock);
                amountInput.MinValueInt = 0;
                amountInput.MaxValueInt = 100;
                amountInput.IntValue = cargoVal;

                amountInput.OnValueChanged += (numberInput) =>
                {
                    if (extraCargo.ContainsKey(pf.Name))
                    {
                        extraCargo[pf.Name] = numberInput.IntValue;
                    }
                    else
                    {
                        extraCargo.Add(pf.Name, numberInput.IntValue);
                    }
                };                
            }


            //--------------------------------------------------------------------------------
            //                              server settings 
            //--------------------------------------------------------------------------------

            y = 0;


            var startIntervalText = new GUITextBlock(new Rectangle(-10, y, 100, 20), "Autorestart delay", "", settingsTabs[1]);
            var startIntervalSlider = new GUIScrollBar(new Rectangle(10, y + 22, 100, 15), "", 0.1f, settingsTabs[1]);
            startIntervalSlider.UserData = startIntervalText;
            startIntervalSlider.Step = 0.05f;
            startIntervalSlider.BarScroll = AutoRestartInterval / 300.0f;
            startIntervalSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock text = scrollBar.UserData as GUITextBlock;

                AutoRestartInterval = Math.Max(barScroll * 300.0f, 10.0f);

                text.Text = "Autorestart delay: " + ToolBox.SecondsToReadableTime(AutoRestartInterval);
                return true;
            };
            startIntervalSlider.OnMoved(startIntervalSlider, startIntervalSlider.BarScroll);

            y += 45;

            var allowSpecBox = new GUITickBox(new Rectangle(0, y, 20, 20), "Allow spectating", Alignment.Left, settingsTabs[1]);
            allowSpecBox.Selected = AllowSpectating;
            allowSpecBox.OnSelected = (GUITickBox) =>
            {
                AllowSpectating = GUITickBox.Selected;
                GameMain.NetLobbyScreen.LastUpdateID++;
                return true;
            };

            y += 40;

            var voteKickBox = new GUITickBox(new Rectangle(0, y, 20, 20), "Allow vote kicking", Alignment.Left, settingsTabs[1]);
            voteKickBox.Selected = Voting.AllowVoteKick;
            voteKickBox.OnSelected = (GUITickBox) =>
            {
                Voting.AllowVoteKick = !Voting.AllowVoteKick;
                GameMain.Server.UpdateVoteStatus();
                return true;
            };

            var kickVotesRequiredText = new GUITextBlock(new Rectangle(20, y + 20, 20, 20), "Votes required: 50 %", "", settingsTabs[1], GUI.SmallFont);

            var kickVoteSlider = new GUIScrollBar(new Rectangle(150, y + 22, 100, 15), "", 0.1f, settingsTabs[1]);
            kickVoteSlider.UserData = kickVotesRequiredText;
            kickVoteSlider.Step = 0.2f;
            kickVoteSlider.BarScroll = (KickVoteRequiredRatio - 0.5f) * 2.0f;
            kickVoteSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                GUITextBlock voteText = scrollBar.UserData as GUITextBlock;

                KickVoteRequiredRatio = barScroll / 2.0f + 0.5f;
                voteText.Text = "Votes required: " + (int)MathUtils.Round(KickVoteRequiredRatio * 100.0f, 10.0f) + " %";
                return true;
            };
            kickVoteSlider.OnMoved(kickVoteSlider, kickVoteSlider.BarScroll);

            y += 45;

            var shareSubsBox = new GUITickBox(new Rectangle(0, y, 20, 20), "Share submarine files with players", Alignment.Left, settingsTabs[1]);
            shareSubsBox.Selected = AllowFileTransfers;
            shareSubsBox.OnSelected = (GUITickBox) =>
            {
                AllowFileTransfers = GUITickBox.Selected;
                return true;
            };

            y += 40;

            var randomizeLevelBox = new GUITickBox(new Rectangle(0, y, 20, 20), "Randomize level seed between rounds", Alignment.Left, settingsTabs[1]);
            randomizeLevelBox.Selected = RandomizeSeed;
            randomizeLevelBox.OnSelected = (GUITickBox) =>
            {
                RandomizeSeed = GUITickBox.Selected;
                return true;
            };

            y += 40;

            var saveLogsBox = new GUITickBox(new Rectangle(0, y, 20, 20), "Save server logs", Alignment.Left, settingsTabs[1]);
            saveLogsBox.Selected = SaveServerLogs;
            saveLogsBox.OnSelected = (GUITickBox) =>
            {
                SaveServerLogs = GUITickBox.Selected;
                showLogButton.Visible = SaveServerLogs;
                return true;
            };


            //--------------------------------------------------------------------------------
            //                              banlist
            //--------------------------------------------------------------------------------


            banList.CreateBanFrame(settingsTabs[2]);

            //--------------------------------------------------------------------------------
            //                              whitelist
            //--------------------------------------------------------------------------------

            whitelist.CreateWhiteListFrame(settingsTabs[3]);

        }


        private bool SwitchSubSelection(GUITickBox tickBox)
        {
            subSelectionMode = (SelectionMode)tickBox.UserData;

            foreach (GUIComponent otherTickBox in tickBox.Parent.children)
            {
                if (otherTickBox == tickBox) continue;
                ((GUITickBox)otherTickBox).Selected = false;
            }

            Voting.AllowSubVoting = subSelectionMode == SelectionMode.Vote;

            if (subSelectionMode == SelectionMode.Random)
            {
                GameMain.NetLobbyScreen.SubList.Select(Rand.Range(0, GameMain.NetLobbyScreen.SubList.CountChildren));
            }

            return true;
        }

        private bool SelectSettingsTab(GUIButton button, object obj)
        {
            settingsTabIndex = (int)obj;

            for (int i = 0; i < settingsTabs.Length; i++)
            {
                settingsTabs[i].Visible = i == settingsTabIndex;
            }

            return true;
        }

        private bool SwitchModeSelection(GUITickBox tickBox)
        {
            modeSelectionMode = (SelectionMode)tickBox.UserData;

            foreach (GUIComponent otherTickBox in tickBox.Parent.children)
            {
                if (otherTickBox == tickBox) continue;
                ((GUITickBox)otherTickBox).Selected = false;
            }

            Voting.AllowModeVoting = modeSelectionMode == SelectionMode.Vote;

            if (modeSelectionMode == SelectionMode.Random)
            {
                GameMain.NetLobbyScreen.ModeList.Select(Rand.Range(0, GameMain.NetLobbyScreen.ModeList.CountChildren));
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
                settingsFrame = null;
                SaveSettings();
            }

            return false;
        }

        public void ManagePlayersFrame(GUIFrame infoFrame)
        {
            GUIListBox cList = new GUIListBox(new Rectangle(0, 0, 0, 300), Color.White * 0.7f, "", infoFrame);
            cList.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
            //crewList.OnSelected = SelectCrewCharacter;

            foreach (Client c in ConnectedClients)
            {
                GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 40), Color.Transparent, null, cList);
                frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                frame.Color = (c.inGame && c.Character != null && !c.Character.IsDead) ? Color.Gold * 0.2f : Color.Transparent;
                frame.HoverColor = Color.LightGray * 0.5f;
                frame.SelectedColor = Color.Gold * 0.5f;

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(40, 0, 0, 25),
                    c.name + " (" + c.Connection.RemoteEndPoint.Address.ToString() + ")",
                    Color.Transparent, Color.White,
                    Alignment.Left, Alignment.Left,
                    null, frame);

                var banButton = new GUIButton(new Rectangle(-110, 0, 100, 20), "Ban", Alignment.Right | Alignment.CenterY, "", frame);
                banButton.UserData = c.name;
                banButton.OnClicked = GameMain.NetLobbyScreen.BanPlayer;

                var rangebanButton = new GUIButton(new Rectangle(-220, 0, 100, 20), "Ban range", Alignment.Right | Alignment.CenterY, "", frame);
                rangebanButton.UserData = c.name;
                rangebanButton.OnClicked = GameMain.NetLobbyScreen.BanPlayerRange;

                var kickButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Kick", Alignment.Right | Alignment.CenterY, "", frame);
                kickButton.UserData = c.name;
                kickButton.OnClicked = GameMain.NetLobbyScreen.KickPlayer;

                textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);
            }
        }
    }
}
