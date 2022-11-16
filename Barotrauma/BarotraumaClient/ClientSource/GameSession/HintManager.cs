using Barotrauma.Extensions;
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

        public static bool Enabled => !GameSettings.CurrentConfig.DisableInGameHints;
        private static HashSet<Identifier> HintIdentifiers { get; set; }
        private static Dictionary<Identifier, HashSet<Identifier>> HintTags { get; } = new Dictionary<Identifier, HashSet<Identifier>>();
        private static Dictionary<Identifier, (Identifier identifier, Identifier option)> HintOrders { get; } = new Dictionary<Identifier, (Identifier orderIdentifier, Identifier orderOption)>();
        /// <summary>
        /// Hints that have already been shown this round and shouldn't be shown shown again until the next round
        /// </summary>
        private static HashSet<Identifier> HintsIgnoredThisRound { get; } = new HashSet<Identifier>();
        private static GUIMessageBox ActiveHintMessageBox { get; set; }
        private static Action OnUpdate { get; set; }
        private static double TimeStoppedInteracting { get; set; }
        private static double TimeRoundStarted { get; set; }
        /// <summary>
        /// Seconds before any reminders can be shown
        /// </summary>
        private static int TimeBeforeReminders { get; set; }
        /// <summary>
        /// Seconds before another reminder can be shown
        /// </summary>
        private static int ReminderCooldown { get; set; }
        private static double TimeReminderLastDisplayed { get; set; }
        private static HashSet<Hull> BallastHulls { get; } = new HashSet<Hull>();

        public static void Init()
        {
            if (File.Exists(HintManagerFile))
            {
                var doc = XMLExtensions.TryLoadXml(HintManagerFile);
                if (doc?.Root != null)
                {
                    HintIdentifiers = new HashSet<Identifier>();
                    foreach (var element in doc.Root.Elements())
                    {
                        GetHintsRecursive(element, element.NameAsIdentifier());
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

            static void GetHintsRecursive(XElement element, Identifier identifier)
            {
                if (!element.HasElements)
                {
                    HintIdentifiers.Add(identifier);
                    if (element.GetAttributeIdentifierArray("tags", null) is Identifier[] tags)
                    {
                        HintTags.TryAdd(identifier, tags.ToHashSet());
                    }
                    if (element.GetAttributeIdentifier("order", Identifier.Empty) is Identifier orderIdentifier && orderIdentifier != Identifier.Empty)
                    {
                        Identifier orderOption = element.GetAttributeIdentifier("orderoption", Identifier.Empty);
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
                    GetHintsRecursive(childElement, $"{identifier}.{childElement.Name}".ToIdentifier());
                }
            }
        }

        public static void Update()
        {
            if (HintIdentifiers == null || GameSettings.CurrentConfig.DisableInGameHints) { return; }
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

            CheckIsInteracting();
            CheckIfDivingGearOutOfOxygen();
            CheckHulls();
            CheckReminders();
        }

        public static void OnSetSelectedItem(Character character, Item oldItem, Item newItem)
        {
            if (oldItem == newItem) { return; }

            if (Character.Controlled != null && Character.Controlled == character && oldItem != null && !oldItem.IsLadder)
            {
                TimeStoppedInteracting = Timing.TotalTime;
            }

            if (newItem == null) { return; }
            if (newItem.IsLadder) { return; }
            if (newItem.GetComponent<ConnectionPanel>() is ConnectionPanel cp && cp.User == character) { return; }
            OnStartedInteracting(character, newItem);
        }

        private static void OnStartedInteracting(Character character, Item item)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled || item == null) { return; }

            string hintIdentifierBase = "onstartedinteracting";

            // onstartedinteracting.brokenitem
            if (item.Repairables.Any(r => r.IsBelowRepairThreshold))
            {
                if (DisplayHint($"{hintIdentifierBase}.brokenitem".ToIdentifier())) { return; }
            }

            // Don't display other item-related hints if the repair interface is displayed
            if (item.Repairables.Any(r => r.ShouldDrawHUD(character))) { return; }

            // onstartedinteracting.lootingisstealing
            if (item.Submarine?.Info?.Type == SubmarineType.Outpost &&
                item.ContainedItems.Any(i => !i.AllowStealing))
            {
                if (DisplayHint($"{hintIdentifierBase}.lootingisstealing".ToIdentifier())) { return; }
            }

            // onstartedinteracting.turretperiscope
            if (item.HasTag("periscope") &&
                item.GetConnectedComponents<Turret>().FirstOrDefault(t => t.Item.HasTag("turret")) is Turret)
            {
                if (DisplayHint($"{hintIdentifierBase}.turretperiscope".ToIdentifier(),
                        variables: new[]
                        {
                            ("[shootkey]".ToIdentifier(), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Shoot)),
                            ("[deselectkey]".ToIdentifier(), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Deselect))
                        }))
                { return; }
            }

            // onstartedinteracting.item...
            hintIdentifierBase += ".item";
            foreach (Identifier hintIdentifier in HintIdentifiers)
            {
                if (!hintIdentifier.StartsWith(hintIdentifierBase)) { continue; }
                if (!HintTags.TryGetValue(hintIdentifier, out var hintTags)) { continue; }
                if (!item.HasTag(hintTags)) { continue; }
                if (DisplayHint(hintIdentifier)) { return; }
            }
        }

        private static void CheckIsInteracting()
        {
            if (!CanDisplayHints()) { return; }
            if (Character.Controlled?.SelectedItem == null) { return; }

            if (Character.Controlled.SelectedItem.GetComponent<Reactor>() is Reactor reactor && reactor.PowerOn &&
                Character.Controlled.SelectedItem.OwnInventory?.AllItems is IEnumerable<Item> containedItems &&
                containedItems.Count(i => i.HasTag("reactorfuel")) > 1)
            {
                if (DisplayHint("onisinteracting.reactorwithextrarods".ToIdentifier())) { return; }
            }
        }

        public static void OnRoundStarted()
        {
            // Make sure everything's been reset properly, OnRoundEnded() isn't always called when exiting a game
            Reset();
            TimeRoundStarted = GameMain.GameScreen.GameTime;

            var initRoundHandle = CoroutineManager.StartCoroutine(InitRound(), "HintManager.InitRound");
            if (!CanDisplayHints(requireGameScreen: false, requireControllingCharacter: false)) { return; }
            CoroutineManager.StartCoroutine(DisplayRoundStartedHints(initRoundHandle), "HintManager.DisplayRoundStartedHints");

            static IEnumerable<CoroutineStatus> InitRound()
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

            static IEnumerable<CoroutineStatus> DisplayRoundStartedHints(CoroutineHandle initRoundHandle)
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

                while (ActiveHintMessageBox != null)
                {
                    yield return CoroutineStatus.Running;
                }

                if (!GameMain.GameSession.GameMode.IsSinglePlayer &&
                    GameSettings.CurrentConfig.Audio.VoiceSetting == VoiceMode.Disabled)
                {
                    DisplayHint("onroundstarted.voipdisabled".ToIdentifier(), onUpdate: () =>
                    {
                        if (GameSettings.CurrentConfig.Audio.VoiceSetting == VoiceMode.Disabled) { return; }
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
            HintsIgnoredThisRound.Clear();
        }

        public static void OnSonarSpottedCharacter(Item sonar, Character spottedCharacter)
        {
            if (!CanDisplayHints()) { return; }
            if (sonar == null || sonar.Removed) { return; }
            if (spottedCharacter == null || spottedCharacter.Removed || spottedCharacter.IsDead) { return; }
            if (Character.Controlled.SelectedItem != sonar) { return; }
            if (HumanAIController.IsFriendly(Character.Controlled, spottedCharacter)) { return; }
            DisplayHint("onsonarspottedenemy".ToIdentifier());
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
                if (affliction.Strength < affliction.Prefab.ShowIconThreshold) { continue; }
                DisplayHint("onafflictiondisplayed".ToIdentifier(),
                    variables: new[] { ("[key]".ToIdentifier(), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Health)) },
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
            if (character.HasSelectedAnyItem || character.FocusedItem != null) { return; }
            if (item == null || !item.IsShootable || !item.RequireAimToUse) { return; }
            if (TimeStoppedInteracting + 1 > Timing.TotalTime) { return; }
            if (GUI.MouseOn != null) { return; }
            if (Character.Controlled.Inventory?.visualSlots != null && Character.Controlled.Inventory.visualSlots.Any(s => s.InteractRect.Contains(PlayerInput.MousePosition))) { return; }
            Identifier hintIdentifier = "onshootwithoutaiming".ToIdentifier();
            if (!HintTags.TryGetValue(hintIdentifier, out var tags)) { return; }
            if (!item.HasTag(tags)) { return; }
            DisplayHint(hintIdentifier,
                variables: new[] { ("[key]".ToIdentifier(), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Aim)) },
                onUpdate: () =>
                {
                    if (character.SelectedItem == null && GUI.MouseOn == null && PlayerInput.KeyDown(InputType.Aim))
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
            DisplayHint("onweldingdoor".ToIdentifier());
        }

        public static void OnTryOpenStuckDoor(Character character)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled) { return; }
            DisplayHint("ontryopenstuckdoor".ToIdentifier());
        }

        public static void OnShowCampaignInterface(CampaignMode.InteractionType interactionType)
        {
            if (!CanDisplayHints()) { return; }
            if (interactionType == CampaignMode.InteractionType.None) { return; }
            Identifier hintIdentifier = $"onshowcampaigninterface.{interactionType}".ToIdentifier();
            DisplayHint(hintIdentifier, onUpdate: () =>
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
            DisplayHint("onshowcommandinterface".ToIdentifier(), onUpdate: () =>
            {
                if (CrewManager.IsCommandInterfaceOpen) { return; }
                ActiveHintMessageBox.Close();
            });
        }

        public static void OnShowHealthInterface()
        {
            if (!CanDisplayHints()) { return; }
            if (CharacterHealth.OpenHealthWindow == null) { return; }
            DisplayHint("onshowhealthinterface".ToIdentifier(), onUpdate: () =>
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
            if (item == null || item.AllowStealing || !item.StolenDuringRound) { return; }
            DisplayHint("onstoleitem".ToIdentifier(), onUpdate: () =>
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
            DisplayHint("onhandcuffed".ToIdentifier(), onUpdate: () =>
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
            DisplayHint("onreactoroutoffuel".ToIdentifier(), onUpdate: () =>
            {
                if (reactor?.Item != null && !reactor.Item.Removed && reactor.AvailableFuel < 1) { return; }
                ActiveHintMessageBox.Close();
            });
        }

        public static void OnAvailableTransition(CampaignMode.TransitionType transitionType)
        {
            if (!CanDisplayHints()) { return; }
            if (transitionType == CampaignMode.TransitionType.None) { return; }
            DisplayHint($"onavailabletransition.{transitionType}".ToIdentifier());
        }

        public static void OnShowSubInventory(Item item)
        {
            if (item?.Prefab == null) { return; }
            if (item.Prefab.Identifier == "toolbelt")
            {
                IgnoreReminder("toolbelt");
            }
        }

        public static void OnChangeCharacter()
        {
            IgnoreReminder("characterchange");
        }

        public static void OnCharacterUnconscious(Character character)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled) { return; }
            if (character.IsDead) { return; }
            if (character.CharacterHealth != null && character.Vitality < character.CharacterHealth.MinVitality) { return; }
            DisplayHint("oncharacterunconscious".ToIdentifier());
        }

        public static void OnCharacterKilled(Character character)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled) { return; }
            if (GameMain.IsMultiplayer) { return; }
            if (GameMain.GameSession?.CrewManager == null) { return; }
            if (GameMain.GameSession.CrewManager.GetCharacters().None(c => !c.IsDead)) { return; }
            DisplayHint("oncharacterkilled".ToIdentifier());
        }

        private static void OnStartedControlling()
        {
            if (Level.IsLoadedOutpost) { return; }
            if (Character.Controlled?.Info?.Job?.Prefab == null) { return; }
            Identifier hintIdentifier = $"onstartedcontrolling.job.{Character.Controlled.Info.Job.Prefab.Identifier}".ToIdentifier();
            DisplayHint(hintIdentifier,
                icon: Character.Controlled.Info.Job.Prefab.Icon,
                iconColor: Character.Controlled.Info.Job.Prefab.UIColor,
                onDisplay: () =>
                {
                    if (!HintOrders.TryGetValue(hintIdentifier, out var orderInfo)) { return; }
                    var orderPrefab = OrderPrefab.Prefabs[orderInfo.identifier];
                    if (orderPrefab == null) { return; }
                    Item targetEntity = null;
                    ItemComponent targetItem = null;
                    if (orderPrefab.MustSetTarget)
                    {
                        targetEntity = orderPrefab.GetMatchingItems(true, interactableFor: Character.Controlled, orderOption: orderInfo.option).FirstOrDefault();
                        if (targetEntity == null) { return; }
                        targetItem = orderPrefab.GetTargetItemComponent(targetEntity);
                    }
                    var order = new Order(orderPrefab, orderInfo.option, targetEntity, targetItem, orderGiver: Character.Controlled).WithManualPriority(CharacterInfo.HighestManualOrderPriority);
                    GameMain.GameSession.CrewManager.SetCharacterOrder(Character.Controlled, order);
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
            DisplayHint("onautopilotreachedoutpost".ToIdentifier());
        }

        public static void OnStatusEffectApplied(ItemComponent component, ActionType actionType, Character character)
        {
            if (!CanDisplayHints()) { return; }
            if (character != Character.Controlled) { return; }
            // Could make this more generic if there will ever be any other status effect related hints
            if (!(component is Repairable) || actionType != ActionType.OnFailure) { return; }
            DisplayHint("onrepairfailed".ToIdentifier());
        }

        public static void OnActiveOrderAdded(Order order)
        {
            if (!CanDisplayHints()) { return; }
            if (order == null) { return; }

            if (order.Identifier == "reportballastflora" &&
                order.TargetEntity is Hull h &&
                h.Submarine?.TeamID == Character.Controlled.TeamID)
            {
                DisplayHint("onballastflorainfected".ToIdentifier());
            }
        }

        private static void CheckIfDivingGearOutOfOxygen()
        {
            if (!CanDisplayHints()) { return; }
            var divingGear = Character.Controlled.GetEquippedItem("diving", InvSlotType.OuterClothes);
            if (divingGear?.OwnInventory == null) { return; }
            if (divingGear.GetContainedItemConditionPercentage() > 0.0f) { return; }
            DisplayHint("ondivinggearoutofoxygen".ToIdentifier(), onUpdate: () =>
            {
                if (divingGear == null || divingGear.Removed ||
                    Character.Controlled == null || !Character.Controlled.HasEquippedItem(divingGear) ||
                    divingGear.GetContainedItemConditionPercentage() > 0.0f)
                {
                    ActiveHintMessageBox.Close();
                }
            });
        }

        private static void CheckHulls()
        {
            if (!CanDisplayHints()) { return; }
            if (Character.Controlled.CurrentHull == null) { return; }
            if (HumanAIController.IsBallastFloraNoticeable(Character.Controlled, Character.Controlled.CurrentHull))
            {
                if (IsOnFriendlySub() && DisplayHint("onballastflorainfected".ToIdentifier())) { return; }
            }
            foreach (var gap in Character.Controlled.CurrentHull.ConnectedGaps)
            {
                if (gap.ConnectedDoor == null || gap.ConnectedDoor.Impassable) { continue; }
                if (Vector2.DistanceSquared(Character.Controlled.WorldPosition, gap.ConnectedDoor.Item.WorldPosition) > 400 * 400) { continue; }
                if (!gap.IsRoomToRoom)
                {
                    if (!IsWearingDivingSuit()) { continue; }
                    if (Character.Controlled.IsProtectedFromPressure()) { continue; }
                    if (DisplayHint("divingsuitwarning".ToIdentifier(), extendTextTag: false)) { return; }
                    continue;
                }
                foreach (var me in gap.linkedTo)
                {
                    if (me == Character.Controlled.CurrentHull) { continue; }
                    if (!(me is Hull adjacentHull)) { continue; }
                    if (!IsOnFriendlySub()) { continue; }
                    if (IsWearingDivingSuit()) { continue; }
                    if (adjacentHull.LethalPressure > 5.0f && DisplayHint("onadjacenthull.highpressure".ToIdentifier())) { return; }
                    if (adjacentHull.WaterPercentage > 75 && !BallastHulls.Contains(adjacentHull) && DisplayHint("onadjacenthull.highwaterpercentage".ToIdentifier())) { return; }
                }

                static bool IsWearingDivingSuit() => Character.Controlled.GetEquippedItem("deepdiving", InvSlotType.OuterClothes) is Item;
            }

            static bool IsOnFriendlySub() => Character.Controlled.Submarine is Submarine sub && (sub.TeamID == Character.Controlled.TeamID || sub.TeamID == CharacterTeamType.FriendlyNPC);
        }

        private static void CheckReminders()
        {
            if (!CanDisplayHints()) { return; }
            if (Level.Loaded == null) { return; }
            if (GameMain.GameScreen.GameTime < TimeRoundStarted + TimeBeforeReminders) { return; }
            if (GameMain.GameScreen.GameTime < TimeReminderLastDisplayed + ReminderCooldown) { return; }

            string hintIdentifierBase = "reminder";

            if (GameMain.GameSession.GameMode.IsSinglePlayer)
            {
                if (DisplayHint($"{hintIdentifierBase}.characterchange".ToIdentifier()))
                {
                    TimeReminderLastDisplayed = GameMain.GameScreen.GameTime;
                    return;
                }
            }

            if (Level.Loaded.Type != LevelData.LevelType.Outpost)
            {
                if (DisplayHint($"{hintIdentifierBase}.commandinterface".ToIdentifier(),
                        variables: new[] { ("[commandkey]".ToIdentifier(), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Command)) },
                        onUpdate: () =>
                        {
                            if (!CrewManager.IsCommandInterfaceOpen) { return; }
                            ActiveHintMessageBox.Close();
                        }))
                {
                    TimeReminderLastDisplayed = GameMain.GameScreen.GameTime;
                    return;
                }
            }

            if (DisplayHint($"{hintIdentifierBase}.tabmenu".ToIdentifier(),
                    variables: new[] { ("[infotabkey]".ToIdentifier(), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.InfoTab)) },
                    onUpdate: () =>
                    {
                        if (!GameSession.IsTabMenuOpen) { return; }
                        ActiveHintMessageBox.Close();
                    }))
            {
                TimeReminderLastDisplayed = GameMain.GameScreen.GameTime;
                return;
            }

            if (Character.Controlled.Inventory?.GetItemInLimbSlot(InvSlotType.Bag)?.Prefab?.Identifier == "toolbelt")
            {
                if (DisplayHint($"{hintIdentifierBase}.toolbelt".ToIdentifier()))
                {
                    TimeReminderLastDisplayed = GameMain.GameScreen.GameTime;
                    return;
                }
            }
        }

        private static bool DisplayHint(Identifier hintIdentifier, bool extendTextTag = true, (Identifier Tag, LocalizedString Value)[] variables = null, Sprite icon = null, Color? iconColor = null, Action onDisplay = null, Action onUpdate = null)
        {
            if (hintIdentifier == Identifier.Empty) { return false; }
            if (!HintIdentifiers.Contains(hintIdentifier)) { return false; }
            if (IgnoredHints.Instance.Contains(hintIdentifier)) { return false; }
            if (HintsIgnoredThisRound.Contains(hintIdentifier)) { return false; }

            LocalizedString text;
            Identifier textTag = extendTextTag ? $"hint.{hintIdentifier}".ToIdentifier() : hintIdentifier;
            if (variables != null && variables.Length > 0)
            {
                text = TextManager.GetWithVariables(textTag, variables);
            }
            else
            {
                text = TextManager.Get(textTag);
            }

            if (text.IsNullOrEmpty())
            {
#if DEBUG
                DebugConsole.ThrowError($"No hint text found for text tag \"{textTag}\"");
#endif
                return false;
            }

            HintsIgnoredThisRound.Add(hintIdentifier);

            ActiveHintMessageBox = new GUIMessageBox(hintIdentifier, text, icon);
            if (iconColor.HasValue) { ActiveHintMessageBox.IconColor = iconColor.Value; }
            OnUpdate = onUpdate;

            SoundPlayer.PlayUISound(GUISoundType.UIMessage);
            ActiveHintMessageBox.InnerFrame.Flash(color: iconColor ?? Color.Orange, flashDuration: 0.75f);
            onDisplay?.Invoke();

            GameAnalyticsManager.AddDesignEvent($"HintManager:{GameMain.GameSession?.GameMode?.Preset?.Identifier ?? "none".ToIdentifier()}:HintDisplayed:{hintIdentifier}");

            return true;
        }

        public static bool OnDontShowAgain(GUITickBox tickBox)
        {
            IgnoreHint((Identifier)tickBox.UserData, ignore: tickBox.Selected);
            return true;
        }

        private static void IgnoreHint(Identifier hintIdentifier, bool ignore = true)
        {
            if (hintIdentifier.IsEmpty) { return; }
            if (!HintIdentifiers.Contains(hintIdentifier))
            {
#if DEBUG
                DebugConsole.ThrowError($"Tried to ignore a hint not defined in {HintManagerFile}: {hintIdentifier}");
#endif
                return;
            }
            if (ignore)
            {
                IgnoredHints.Instance.Add(hintIdentifier);
            }
            else
            {
                IgnoredHints.Instance.Remove(hintIdentifier);
            }
        }

        private static void IgnoreReminder(string reminderIdentifier)
        {
            HintsIgnoredThisRound.Add($"reminder.{reminderIdentifier}".ToIdentifier());
        }

        public static bool OnDisableHints(GUITickBox tickBox)
        {
            var config = GameSettings.CurrentConfig;
            config.DisableInGameHints = tickBox.Selected;
            GameSettings.SetCurrentConfig(config);
            GameSettings.SaveCurrentConfig();
            return true;
        }

        private static bool CanDisplayHints(bool requireGameScreen = true, bool requireControllingCharacter = true)
        {
            if (HintIdentifiers == null) { return false; }
            if (GameSettings.CurrentConfig.DisableInGameHints) { return false; }
            if (ActiveHintMessageBox != null) { return false; }
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