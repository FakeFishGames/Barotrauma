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

        public static void StartCampaignSetup(IEnumerable<string> saveFiles)
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
            var buttonContainer = new GUILayoutGroup(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ButtonAreaTop, GUI.Canvas),
                isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                CanBeFocused = false
            };

            int buttonHeight = (int) (GUI.Scale * 40),
                buttonWidth = GUI.IntScale(450),
                buttonCenter = buttonHeight / 2,
                screenMiddle = GameMain.GraphicsWidth / 2;

            endRoundButton = new GUIButton(HUDLayoutSettings.ToRectTransform(new Rectangle(screenMiddle - buttonWidth / 2, HUDLayoutSettings.ButtonAreaTop.Center.Y - buttonCenter, buttonWidth, buttonHeight), GUI.Canvas),
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
                        GameMain.Client.RequestStartRound();
                    }
                    else if (Character.Controlled != null &&
                        availableTransition == TransitionType.ProgressToNextLocation &&
                        Character.Controlled?.Submarine == Level.Loaded?.EndOutpost)
                    {
                        GameMain.Client.RequestStartRound();
                    }
                    else
                    {
                        ShowCampaignUI = true;
                        if (CampaignUI == null) { InitCampaignUI(); }
                        CampaignUI.SelectTab(InteractionType.Map);
                    }
                    return true;
                }
            };

            int readyButtonHeight = buttonHeight;
            int readyButtonWidth = (int) (GUI.Scale * 50);

            ReadyCheckButton = new GUIButton(HUDLayoutSettings.ToRectTransform(new Rectangle(screenMiddle + (buttonWidth / 2) + GUI.IntScale(16), HUDLayoutSettings.ButtonAreaTop.Center.Y - buttonCenter, readyButtonWidth, readyButtonHeight), GUI.Canvas), 
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
            
            buttonContainer.Recalculate();
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

            if (GameMain.Client.LateCampaignJoin)
            {
                GameMain.Client.LateCampaignJoin = false;
                yield return CoroutineStatus.Success;
            }

            Character prevControlled = Character.Controlled;
            if (prevControlled?.AIController != null)
            {
                prevControlled.AIController.Enabled = false;
            }
            GUI.DisableHUD = true;
            if (IsFirstRound)
            {
                Character.Controlled = null;

                prevControlled?.ClearInputs();

                overlayColor = Color.LightGray;
                overlaySprite = Map.CurrentLocation.Type.GetPortrait(Map.CurrentLocation.PortraitId);
                overlayTextColor = Color.Transparent;
                overlayText = TextManager.GetWithVariables("campaignstart",
                    ("xxxx", Map.CurrentLocation.Name), ("yyyy", TextManager.Get($"submarineclass.{Submarine.MainSub.Info.SubmarineClass}")));
                float fadeInDuration = 1.0f;
                float textDuration = 10.0f;
                float timer = 0.0f;
                while (timer < textDuration)
                {
                    if (GameMain.GameSession == null || Screen.Selected != GameMain.GameScreen)
                    {
                        GUI.DisableHUD = false;
                        yield return CoroutineStatus.Success;
                    }
                    // Try to grab the controlled here to prevent inputs, assigned late on multiplayer
                    if (Character.Controlled != null)
                    {
                        prevControlled = Character.Controlled;
                        Character.Controlled = null;
                        prevControlled.ClearInputs();
                    }
                    GameMain.GameScreen.Cam.Freeze = true;
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
                prevControlled.SelectedConstruction = null;
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
                t += CoroutineManager.UnscaledDeltaTime;
                overlayColor = Color.Lerp(Color.Transparent, Color.White, t / fadeOutDuration);
                yield return CoroutineStatus.Running;
            }
            overlayColor = Color.White;
            yield return CoroutineStatus.Running;

            //--------------------------------------

            //wait for the new level to be loaded
            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, seconds: 60);
            while (Level.Loaded == prevLevel || Level.Loaded == null)
            {
                if (DateTime.Now > timeOut || Screen.Selected != GameMain.GameScreen)  { break; }
                yield return CoroutineStatus.Running;
            }

            endTransition.Stop();
            overlayColor = Color.Transparent;

            if (DateTime.Now > timeOut) { GameMain.NetLobbyScreen.Select(); }
            if (!(Screen.Selected is RoundSummaryScreen))
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
                else
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

        public override void End(TransitionType transitionType = TransitionType.None)
        {
            base.End(transitionType);
            ForceMapUI = ShowCampaignUI = false;
            UpgradeManager.CanUpgrade = true;
            
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
            CoroutineManager.StartCoroutine(DoEndCampaignCameraTransition(), "DoEndCampaignCameraTransition");
            GameMain.CampaignEndScreen.OnFinished = () =>
            {
                GameMain.NetLobbyScreen.Select();
                if (GameMain.NetLobbyScreen.ContinueCampaignButton != null) { GameMain.NetLobbyScreen.ContinueCampaignButton.Enabled = false; }
                if (GameMain.NetLobbyScreen.QuitCampaignButton != null) { GameMain.NetLobbyScreen.QuitCampaignButton.Enabled = false; }
            };
        }

        private IEnumerable<CoroutineStatus> DoEndCampaignCameraTransition()
        {
            Character controlled = Character.Controlled;
            if (controlled != null)
            {
                controlled.AIController.Enabled = false;
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
            GameMain.CampaignEndScreen.Select();
            GUI.DisableHUD = false;

            yield return CoroutineStatus.Success;
        }

        public void ClientWrite(IWriteMessage msg)
        {
            System.Diagnostics.Debug.Assert(map.Locations.Count < UInt16.MaxValue);

            msg.Write(map.CurrentLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.CurrentLocationIndex);
            msg.Write(map.SelectedLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.SelectedLocationIndex);

            var selectedMissionIndices = map.GetSelectedMissionIndices();
            msg.Write((byte)selectedMissionIndices.Count());
            foreach (int selectedMissionIndex in selectedMissionIndices)
            {
                msg.Write((byte)selectedMissionIndex);
            }
            msg.Write(PurchasedHullRepairs);
            msg.Write(PurchasedItemRepairs);
            msg.Write(PurchasedLostShuttles);

            msg.Write((UInt16)CargoManager.ItemsInBuyCrate.Count);
            foreach (PurchasedItem pi in CargoManager.ItemsInBuyCrate)
            {
                msg.Write(pi.ItemPrefab.Identifier);
                msg.WriteRangedInteger(pi.Quantity, 0, CargoManager.MaxQuantity);
            }

            msg.Write((UInt16)CargoManager.ItemsInSellFromSubCrate.Count);
            foreach (PurchasedItem pi in CargoManager.ItemsInSellFromSubCrate)
            {
                msg.Write(pi.ItemPrefab.Identifier);
                msg.WriteRangedInteger(pi.Quantity, 0, CargoManager.MaxQuantity);
            }

            msg.Write((UInt16)CargoManager.PurchasedItems.Count);
            foreach (PurchasedItem pi in CargoManager.PurchasedItems)
            {
                msg.Write(pi.ItemPrefab.Identifier);
                msg.WriteRangedInteger(pi.Quantity, 0, CargoManager.MaxQuantity);
            }

            msg.Write((UInt16)CargoManager.SoldItems.Count);
            foreach (SoldItem si in CargoManager.SoldItems)
            {
                msg.Write(si.ItemPrefab.Identifier);
                msg.Write((UInt16)si.ID);
                msg.Write(si.Removed);
                msg.Write(si.SellerID);
                msg.Write((byte)si.Origin);
            }

            msg.Write((ushort)UpgradeManager.PurchasedUpgrades.Count);
            foreach (var (prefab, category, level) in UpgradeManager.PurchasedUpgrades)
            {
                msg.Write(prefab.Identifier);
                msg.Write(category.Identifier);
                msg.Write((byte)level);
            }

            msg.Write((ushort)UpgradeManager.PurchasedItemSwaps.Count);
            foreach (var itemSwap in UpgradeManager.PurchasedItemSwaps)
            {
                msg.Write(itemSwap.ItemToRemove.ID);
                msg.Write(itemSwap.ItemToInstall?.Identifier ?? Identifier.Empty);
            }
        }

        //static because we may need to instantiate the campaign if it hasn't been done yet
        public static void ClientRead(IReadMessage msg)
        {
            bool isFirstRound   =  msg.ReadBoolean();
            byte campaignID     = msg.ReadByte();
            UInt16 updateID     = msg.ReadUInt16();
            UInt16 saveID       = msg.ReadUInt16();
            string mapSeed      = msg.ReadString();
            UInt16 currentLocIndex      = msg.ReadUInt16();
            UInt16 selectedLocIndex     = msg.ReadUInt16();

            byte selectedMissionCount = msg.ReadByte();
            List<int> selectedMissionIndices = new List<int>();
            for (int i = 0; i < selectedMissionCount; i++)
            {
                selectedMissionIndices.Add(msg.ReadByte());
            }

            bool allowDebugTeleport = msg.ReadBoolean();
            float? reputation = null;
            if (msg.ReadBoolean()) { reputation = msg.ReadSingle(); }
            
            Dictionary<Identifier, float> factionReps = new Dictionary<Identifier, float>();
            byte factionsCount = msg.ReadByte();
            for (int i = 0; i < factionsCount; i++)
            {
                factionReps.Add(msg.ReadIdentifier(), msg.ReadSingle());
            }

            bool forceMapUI = msg.ReadBoolean();

            int money = msg.ReadInt32();
            bool purchasedHullRepairs   = msg.ReadBoolean();
            bool purchasedItemRepairs   = msg.ReadBoolean();
            bool purchasedLostShuttles  = msg.ReadBoolean();

            byte missionCount = msg.ReadByte();
            var availableMissions = new List<(Identifier Identifier, byte ConnectionIndex)>();
            for (int i = 0; i < missionCount; i++)
            {
                Identifier missionIdentifier = msg.ReadIdentifier();
                byte connectionIndex = msg.ReadByte();
                availableMissions.Add((missionIdentifier, connectionIndex));
            }

            UInt16? storeBalance = null;
            if (msg.ReadBoolean())
            {
                storeBalance = msg.ReadUInt16();
            }

            UInt16 buyCrateItemCount = msg.ReadUInt16();
            List<PurchasedItem> buyCrateItems = new List<PurchasedItem>();
            for (int i = 0; i < buyCrateItemCount; i++)
            {
                Identifier itemPrefabIdentifier = msg.ReadIdentifier();
                int itemQuantity = msg.ReadRangedInteger(0, CargoManager.MaxQuantity);
                buyCrateItems.Add(new PurchasedItem(ItemPrefab.Prefabs[itemPrefabIdentifier], itemQuantity));
            }

            UInt16 subSellCrateItemCount = msg.ReadUInt16();
            List<PurchasedItem> subSellCrateItems = new List<PurchasedItem>();
            for (int i = 0; i < subSellCrateItemCount; i++)
            {
                string itemPrefabIdentifier = msg.ReadString();
                int itemQuantity = msg.ReadRangedInteger(0, CargoManager.MaxQuantity);
                subSellCrateItems.Add(new PurchasedItem(ItemPrefab.Prefabs[itemPrefabIdentifier], itemQuantity));
            }

            UInt16 purchasedItemCount = msg.ReadUInt16();
            List<PurchasedItem> purchasedItems = new List<PurchasedItem>();
            for (int i = 0; i < purchasedItemCount; i++)
            {
                Identifier itemPrefabIdentifier = msg.ReadIdentifier();
                int itemQuantity = msg.ReadRangedInteger(0, CargoManager.MaxQuantity);
                purchasedItems.Add(new PurchasedItem(ItemPrefab.Prefabs[itemPrefabIdentifier], itemQuantity));
            }

            UInt16 soldItemCount = msg.ReadUInt16();
            List<SoldItem> soldItems = new List<SoldItem>();
            for (int i = 0; i < soldItemCount; i++)
            {
                Identifier itemPrefabIdentifier = msg.ReadIdentifier();
                UInt16 id = msg.ReadUInt16();
                bool removed = msg.ReadBoolean();
                byte sellerId = msg.ReadByte();
                byte origin = msg.ReadByte();
                soldItems.Add(new SoldItem(ItemPrefab.Prefabs[itemPrefabIdentifier], id, removed, sellerId, (SoldItem.SellOrigin)origin));
            }

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

            bool hasCharacterData = msg.ReadBoolean();
            CharacterInfo myCharacterInfo = null;
            if (hasCharacterData)
            {
                myCharacterInfo = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName, msg);
            }

            if (!(GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign) || campaignID != campaign.CampaignID)
            {
                string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer);

                GameMain.GameSession = new GameSession(null, savePath, GameModePreset.MultiPlayerCampaign, CampaignSettings.Unsure, mapSeed);
                campaign = (MultiPlayerCampaign)GameMain.GameSession.GameMode;
                campaign.CampaignID = campaignID;
                GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            }

            //server has a newer save file
            if (NetIdUtils.IdMoreRecent(saveID, campaign.PendingSaveID))
            {
                campaign.PendingSaveID = saveID;
            }
            
            if (NetIdUtils.IdMoreRecent(updateID, campaign.lastUpdateID))
            {
                campaign.SuppressStateSending = true;
                campaign.IsFirstRound = isFirstRound;

                //we need to have the latest save file to display location/mission/store
                if (campaign.LastSaveID == saveID)
                {
                    campaign.ForceMapUI = forceMapUI;

                    UpgradeStore.WaitForServerUpdate = false;

                    campaign.Map.SetLocation(currentLocIndex == UInt16.MaxValue ? -1 : currentLocIndex);
                    campaign.Map.SelectLocation(selectedLocIndex == UInt16.MaxValue ? -1 : selectedLocIndex);
                    campaign.Map.SelectMission(selectedMissionIndices);
                    campaign.Map.AllowDebugTeleport = allowDebugTeleport;
                    campaign.CargoManager.SetItemsInBuyCrate(buyCrateItems);
                    campaign.CargoManager.SetItemsInSubSellCrate(subSellCrateItems);
                    campaign.CargoManager.SetPurchasedItems(purchasedItems);
                    campaign.CargoManager.SetSoldItems(soldItems);
                    if (storeBalance.HasValue) { campaign.Map.CurrentLocation.StoreCurrentBalance = storeBalance.Value; }
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
                    foreach (Item item in Item.ItemList)
                    {
                        if (item.PendingItemSwap != null && !purchasedItemSwaps.Any(it => it.ItemToRemove == item))
                        {
                            item.PendingItemSwap = null;
                        }
                    }

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

                    if (reputation.HasValue)
                    {
                        campaign.Map.CurrentLocation.Reputation.SetReputation(reputation.Value);
                        campaign?.CampaignUI?.UpgradeStore?.RefreshAll();
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

                    GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                }

                bool shouldRefresh = campaign.Money != money ||
                                     campaign.PurchasedHullRepairs != purchasedHullRepairs ||
                                     campaign.PurchasedItemRepairs != purchasedItemRepairs ||
                                     campaign.PurchasedLostShuttles != purchasedLostShuttles;

                campaign.Money = money;
                campaign.PurchasedHullRepairs = purchasedHullRepairs;
                campaign.PurchasedItemRepairs = purchasedItemRepairs;
                campaign.PurchasedLostShuttles = purchasedLostShuttles;

                if (shouldRefresh)
                {
                    campaign?.CampaignUI?.UpgradeStore?.RefreshAll();
                }

                if (myCharacterInfo != null)
                {
                    GameMain.Client.CharacterInfo = myCharacterInfo;
                    GameMain.NetLobbyScreen.SetCampaignCharacterInfo(myCharacterInfo);
                }
                else
                {
                    GameMain.NetLobbyScreen.SetCampaignCharacterInfo(null);
                }

                campaign.lastUpdateID = updateID;
                campaign.SuppressStateSending = false;
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

            if (map?.CurrentLocation?.HireManager != null && CampaignUI?.CrewManagement != null)
            {
                CampaignUI.CrewManagement.SetHireables(map.CurrentLocation, availableHires);
                if (hiredCharacters.Any()) { CampaignUI.CrewManagement.ValidateHires(hiredCharacters); }
                CampaignUI.CrewManagement.SetPendingHires(pendingHires, map.CurrentLocation);
                if (renameCrewMember || fireCharacter) { CampaignUI.CrewManagement.UpdateCrew(); }
            }
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
