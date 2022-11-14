using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class SinglePlayerCampaign : CampaignMode
    {
        public const int MinimumInitialMoney = 0;

        public override bool Paused
        {
            get 
            { 
                return 
                    ForceMapUI || CoroutineManager.IsCoroutineRunning("LevelTransition") || 
                    ShowCampaignUI && CampaignUI.SelectedTab == InteractionType.Map || 
                    (SlideshowPlayer != null && !SlideshowPlayer.LastTextShown); 
            }
        }

        public override void UpdateWhilePaused(float deltaTime)
        {
            if (CoroutineManager.IsCoroutineRunning("LevelTransition") || CoroutineManager.IsCoroutineRunning("SubmarineTransition") || gameOver) { return; }

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

            SlideshowPlayer?.UpdateManually(deltaTime);

            CrewManager.ChatBox?.Update(deltaTime);
            CrewManager.UpdateReports();
        }

        private float endTimer;
        
        private bool savedOnStart;
        
        private bool gameOver;

        private Character lastControlledCharacter;

        private bool showCampaignResetText;

        public override bool PurchasedHullRepairs
        {
            get { return PurchasedHullRepairsInLatestSave; }
            set
            {
                PurchasedHullRepairsInLatestSave = value;
            }
        }
        public override bool PurchasedLostShuttles
        {
            get { return PurchasedLostShuttlesInLatestSave; }
            set
            {
                PurchasedLostShuttlesInLatestSave = value;
            }
        }
        public override bool PurchasedItemRepairs
        {
            get { return PurchasedItemRepairsInLatestSave; }
            set
            {
                PurchasedItemRepairsInLatestSave = value;
            }
        }

        #region Constructors/initialization

        /// <summary>
        /// Instantiates a new single player campaign
        /// </summary>
        private SinglePlayerCampaign(string mapSeed, CampaignSettings settings) : base(GameModePreset.SinglePlayerCampaign, settings)
        {
            CampaignMetadata = new CampaignMetadata();
            UpgradeManager = new UpgradeManager(this);
            Settings = settings;
            InitFactions();
            map = new Map(this, mapSeed);
            foreach (JobPrefab jobPrefab in JobPrefab.Prefabs)
            {
                for (int i = 0; i < jobPrefab.InitialCount; i++)
                {
                    var variant = Rand.Range(0, jobPrefab.Variants);
                    CrewManager.AddCharacterInfo(new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: jobPrefab, variant: variant));
                }
            }
            InitUI();
        }

        /// <summary>
        /// Loads a previously saved single player campaign from XML
        /// </summary>
        private SinglePlayerCampaign(XElement element) : base(GameModePreset.SinglePlayerCampaign, CampaignSettings.Empty)
        {
            IsFirstRound = false;

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "metadata":
                        CampaignMetadata = new CampaignMetadata(subElement);
                        break;
                }
            }

            CampaignMetadata ??= new CampaignMetadata();
            InitFactions();

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case CampaignSettings.LowerCaseSaveElementName:
                        Settings = new CampaignSettings(subElement);
                        break;
                    case "crew":
                        GameMain.GameSession.CrewManager = new CrewManager(subElement, true);
                        ActiveOrdersElement = subElement.GetChildElement("activeorders");
                        break;
                    case "map":
                        map = Map.Load(this, subElement);
                        break;
                    case "cargo":
                        CargoManager.LoadPurchasedItems(subElement);
                        break;
                    case "pendingupgrades": //backwards compatibility
                    case "upgrademanager":
                        UpgradeManager = new UpgradeManager(this, subElement, isSingleplayer: true);
                        break;
                    case "pets":
                        petsElement = subElement;
                        break;
                    case Wallet.LowerCaseSaveElementName:
                        Bank = new Wallet(Option<Character>.None(), subElement);
                        break;
                    case "stats":
                        LoadStats(subElement);
                        break;
                }
            }

            UpgradeManager ??= new UpgradeManager(this);

            InitUI();

            //backwards compatibility for saves made prior to the addition of personal wallets
            int oldMoney = element.GetAttributeInt("money", 0);
            if (oldMoney > 0)
            {
                Bank = new Wallet(Option<Character>.None())
                {
                    Balance = oldMoney
                };
            }

            PurchasedLostShuttlesInLatestSave = element.GetAttributeBool("purchasedlostshuttles", false);
            PurchasedHullRepairsInLatestSave = element.GetAttributeBool("purchasedhullrepairs", false);
            PurchasedItemRepairsInLatestSave = element.GetAttributeBool("purchaseditemrepairs", false);
            CheatsEnabled = element.GetAttributeBool("cheatsenabled", false);
            if (CheatsEnabled)
            {
                DebugConsole.CheatsEnabled = true;
#if USE_STEAM
                if (!SteamAchievementManager.CheatsEnabled)
                {
                    SteamAchievementManager.CheatsEnabled = true;
                    new GUIMessageBox("Cheats enabled", "Cheat commands have been enabled on the campaign. You will not receive Steam Achievements until you restart the game.");
                }
#endif
            }

            if (map == null)
            {
                throw new System.Exception("Failed to load the campaign save file (saved with an older, incompatible version of Barotrauma).");
            }

            savedOnStart = true;
        }

        /// <summary>
        /// Start a completely new single player campaign
        /// </summary>
        public static SinglePlayerCampaign StartNew(string mapSeed, CampaignSettings startingSettings) => new SinglePlayerCampaign(mapSeed, startingSettings);

        /// <summary>
        /// Load a previously saved single player campaign from xml
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        public static SinglePlayerCampaign Load(XElement element) => new SinglePlayerCampaign(element);

        private void InitUI()
        {
            CreateEndRoundButton();

            campaignUIContainer = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: "InnerGlow", color: Color.Black);
            CampaignUI = new CampaignUI(this, campaignUIContainer)
            {
                StartRound = () => { TryEndRound(); }
            };
        }

        private void CreateEndRoundButton()
        {
            int buttonHeight = (int)(GUI.Scale * 40);
            int buttonWidth = GUI.IntScale(450);
            endRoundButton = new GUIButton(HUDLayoutSettings.ToRectTransform(new Rectangle((GameMain.GraphicsWidth / 2) - (buttonWidth / 2), HUDLayoutSettings.ButtonAreaTop.Center.Y - (buttonHeight / 2), buttonWidth, buttonHeight), GUI.Canvas),
                TextManager.Get("EndRound"), textAlignment: Alignment.Center, style: "EndRoundButton")
            {
                Pulse = true,
                TextBlock =
                {
                    Shadow = true,
                    AutoScaleHorizontal = true
                },
                OnClicked = (btn, userdata) =>
                {
                    TryEndRoundWithFuelCheck(
                        onConfirm: () => TryEndRound(),
                        onReturnToMapScreen: () => { ShowCampaignUI = true; CampaignUI.SelectTab(InteractionType.Map); });
                    return true;
                }
            };
        }

        public override void HUDScaleChanged()
        {
            CreateEndRoundButton();
        }

        #endregion

        public override void Start()
        {
            base.Start();
            CargoManager.CreatePurchasedItems();
            UpgradeManager.ApplyUpgrades();
            UpgradeManager.SanityCheckUpgrades();

            if (!savedOnStart)
            {
                GUI.SetSavingIndicatorState(true);
                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                savedOnStart = true;
            }

            crewDead = false;
            endTimer = 5.0f;
            CrewManager.InitSinglePlayerRound();
            LoadPets();
            LoadActiveOrders();

            CargoManager.InitPurchasedIDCards();

            GUI.DisableSavingIndicatorDelayed();
        }

        protected override void LoadInitialLevel()
        {
            //no level loaded yet -> show a loading screen and load the current location (outpost)
            GameMain.Instance.ShowLoading(
                DoLoadInitialLevel(map.SelectedConnection?.LevelData ?? map.CurrentLocation.LevelData, 
                mirror: map.CurrentLocation != map.SelectedConnection?.Locations[0]));
        }

        private IEnumerable<CoroutineStatus> DoLoadInitialLevel(LevelData level, bool mirror)
        {
            GameMain.GameSession.StartRound(level, mirrorLevel: mirror, startOutpost: GetPredefinedStartOutpost());
            GameMain.GameScreen.Select();

            CoroutineManager.StartCoroutine(DoInitialCameraTransition(), "SinglePlayerCampaign.DoInitialCameraTransition");

            yield return CoroutineStatus.Success;
        }

        private IEnumerable<CoroutineStatus> DoInitialCameraTransition()
        {
            while (GameMain.Instance.LoadingScreenOpen)
            {
                yield return CoroutineStatus.Running;
            }
            Character prevControlled = Character.Controlled;
            if (prevControlled?.AIController != null)
            {
                prevControlled.AIController.Enabled = false;
            }
            Character.Controlled = null;
            prevControlled?.ClearInputs();

            GUI.DisableHUD = true;
            while (GameMain.Instance.LoadingScreenOpen)
            {
                yield return CoroutineStatus.Running;
            }

            if (IsFirstRound || showCampaignResetText)
            {
                if (SlideshowPrefab.Prefabs.TryGet("campaignstart".ToIdentifier(), out var slideshow))
                {
                    SlideshowPlayer = new SlideshowPlayer(GUICanvas.Instance, slideshow);
                }
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
                while (transition.Running)
                {
                    yield return CoroutineStatus.Running;
                }
                showCampaignResetText = false;
            }
            else
            {
                ISpatialEntity transitionTarget;
                transitionTarget = (ISpatialEntity)prevControlled ?? Submarine.MainSub;

                var transition = new CameraTransition(transitionTarget, GameMain.GameScreen.Cam,
                    null, null,
                    fadeOut: false,
                    losFadeIn: prevControlled != null,
                    panDuration: 5,
                    startZoom: 0.5f, endZoom: 1.0f)
                {
                    AllowInterrupt = true,
                    RemoveControlFromCharacter = false                    
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

            if (prevControlled != null)
            {
                Character.Controlled = prevControlled;
            }
            GUI.DisableHUD = false;
            yield return CoroutineStatus.Success;
        }

        protected override IEnumerable<CoroutineStatus> DoLevelTransition(TransitionType transitionType, LevelData newLevel, Submarine leavingSub, bool mirror, List<TraitorMissionResult> traitorResults = null)
        {
            NextLevel = newLevel;
            bool success = CrewManager.GetCharacters().Any(c => !c.IsDead);
            SoundPlayer.OverrideMusicType = (success ? "endround" : "crewdead").ToIdentifier();
            SoundPlayer.OverrideMusicDuration = 18.0f;
            GUI.SetSavingIndicatorState(success);
            crewDead = false;

            if (success)
            {
                // Event history must be registered before ending the round or it will be cleared
                GameMain.GameSession.EventManager.RegisterEventHistory();
            }
            GameMain.GameSession.EndRound("", traitorResults, transitionType);
            var continueButton = GameMain.GameSession.RoundSummary?.ContinueButton;
            RoundSummary roundSummary = null;
            if (GUIMessageBox.VisibleBox?.UserData is RoundSummary)
            {
                roundSummary = GUIMessageBox.VisibleBox?.UserData as RoundSummary;
            }
            if (continueButton != null)
            {
                continueButton.Visible = false;
            }

            lastControlledCharacter = Character.Controlled;
            Character.Controlled = null;

            switch (transitionType)
            {
                case TransitionType.None:
                    throw new InvalidOperationException("Level transition failed (no transitions available).");
                case TransitionType.ReturnToPreviousLocation:
                    //deselect destination on map
                    map.SelectLocation(-1);
                    break;
                case TransitionType.ProgressToNextLocation:
                    Map.MoveToNextLocation();
                    TotalPassedLevels++;
                    break;
                case TransitionType.ProgressToNextEmptyLocation:
                    TotalPassedLevels++;
                    break;
                case TransitionType.End:
                    EndCampaign();
                    IsFirstRound = true;
                    break;
            }

            Map.ProgressWorld(this, transitionType, (float)(Timing.TotalTime - GameMain.GameSession.RoundStartTime));

            GUI.ClearMessages();

            //--------------------------------------
            if (transitionType != TransitionType.End)
            {
                var endTransition = new CameraTransition(Submarine.MainSub, GameMain.GameScreen.Cam, null,
                transitionType == TransitionType.LeaveLocation ? Alignment.BottomCenter : Alignment.Center,
                fadeOut: false,
                panDuration: EndTransitionDuration);

                Location portraitLocation = Map.SelectedLocation ?? Map.CurrentLocation;
                overlaySprite = portraitLocation.Type.GetPortrait(portraitLocation.PortraitId);
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

                if (success)
                {
                    GameMain.GameSession.SubmarineInfo = new SubmarineInfo(GameMain.GameSession.Submarine);
                    SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                }
                else
                {
                    PendingSubmarineSwitch = null;
                    EnableRoundSummaryGameOverState();
                }

                CrewManager?.ClearCurrentOrders();

                SelectSummaryScreen(roundSummary, newLevel, mirror, () =>
                {
                    GameMain.GameScreen.Select();
                    if (continueButton != null)
                    {
                        continueButton.Visible = true;
                    }

                    GUI.DisableHUD = false;
                    GUI.ClearCursorWait();
                    overlayColor = Color.Transparent;
                });
            }

            GUI.SetSavingIndicatorState(false);
            yield return CoroutineStatus.Success;
        }

        protected override void EndCampaignProjSpecific()
        {
            GameMain.GameSession.SubmarineInfo = new SubmarineInfo(GameMain.GameSession.Submarine);
            SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            GameMain.CampaignEndScreen.Select();
            GUI.DisableHUD = false;
            GameMain.CampaignEndScreen.OnFinished = () =>
            {
                showCampaignResetText = true;
                LoadInitialLevel();
                IsFirstRound = true;
            };
        }

        public override void Update(float deltaTime)
        {
            if (CoroutineManager.IsCoroutineRunning("LevelTransition") || CoroutineManager.IsCoroutineRunning("SubmarineTransition") || gameOver) { return; }

            base.Update(deltaTime);

            SlideshowPlayer?.UpdateManually(deltaTime);

            Map?.Radiation?.UpdateRadiation(deltaTime);

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

#if DEBUG
            if (GUI.KeyboardDispatcher.Subscriber == null && PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.M))
            {
                if (GUIMessageBox.MessageBoxes.Any()) { GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.MessageBoxes.Last()); }

                GUIFrame summaryFrame = GameMain.GameSession.RoundSummary.CreateSummaryFrame(GameMain.GameSession, "", null);
                GUIMessageBox.MessageBoxes.Add(summaryFrame);
                GameMain.GameSession.RoundSummary.ContinueButton.OnClicked = (_, __) => { GUIMessageBox.MessageBoxes.Remove(summaryFrame); return true; };
            }
#endif

            if (ShowCampaignUI || ForceMapUI)
            {
                Character.DisableControls = true;
            }

            if (!GUI.DisableHUD && !GUI.DisableUpperHUD)
            {
                endRoundButton.UpdateManually(deltaTime);
                if (CoroutineManager.IsCoroutineRunning("LevelTransition") || ForceMapUI) { return; }
            }

            if (Level.Loaded.Type == LevelData.LevelType.Outpost)
            {
                KeepCharactersCloseToOutpost(deltaTime);
                if (wasDocked)
                {
                    var connectedSubs = Submarine.MainSub.GetConnectedSubs();
                    bool isDocked = Level.Loaded.StartOutpost != null && connectedSubs.Contains(Level.Loaded.StartOutpost);
                    if (!isDocked)
                    {
                        //undocked from outpost, need to choose a destination
                        ForceMapUI = true;
                        CampaignUI.SelectTab(InteractionType.Map);
                    }
                }
                else if (Level.Loaded.IsEndBiome)
                {
                    var transitionType = GetAvailableTransition(out _, out Submarine leavingSub);
                    if (transitionType == TransitionType.ProgressToNextLocation)
                    {
                        LoadNewLevel();
                    }
                }
                else
                {
                    //wasn't initially docked (sub doesn't have a docking port?)
                    // -> choose a destination when the sub is far enough from the start outpost
                    if (!Submarine.MainSub.AtStartExit && !Level.Loaded.StartOutpost.ExitPoints.Any())
                    {
                        ForceMapUI = true;
                        CampaignUI.SelectTab(InteractionType.Map);
                    }
                }
            }
            else
            {
                var transitionType = GetAvailableTransition(out _, out Submarine leavingSub);
                if (Level.Loaded.IsEndBiome && transitionType == TransitionType.ProgressToNextLocation)
                {
                    LoadNewLevel();
                }
                else if (transitionType == TransitionType.ProgressToNextLocation && 
                    Level.Loaded.EndOutpost != null && Level.Loaded.EndOutpost.DockedTo.Contains(leavingSub))
                {
                    LoadNewLevel();
                }
                else if (transitionType == TransitionType.ReturnToPreviousLocation &&
                    Level.Loaded.StartOutpost != null && Level.Loaded.StartOutpost.DockedTo.Contains(leavingSub))
                {
                    LoadNewLevel();
                }
                else if (transitionType == TransitionType.None && CampaignUI.SelectedTab == InteractionType.Map)
                {
                    ShowCampaignUI = false;
                }
                HintManager.OnAvailableTransition(transitionType);
            }

            if (!crewDead)
            {
                if (!CrewManager.GetCharacters().Any(c => !c.IsDead)) { crewDead = true; }                
            }
            else
            {
                endTimer -= deltaTime;
                if (endTimer <= 0.0f) { GameOver(); }
            }  
        }
        
        private bool TryEndRound()
        {
            var transitionType = GetAvailableTransition(out LevelData nextLevel, out Submarine leavingSub);
            if (leavingSub == null || transitionType == TransitionType.None) { return false; }
            
            if (nextLevel == null)
            {
                //no level selected -> force the player to select one
                ForceMapUI = true;
                CampaignUI.SelectTab(InteractionType.Map);
                map.SelectLocation(-1);
                return false;
            }
            else if (transitionType == TransitionType.ProgressToNextEmptyLocation)
            {
                Map.SetLocation(Map.Locations.IndexOf(Level.Loaded.EndLocation ?? Map.CurrentLocation));
            }

            var subsToLeaveBehind = GetSubsToLeaveBehind(leavingSub);
            if (subsToLeaveBehind.Any())
            {
                LocalizedString msg = TextManager.Get(subsToLeaveBehind.Count == 1 ? "LeaveSubBehind" : "LeaveSubsBehind");

                var msgBox = new GUIMessageBox(TextManager.Get("Warning"), msg, new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });
                msgBox.Buttons[0].OnClicked += (btn, userdata) => { LoadNewLevel(); return true; } ;
                msgBox.Buttons[0].OnClicked += msgBox.Close;
                msgBox.Buttons[0].UserData = Submarine.Loaded.FindAll(s => !subsToLeaveBehind.Contains(s));
                msgBox.Buttons[1].OnClicked += msgBox.Close;
            }
            else
            {
                LoadNewLevel();
            }

            return true;
        }

        private void GameOver()
        {
            gameOver = true;
            GameMain.GameSession.EndRound("", transitionType: TransitionType.None);
            EnableRoundSummaryGameOverState();
        }

        private void EnableRoundSummaryGameOverState()
        {
            var roundSummary = GameMain.GameSession.RoundSummary;
            if (roundSummary != null)
            {
                roundSummary.ContinueButton.Visible = false;
                roundSummary.ContinueButton.IgnoreLayoutGroups = true;

                new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), roundSummary.ButtonArea.RectTransform),
                    TextManager.Get("QuitButton"))
                {
                    OnClicked = (GUIButton button, object obj) =>
                    {
                        GameMain.MainMenuScreen.Select();
                        GUIMessageBox.MessageBoxes.Remove(roundSummary.Frame);
                        return true;
                    }
                };
                new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), roundSummary.ButtonArea.RectTransform),
                    TextManager.Get("LoadGameButton"))
                {
                    OnClicked = (GUIButton button, object obj) =>
                    {
                        GameMain.GameSession.LoadPreviousSave();
                        GUIMessageBox.MessageBoxes.Remove(roundSummary.Frame);
                        return true;
                    }
                };
            }
        }

        public override void Save(XElement element)
        {
            XElement modeElement = new XElement("SinglePlayerCampaign",
                new XAttribute("purchasedlostshuttles", PurchasedLostShuttles),
                new XAttribute("purchasedhullrepairs", PurchasedHullRepairs),
                new XAttribute("purchaseditemrepairs", PurchasedItemRepairs),
                new XAttribute("cheatsenabled", CheatsEnabled));
            modeElement.Add(Settings.Save());
            modeElement.Add(SaveStats());

            //save and remove all items that are in someone's inventory so they don't get included in the sub file as well
            foreach (Character c in Character.CharacterList)
            {
                if (c.Info == null) { continue; }
                if (c.IsDead) { CrewManager.RemoveCharacterInfo(c.Info); }
                c.Info.LastControlled = c == lastControlledCharacter;
                c.Info.HealthData = new XElement("health");
                c.CharacterHealth.Save(c.Info.HealthData);
                if (c.Inventory != null)
                {
                    c.Info.InventoryData = new XElement("inventory");
                    c.SaveInventory();
                    c.Inventory?.DeleteAllItems();
                }
                c.Info.SaveOrderData();
            }

            SavePets(modeElement);
            var crewManagerElement = CrewManager.Save(modeElement);
            SaveActiveOrders(crewManagerElement);

            CampaignMetadata.Save(modeElement);
            Map.Save(modeElement);
            CargoManager?.SavePurchasedItems(modeElement);
            UpgradeManager?.Save(modeElement);
            modeElement.Add(Bank.Save());
            element.Add(modeElement);
        }
    }
}
