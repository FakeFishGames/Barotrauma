using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma
{
    class SinglePlayerCampaign : CampaignMode
    {
        public const int MinimumInitialMoney = 0;

        public override bool Paused
        {
            get { return ForceMapUI || CoroutineManager.IsCoroutineRunning("LevelTransition") || ShowCampaignUI && CampaignUI.SelectedTab == InteractionType.Map; }
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

            if (CrewManager.ChatBox != null)
            {
                CrewManager.ChatBox.Update(deltaTime);
            }

            CrewManager.UpdateReports();
        }

        private float endTimer;
        
        private bool savedOnStart;
        
        private bool gameOver;

        private Character lastControlledCharacter;

        private bool showCampaignResetText;

        #region Constructors/initialization

        /// <summary>
        /// Instantiates a new single player campaign
        /// </summary>
        private SinglePlayerCampaign(string mapSeed, CampaignSettings settings) : base(GameModePreset.SinglePlayerCampaign)
        {
            CampaignMetadata = new CampaignMetadata(this);
            UpgradeManager = new UpgradeManager(this);
            map = new Map(this, mapSeed, settings);
            Settings = settings;
            foreach (JobPrefab jobPrefab in JobPrefab.Prefabs)
            {
                for (int i = 0; i < jobPrefab.InitialCount; i++)
                {
                    var variant = Rand.Range(0, jobPrefab.Variants);
                    CrewManager.AddCharacterInfo(new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: jobPrefab, variant: variant));
                }
            }
            InitCampaignData();
            InitUI();
        }

        /// <summary>
        /// Loads a previously saved single player campaign from XML
        /// </summary>
        private SinglePlayerCampaign(XElement element) : base(GameModePreset.SinglePlayerCampaign)
        {
            IsFirstRound = false;

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "campaignsettings":
                        Settings = new CampaignSettings(subElement);
                        break;
                    case "crew":
                        GameMain.GameSession.CrewManager = new CrewManager(subElement, true);
                        break;
                    case "map":
                        map = Map.Load(this, subElement, Settings);
                        break;
                    case "metadata":
                        CampaignMetadata = new CampaignMetadata(this, subElement);
                        break;
                    case "cargo":
                        CargoManager.LoadPurchasedItems(subElement);
                        break;
                    case "pendingupgrades":
                        UpgradeManager = new UpgradeManager(this, subElement, isSingleplayer: true);
                        break;
                    case "pets":
                        petsElement = subElement;
                        break;
                }
            }

            CampaignMetadata ??= new CampaignMetadata(this);
            UpgradeManager ??= new UpgradeManager(this);

            InitCampaignData();

            InitUI();

            Money = element.GetAttributeInt("money", 0);
            PurchasedLostShuttles = element.GetAttributeBool("purchasedlostshuttles", false);
            PurchasedHullRepairs = element.GetAttributeBool("purchasedhullrepairs", false);
            PurchasedItemRepairs = element.GetAttributeBool("purchaseditemrepairs", false);
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
        public static SinglePlayerCampaign StartNew(string mapSeed, SubmarineInfo selectedSub, CampaignSettings settings)
        {
            var campaign = new SinglePlayerCampaign(mapSeed, settings);
            return campaign;
        }

        /// <summary>
        /// Load a previously saved single player campaign from xml
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        public static SinglePlayerCampaign Load(XElement element)
        {
            return new SinglePlayerCampaign(element);
        }

        private void InitUI()
        {
            int buttonHeight = (int)(GUI.Scale * 40);
            int buttonWidth = GUI.IntScale(450);

            endRoundButton = new GUIButton(HUDLayoutSettings.ToRectTransform(new Rectangle((GameMain.GraphicsWidth / 2) - (buttonWidth / 2), HUDLayoutSettings.ButtonAreaTop.Center.Y - (buttonHeight / 2), buttonWidth, buttonHeight), GUICanvas.Instance),
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
                    var availableTransition = GetAvailableTransition(out _, out _);
                    if (Character.Controlled != null &&
                        availableTransition == TransitionType.ReturnToPreviousLocation &&
                        Character.Controlled?.Submarine == Level.Loaded?.StartOutpost)
                    {
                        TryEndRound();
                    }
                    else if (Character.Controlled != null &&
                        availableTransition == TransitionType.ProgressToNextLocation &&
                        Character.Controlled?.Submarine == Level.Loaded?.EndOutpost)
                    {
                        TryEndRound();
                    }
                    else
                    {
                        ShowCampaignUI = true;
                        CampaignUI.SelectTab(InteractionType.Map);
                    }
                    return true;
                }
            };

            campaignUIContainer = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: "InnerGlow", color: Color.Black);
            CampaignUI = new CampaignUI(this, campaignUIContainer)
            {
                StartRound = () => { TryEndRound(); }
            };
        }

        #endregion

        public override void Start()
        {
            base.Start();
            CargoManager.CreatePurchasedItems();
            UpgradeManager.ApplyUpgrades();
            UpgradeManager.SanityCheckUpgrades(Submarine.MainSub);

            if (!savedOnStart)
            {
                GUI.SetSavingIndicatorState(true);
                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                savedOnStart = true;
            }

            crewDead = false;
            endTimer = 5.0f;
            CrewManager.InitSinglePlayerRound();
            if (petsElement != null)
            {
                PetBehavior.LoadPets(petsElement);
            }

            GUI.DisableSavingIndicatorDelayed();
        }

        protected override void LoadInitialLevel()
        {
            //no level loaded yet -> show a loading screen and load the current location (outpost)
            GameMain.Instance.ShowLoading(
                DoLoadInitialLevel(map.SelectedConnection?.LevelData ?? map.CurrentLocation.LevelData, 
                mirror: map.CurrentLocation != map.SelectedConnection?.Locations[0]));
        }

        private IEnumerable<object> DoLoadInitialLevel(LevelData level, bool mirror)
        {
            GameMain.GameSession.StartRound(level,
                mirrorLevel: mirror);
            GameMain.GameScreen.Select();

            CoroutineManager.StartCoroutine(DoInitialCameraTransition(), "SinglePlayerCampaign.DoInitialCameraTransition");

            yield return CoroutineStatus.Success;
        }

        private IEnumerable<object> DoInitialCameraTransition()
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
            if (prevControlled != null)
            {
                prevControlled.ClearInputs();
            }

            GUI.DisableHUD = true;
            while (GameMain.Instance.LoadingScreenOpen)
            {
                yield return CoroutineStatus.Running;
            }

            if (IsFirstRound || showCampaignResetText)
            {
                overlayColor = Color.LightGray;
                overlaySprite = Map.CurrentLocation.Type.GetPortrait(Map.CurrentLocation.PortraitId);
                overlayTextColor = Color.Transparent;
                overlayText = TextManager.GetWithVariables(showCampaignResetText ? "campaignend4" : "campaignstart",
                        new string[] { "xxxx", "yyyy" },
                        new string[] { Map.CurrentLocation.Name, TextManager.Get("submarineclass." + Submarine.MainSub.Info.SubmarineClass) });
                string pressAnyKeyText = TextManager.Get("pressanykey");
                float fadeInDuration = 2.0f;
                float textDuration = 10.0f;
                float timer = 0.0f;
                while (true)
                {
                    if (timer > fadeInDuration)
                    {
                        overlayTextBottom = pressAnyKeyText;
                        if (PlayerInput.GetKeyboardState.GetPressedKeys().Length > 0 || PlayerInput.PrimaryMouseButtonClicked())
                        {
                            break;
                        }
                    }
                    if (GameMain.GameSession == null)
                    {
                        GUI.DisableHUD = false;
                        yield return CoroutineStatus.Success;
                    }
                    overlayTextColor = Color.Lerp(Color.Transparent, Color.White, (timer - 1.0f) / fadeInDuration);
                    timer = Math.Min(timer + CoroutineManager.DeltaTime, textDuration);
                    yield return CoroutineStatus.Running;
                }
                var outpost = GameMain.GameSession.Level.StartOutpost;
                var borders = outpost.GetDockedBorders();
                borders.Location += outpost.WorldPosition.ToPoint();
                GameMain.GameScreen.Cam.Position = new Vector2(borders.X + borders.Width / 2, borders.Y - borders.Height / 2);
                float startZoom = 0.8f /
                    ((float)Math.Max(borders.Width, borders.Height) / (float)GameMain.GameScreen.Cam.Resolution.X);
                GameMain.GameScreen.Cam.MinZoom = Math.Min(startZoom, GameMain.GameScreen.Cam.MinZoom);
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
                fadeInDuration = 1.0f;
                timer = 0.0f;
                overlayTextColor = Color.Transparent;
                overlayText = "";
                while (timer < fadeInDuration)
                {
                    overlayColor = Color.Lerp(Color.LightGray, Color.Transparent, timer / fadeInDuration);
                    timer += CoroutineManager.DeltaTime;
                    yield return CoroutineStatus.Running;
                }
                overlayColor = Color.Transparent;
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
                prevControlled.SelectedConstruction = null;
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

        protected override IEnumerable<object> DoLevelTransition(TransitionType transitionType, LevelData newLevel, Submarine leavingSub, bool mirror, List<TraitorMissionResult> traitorResults = null)
        {
            NextLevel = newLevel;
            bool success = CrewManager.GetCharacters().Any(c => !c.IsDead);
            SoundPlayer.OverrideMusicType = success ? "endround" : "crewdead";
            SoundPlayer.OverrideMusicDuration = 18.0f;
            GUI.SetSavingIndicatorState(success);
            crewDead = false;

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
                    break;
            }

            Map.ProgressWorld(transitionType, (float)(Timing.TotalTime - GameMain.GameSession.RoundStartTime));

            var endTransition = new CameraTransition(Submarine.MainSub, GameMain.GameScreen.Cam, null,
                transitionType == TransitionType.LeaveLocation ? Alignment.BottomCenter : Alignment.Center,
                fadeOut: false,
                panDuration: EndTransitionDuration);

            GUI.ClearMessages();

            Location portraitLocation = Map.SelectedLocation ?? Map.CurrentLocation;
            overlaySprite = portraitLocation.Type.GetPortrait(portraitLocation.PortraitId);
            float fadeOutDuration = endTransition.PanDuration;
            float t = 0.0f;
            while (t < fadeOutDuration || endTransition.Running)
            {
                t += CoroutineManager.UnscaledDeltaTime;
                overlayColor = Color.Lerp(Color.Transparent, Color.White, t / fadeOutDuration);
                yield return CoroutineStatus.Running;
            }
            overlayColor = Color.White;
            yield return CoroutineStatus.Running;

            //--------------------------------------

            if (success)
            {
                if (leavingSub != Submarine.MainSub && !leavingSub.DockedTo.Contains(Submarine.MainSub))
                {
                    Submarine.MainSub = leavingSub;
                    GameMain.GameSession.Submarine = leavingSub;
                    var subsToLeaveBehind = GetSubsToLeaveBehind(leavingSub);
                    foreach (Submarine sub in subsToLeaveBehind)
                    {
                        MapEntity.mapEntityList.RemoveAll(e => e.Submarine == sub && e is LinkedSubmarine);
                        LinkedSubmarine.CreateDummy(leavingSub, sub);
                    }
                }

                GameMain.GameSession.SubmarineInfo = new SubmarineInfo(GameMain.GameSession.Submarine);

                if (PendingSubmarineSwitch != null)
                {
                    SubmarineInfo previousSub = GameMain.GameSession.SubmarineInfo;
                    GameMain.GameSession.SubmarineInfo = PendingSubmarineSwitch;
                    PendingSubmarineSwitch = null;

                    for (int i = 0; i < GameMain.GameSession.OwnedSubmarines.Count; i++)
                    {
                        if (GameMain.GameSession.OwnedSubmarines[i].Name == previousSub.Name)
                        {
                            GameMain.GameSession.OwnedSubmarines[i] = previousSub;
                            break;
                        }
                    }
                }

                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            }
            else
            {
                PendingSubmarineSwitch = null;
                EnableRoundSummaryGameOverState();
            }

            //--------------------------------------

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

            GUI.SetSavingIndicatorState(false);
            yield return CoroutineStatus.Success;
        }

        protected override void EndCampaignProjSpecific()
        {
            CoroutineManager.StartCoroutine(DoEndCampaignCameraTransition(), "DoEndCampaignCameraTransition");
            GameMain.CampaignEndScreen.OnFinished = () =>
            {
                showCampaignResetText = true;
                LoadInitialLevel();
                IsFirstRound = true;
            };
        }

        private IEnumerable<object> DoEndCampaignCameraTransition()
        {
            if (Character.Controlled != null)
            {
                Character.Controlled.AIController.Enabled = false;
                Character.Controlled = null;
            }
            GUI.DisableHUD = true;
            ISpatialEntity endObject = Level.Loaded.LevelObjectManager.GetAllObjects().FirstOrDefault(obj => obj.Prefab.SpawnPos == LevelObjectPrefab.SpawnPosType.LevelEnd);
            var transition = new CameraTransition(endObject ?? Submarine.MainSub, GameMain.GameScreen.Cam,
                null, Alignment.Center,
                fadeOut: true,
                panDuration: 10,
                startZoom: null, endZoom: 0.2f);

            while (transition.Running)
            {
                yield return CoroutineStatus.Running;
            }
            GameMain.GameSession.SubmarineInfo = new SubmarineInfo(GameMain.GameSession.Submarine);
            SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            GameMain.CampaignEndScreen.Select();
            GUI.DisableHUD = false;

            yield return CoroutineStatus.Success;
        }

        public override void Update(float deltaTime)
        {
            if (CoroutineManager.IsCoroutineRunning("LevelTransition") || CoroutineManager.IsCoroutineRunning("SubmarineTransition") || gameOver) { return; }

            base.Update(deltaTime);
            
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
            if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.R))
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
                else
                {
                    //wasn't initially docked (sub doesn't have a docking port?)
                    // -> choose a destination when the sub is far enough from the start outpost
                    if (!Submarine.MainSub.AtStartExit)
                    {
                        ForceMapUI = true;
                        CampaignUI.SelectTab(InteractionType.Map);
                    }
                }
            }
            else
            {
                var transitionType = GetAvailableTransition(out _, out Submarine leavingSub);
                if (transitionType == TransitionType.End)
                {
                    EndCampaign();
                }
                if (transitionType == TransitionType.ProgressToNextLocation && 
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
                string msg = TextManager.Get(subsToLeaveBehind.Count == 1 ? "LeaveSubBehind" : "LeaveSubsBehind");

                var msgBox = new GUIMessageBox(TextManager.Get("Warning"), msg, new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
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
                new XAttribute("money", Money),
                new XAttribute("purchasedlostshuttles", PurchasedLostShuttles),
                new XAttribute("purchasedhullrepairs", PurchasedHullRepairs),
                new XAttribute("purchaseditemrepairs", PurchasedItemRepairs),
                new XAttribute("cheatsenabled", CheatsEnabled));
            modeElement.Add(Settings.Save());

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
                    c.SaveInventory(c.Inventory, c.Info.InventoryData);
                    c.Inventory?.DeleteAllItems();
                }
            }

            petsElement = new XElement("pets");
            PetBehavior.SavePets(petsElement);
            modeElement.Add(petsElement);            

            CrewManager.Save(modeElement);
            CampaignMetadata.Save(modeElement);
            Map.Save(modeElement);
            CargoManager?.SavePurchasedItems(modeElement);
            UpgradeManager?.SavePendingUpgrades(modeElement, UpgradeManager?.PendingUpgrades);
            element.Add(modeElement);
        }
    }
}
