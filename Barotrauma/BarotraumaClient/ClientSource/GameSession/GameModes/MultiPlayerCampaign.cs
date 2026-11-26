using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class MultiPlayerCampaign : CampaignMode
    {
        public bool SuppressStateSending = false;

        public override bool Paused
        {
            get { return ForceMapUI || CoroutineManager.IsCoroutineRunning("LevelTransition"); }
        }

        private UInt16 pendingSaveID = 1;
        public UInt16 PendingSaveID
        {
            get
            {
                return pendingSaveID;
            }
            set
            {
                pendingSaveID = value;
                //pending save ID 0 means "no save received yet"
                //save IDs are always above 0, so we should never be waiting for 0
                if (pendingSaveID == 0) { pendingSaveID++; }
            }
        }

        /// <summary>
        /// Stores received from the server that are more recent than a save we don't have yet, applied once that save has been loaded (see <see cref="ApplyPendingStores"/>).
        /// </summary>
        private (UInt16 SaveID, UInt16 LocationIndex, XElement StoresElement)? pendingStores;

        public Wallet PersonalWallet => Character.Controlled?.Wallet ?? Wallet.Invalid;
        public override Wallet Wallet => GetWallet();

        public override int GetBalance(Client client = null)
        {
            if (!AllowedToManageWallets())
            {
                return PersonalWallet.Balance;
            }

            return PersonalWallet.Balance + Bank.Balance;
        }

        public override Wallet GetWallet(Client client = null)
        {
            return PersonalWallet;
        }

        public static void StartCampaignSetup(List<SaveInfo> saveFiles)
        {
            var parent = GameMain.NetLobbyScreen.CampaignSetupFrame;
            parent.ClearChildren();
            parent.Visible = true;
            GameMain.NetLobbyScreen.HighlightMode(
               GameMain.NetLobbyScreen.ModeList.Content.GetChildIndex(GameMain.NetLobbyScreen.ModeList.Content.GetChildByUserData(GameModePreset.MultiPlayerCampaign)));

            var layout = new GUILayoutGroup(new RectTransform(Vector2.One, parent.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.07f), layout.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.1f) }, isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var campaignContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), layout.RectTransform, Anchor.BottomLeft), style: "GUIFrameListBox")
            {
                CanBeFocused = false
            };
            
            var newCampaignContainer = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.95f), campaignContainer.RectTransform, Anchor.Center), style: null);
            var loadCampaignContainer = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.95f), campaignContainer.RectTransform, Anchor.Center), style: null);

            GameMain.NetLobbyScreen.CampaignSetupUI = new MultiPlayerCampaignSetupUI(newCampaignContainer, loadCampaignContainer, saveFiles);

            var newCampaignButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonContainer.RectTransform),
                TextManager.Get("NewCampaign"), style: "GUITabButton")
            {
                Selected = true
            };

            var loadCampaignButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.00f), buttonContainer.RectTransform),
                TextManager.Get("LoadCampaign"), style: "GUITabButton");

            newCampaignButton.OnClicked = (btn, obj) =>
            {
                newCampaignButton.Selected = true;
                loadCampaignButton.Selected = false;
                newCampaignContainer.Visible = true;
                loadCampaignContainer.Visible = false;
                GameMain.NetLobbyScreen?.RefreshStartButtonVisibility();
                return true;
            };
            loadCampaignButton.OnClicked = (btn, obj) =>
            {
                newCampaignButton.Selected = false;
                loadCampaignButton.Selected = true;
                newCampaignContainer.Visible = false;
                loadCampaignContainer.Visible = true;
                GameMain.NetLobbyScreen?.RefreshStartButtonVisibility();
                return true;
            };
            loadCampaignContainer.Visible = false;

            GUITextBlock.AutoScaleAndNormalize(newCampaignButton.TextBlock, loadCampaignButton.TextBlock);

            GameMain.NetLobbyScreen.CampaignSetupUI.StartNewGame = (SubmarineInfo sub, string saveName, string mapSeed, CampaignSettings settings) =>
            {
                GameMain.NetLobbyScreen.SetAFKSelected(false);
                GameMain.Client.SetupNewCampaign(sub, saveName, mapSeed, settings);
            };
            GameMain.NetLobbyScreen.CampaignSetupUI.LoadGame = (string filePath, Option<uint> backupIndex) =>
            {
                GameMain.NetLobbyScreen.SetAFKSelected(false);
                GameMain.Client.SetupLoadCampaign(filePath, backupIndex);
            };
        }

        partial void InitProjSpecific()
        {
            CreateButtons();
        }

        public override void HUDScaleChanged()
        {
            CreateButtons();
        }

        private void CreateButtons()
        {
            endRoundButton = CreateEndRoundButton();
            endRoundButton.OnClicked = (btn, userdata) =>
            {
                TryEndRoundWithFuelCheck(
                    onConfirm: () => GameMain.Client.RequestStartRound(),
                    onReturnToMapScreen: () =>
                    {
                        ShowCampaignUI = true;
                        if (CampaignUI == null) { InitCampaignUI(); }
                        CampaignUI.SelectTab(InteractionType.Map);
                    });
                return true;
            };

            int readyButtonWidth = (int)(GUI.Scale * 50 * (GUI.IsUltrawide ? 3.0f : 1.0f));
            int readyButtonHeight = (int)(GUI.Scale * 40);
            int readyButtonCenter = readyButtonHeight / 2,
                screenMiddle = GameMain.GraphicsWidth / 2;
            ReadyCheckButton = new GUIButton(HUDLayoutSettings.ToRectTransform(new Rectangle(screenMiddle + (endRoundButton.Rect.Width / 2) + GUI.IntScale(16), HUDLayoutSettings.ButtonAreaTop.Center.Y - readyButtonCenter, readyButtonWidth, readyButtonHeight), GUI.Canvas), 
                style: "RepairBuyButton")
            {
                ToolTip = TextManager.Get("ReadyCheck.Tooltip"),
                OnClicked = delegate
                {
                    if (CrewManager != null && CrewManager.ActiveReadyCheck == null)
                    {
                        ReadyCheck.CreateReadyCheck();
                    }
                    return true;
                },
                UserData = "ReadyCheckButton"
            };
        }

        public void InitCampaignUI()
        {
            campaignUIContainer = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: "InnerGlow", color: Color.Black);
            CampaignUI = new CampaignUI(this, campaignUIContainer)
            {
                StartRound = () =>
                {
                    GameMain.NetLobbyScreen.SetAFKSelected(false);
                    GameMain.Client.RequestStartRound();
                }
            };
        }

        public override void Start()
        {
            base.Start();
            CoroutineManager.StartCoroutine(DoInitialCameraTransition(), "MultiplayerCampaign.DoInitialCameraTransition");
        }

        protected override void LoadInitialLevel()
        {
            //clients should never call this
            throw new InvalidOperationException("");
        }


        private IEnumerable<CoroutineStatus> DoInitialCameraTransition()
        {
            while (GameMain.Instance.LoadingScreenOpen)
            {
                yield return CoroutineStatus.Running;
            }

            if (GameMain.Client == null)
            {
                yield return CoroutineStatus.Success;
            }

            if (GameMain.Client.LateCampaignJoin)
            {
                GameMain.Client.LateCampaignJoin = false;
                yield return CoroutineStatus.Success;
            }

            Character prevControlled = Character.Controlled;
            GUI.DisableHUD = true;
            if (IsFirstRound)
            {
                if (SlideshowPrefab.Prefabs.TryGet("campaignstart".ToIdentifier(), out var slideshow))
                {
                    SlideshowPlayer = new SlideshowPlayer(GUICanvas.Instance, slideshow);
                }

                Character.Controlled = null;
                prevControlled?.ClearInputs();

                overlayColor = Color.LightGray;
                overlaySprite = Map.CurrentLocation.Type.GetPortrait(Map.CurrentLocation.PortraitId);

                var outpost = GameMain.GameSession.Level.StartOutpost ?? Submarine.MainSub;
                var borders = outpost.GetDockedBorders();
                borders.Location += outpost.WorldPosition.ToPoint();
                GameMain.GameScreen.Cam.Position = new Vector2(borders.X + borders.Width / 2, borders.Y - borders.Height / 2);
                float startZoom = 0.8f /
                    ((float)Math.Max(borders.Width, borders.Height) / (float)GameMain.GameScreen.Cam.Resolution.X);
                GameMain.GameScreen.Cam.Zoom = GameMain.GameScreen.Cam.MinZoom = Math.Min(startZoom, GameMain.GameScreen.Cam.MinZoom);
                while (SlideshowPlayer != null && !SlideshowPlayer.LastTextShown)
                {
                    GUI.PreventPauseMenuToggle = true;
                    yield return CoroutineStatus.Running;
                }
                GUI.PreventPauseMenuToggle = false;
                prevControlled ??= Character.Controlled;
                GameMain.LightManager.LosAlpha = 0.0f;
                var transition = new CameraTransition(prevControlled, GameMain.GameScreen.Cam,
                    null, null,
                    fadeOut: false,
                    losFadeIn: true,
                    waitDuration: 1,
                    panDuration: 5,
                    startZoom: startZoom, endZoom: 1.0f)
                {
                    AllowInterrupt = true,
                    RemoveControlFromCharacter = false
                };
                overlayColor = Color.Transparent;
                while (transition.Running)
                {
                    yield return CoroutineStatus.Running;
                }

                if (prevControlled != null)
                {
                    Character.Controlled = prevControlled;
                }
            }
            else
            {
                var transition = new CameraTransition(Submarine.MainSub, GameMain.GameScreen.Cam,
                    null, null,
                    fadeOut: false,
                    losFadeIn: true,
                    panDuration: 5,
                    startZoom: 0.5f, endZoom: 1.0f)
                {
                    AllowInterrupt = true,
                    RemoveControlFromCharacter = true
                };
                while (transition.Running)
                {
                    yield return CoroutineStatus.Running;
                }
            }

            if (prevControlled != null)
            {
                prevControlled.SelectedItem = prevControlled.SelectedSecondaryItem = null;
                if (prevControlled.AIController != null)
                {
                    prevControlled.AIController.Enabled = true;
                }
            }
            GUI.DisableHUD = false;
            yield return CoroutineStatus.Success;
        }

        protected override IEnumerable<CoroutineStatus> DoLevelTransition(TransitionType transitionType, LevelData newLevel, Submarine leavingSub, bool mirror)
        {
            yield return CoroutineStatus.Success;
        }

        private IEnumerable<CoroutineStatus> DoLevelTransition()
        {
            SoundPlayer.OverrideMusicType = (CrewManager.GetCharacters().Any(c => !c.IsDead) ? "endround" : "crewdead").ToIdentifier();
            SoundPlayer.OverrideMusicDuration = 18.0f;

            Level prevLevel = Level.Loaded;

            bool success = CrewManager.GetCharacters().Any(c => !c.IsDead);
            CrewDead = false;

            var continueButton = GameMain.GameSession.RoundSummary?.ContinueButton;
            if (continueButton != null)
            {
                continueButton.Visible = false;
            }

            Character.Controlled = null;

            yield return new WaitForSeconds(0.1f);

            GameMain.Client.EndCinematic?.Stop();
            var endTransition = new CameraTransition(Submarine.MainSub, GameMain.GameScreen.Cam, null,
                Alignment.Center,
                fadeOut: false,
                panDuration: EndTransitionDuration);
            GameMain.Client.EndCinematic = endTransition;

            Location portraitLocation = Map?.SelectedLocation ?? Map?.CurrentLocation ?? Level.Loaded?.StartLocation;
            if (portraitLocation != null)
            {
                overlaySprite = portraitLocation.Type.GetPortrait(portraitLocation.PortraitId);
            }
            float fadeOutDuration = endTransition.PanDuration;
            float t = 0.0f;
            while (t < fadeOutDuration || endTransition.Running)
            {
                t += CoroutineManager.DeltaTime;
                overlayColor = Color.Lerp(Color.Transparent, Color.White, t / fadeOutDuration);
                yield return CoroutineStatus.Running;
            }
            overlayColor = Color.White;
            yield return CoroutineStatus.Running;

            //--------------------------------------

            //wait for the new level to be loaded
            DateTime timeOut = DateTime.Now + GameClient.LevelTransitionTimeOut;
            while (Level.Loaded == prevLevel || Level.Loaded == null)
            {
                if (DateTime.Now > timeOut || Screen.Selected != GameMain.GameScreen)  { break; }
                yield return CoroutineStatus.Running;
            }

            endTransition.Stop();
            overlayColor = Color.Transparent;

            if (DateTime.Now > timeOut) 
            {
                DebugConsole.ThrowError("Failed to start the round. Timed out while waiting for the level transition to finish.");
                GameMain.NetLobbyScreen.Select(); 
            }
            if (Screen.Selected is not RoundSummaryScreen)
            {
                if (continueButton != null)
                {
                    continueButton.Visible = true;
                }
            }

            GUI.SetSavingIndicatorState(false);
            yield return CoroutineStatus.Success;
        }

        protected override void UpdateGameModeSpecific(float deltaTime)
        {
            if (CoroutineManager.IsCoroutineRunning("LevelTransition") || Level.Loaded == null) { return; }

            base.UpdateGameModeSpecific(deltaTime);

            // The server rejects purchases when there's no merchant in range, so close the store instead of letting them just fail without feedback
            if (ShowCampaignUI && CampaignUI?.SelectedTab == InteractionType.Store &&
                // Use a slightly shorter distance than the server, so the store closes before the server would start rejecting
                !HasCampaignInteractionAvailable(Character.Controlled, InteractionType.Store, MaxCampaignInteractionDistance * 0.9f))
            {
                ShowCampaignUI = false;
            }

            if (ShowCampaignUI || ForceMapUI)
            {
                if (CampaignUI == null) { InitCampaignUI(); }
                Character.DisableControls = true;                
            }

            SlideshowPlayer?.UpdateManually(deltaTime);

            if (PlayerInput.SecondaryMouseButtonClicked() ||
                PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.Escape))
            {
                ShowCampaignUI = false;
                if (GUIMessageBox.VisibleBox?.UserData is RoundSummary roundSummary &&
                    roundSummary.ContinueButton != null &&
                    roundSummary.ContinueButton.Visible)
                {
                    GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.VisibleBox);
                }
            }

            if (!GUI.DisableHUD && !GUI.DisableUpperHUD)
            {
                endRoundButton.UpdateManually(deltaTime);
                ReadyCheckButton?.UpdateManually(deltaTime);
                if (CoroutineManager.IsCoroutineRunning("LevelTransition") || ForceMapUI) { return; }
            }

            if (Level.Loaded.Type == LevelData.LevelType.Outpost)
            {
                //note that we don't force the map to open if the sub leaves the bounds of the level, like we do in single player
                //that'd be problematic in MP because it doesn't pause the game, and not every player has access to use the map
                if (CampaignUI == null) { InitCampaignUI(); }
            }
            else
            {
                var transitionType = GetAvailableTransition(out _, out _);
                if (transitionType == TransitionType.None && CampaignUI?.SelectedTab == InteractionType.Map)
                {
                    ShowCampaignUI = false;
                }
                HintManager.OnAvailableTransition(transitionType);
            }
        }

        public override void UpdateWhilePaused(float deltaTime)
        {
            SlideshowPlayer?.UpdateManually(deltaTime);
        }

        public override void End(TransitionType transitionType = TransitionType.None)
        {
            base.End(transitionType);
            ForceMapUI = ShowCampaignUI = false;
            SlideshowPlayer?.Finish();

            // remove all event dialogue boxes
            GUIMessageBox.MessageBoxes.ForEachMod(mb =>
            {
                if (mb is GUIMessageBox msgBox)
                {
                    if (ReadyCheck.IsReadyCheck(mb) || mb.UserData is Pair<string, ushort> pair && pair.First.Equals("conversationaction", StringComparison.OrdinalIgnoreCase))
                    {
                        msgBox.Close();
                    }
                }
            });

            if (transitionType == TransitionType.End)
            {
                EndCampaign();
            }
            else
            {
                IsFirstRound = false;
                CoroutineManager.StartCoroutine(DoLevelTransition(), "LevelTransition");
            }
        }

        protected override void EndCampaignProjSpecific()
        {
            if (GUIMessageBox.VisibleBox?.UserData is RoundSummary roundSummary)
            {
                GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.VisibleBox);
            }
            GameMain.CampaignEndScreen.Select();
            GUI.DisableHUD = false;
            GameMain.CampaignEndScreen.OnFinished = () =>
            {
                GameMain.NetLobbyScreen.Select();
                if (GameMain.NetLobbyScreen.QuitCampaignButton != null) { GameMain.NetLobbyScreen.QuitCampaignButton.Enabled = false; }
            };
        }

        public void ClientWrite(IWriteMessage msg)
        {
            System.Diagnostics.Debug.Assert(map.Locations.Count < UInt16.MaxValue);

            msg.WriteUInt16(map.CurrentLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.CurrentLocationIndex);
            msg.WriteUInt16(map.SelectedLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.SelectedLocationIndex);
            msg.WriteUInt16(map.LocationToTeleportTo == null ? UInt16.MaxValue : (UInt16)map.Locations.IndexOf(map.LocationToTeleportTo));

            var selectedMissionIndices = map.GetSelectedMissionIndices();
            msg.WriteByte((byte)selectedMissionIndices.Count());
            foreach (int selectedMissionIndex in selectedMissionIndices)
            {
                msg.WriteByte((byte)selectedMissionIndex);
            }
            msg.WriteBoolean(PurchasedHullRepairs);
            msg.WriteBoolean(PurchasedItemRepairs);
            msg.WriteBoolean(PurchasedLostShuttles);

            WriteItems(msg, CargoManager.ItemsInBuyCrate);
            WriteItems(msg, CargoManager.ItemsInSellFromSubCrate);
            WriteItems(msg, CargoManager.PurchasedItems);
            WriteItems(msg, CargoManager.SoldItems);

            msg.WriteUInt16((ushort)UpgradeManager.PurchasedUpgrades.Count);
            foreach (var (prefab, category, level) in UpgradeManager.PurchasedUpgrades)
            {
                msg.WriteIdentifier(prefab.Identifier);
                msg.WriteIdentifier(category.Identifier);
                msg.WriteByte((byte)level);
            }

            msg.WriteUInt16((ushort)UpgradeManager.PurchasedItemSwaps.Count);
            foreach (var itemSwap in UpgradeManager.PurchasedItemSwaps)
            {
                msg.WriteUInt16(itemSwap.ItemToRemove.ID);
                msg.WriteIdentifier(itemSwap.ItemToInstall?.Identifier ?? Identifier.Empty);
            }
        }

        //static because we may need to instantiate the campaign if it hasn't been done yet
        public static void ClientRead(IReadMessage msg)
        {
            NetFlags requiredFlags = (NetFlags)msg.ReadUInt16();

            bool isFirstRound   =  msg.ReadBoolean();
            byte campaignID     = msg.ReadByte();
            byte roundId        = msg.ReadByte();
            UInt16 saveID       = msg.ReadUInt16();
            string mapSeed      = msg.ReadString();

            bool refreshCampaignUI = false;

            if (GameMain.GameSession?.GameMode is not MultiPlayerCampaign campaign || campaignID != campaign.CampaignID)
            {
                string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer);

                GameMain.GameSession = new GameSession(submarineInfo: null!, CampaignDataPath.CreateRegular(savePath), GameModePreset.MultiPlayerCampaign, CampaignSettings.Empty, mapSeed);
                campaign = (MultiPlayerCampaign)GameMain.GameSession.GameMode;
                campaign.CampaignID = campaignID;
                GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            }

            //server has a newer save file
            if (NetIdUtils.IdMoreRecent(saveID, campaign.PendingSaveID)) { campaign.PendingSaveID = saveID;  }
            campaign.IsFirstRound = isFirstRound;

            if (requiredFlags.HasFlag(NetFlags.Misc))
            {
                DebugConsole.Log("Received campaign update (Misc), round id: " + roundId);
                UInt16 id           = msg.ReadUInt16();
                bool purchasedHullRepairs = msg.ReadBoolean();
                bool purchasedItemRepairs = msg.ReadBoolean();
                bool purchasedLostShuttles = msg.ReadBoolean();
                if (ShouldApply(NetFlags.Misc, id, requireUpToDateSave: false))
                {
                    refreshCampaignUI = campaign.PurchasedHullRepairs != purchasedHullRepairs ||
                                    campaign.PurchasedItemRepairs != purchasedItemRepairs ||
                                    campaign.PurchasedLostShuttles != purchasedLostShuttles;
                    campaign.PurchasedHullRepairs = purchasedHullRepairs;
                    campaign.PurchasedItemRepairs = purchasedItemRepairs;
                    campaign.PurchasedLostShuttles = purchasedLostShuttles;
                }
            }

            if (requiredFlags.HasFlag(NetFlags.MapAndMissions))
            {
                DebugConsole.Log("Received campaign update (MapAndMissions), round id: " + roundId);
                UInt16 id = msg.ReadUInt16();
                bool forceMapUI = msg.ReadBoolean();
                bool allowDebugTeleport = msg.ReadBoolean();
                UInt16 currentLocIndex = msg.ReadUInt16();
                UInt16 selectedLocIndex = msg.ReadUInt16();

                byte missionCount = msg.ReadByte();
                var availableMissions = new List<(Identifier Identifier, byte ConnectionIndex)>();
                for (int i = 0; i < missionCount; i++)
                {
                    Identifier missionIdentifier = msg.ReadIdentifier();
                    byte connectionIndex = msg.ReadByte();
                    availableMissions.Add((missionIdentifier, connectionIndex));
                }

                byte selectedMissionCount = msg.ReadByte();
                List<int> selectedMissionIndices = new List<int>();
                for (int i = 0; i < selectedMissionCount; i++)
                {
                    selectedMissionIndices.Add(msg.ReadByte());
                }

                if (ShouldApply(NetFlags.MapAndMissions, id, requireUpToDateSave: true))
                {
                    campaign.ForceMapUI = forceMapUI;
                    campaign.Map.AllowDebugTeleport = allowDebugTeleport;
                    campaign.Map.SetLocation(currentLocIndex == UInt16.MaxValue ? -1 : currentLocIndex);
                    campaign.Map.SelectLocation(selectedLocIndex == UInt16.MaxValue ? -1 : selectedLocIndex);
                    //make the missions match the server's list exactly (including the order and any actual duplicates), because missions are selected by their index:
                    //reuse existing missions that match the ones received from the server, unlock the missing ones and drop the ones the server doesn't have
                    List<Mission> unmatchedMissions = campaign.Map.CurrentLocation.AvailableMissions.ToList();
                    List<Mission> missions = new List<Mission>();
                    Mission takeExistingMission(MissionPrefab prefab, Location destination)
                    {
                        Mission existingMission = unmatchedMissions.FirstOrDefault(m => m.Prefab == prefab && m.Locations[1] == destination);
                        unmatchedMissions.Remove(existingMission);
                        return existingMission;
                    }
                    foreach (var availableMission in availableMissions)
                    {
                        MissionPrefab missionPrefab = MissionPrefab.Prefabs.Find(mp => mp.Identifier == availableMission.Identifier);
                        if (missionPrefab == null)
                        {
                            DebugConsole.ThrowError($"Error when receiving campaign data from the server: mission prefab \"{availableMission.Identifier}\" not found.");
                            continue;
                        }
                        if (availableMission.ConnectionIndex == 255)
                        {
                            missions.Add(
                                takeExistingMission(missionPrefab, campaign.Map.CurrentLocation) ??
                                campaign.Map.CurrentLocation.ForceUnlockMission(missionPrefab));
                        }
                        else
                        {
                            if (availableMission.ConnectionIndex < 0 || availableMission.ConnectionIndex >= campaign.Map.CurrentLocation.Connections.Count)
                            {
                                DebugConsole.ThrowError($"Error when receiving campaign data from the server: connection index for mission \"{availableMission.Identifier}\" out of range (index: {availableMission.ConnectionIndex}, current location: {campaign.Map.CurrentLocation.DisplayName}, connections: {campaign.Map.CurrentLocation.Connections.Count}).");
                                continue;
                            }
                            LocationConnection connection = campaign.Map.CurrentLocation.Connections[availableMission.ConnectionIndex];
                            missions.Add(
                                takeExistingMission(missionPrefab, connection.OtherLocation(campaign.Map.CurrentLocation)) ??
                                campaign.Map.CurrentLocation.ForceUnlockMission(missionPrefab, connection));
                        }
                    }
                    if (missions.Contains(null))
                    {
                        DebugConsole.ThrowError($"Error when receiving campaign data from the server: failed to unlock some of the missions in the location {campaign.Map.CurrentLocation.DisplayName}. This may cause issues with selecting missions.");
                    }
                    campaign.Map.CurrentLocation.SetAvailableMissions(missions.Where(m => m != null));
                    campaign.Map.SelectMission(selectedMissionIndices);
                    ReadStores(msg, apply: true);
                }
                else
                {
                    ReadStores(msg, apply: false);
                }
            }

            if (requiredFlags.HasFlag(NetFlags.SubList))
            {
                DebugConsole.Log("Received campaign update (SubList), round id: " + roundId);
                UInt16 id = msg.ReadUInt16();
                ushort ownedSubCount = msg.ReadUInt16();
                List<ushort> ownedSubIndices = new List<ushort>();
                for (int i = 0; i < ownedSubCount; i++)
                {
                    ownedSubIndices.Add(msg.ReadUInt16());
                }

                if (ShouldApply(NetFlags.SubList, id, requireUpToDateSave: false))
                {
                    foreach (ushort ownedSubIndex in ownedSubIndices)
                    {
                        if (ownedSubIndex >= GameMain.Client.ServerSubmarines.Count)
                        {
                            string errorMsg;
                            if (GameMain.Client.ServerSubmarines.None())
                            {
                                errorMsg = $"Error in {nameof(MultiPlayerCampaign.ClientRead)}. Owned submarine index was out of bounds (list of server submarines is empty).";
                            }
                            else
                            {
                                errorMsg = $"Error in {nameof(MultiPlayerCampaign.ClientRead)}. Owned submarine index was out of bounds. Index: {ownedSubIndex}, submarines: {string.Join(", ", GameMain.Client.ServerSubmarines.Select(s => s.Name))}";
                            }
                            DebugConsole.ThrowError(errorMsg);
                            GameAnalyticsManager.AddErrorEventOnce(
                                "MultiPlayerCampaign.ClientRead.OwnerSubIndexOutOfBounds" + ownedSubIndex,
                                GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                            continue;
                        }

                        SubmarineInfo sub = GameMain.Client.ServerSubmarines[ownedSubIndex];
                        if (GameMain.NetLobbyScreen.CheckIfCampaignSubMatches(sub, NetLobbyScreen.SubmarineDeliveryData.Owned))
                        {
                            if (GameMain.GameSession.OwnedSubmarines.None(s => s.Name == sub.Name))
                            {
                                GameMain.GameSession.OwnedSubmarines.Add(sub);
                            }
                        }
                    }
                }
            }

            if (requiredFlags.HasFlag(NetFlags.UpgradeManager))
            {
                DebugConsole.Log("Received campaign update (UpgradeManager), round id: " + roundId);
                UInt16 id = msg.ReadUInt16();

                ushort pendingUpgradeCount = msg.ReadUInt16();
                List<PurchasedUpgrade> pendingUpgrades = new List<PurchasedUpgrade>();
                for (int i = 0; i < pendingUpgradeCount; i++)
                {
                    Identifier upgradeIdentifier = msg.ReadIdentifier();
                    UpgradePrefab prefab = UpgradePrefab.Find(upgradeIdentifier);
                    Identifier categoryIdentifier = msg.ReadIdentifier();
                    UpgradeCategory category = UpgradeCategory.Find(categoryIdentifier);
                    int upgradeLevel = msg.ReadByte();
                    if (prefab == null || category == null) { continue; }
                    pendingUpgrades.Add(new PurchasedUpgrade(prefab, category, upgradeLevel));
                }

                ushort purchasedItemSwapCount = msg.ReadUInt16();
                List<PurchasedItemSwap> purchasedItemSwaps = new List<PurchasedItemSwap>();
                for (int i = 0; i < purchasedItemSwapCount; i++)
                {
                    UInt16 itemToRemoveID = msg.ReadUInt16();
                    Identifier itemToInstallIdentifier = msg.ReadIdentifier();
                    ItemPrefab itemToInstall = itemToInstallIdentifier.IsEmpty ? null : ItemPrefab.Find(string.Empty, itemToInstallIdentifier);
                    if (Entity.FindEntityByID(itemToRemoveID) is not Item itemToRemove) { continue; }
                    purchasedItemSwaps.Add(new PurchasedItemSwap(itemToRemove, itemToInstall));
                }

                if (!Submarine.Unloading && Submarine.MainSub is not { Loading: true } && 
                    ShouldApply(NetFlags.UpgradeManager, id, requireUpToDateSave: true))
                {
                    UpgradeStore.WaitForServerUpdate = false;
                    campaign.UpgradeManager.SetPendingUpgrades(pendingUpgrades);
                    campaign.UpgradeManager.PurchasedUpgrades.Clear();
                    foreach (var purchasedItemSwap in purchasedItemSwaps)
                    {
                        UpgradeManager.DebugLog($"CLIENT (msg): Purchasing item swap to replace \"{purchasedItemSwap.ItemToRemove?.Prefab.Name}\" with \"{purchasedItemSwap.ItemToInstall?.Name}\"");
                        campaign.UpgradeManager.PurchaseItemSwap(purchasedItemSwap.ItemToRemove, purchasedItemSwap.ItemToInstall, isNetworkMessage: true);
                    }

                    // Drop swaps the server no longer has (e.g. cancelled by another client). 
                    // Must happen before resetting PendingItemSwap below, otherwise the stale swaps would keep the items' pending swaps alive.
                    campaign.UpgradeManager.PurchasedItemSwaps.RemoveAll(swap => !purchasedItemSwaps.Contains(swap));

                    foreach (Item item in Item.ItemList.ToList())
                    {
                        // Check against the complete list of pending swaps, not just the items modified in this message.
                        // Otherwise, items with existing pending swaps get reset when other items are modified.
                        if (item.PendingItemSwap != null && campaign.UpgradeManager.PurchasedItemSwaps.All(it => it.ItemToRemove != item))
                        {
                            item.PendingItemSwap = null;
                        }
                    }

                    campaign.CampaignUI?.UpgradeStore?.RequestRefresh();
                }
            }


            if (requiredFlags.HasFlag(NetFlags.ItemsInBuyCrate))
            {
                DebugConsole.Log("Received campaign update (ItemsInBuyCrate), round id: " + roundId);
                UInt16 id = msg.ReadUInt16();
                var buyCrateItems = ReadPurchasedItems(msg, sender: null);
                if (ShouldApply(NetFlags.ItemsInBuyCrate, id, requireUpToDateSave: true))
                {
                    campaign.CargoManager.SetItemsInBuyCrate(buyCrateItems);
                    campaign.SetLastUpdateIdForFlag(NetFlags.ItemsInBuyCrate, id);
                    ReadStores(msg, apply: true);
                }
                else
                {
                    ReadStores(msg, apply: false);
                }
            }
            if (requiredFlags.HasFlag(NetFlags.ItemsInSellFromSubCrate))
            {
                DebugConsole.Log("Received campaign update (ItemsInSellFromSubCrate), round id: " + roundId);
                UInt16 id = msg.ReadUInt16();
                var subSellCrateItems = ReadPurchasedItems(msg, sender: null);
                if (ShouldApply(NetFlags.ItemsInSellFromSubCrate, id, requireUpToDateSave: true))
                {
                    campaign.CargoManager.SetItemsInSubSellCrate(subSellCrateItems);
                    campaign.SetLastUpdateIdForFlag(NetFlags.ItemsInSellFromSubCrate, id);
                    ReadStores(msg, apply: true);
                }
                else
                {
                    ReadStores(msg, apply: false);
                }
            }
            if (requiredFlags.HasFlag(NetFlags.PurchasedItems))
            {
                DebugConsole.Log("Received campaign update (PuchasedItems), round id: " + roundId);
                UInt16 id = msg.ReadUInt16();
                var purchasedItems = ReadPurchasedItems(msg, sender: null);
                if (ShouldApply(NetFlags.PurchasedItems, id, requireUpToDateSave: true))
                {
                    campaign.CargoManager.SetPurchasedItems(purchasedItems);
                    campaign.SetLastUpdateIdForFlag(NetFlags.PurchasedItems, id);
                    ReadStores(msg, apply: true);
                }
                else
                {
                    ReadStores(msg, apply: false);
                }
            }
            if (requiredFlags.HasFlag(NetFlags.SoldItems))
            {
                DebugConsole.Log("Received campaign update (SoldItems), round id: " + roundId);
                UInt16 id = msg.ReadUInt16();
                var soldItems = ReadSoldItems(msg);
                if (ShouldApply(NetFlags.SoldItems, id, requireUpToDateSave: true))
                {
                    campaign.CargoManager.SetSoldItems(soldItems);
                    campaign.SetLastUpdateIdForFlag(NetFlags.SoldItems, id);
                    ReadStores(msg, apply: true);
                }
                else
                {
                    ReadStores(msg, apply: false);
                }
            }
            if (requiredFlags.HasFlag(NetFlags.Reputation))
            {
                DebugConsole.Log("Received campaign update (Reputation), round id: " + roundId);
                UInt16 id = msg.ReadUInt16();
                Dictionary<Identifier, float> factionReps = new Dictionary<Identifier, float>();
                byte factionsCount = msg.ReadByte();
                for (int i = 0; i < factionsCount; i++)
                {
                    factionReps.Add(msg.ReadIdentifier(), msg.ReadSingle());
                }
                if (ShouldApply(NetFlags.Reputation, id, requireUpToDateSave: true))
                {
                    foreach ((Identifier identifier, float rep) in factionReps)
                    {
                        Faction faction = campaign.GetFaction(identifier);
                        if (faction?.Reputation != null)
                        {
                            faction.Reputation.SetReputation(rep);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Received an update for a faction that doesn't exist \"{identifier}\".");
                        }
                    }
                    campaign?.CampaignUI?.UpgradeStore?.RequestRefresh();
                }
            }
            if (requiredFlags.HasFlag(NetFlags.CharacterInfo))
            {
                DebugConsole.Log("Received campaign update (CharacterInfo), round id: " + roundId);
                UInt16 id = msg.ReadUInt16();
                bool hasCharacterData = msg.ReadBoolean();
                CharacterInfo myCharacterInfo = null;
                bool waitForModsDownloaded = Screen.Selected is ModDownloadScreen;
                if (hasCharacterData)
                {
                    myCharacterInfo = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName, msg, requireJobPrefabFound: !waitForModsDownloaded);
                }
                //don't require the correct round ID for the character info if we're in the lobby
                // = allow updating the character to the latest one in the lobby, even though we've not loaded to the same round as the server
                if (!waitForModsDownloaded && ShouldApply(NetFlags.CharacterInfo, id, requireUpToDateSave: true, requireCorrectRoundId: Screen.Selected != GameMain.NetLobbyScreen))
                {
                    if (myCharacterInfo != null)
                    {
                        GameMain.Client.CharacterInfo = myCharacterInfo;
                        GameMain.NetLobbyScreen.SetCampaignCharacterInfo(myCharacterInfo);
                        GameMain.GameSession.RefreshAnyOpenPlayerInfo();
                    }
                    else
                    {
                        //don't reset the character info nor the open UI here here,
                        //the client needs it to be able to customize the character they want to next spawn as
                        GameMain.NetLobbyScreen.SetCampaignCharacterInfo(null);
                        //if we've already discarded our current character and the server is just "verifying" that,
                        //no need to refresh the UI (no changes, refreshing would just throw the client out of the character settings panel)
                        if (!GameMain.NetLobbyScreen.CampaignCharacterDiscarded)
                        {
                            GameMain.GameSession.RefreshAnyOpenPlayerInfo();
                        }
                    }
                }
            }

            campaign.SuppressStateSending = true;
            //we need to have the latest save file to display location/mission/store
            if (campaign.LastSaveID == saveID)
            {
                GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            }
            if (refreshCampaignUI)
            {
                campaign?.CampaignUI?.UpgradeStore?.RequestRefresh();
            }
            campaign.SuppressStateSending = false;            

            bool ShouldApply(NetFlags flag, UInt16 id, bool requireUpToDateSave, bool requireCorrectRoundId = true)
            {
                if (requireCorrectRoundId && roundId != campaign.RoundID)
                {
                    //set update flag anyway so we can tell the server we received this data and it won't need to keep resending it
                    campaign.SetLastUpdateIdForFlag(flag, id);
                    DebugConsole.Log($"Received campaing update for a different round (client: {campaign.RoundID}, server: {roundId}), ignoring...");                    
                    return false;
                }

                if (NetIdUtils.IdMoreRecent(id, campaign.GetLastUpdateIdForFlag(flag)) &&
                     (!requireUpToDateSave || saveID == campaign.LastSaveID))
                {
                    campaign.SetLastUpdateIdForFlag(flag, id);
                    return true;
                }
                else
                {
                    return false;
                }
            }

            void ReadStores(IReadMessage msg, bool apply)
            {
                var storeBalances = new Dictionary<Identifier, UInt16>();
                if (msg.ReadBoolean())
                {
                    byte storeCount = msg.ReadByte();
                    for (int i = 0; i < storeCount; i++)
                    {
                        Identifier identifier = msg.ReadIdentifier();
                        UInt16 storeBalance = msg.ReadUInt16();
                        storeBalances.Add(identifier, storeBalance);
                    }
                }
                if (apply)
                {
                    foreach (var balance in storeBalances)
                    {
                        if (campaign.Map?.CurrentLocation?.GetStore(balance.Key) is { } store)
                        {
                            store.Balance = balance.Value;
                        }
                    }
                }
            }

        }

        public void ClientReadCrew(IReadMessage msg)
        {
            bool createNotification = msg.ReadBoolean();

            ushort availableHireLength = msg.ReadUInt16();
            List<CharacterInfo> availableHires = new List<CharacterInfo>();
            for (int i = 0; i < availableHireLength; i++)
            {
                CharacterInfo hire = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName, msg);
                hire.Salary = msg.ReadInt32();
                availableHires.Add(hire);
            }

            ushort pendingHireLength = msg.ReadUInt16();
            List<UInt16> pendingHires = new List<UInt16>();
            bool[] pendingHiresToReserveBench = new bool[pendingHireLength];
            for (int i = 0; i < pendingHireLength; i++)
            {
                pendingHires.Add(msg.ReadUInt16());
                pendingHiresToReserveBench[i] = msg.ReadBoolean();
            }

            List<CharacterInfo> hiredCharacters = new List<CharacterInfo>();
            List<CharacterInfo> updatedCrewManager = new List<CharacterInfo>();
            ushort crewLength = msg.ReadUInt16();
            for (int i = 0; i < crewLength; i++)
            {
                CharacterInfo crewMember = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName, msg);
                if (crewMember.IsNewHire)
                {
                    hiredCharacters.Add(crewMember);
                }
                updatedCrewManager.Add(crewMember);
            }
            bool crewManagerUpdated = GameMain.GameSession.CrewManager?.UpdateCrewManagerIfNecessary(updatedCrewManager) ?? false;
            
            ushort reserveBenchLength = msg.ReadUInt16();
            List<CharacterInfo> updatedReserveBench = new List<CharacterInfo>();
            for (int i = 0; i < reserveBenchLength; i++)
            {
                CharacterInfo info = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName, msg);
                updatedReserveBench.Add(info);
            }
            bool reserveBenchUpdated = GameMain.GameSession.CrewManager?.UpdateReserveBenchIfNeeded(updatedReserveBench) ?? false;
            
            bool renameCrewMember = msg.ReadBoolean();
            if (renameCrewMember)
            {
                UInt16 renamedIdentifier = msg.ReadUInt16();
                string newName = msg.ReadString();
                CharacterInfo renamedCharacter = CrewManager.GetCharacterInfos(includeReserveBench: true).FirstOrDefault(info => info.ID == renamedIdentifier);
                if (renamedCharacter != null)
                {
                    CrewManager.RenameCharacter(renamedCharacter, newName);
                    // Since renaming can only be done once in permadeath, we can safely set this to false to disable the renaming in the UI.
                    renamedCharacter.RenamingEnabled = false;
                }
                else
                {
                    DebugConsole.ThrowError($"Could not find a character to rename with the ID {renamedIdentifier}.");
                }
            }

            bool fireCharacter = msg.ReadBoolean();
            if (fireCharacter)
            {
                UInt16 firedIdentifier = msg.ReadUInt16();
                CharacterInfo firedCharacter = CrewManager.GetCharacterInfos(includeReserveBench: true).FirstOrDefault(info => info.ID == firedIdentifier);
                // this one might and is allowed to be null since the character is already fired on the original sender's game
                if (firedCharacter != null) { CrewManager.FireCharacter(firedCharacter); }
            }

            if (map?.CurrentLocation?.HireManager != null && CampaignUI?.HRManagerUI != null)
            {
                //can't apply until we have the latest save file
                if (!NetIdUtils.IdMoreRecent(pendingSaveID, LastSaveID))
                {
                    CampaignUI.HRManagerUI.SetHireables(map.CurrentLocation, availableHires);
                    if (hiredCharacters.Any()) { CampaignUI.HRManagerUI.ValidateHires(hiredCharacters, takeMoney: false, createNotification: createNotification); }
                    //don't check the crew size limit: if the server says someone's hired, then it's so
                    CampaignUI.HRManagerUI.SetPendingHires(pendingHires, pendingHiresToReserveBench, map.CurrentLocation, checkCrewSizeLimit: false);
                }
            }
            else
            {
                //This is pretty nasty: setting hireables is handled through CrewManagement,
                //which is part of the Campaign UI that might not exist when the client is still initializing the round.
                //If that's the case, let's force the available hires here so they're available when the UI is created
                CurrentLocation?.ForceHireableCharacters(availableHires);
            }
            
            if (fireCharacter || renameCrewMember || crewManagerUpdated || reserveBenchUpdated)
            {
                CampaignUI?.HRManagerUI?.RefreshHRView();
                GameMain.GameSession?.DeathPrompt?.UpdateBotList();
            }
        }

        /// <summary>
        /// Replaces the stores at the current location with those of the server.
        /// </summary>
        public static void ClientReadStores(IReadMessage inc)
        {
            byte campaignID = inc.ReadByte();
            UInt16 saveID = inc.ReadUInt16();
            UInt16 locationIndex = inc.ReadUInt16();
            string storesXml = inc.ReadString();

            // Not loaded yet (or anymore), in which case the stores will come from the save
            if (GameMain.GameSession?.Campaign is not MultiPlayerCampaign campaign || campaign.CampaignID != campaignID) { return; }

            XElement storesElement;
            try
            {
                storesElement = XElement.Parse(storesXml);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to read the stores sent by the server.", e);
                return;
            }

            if (NetIdUtils.IdMoreRecent(saveID, campaign.LastSaveID))
            {
                // Loading the save would overwrite these stores, so apply them after it's been loaded
                campaign.pendingStores = (saveID, locationIndex, storesElement);
            }
            else if (saveID == campaign.LastSaveID)
            {
                campaign.ApplyStores(locationIndex, storesElement);
            }
            // Otherwise these stores are older than the ones in the save we've loaded
        }

        /// <summary>
        /// Applies the stores received while the save they're more recent than was still being loaded, called after loading a save.
        /// </summary>
        public void ApplyPendingStores()
        {
            if (pendingStores is not { } pending) { return; }
            // Still waiting for the save
            if (NetIdUtils.IdMoreRecent(pending.SaveID, LastSaveID)) { return; }

            pendingStores = null;
            // If the save we loaded is newer, its stores are also more recent than these
            if (pending.SaveID == LastSaveID)
            {
                ApplyStores(pending.LocationIndex, pending.StoresElement);
            }
        }

        private void ApplyStores(UInt16 locationIndex, XElement storesElement)
        {
            if (Map is not { } map || locationIndex >= map.Locations.Count) { return; }

            Location location = map.Locations[locationIndex];
            location.LoadStores(storesElement, createMissingStores: false);
            if (location == map.CurrentLocation)
            {
                CampaignUI?.Store?.RefreshActiveStore();
            }
        }

        /// <summary>
        /// Switch to the server's round. Campaign updates received for a different round are acknowledged without applying them
        /// (see ShouldApply in <see cref="ClientRead"/>), so mark us being behind on all data to make the server resend its current state.
        /// Otherwise e.g. a client joining mid-round would never get the store and mission state sent before it finished loading.
        /// </summary>
        public void SetRoundID(byte roundId)
        {
            DebugConsole.Log($"Set round ID from {RoundID} to {roundId}.");
            if (roundId != RoundID)
            {
                DecrementAllLastUpdateIds();
            }
            RoundID = roundId;
        }

        /// <summary>
        /// The server rejected our store changes, which we've already applied on our end.
        /// Mark us being behind on that store data, so the server sends us its state.
        /// </summary>
        public void ClientReadStoreChangesRejected(IReadMessage inc)
        {
            NetFlags rejectedFlags = (NetFlags)inc.ReadUInt16();
            foreach (NetFlags flag in Enum.GetValues(typeof(NetFlags)))
            {
                if (rejectedFlags.HasFlag(flag))
                {
                    SetLastUpdateIdForFlag(flag, (UInt16)(GetLastUpdateIdForFlag(flag) - 1));
                }
            }
        }

        public void ClientReadMoney(IReadMessage inc)
        {
            NetWalletUpdate update = INetSerializableStruct.Read<NetWalletUpdate>(inc);
            foreach (NetWalletTransaction transaction in update.Transactions)
            {
                WalletInfo info = transaction.Info;
                if (transaction.CharacterID.TryUnwrap(out var charID))
                {
                    Character targetCharacter = Character.CharacterList?.FirstOrDefault(c => c.ID == charID);
                    if (targetCharacter is null) { break; }
                    Wallet wallet = targetCharacter.Wallet;

                    wallet.Balance = info.Balance;
                    wallet.RewardDistribution = info.RewardDistribution;
                    TryInvokeEvent(wallet, transaction.ChangedData, info);
                }
                else
                {
                    Bank.Balance = info.Balance;
                    Bank.RewardDistribution = info.RewardDistribution;
                    TryInvokeEvent(Bank, transaction.ChangedData, info);
                }
            }

            void TryInvokeEvent(Wallet wallet, WalletChangedData data, WalletInfo info)
            {
                if (data.BalanceChanged.IsSome() || data.RewardDistributionChanged.IsSome())
                {
                    OnMoneyChanged.Invoke(new WalletChangedEvent(wallet, data, info));
                }
            }
        }

        protected override bool TryDeductFunds(Client client, int price)
        {
            if (price == 0)
            {
                return true;
            }

            if (!AllowedToManageCampaign(ClientPermissions.ManageMoney))
            {
                return PersonalWallet.TryDeduct(price);
            }

            int balance = PersonalWallet.Balance;

            if (balance >= price)
            {
                return PersonalWallet.TryDeduct(price);
            }

            if (balance + Bank.Balance >= price)
            {
                int remainder = price - balance;
                if (balance > 0) { PersonalWallet.Deduct(balance); }
                Bank.Deduct(remainder);
                return true;
            }

            return false;
        }

        public override void Save(XElement element, bool isSavingOnLoading)
        {
            //do nothing, the clients get the save files from the server
        }

        public void LoadState(string filePath)
        {
            DebugConsole.Log($"Loading save file for an existing game session ({filePath})");
            SaveUtil.DecompressToDirectory(filePath, SaveUtil.TempPath);

            string gamesessionDocPath = Path.Combine(SaveUtil.TempPath, SaveUtil.GameSessionFileName);
            XDocument doc = XMLExtensions.TryLoadXml(gamesessionDocPath);
            if (doc == null)
            {
                DebugConsole.ThrowError($"Failed to load the state of a multiplayer campaign. Could not open the file \"{gamesessionDocPath}\".");
                return;
            }
            Load(doc.Root.Element("MultiPlayerCampaign"));
            GameMain.GameSession.OwnedSubmarines = SaveUtil.LoadOwnedSubmarines(doc, out SubmarineInfo selectedSub);
            GameMain.GameSession.SubmarineInfo = selectedSub;
        }
    }
}
