﻿using Barotrauma.Extensions;
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
                RelativeSpacing = 0.02f
            };

            var campaignContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), layout.RectTransform, Anchor.BottomLeft), style: "InnerFrame")
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
                return true;
            };
            loadCampaignButton.OnClicked = (btn, obj) =>
            {
                newCampaignButton.Selected = false;
                loadCampaignButton.Selected = true;
                newCampaignContainer.Visible = false;
                loadCampaignContainer.Visible = true;
                return true;
            };
            loadCampaignContainer.Visible = false;

            GUITextBlock.AutoScaleAndNormalize(newCampaignButton.TextBlock, loadCampaignButton.TextBlock);

            GameMain.NetLobbyScreen.CampaignSetupUI.StartNewGame = GameMain.Client.SetupNewCampaign;
            GameMain.NetLobbyScreen.CampaignSetupUI.LoadGame = GameMain.Client.SetupLoadCampaign;
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

        private void InitCampaignUI()
        {
            campaignUIContainer = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: "InnerGlow", color: Color.Black);
            CampaignUI = new CampaignUI(this, campaignUIContainer)
            {
                StartRound = () =>
                {
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

                var outpost = GameMain.GameSession.Level.StartOutpost;
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

        protected override IEnumerable<CoroutineStatus> DoLevelTransition(TransitionType transitionType, LevelData newLevel, Submarine leavingSub, bool mirror, List<TraitorMissionResult> traitorResults = null)
        {
            yield return CoroutineStatus.Success;
        }

        private IEnumerable<CoroutineStatus> DoLevelTransition()
        {
            SoundPlayer.OverrideMusicType = (CrewManager.GetCharacters().Any(c => !c.IsDead) ? "endround" : "crewdead").ToIdentifier();
            SoundPlayer.OverrideMusicDuration = 18.0f;

            Level prevLevel = Level.Loaded;

            bool success = CrewManager.GetCharacters().Any(c => !c.IsDead);
            crewDead = false;

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

        public override void Update(float deltaTime)
        {
            if (CoroutineManager.IsCoroutineRunning("LevelTransition") || Level.Loaded == null) { return; }

            if (ShowCampaignUI || ForceMapUI)
            {
                if (CampaignUI == null) { InitCampaignUI(); }
                Character.DisableControls = true;                
            }

            base.Update(deltaTime);

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
                if (wasDocked)
                {
                    var connectedSubs = Submarine.MainSub.GetConnectedSubs();
                    bool isDocked = Level.Loaded.StartOutpost != null && connectedSubs.Contains(Level.Loaded.StartOutpost);
                    if (!isDocked)
                    {
                        //undocked from outpost, need to choose a destination
                        ForceMapUI = true; 
                        if (CampaignUI == null) { InitCampaignUI(); }
                        CampaignUI.SelectTab(InteractionType.Map);
                    }
                }
                //end biome is handled by the server (automatic transition without a map screen when the end of the level is reached)
                else if (!Level.Loaded.IsEndBiome)
                {
                    //wasn't initially docked (sub doesn't have a docking port?)
                    // -> choose a destination when the sub is far enough from the start outpost
                    if (!Submarine.MainSub.AtStartExit)
                    {
                        ForceMapUI = true;
                        if (CampaignUI == null) { InitCampaignUI(); }
                        CampaignUI.SelectTab(InteractionType.Map);
                    }
                }

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
                if (GameMain.NetLobbyScreen.ContinueCampaignButton != null) { GameMain.NetLobbyScreen.ContinueCampaignButton.Enabled = false; }
                if (GameMain.NetLobbyScreen.QuitCampaignButton != null) { GameMain.NetLobbyScreen.QuitCampaignButton.Enabled = false; }
            };
        }

        public void ClientWrite(IWriteMessage msg)
        {
            System.Diagnostics.Debug.Assert(map.Locations.Count < UInt16.MaxValue);

            msg.WriteUInt16(map.CurrentLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.CurrentLocationIndex);
            msg.WriteUInt16(map.SelectedLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.SelectedLocationIndex);

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
            UInt16 saveID       = msg.ReadUInt16();
            string mapSeed      = msg.ReadString();

            bool refreshCampaignUI = false;

            if (!(GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign) || campaignID != campaign.CampaignID)
            {
                string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer);

                GameMain.GameSession = new GameSession(null, savePath, GameModePreset.MultiPlayerCampaign, CampaignSettings.Empty, mapSeed);
                campaign = (MultiPlayerCampaign)GameMain.GameSession.GameMode;
                campaign.CampaignID = campaignID;
                GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            }

            //server has a newer save file
            if (NetIdUtils.IdMoreRecent(saveID, campaign.PendingSaveID)) { campaign.PendingSaveID = saveID;  }
            campaign.IsFirstRound = isFirstRound;

            if (requiredFlags.HasFlag(NetFlags.Misc))
            {
                DebugConsole.Log("Received campaign update (Misc)");
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
                DebugConsole.Log("Received campaign update (MapAndMissions)");
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
                            campaign.Map.CurrentLocation.UnlockMission(missionPrefab);
                        }
                        else
                        {
                            if (availableMission.ConnectionIndex < 0 || availableMission.ConnectionIndex >= campaign.Map.CurrentLocation.Connections.Count)
                            {
                                DebugConsole.ThrowError($"Error when receiving campaign data from the server: connection index for mission \"{availableMission.Identifier}\" out of range (index: {availableMission.ConnectionIndex}, current location: {campaign.Map.CurrentLocation.Name}, connections: {campaign.Map.CurrentLocation.Connections.Count}).");
                                continue;
                            }
                            LocationConnection connection = campaign.Map.CurrentLocation.Connections[availableMission.ConnectionIndex];
                            campaign.Map.CurrentLocation.UnlockMission(missionPrefab, connection);
                        }
                    }
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
                DebugConsole.Log("Received campaign update (SubList)");
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
                            string errorMsg = $"Error in {nameof(MultiPlayerCampaign.ClientRead)}. Owned submarine index was out of bounds. Index: {ownedSubIndex}, submarines: {string.Join(", ", GameMain.Client.ServerSubmarines.Select(s => s.Name))}";
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
                DebugConsole.Log("Received campaign update (UpgradeManager)");
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
                    if (!(Entity.FindEntityByID(itemToRemoveID) is Item itemToRemove)) { continue; }
                    purchasedItemSwaps.Add(new PurchasedItemSwap(itemToRemove, itemToInstall));
                }

                if (!Submarine.Unloading && !(Submarine.MainSub is { Loading: true }) && 
                    ShouldApply(NetFlags.UpgradeManager, id, requireUpToDateSave: true))
                {
                    UpgradeStore.WaitForServerUpdate = false;
                    campaign.UpgradeManager.SetPendingUpgrades(pendingUpgrades);
                    campaign.UpgradeManager.PurchasedUpgrades.Clear();
                    foreach (var purchasedItemSwap in purchasedItemSwaps)
                    {
                        if (purchasedItemSwap.ItemToInstall == null)
                        {
                            campaign.UpgradeManager.CancelItemSwap(purchasedItemSwap.ItemToRemove, force: true);
                        }
                        else
                        {
                            campaign.UpgradeManager.PurchaseItemSwap(purchasedItemSwap.ItemToRemove, purchasedItemSwap.ItemToInstall, force: true);
                        }
                    }
                    foreach (Item item in Item.ItemList.ToList())
                    {
                        if (item.PendingItemSwap != null && !purchasedItemSwaps.Any(it => it.ItemToRemove == item))
                        {
                            item.PendingItemSwap = null;
                        }
                    }
                    campaign.CampaignUI?.UpgradeStore?.RequestRefresh();
                }
            }


            if (requiredFlags.HasFlag(NetFlags.ItemsInBuyCrate))
            {
                DebugConsole.Log("Received campaign update (ItemsInBuyCrate)");
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
                DebugConsole.Log("Received campaign update (ItemsInSellFromSubCrate)");
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
                DebugConsole.Log("Received campaign update (PuchasedItems)");
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
                DebugConsole.Log("Received campaign update (SoldItems)");
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
                DebugConsole.Log("Received campaign update (Reputation)");
                UInt16 id = msg.ReadUInt16();
                Dictionary<Identifier, float> factionReps = new Dictionary<Identifier, float>();
                byte factionsCount = msg.ReadByte();
                for (int i = 0; i < factionsCount; i++)
                {
                    factionReps.Add(msg.ReadIdentifier(), msg.ReadSingle());
                }
                if (ShouldApply(NetFlags.Reputation, id, requireUpToDateSave: true))
                {
                    foreach (var (identifier, rep) in factionReps)
                    {
                        Faction faction = campaign.Factions.FirstOrDefault(f => f.Prefab.Identifier == identifier);
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
                DebugConsole.Log("Received campaign update (CharacterInfo)");
                UInt16 id = msg.ReadUInt16();
                bool hasCharacterData = msg.ReadBoolean();
                CharacterInfo myCharacterInfo = null;
                if (hasCharacterData)
                {
                    myCharacterInfo = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName, msg);
                }
                if (ShouldApply(NetFlags.CharacterInfo, id, requireUpToDateSave: true))
                {
                    if (myCharacterInfo != null)
                    {
                        GameMain.Client.CharacterInfo = myCharacterInfo;
                        GameMain.NetLobbyScreen.SetCampaignCharacterInfo(myCharacterInfo);
                    }
                    else
                    {
                        GameMain.NetLobbyScreen.SetCampaignCharacterInfo(null);
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

            bool ShouldApply(NetFlags flag, UInt16 id, bool requireUpToDateSave)
            {
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
            ushort availableHireLength = msg.ReadUInt16();
            List<CharacterInfo> availableHires = new List<CharacterInfo>();
            for (int i = 0; i < availableHireLength; i++)
            {
                CharacterInfo hire = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName, msg);
                hire.Salary = msg.ReadInt32();
                availableHires.Add(hire);
            }

            ushort pendingHireLength = msg.ReadUInt16();
            List<int> pendingHires = new List<int>();
            for (int i = 0; i < pendingHireLength; i++)
            {
                pendingHires.Add(msg.ReadInt32());
            }

            ushort hiredLength = msg.ReadUInt16();
            List<CharacterInfo> hiredCharacters = new List<CharacterInfo>();
            for (int i = 0; i < hiredLength; i++)
            {
                CharacterInfo hired = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName, msg);
                hired.Salary = msg.ReadInt32();
                hiredCharacters.Add(hired);
            }

            bool renameCrewMember = msg.ReadBoolean();
            if (renameCrewMember)
            {
                int renamedIdentifier = msg.ReadInt32();
                string newName = msg.ReadString();
                CharacterInfo renamedCharacter = CrewManager.CharacterInfos.FirstOrDefault(info => info.GetIdentifierUsingOriginalName() == renamedIdentifier);
                if (renamedCharacter != null) { CrewManager.RenameCharacter(renamedCharacter, newName); }
            }

            bool fireCharacter = msg.ReadBoolean();
            if (fireCharacter)
            {
                int firedIdentifier = msg.ReadInt32();
                CharacterInfo firedCharacter = CrewManager.CharacterInfos.FirstOrDefault(info => info.GetIdentifier() == firedIdentifier);
                // this one might and is allowed to be null since the character is already fired on the original sender's game
                if (firedCharacter != null) { CrewManager.FireCharacter(firedCharacter); }
            }

            if (map?.CurrentLocation?.HireManager != null && CampaignUI?.CrewManagement != null && 
                /*can't apply until we have the latest save file*/
                !NetIdUtils.IdMoreRecent(pendingSaveID, LastSaveID))
            {
                CampaignUI.CrewManagement.SetHireables(map.CurrentLocation, availableHires);
                if (hiredCharacters.Any()) { CampaignUI.CrewManagement.ValidateHires(hiredCharacters); }
                CampaignUI.CrewManagement.SetPendingHires(pendingHires, map.CurrentLocation);
                if (renameCrewMember || fireCharacter) { CampaignUI.CrewManagement.UpdateCrew(); }
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

        public override bool TryPurchase(Client client, int price)
        {
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
                return true ;
            }

            return false;
        }

        public override void Save(XElement element)
        {
            //do nothing, the clients get the save files from the server
        }

        public void LoadState(string filePath)
        {
            DebugConsole.Log($"Loading save file for an existing game session ({filePath})");
            SaveUtil.DecompressToDirectory(filePath, SaveUtil.TempPath, null);

            string gamesessionDocPath = Path.Combine(SaveUtil.TempPath, "gamesession.xml");
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
