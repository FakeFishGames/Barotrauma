using Barotrauma.Extensions;
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
                    //don't pause if the map is opened during a conversation, the conversation needs the event to keep running (see ConversationAction.IsBlockedByMenu)
                    ShowCampaignUI && CampaignUI.SelectedTab == InteractionType.Map && !ConversationAction.IsDialogOpen ||
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

            foreach (var hook in PluginData.GetHooks<PluginCampaignLoadDelegate>())
            {
                hook(this, Option.None, Settings);
            }
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
                        CampaignMetadata.Load(subElement);
                        break;
                }
            }

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
                        CrewWideOrdersElement = subElement.GetChildElement("activeorders");
                        break;
                    case "map":
                        map = Map.Load(this, subElement);
                        break;
                }
            }

            LoadSaveSharedSingleAndMultiplayer(element);

            UpgradeManager ??= new UpgradeManager(this);

            InitUI();

            if (map == null)
            {
                throw new System.Exception("Failed to load the campaign save file (saved with an older, incompatible version of Barotrauma).");
            }

            savedOnStart = true;

            foreach (var hook in PluginData.GetHooks<PluginCampaignLoadDelegate>())
            {
                hook(this, Option.Some(element), Settings);
            }
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

            endRoundButton = CreateEndRoundButton();
            endRoundButton.OnClicked = (btn, userdata) =>
            {
                TryEndRoundWithFuelCheck(
                    onConfirm: () => TryEndRound(),
                    onReturnToMapScreen: () => { ShowCampaignUI = true; CampaignUI.SelectTab(InteractionType.Map); });
                return true;
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
                SaveUtil.SaveGame(GameMain.GameSession.DataPath, isSavingOnLoading: true);
                savedOnStart = true;
            }

            CrewDead = false;
            endTimer = 5.0f;
            CrewManager.InitSinglePlayerRound();
            LoadCreatures();
            LoadCrewWideOrders();

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
            GameMain.GameSession.StartRound(level, mirrorLevel: mirror);
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
                var subToFocusTo = GameMain.GameSession.Level.StartOutpost ?? Submarine.MainSub;
                var borders = subToFocusTo.GetDockedBorders();
                borders.Location += subToFocusTo.WorldPosition.ToPoint();
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

        protected override IEnumerable<CoroutineStatus> DoLevelTransition(TransitionType transitionType, LevelData newLevel, Submarine leavingSub, bool mirror)
        {
            NextLevel = newLevel;
            bool success = CrewManager.GetCharacters().Any(c => !c.IsDead);
            SoundPlayer.OverrideMusicType = (success ? "endround" : "crewdead").ToIdentifier();
            SoundPlayer.OverrideMusicDuration = 18.0f;
            GUI.SetSavingIndicatorState(success);
            CrewDead = false;

            if (success)
            {
                // Event history must be registered before ending the round or it will be cleared
                GameMain.GameSession.EventManager.StoreEventDataAtRoundEnd();
            }

            //store the location and missions of the round for progressing the world (EndRound clears the missions and the transition may change the location)
            Location roundLocation = Map.CurrentLocation;
            List<Mission> roundMissions = GameMain.GameSession.Missions.ToList();

            GameMain.GameSession.EndRound("", transitionType);
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
                    Map.Visit(Map.CurrentLocation);
                    //deselect destination on map
                    map.SelectLocation(-1);
                    break;
                case TransitionType.ProgressToNextLocation:
                    Map.MoveToNextLocation();
                    TotalPassedLevels++;
                    break;
                case TransitionType.ProgressToNextEmptyLocation:
                    Map.Visit(Map.CurrentLocation);
                    TotalPassedLevels++;
                    break;
                case TransitionType.End:
                    EndCampaign();
                    IsFirstRound = true;
                    break;
                case TransitionType.TeleportToLocation:
                    Map.SelectLocation(Map.LocationToTeleportTo);
                    Map.MoveToNextLocation(teleportDirectlyToSelectedLocation: true);
                    Map.LocationToTeleportTo = null;
                    break;
            }

            Map.ProgressWorld(this, transitionType, GameMain.GameSession.RoundDuration, roundLocation, roundMissions);

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
                    SaveUtil.SaveGame(GameMain.GameSession.DataPath);
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
            SaveUtil.SaveGame(GameMain.GameSession.DataPath);
            GameMain.CampaignEndScreen.Select();
            GUI.DisableHUD = false;
            GameMain.CampaignEndScreen.OnFinished = () =>
            {
                showCampaignResetText = true;
                LoadInitialLevel();
                IsFirstRound = true;
            };
        }

        protected override void UpdateGameModeSpecific(float deltaTime)
        {
            if (CoroutineManager.IsCoroutineRunning("LevelTransition") || CoroutineManager.IsCoroutineRunning("SubmarineTransition") || gameOver) { return; }

            base.UpdateGameModeSpecific(deltaTime);

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

            if (ShowCampaignUI || ForceMapUI)
            {
                Character.DisableControls = true;
            }

            if (!GUI.DisableHUD && !GUI.DisableUpperHUD)
            {
                endRoundButton.UpdateManually(deltaTime);
                if (CoroutineManager.IsCoroutineRunning("LevelTransition") || ForceMapUI) { return; }
            }

            var transitionType = GetAvailableTransition(out _, out Submarine leavingSub);
            if (transitionType == TransitionType.TeleportToLocation)
            {
                LoadNewLevel();
            }
            else if (Level.Loaded.Type == LevelData.LevelType.Outpost)
            {
                if (Level.Loaded.IsEndBiome)
                {
                    if (transitionType == TransitionType.ProgressToNextLocation)
                    {
                        LoadNewLevel();
                    }
                }
            }
            else
            {
                if (Level.Loaded.IsEndBiome && transitionType == TransitionType.ProgressToNextLocation)
                {
                    LoadNewLevel();
                }
                else if (ShouldLoadNextLocation(transitionType, leavingSub))
                {
                    LoadNewLevel();
                }
                else if (ShouldLoadPreviousLocation(transitionType, leavingSub))
                {
                    LoadNewLevel();
                }
                else if (transitionType == TransitionType.None && CampaignUI.SelectedTab == InteractionType.Map)
                {
                    ShowCampaignUI = false;
                }
                HintManager.OnAvailableTransition(transitionType);
            }

            if (!CrewDead)
            {
                if (CrewManager.GetCharacters().None(c => !c.IsDead && !CrewManager.IsFired(c))) 
                { 
                    CrewDead = true; 
                }                
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

        public override void Save(XElement element, bool isSavingOnLoading)
        {
            XElement modeElement = new XElement("SinglePlayerCampaign",
                new XAttribute("purchasedlostshuttles", PurchasedLostShuttles),
                new XAttribute("purchasedhullrepairs", PurchasedHullRepairs),
                new XAttribute("purchaseditemrepairs", PurchasedItemRepairs),
                new XAttribute("cheatsenabled", CheatsEnabled));
            modeElement.Add(Settings.Save());
            modeElement.Add(SaveStats());

            if (GameMain.GameSession?.EventManager != null)
            {
                modeElement.Add(GameMain.GameSession.EventManager.Save());
                modeElement.Add(GameMain.GameSession.EventManager.EventLog.Save());
            }

            foreach ((CharacterTeamType team, Identifier unlockedRecipe) in GameMain.GameSession.UnlockedRecipes)
            {
                modeElement.Add(
                    new XElement("unlockedrecipe",
                    new XAttribute("identifier", unlockedRecipe),
                    new XAttribute("team", team)));
            }

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
                    c.SaveInventory(removeStolenItemsOnOutpost: true);
                    c.Inventory?.DeleteAllItems();
                }
                c.Info.SaveOrderData();
            }

            SaveCreatures(modeElement);
            CrewManager.Save(modeElement);

            CampaignMetadata.Save(modeElement);
            Map.Save(modeElement);
            CargoManager?.SavePurchasedItems(modeElement);
            UpgradeManager?.Save(modeElement);
            modeElement.Add(Bank.Save());
            element.Add(modeElement);

            foreach (var hook in PluginData.GetHooks<PluginCampaignSaveDelegate>())
            {
                hook(this, modeElement);
            }
        }
    }
}
