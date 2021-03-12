using Barotrauma.IO;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    static class HintManager
    {
        private const string HintManagerFile = "hintmanager.xml";
        private static HashSet<string> HintIdentifiers { get; set; }
        private static Dictionary<string, HashSet<string>> HintTags { get; } = new Dictionary<string, HashSet<string>>();
        private static Dictionary<string, (string identifier, string option)> HintOrders { get; } = new Dictionary<string, (string orderIdentifier, string orderOption)>();
        /// <summary>
        /// Hints that have already been shown this round and shouldn't be shown shown again until the next round
        /// </summary>
        private static HashSet<string> HintsIgnoredThisRound { get; } = new HashSet<string>();
        private static GUIMessageBox ActiveHintMessageBox { get; set; }
        private static Action OnUpdate { get; set; }
        private static double TimeStoppedInteracting { get; set; }

        /// <summary>
        /// Seconds before any reminders can be shown
        /// </summary>
        private static int TimeBeforeReminders { get; set; }

        /// <summary>
        /// Seconds before another reminder can be shown
        /// </summary>
        private static int ReminderCooldown { get; set; }

        private static double TimeReminderLastDisplayed { get; set; }

        private static Queue<HintInfo> HintQueue { get; } = new Queue<HintInfo>();

        public struct HintInfo
        {
            public string Identifier { get; }
            public string Text { get; }
            public Sprite Icon { get; }
            public Color? IconColor { get; }
            public Action OnDisplay { get; }
            public Action OnUpdate { get; }

            public HintInfo(string hintIdentifier, string text, Sprite icon, Color? iconColor, Action onDisplay, Action onUpdate)
            {
                Identifier = hintIdentifier;
                Text = text;
                Icon = icon;
                IconColor = iconColor;
                OnDisplay = onDisplay;
                OnUpdate = onUpdate;
            }
        }

        private static HashSet<Hull> BallastHulls { get; } = new HashSet<Hull>();

        public static void Init()
        {
            if (File.Exists(HintManagerFile))
            {
                var doc = XMLExtensions.TryLoadXml(HintManagerFile);
                if (doc?.Root != null)
                {
                    HintIdentifiers = new HashSet<string>();
                    foreach (var element in doc.Root.Elements())
                    {
                        GetHintsRecursive(element, element.Name.ToString());
                    }
                }
                else
                {
                    DebugConsole.ThrowError($"File \"{HintManagerFile}\" is empty - cannot initialize the HintManager!");
                }
            }
            else
            {
                DebugConsole.ThrowError($"File \"{HintManagerFile}\" is missing - cannot initialize the HintManager!");
            }

            static void GetHintsRecursive(XElement element, string identifier)
            {
                if (!element.HasElements)
                {
                    HintIdentifiers.Add(identifier);
                    if (element.GetAttributeStringArray("tags", null, convertToLowerInvariant: true) is string[] tags)
                    {
                        HintTags.TryAdd(identifier, tags.ToHashSet());
                    }
                    if (element.GetAttributeString("order", null) is string orderIdentifier && !string.IsNullOrEmpty(orderIdentifier))
                    {
                        string orderOption = element.GetAttributeString("orderoption", "");
                        HintOrders.Add(identifier, (orderIdentifier, orderOption));
                    }
                    return;
                }
                else if (element.Name.ToString().Equals("reminder"))
                {
                    TimeBeforeReminders = element.GetAttributeInt("timebeforereminders", TimeBeforeReminders);
                    ReminderCooldown = element.GetAttributeInt("remindercooldown", ReminderCooldown);
                }
                foreach (var childElement in element.Elements())
                {
                    GetHintsRecursive(childElement, $"{identifier}.{childElement.Name}");
                }
            }
        }

        public static void Update()
        {
            if (HintIdentifiers == null || GameMain.Config.DisableInGameHints) { return; }
            if (GameMain.GameSession == null || !GameMain.GameSession.IsRunning) { return; }

            if (ActiveHintMessageBox != null)
            {
                if (ActiveHintMessageBox.Closed)
                {
                    ActiveHintMessageBox = null;
                    OnUpdate = null;
                }
                else
                {
                    OnUpdate?.Invoke();
                    return;
                }
            }
            else if (HintQueue.TryDequeue(out var hint))
            {
                ActiveHintMessageBox = new GUIMessageBox(hint.Identifier, hint.Text, hint.Icon);
                if (hint.IconColor.HasValue) { ActiveHintMessageBox.IconColor = hint.IconColor.Value; }
                OnUpdate = hint.OnUpdate;
                ActiveHintMessageBox.InnerFrame.Flash(color: hint.IconColor ?? Color.Orange, flashDuration: 0.75f);
                SoundPlayer.PlayUISound(GUISoundType.UIMessage);
                hint.OnDisplay?.Invoke();
            }

            CheckIsInteracting();
            CheckIfDivingGearOutOfOxygen();
            CheckAdjacentHulls();
            CheckReminders();
        }

        public static void OnSetSelectedConstruction(Character character, Item oldConstruction, Item newConstruction)
        {
            if (oldConstruction == newConstruction) { return; }
            if (Character.Controlled != null && Character.Controlled == character && oldConstruction != null && oldConstruction.GetComponent<Ladder>() == null)
            {
                TimeStoppedInteracting = Timing.TotalTime;
            }
            if (newConstruction != null && newConstruction.GetComponent<Ladder>() == null)
            {
                OnStartedInteracting(character, newConstruction);
            }
        }

        private static void OnStartedInteracting(Character character, Item item)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled || item == null) { return; }

            string hintIdentifierBase = "onstartedinteracting";

            // onstartedinteracting.brokenitem
            if (item.Repairables.Any(r => item.ConditionPercentage < r.RepairThreshold))
            {
                if (EnqueueHint($"{hintIdentifierBase}.brokenitem")) { return; }
            }

            // onstartedinteracting.lootingisstealing
            if (item.Submarine?.Info?.Type == SubmarineType.Outpost &&
                item.ContainedItems.Any(i => i.SpawnedInOutpost))
            {
                if (EnqueueHint($"{hintIdentifierBase}.lootingisstealing")) { return; }
            }

            // onstartedinteracting.turretperiscope
            if (item.HasTag("periscope") &&
                item.GetConnectedComponents<Turret>().FirstOrDefault(t => t.Item.HasTag("turret")) is Turret)
            {
                if (EnqueueHint($"{hintIdentifierBase}.turretperiscope",
                        variableTags: new string[] { "[shootkey]", "[deselectkey]", },
                        variableValues: new string[] { GameMain.Config.KeyBindText(InputType.Shoot), GameMain.Config.KeyBindText(InputType.Deselect) }))
                { return; }
            }

            // onstartedinteracting.item...
            hintIdentifierBase += ".item";
            foreach (string hintIdentifier in HintIdentifiers)
            {
                if (!hintIdentifier.StartsWith(hintIdentifierBase)) { continue; }
                if (!HintTags.TryGetValue(hintIdentifier, out var hintTags)) { continue; }
                if (!item.HasTag(hintTags)) { continue; }
                if (EnqueueHint(hintIdentifier)) { return; }
            }
        }

        private static void CheckIsInteracting()
        {
            if (!CanDisplayHints()) { return; }
            if (Character.Controlled?.SelectedConstruction == null) { return; }

            if (Character.Controlled.SelectedConstruction.GetComponent<Reactor>() is Reactor reactor && reactor.PowerOn &&
                Character.Controlled.SelectedConstruction.OwnInventory?.AllItems is IEnumerable<Item> containedItems &&
                containedItems.Count(i => i.HasTag("reactorfuel")) > 1)
            {
                if (EnqueueHint("onisinteracting.reactorwithextrarods")) { return; }
            }
        }

        public static void OnRoundStarted()
        {
            // Make sure everything's been reset properly, OnRoundEnded() isn't always called when exiting a game
            Reset();

            var initRoundHandle = CoroutineManager.StartCoroutine(InitRound(), "HintManager.InitRound");
            if (!CanDisplayHints(requireGameScreen: false, requireControllingCharacter: false)) { return; }
            CoroutineManager.StartCoroutine(DisplayRoundStartedHints(initRoundHandle), "HintManager.DisplayRoundStartedHints");

            static IEnumerable<object> InitRound()
            {
                while (Character.Controlled == null) { yield return CoroutineStatus.Running; }
                // Get the ballast hulls on round start not to find them again and again later
                BallastHulls.Clear();
                var sub = Submarine.MainSubs.FirstOrDefault(s => s != null && s.TeamID == Character.Controlled.TeamID);
                if (sub != null)
                {
                    foreach (var item in sub.GetItems(true))
                    {
                        if (item.CurrentHull == null) { continue; }
                        if (item.GetComponent<Pump>() == null) { continue; }
                        if (!item.HasTag("ballast")) { continue; }
                        BallastHulls.Add(item.CurrentHull);
                    }
                }
                yield return CoroutineStatus.Success;
            }

            static IEnumerable<object> DisplayRoundStartedHints(CoroutineHandle initRoundHandle)
            {
                while (GameMain.Instance.LoadingScreenOpen || Screen.Selected != GameMain.GameScreen ||
                       CoroutineManager.IsCoroutineRunning(initRoundHandle) ||
                       CoroutineManager.IsCoroutineRunning("LevelTransition") ||
                       CoroutineManager.IsCoroutineRunning("SinglePlayerCampaign.DoInitialCameraTransition") ||
                       CoroutineManager.IsCoroutineRunning("MultiPlayerCampaign.DoInitialCameraTransition") ||
                       GUIMessageBox.VisibleBox != null || Character.Controlled == null)
                {
                    yield return CoroutineStatus.Running;
                }

                OnStartedControlling();

                if (!GameMain.GameSession.GameMode.IsSinglePlayer &&
                    GameMain.Config.VoiceSetting == GameSettings.VoiceMode.Disabled)
                {
                    EnqueueHint("onroundstarted.voipdisabled", onUpdate: () =>
                    {
                        if (GameMain.Config.VoiceSetting == GameSettings.VoiceMode.Disabled) { return; }
                        ActiveHintMessageBox.Close();
                    });
                }

                yield return CoroutineStatus.Success;
            }
        }

        public static void OnRoundEnded()
        {
            Reset();
        }

        private static void Reset()
        {
            CoroutineManager.StopCoroutines("HintManager.InitRound");
            CoroutineManager.StopCoroutines("HintManager.DisplayRoundStartedHints");
            if (ActiveHintMessageBox != null)
            {
                GUIMessageBox.MessageBoxes.Remove(ActiveHintMessageBox);
                ActiveHintMessageBox = null;
            }
            OnUpdate = null;
            HintQueue.Clear();
            HintsIgnoredThisRound.Clear();
        }

        public static void OnSonarSpottedCharacter(Item sonar, Character spottedCharacter)
        {
            if (!CanDisplayHints()) { return; }
            if (sonar == null || sonar.Removed) { return; }
            if (spottedCharacter == null || spottedCharacter.Removed || spottedCharacter.IsDead) { return; }
            if (Character.Controlled.SelectedConstruction != sonar) { return; }
            if (HumanAIController.IsFriendly(Character.Controlled, spottedCharacter)) { return; }
            EnqueueHint("onsonarspottedenemy");
        }

        public static void OnAfflictionDisplayed(Character character, List<Affliction> displayedAfflictions)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled || displayedAfflictions == null) { return; }
            foreach (var affliction in displayedAfflictions)
            {
                if (affliction?.Prefab == null) { continue; }
                if (affliction.Prefab.IsBuff) { continue; }
                if (affliction.Prefab == AfflictionPrefab.OxygenLow) { continue; }
                if (affliction.Prefab == AfflictionPrefab.RadiationSickness && (GameMain.GameSession.Map?.Radiation?.IsEntityRadiated(character) ?? false)) { continue; }
                EnqueueHint("onafflictiondisplayed",
                    variableTags: new string[1] { "[key]" },
                    variableValues: new string[1] { GameMain.Config.KeyBindText(InputType.Health) },
                    icon: affliction.Prefab.Icon,
                    iconColor: CharacterHealth.GetAfflictionIconColor(affliction),
                    onUpdate: () =>
                    {
                        if (CharacterHealth.OpenHealthWindow == null) { return; }
                        ActiveHintMessageBox.Close();
                    });
                return;
            }
        }

        public static void OnShootWithoutAiming(Character character, Item item)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled) { return; }
            if (character.SelectedConstruction != null || character.FocusedItem != null) { return; }
            if (item == null || !item.IsShootable || !item.RequireAimToUse) { return; }
            if (GUI.MouseOn != null) { return; }
            if (TimeStoppedInteracting + 1 > Timing.TotalTime) { return; }
            string hintIdentifier = "onshootwithoutaiming";
            if (!HintTags.TryGetValue(hintIdentifier, out var tags)) { return; }
            if (!item.HasTag(tags)) { return; }
            EnqueueHint(hintIdentifier,
                variableTags: new string[1] { "[key]" },
                variableValues: new string[1] { GameMain.Config.KeyBindText(InputType.Aim) },
                onUpdate: () =>
                {
                    if (character.SelectedConstruction == null && GUI.MouseOn == null && PlayerInput.KeyDown(InputType.Aim))
                    {
                        ActiveHintMessageBox.Close();
                    }
                });
        }

        public static void OnWeldingDoor(Character character, Door door)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled) { return; }
            if (door == null || door.Stuck < 20.0f) { return; }
            EnqueueHint("onweldingdoor");
        }

        public static void OnTryOpenStuckDoor(Character character)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled) { return; }
            EnqueueHint("ontryopenstuckdoor");
        }

        public static void OnShowCampaignInterface(CampaignMode.InteractionType interactionType)
        {
            if (!CanDisplayHints()) { return; }
            if (interactionType == CampaignMode.InteractionType.None) { return; }
            string hintIdentifier = $"onshowcampaigninterface.{interactionType.ToString().ToLowerInvariant()}";
            EnqueueHint(hintIdentifier,
                onUpdate: () =>
                {

                    if (!(GameMain.GameSession?.Campaign is CampaignMode campaign) ||
                        (!campaign.ShowCampaignUI && !campaign.ForceMapUI) ||
                        campaign.CampaignUI?.SelectedTab != CampaignMode.InteractionType.Map)
                    {
                        ActiveHintMessageBox.Close();
                    }
                });
        }

        public static void OnShowCommandInterface()
        {
            IgnoreReminder("commandinterface");
            if (!CanDisplayHints()) { return; }
            EnqueueHint("onshowcommandinterface",
                onUpdate: () =>
                {
                    if (CrewManager.IsCommandInterfaceOpen) { return; }
                    ActiveHintMessageBox.Close();
                });
        }

        public static void OnShowHealthInterface()
        {
            if (!CanDisplayHints()) { return; }
            if (CharacterHealth.OpenHealthWindow == null) { return; }
            EnqueueHint("onshowhealthinterface", onUpdate: () =>
            {
                if (CharacterHealth.OpenHealthWindow != null) { return; }
                ActiveHintMessageBox.Close();
            });
        }

        public static void OnShowTabMenu()
        {
            IgnoreReminder("tabmenu");
        }

        public static void OnStoleItem(Character character, Item item)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled) { return; }
            if (item == null || !item.SpawnedInOutpost || !item.StolenDuringRound) { return; }
            EnqueueHint("onstoleitem", onUpdate: () =>
            {
                if (item == null || item.Removed || item.GetRootInventoryOwner() != character)
                {
                    ActiveHintMessageBox.Close();
                }
            });
        }

        public static void OnHandcuffed(Character character)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled || !character.LockHands) { return; }
            EnqueueHint("onhandcuffed", onUpdate: () =>
            {
                if (character != null && !character.Removed && character.LockHands) { return; }
                ActiveHintMessageBox.Close();
            });
        }

        public static void OnReactorOutOfFuel(Reactor reactor)
        {
            if (!CanDisplayHints()) { return; }
            if (reactor == null) { return; }
            if (reactor.Item.Submarine?.Info?.Type != SubmarineType.Player || reactor.Item.Submarine.TeamID != Character.Controlled.TeamID) { return; }
            if (!HasValidJob("engineer")) { return; }
            EnqueueHint("onreactoroutoffuel", onUpdate: () =>
            {
                if (reactor?.Item != null && !reactor.Item.Removed && reactor.AvailableFuel < 1) { return; }
                ActiveHintMessageBox.Close();
            });
        }

        public static void OnAvailableTransition(CampaignMode.TransitionType transitionType)
        {
            if (!CanDisplayHints()) { return; }
            if (transitionType == CampaignMode.TransitionType.None) { return; }
            EnqueueHint($"onavailabletransition.{transitionType.ToString().ToLowerInvariant()}");
        }

        public static void OnShowSubInventory(Item item)
        {
            if (item?.Prefab == null) { return; }
            if (item.Prefab.Identifier.Equals("toolbelt", StringComparison.OrdinalIgnoreCase))
            {
                IgnoreReminder("toolbelt");
            }
        }

        public static void OnChangeCharacter()
        {
            IgnoreReminder("characterchange");
        }

        private static void OnStartedControlling()
        {
            if (Level.IsLoadedOutpost) { return; }
            if (Character.Controlled?.Info?.Job?.Prefab == null) { return; }
            string hintIdentifier = $"onstartedcontrolling.job.{Character.Controlled.Info.Job.Prefab.Identifier}";
            EnqueueHint(hintIdentifier,
                icon: Character.Controlled.Info.Job.Prefab.Icon,
                iconColor: Character.Controlled.Info.Job.Prefab.UIColor,
                onDisplay: () =>
                {
                    if (!HintOrders.TryGetValue(hintIdentifier, out var orderInfo)) { return; }
                    var orderPrefab = Order.GetPrefab(orderInfo.identifier);
                    if (orderPrefab == null) { return; }
                    Item targetEntity = null;
                    ItemComponent targetItem = null;
                    if (orderPrefab.MustSetTarget)
                    {
                        targetEntity = orderPrefab.GetMatchingItems(true, interactableFor: Character.Controlled).FirstOrDefault();
                        if (targetEntity == null) { return; }
                        targetItem = orderPrefab.GetTargetItemComponent(targetEntity);
                    }
                    var order = new Order(orderPrefab, targetEntity as Entity, targetItem, orderGiver: Character.Controlled);
                    GameMain.GameSession.CrewManager.SetCharacterOrder(Character.Controlled, order, orderInfo.option, CharacterInfo.HighestManualOrderPriority, Character.Controlled);
                });
        }

        public static void OnAutoPilotPathUpdated(Steering steering)
        {
            if (!CanDisplayHints()) { return; }
            if (!HasValidJob("captain")) { return; }
            if (steering?.Item?.Submarine?.Info == null) { return; }
            if (steering.Item.Submarine.Info.Type != SubmarineType.Player) { return; }
            if (steering.Item.Submarine.TeamID != Character.Controlled.TeamID) { return; }
            if (!steering.AutoPilot || steering.MaintainPos) { return; }
            if (steering.SteeringPath?.CurrentNode?.Tunnel?.Type != Level.TunnelType.MainPath) { return; }
            if (!steering.SteeringPath.Finished && steering.SteeringPath.NextNode != null) { return; }
            if (steering.LevelStartSelected && (Level.Loaded.StartOutpost == null || !steering.Item.Submarine.AtStartExit)) { return; }
            if (steering.LevelEndSelected && (Level.Loaded.EndOutpost == null || !steering.Item.Submarine.AtEndExit)) { return; }
            EnqueueHint("onautopilotreachedoutpost");
        }

        public static void OnStatusEffectApplied(ItemComponent component, ActionType actionType, Character character)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled) { return; }
            // Could make this more generic if there will ever be any other status effect related hints
            if (!(component is Repairable) || actionType != ActionType.OnFailure) { return; }
            EnqueueHint("onrepairfailed");
        }

        private static void CheckIfDivingGearOutOfOxygen()
        {
            if (!CanDisplayHints()) { return; }
            var divingGear = Character.Controlled.GetEquippedItem("diving");
            if (divingGear?.OwnInventory == null) { return; }
            if (divingGear.GetContainedItemConditionPercentage() > 0.05f) { return; }
            EnqueueHint("ondivinggearoutofoxygen", onUpdate: () =>
            {
                if (divingGear == null || divingGear.Removed ||
                    Character.Controlled == null || !Character.Controlled.HasEquippedItem(divingGear) ||
                    divingGear.GetContainedItemConditionPercentage() > 0.05f)
                {
                    ActiveHintMessageBox.Close();
                }
            });
        }

        private static void CheckAdjacentHulls()
        {
            if (!CanDisplayHints()) { return; }
            if (Character.Controlled.CurrentHull == null) { return; }
            foreach (var gap in Character.Controlled.CurrentHull.ConnectedGaps)
            {
                if (!gap.IsRoomToRoom) { continue; }
                if (gap.ConnectedDoor == null || gap.ConnectedDoor.Impassable) { continue; }
                if (Vector2.DistanceSquared(Character.Controlled.WorldPosition, gap.ConnectedDoor.Item.WorldPosition) > 400 * 400) { continue; }
                foreach (var me in gap.linkedTo)
                {
                    if (me == Character.Controlled.CurrentHull) { continue; }
                    if (!(me is Hull adjacentHull)) { continue; }
                    if (adjacentHull.LethalPressure > 5.0f && EnqueueHint("onadjacenthull.highpressure")) { return; }
                    if (adjacentHull.WaterPercentage > 75 && !BallastHulls.Contains(adjacentHull) && EnqueueHint("onadjacenthull.highwaterpercentage")) { return; }
                }
            }
        }

        private static void CheckReminders()
        {
            if (!CanDisplayHints()) { return; }
            if (Level.Loaded == null) { return; }
            if (Timing.TotalTime < GameMain.GameSession.RoundStartTime + TimeBeforeReminders) { return; }
            if (Timing.TotalTime < TimeReminderLastDisplayed + ReminderCooldown) { return; }

            string hintIdentifierBase = "reminder";

            if (GameMain.GameSession.GameMode.IsSinglePlayer)
            {
                if (EnqueueHint($"{hintIdentifierBase}.characterchange"))
                {
                    TimeReminderLastDisplayed = Timing.TotalTime;
                    return;
                }
            }

            if (Level.Loaded.Type != LevelData.LevelType.Outpost)
            {
                if (EnqueueHint($"{hintIdentifierBase}.commandinterface",
                        variableTags: new string[] { "[commandkey]" },
                        variableValues: new string[] { GameMain.Config.KeyBindText(InputType.Command) },
                        onUpdate: () =>
                        {
                            if (!CrewManager.IsCommandInterfaceOpen) { return; }
                            ActiveHintMessageBox.Close();
                        }))
                {
                    TimeReminderLastDisplayed = Timing.TotalTime;
                    return;
                }
            }

            if (EnqueueHint($"{hintIdentifierBase}.tabmenu",
                    variableTags: new string[] { "[infotabkey]" },
                    variableValues: new string[] { GameMain.Config.KeyBindText(InputType.InfoTab) },
                    onUpdate: () =>
                    {
                        if (!GameSession.IsTabMenuOpen) { return; }
                        ActiveHintMessageBox.Close();
                    }))
            {
                TimeReminderLastDisplayed = Timing.TotalTime;
                return;
            }

            if (Character.Controlled.Inventory?.GetItemInLimbSlot(InvSlotType.Bag)?.Prefab?.Identifier == "toolbelt")
            {
                if (EnqueueHint($"{hintIdentifierBase}.toolbelt"))
                {
                    TimeReminderLastDisplayed = Timing.TotalTime;
                    return;
                }
            }
        }

        private static bool EnqueueHint(string hintIdentifier, string[] variableTags = null, string[] variableValues = null, Sprite icon = null, Color? iconColor = null, Action onDisplay = null, Action onUpdate = null)
        {
            if (string.IsNullOrEmpty(hintIdentifier)) { return false; }
            if (!HintIdentifiers.Contains(hintIdentifier)) { return false; }
            if (GameMain.Config.IgnoredHints.Contains(hintIdentifier)) { return false; }
            if (HintsIgnoredThisRound.Contains(hintIdentifier)) { return false; }

            string text;
            string textTag = $"hint.{hintIdentifier}";
            if (variableTags != null && variableTags != null && variableTags.Length > 0 && variableTags.Length == variableValues.Length)
            {
                text = TextManager.GetWithVariables(textTag, variableTags, variableValues, returnNull: true);
            }
            else
            {
                text = TextManager.Get(textTag, returnNull: true);
            }

            if (string.IsNullOrEmpty(text))
            {
#if DEBUG
                DebugConsole.ThrowError($"No hint text found for text tag \"{textTag}\"");
#endif
                return false;
            }

            var hint = new HintInfo(hintIdentifier, text, icon, iconColor, onDisplay, onUpdate);
            HintQueue.Enqueue(hint);
            HintsIgnoredThisRound.Add(hintIdentifier);
            return true;
        }

        public static bool OnDontShowAgain(GUITickBox tickBox)
        {
            IgnoreHint((string)tickBox.UserData, ignore: tickBox.Selected);
            return true;
        }

        private static void IgnoreHint(string hintIdentifier, bool ignore = true)
        {
            if (string.IsNullOrEmpty(hintIdentifier)) { return; }
            if (!HintIdentifiers.Contains(hintIdentifier))
            {
#if DEBUG
                DebugConsole.ThrowError($"Tried to ignore a hint not defined in {HintManagerFile}: {hintIdentifier}");
#endif
                return;
            }
            if (ignore)
            {
                GameMain.Config.IgnoredHints.Add(hintIdentifier);
            }
            else
            {
                GameMain.Config.IgnoredHints.Remove(hintIdentifier);
            }
        }

        private static void IgnoreReminder(string reminderIdentifier)
        {
            HintsIgnoredThisRound.Add($"reminder.{reminderIdentifier}");
        }

        public static bool OnDisableHints(GUITickBox tickBox)
        {
            GameMain.Config.DisableInGameHints = tickBox.Selected;
            return GameMain.Config.SaveNewPlayerConfig();
        }

        private static bool CanDisplayHints(bool requireGameScreen = true, bool requireControllingCharacter = true)
        {
            if (HintIdentifiers == null) { return false; }
            if (GameMain.Config.DisableInGameHints) { return false; }
            if (requireControllingCharacter && Character.Controlled == null) { return false; }
            var gameMode = GameMain.GameSession?.GameMode;
            if (!(gameMode is CampaignMode || gameMode is MissionMode)) { return false; }
            if (requireGameScreen && Screen.Selected != GameMain.GameScreen) { return false; }
            return true;
        }

        private static bool HasValidJob(string jobIdentifier)
        {
            // In singleplayer, we can control all character so we don't care about job restrictions
            if (GameMain.GameSession.GameMode.IsSinglePlayer) { return true; }
            if (Character.Controlled.HasJob(jobIdentifier)) { return true; }
            // In multiplayer, if there are players with the job, display the hint to all players
            foreach (var c in GameMain.GameSession.CrewManager.GetCharacters())
            {
                if (c == null || !c.IsRemotePlayer) { continue; }
                if (c.IsUnconscious || c.IsDead || c.Removed) { continue; }
                if (!c.HasJob(jobIdentifier)) { continue; }
                return false;
            }
            return true;
        }
    }
}