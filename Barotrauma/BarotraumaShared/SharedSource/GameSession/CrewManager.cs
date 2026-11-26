using Barotrauma.Extensions;
#if CLIENT
using Barotrauma.Tutorials;
#endif
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{

    /// <summary>
    /// Responsible for keeping track of the characters in the player crew, saving and loading their orders, managing the crew list UI
    /// </summary>
    public partial class CrewManager
    {
        const float ConversationIntervalMin = 100.0f;
        const float ConversationIntervalMax = 180.0f;
        const float ConversationIntervalMultiplierMultiplayer = 5.0f;
        private float conversationTimer, conversationLineTimer;
        private readonly List<(Character speaker, string line)> pendingConversationLines = new List<(Character speaker, string line)>();

        public const int MaxCrewSize = 16;

        private readonly List<CharacterInfo> characterInfos = new List<CharacterInfo>();
        private readonly List<Character> characters = new List<Character>();
        
        private readonly List<CharacterInfo> reserveBench = new List<CharacterInfo>();

        /// <summary>
        /// NOTE: When called from client code, this method will include players, but NOT when called from server code.
        /// CrewManager is used for dealing with things relevant to AI characters, like hiring, firing, renaming, and the reserve bench.
        /// In single player/client code, player CharacterInfos are still stored in it but only for displaying crew listings in the GUI correctly.
        /// Use <see cref="GetSessionCrewCharacters"/> to get all the characters regardless if they're player or AI controlled.
        /// </summary>
        /// <param name="includeReserveBench">Should characters on the reserve be included? Defaults to false.</param>
        public IEnumerable<CharacterInfo> GetCharacterInfos(bool includeReserveBench = false)
        {
            if (includeReserveBench)
            {
                return characterInfos.Concat(reserveBench);
            }
            return characterInfos;
        }
        
        public IEnumerable<CharacterInfo> GetReserveBenchInfos()
        {
            return reserveBench;
        }

        private Character welcomeMessageNPC;

        public bool HasBots { get; set; }

        public class ActiveOrder
        {
            public readonly Order Order;
            public float? FadeOutTime;
            public ActiveOrder(Order order, float? fadeOutTime)
            {
                Order = order;
                FadeOutTime = fadeOutTime;
            }
        }
        public List<ActiveOrder> ActiveOrders { get; } = new List<ActiveOrder>();
        public bool IsSinglePlayer { get; private set; }

        public ReadyCheck ActiveReadyCheck;

        public CrewManager(bool isSinglePlayer)
        {
            IsSinglePlayer = isSinglePlayer;
            conversationTimer = 5.0f;
            InitProjectSpecific();
        }

        partial void InitProjectSpecific();

        public bool AddOrder(Order order, float? fadeOutTime)
        {
            if (order.TargetEntity == null)
            {
                string message = $"Attempted to add a \"{order.Name}\" order with no target entity to CrewManager!\n{Environment.StackTrace.CleanupStackTrace()}";
                DebugConsole.AddWarning(message);
                GameAnalyticsManager.AddErrorEventOnce("CrewManager.AddOrder:OrderTargetEntityNull", GameAnalyticsManager.ErrorSeverity.Error, message);
                return false;
            }

            // Ignore orders work a bit differently since the "unignore" order counters the "ignore" order
            var isUnignoreOrder = order.Identifier == Tags.UnignoreThis;
            var isIgnoreOrder = order.Identifier == Tags.IgnoreThis;
            var orderPrefab = !isUnignoreOrder ? order.Prefab : OrderPrefab.Prefabs[Tags.IgnoreThis];
            ActiveOrder existingOrder = ActiveOrders.Find(o =>
                    o.Order.Prefab == orderPrefab && MatchesTarget(o.Order.TargetEntity, order.TargetEntity) &&
                    (o.Order.TargetType != Order.OrderTargetType.WallSection || o.Order.WallSectionIndex == order.WallSectionIndex));

            if (existingOrder != null)
            {
                if (!isUnignoreOrder)
                {
                    existingOrder.FadeOutTime = fadeOutTime;
                    return false;
                }
                else
                {
                    ActiveOrders.Remove(existingOrder);
                    if (isIgnoreOrder && order.TargetEntity is Item targetItem)
                    {
                        foreach (var stackedItem in targetItem.GetStackedItems())
                        {
                            ActiveOrders.RemoveAll(o => o.Order.Prefab == orderPrefab && o.Order.TargetEntity == stackedItem);
                            stackedItem.OrderedToBeIgnored = false;
                        }
                    }
                    return true;
                }
            }
            else if (!isUnignoreOrder)
            {
                if (order.IsDeconstructOrder)
                {
                    if (order.TargetEntity is Item item)
                    {
                        if (order.Identifier == Tags.DeconstructThis)
                        {
                            foreach (var stackedItem in item.GetStackedItems())
                            {
                                Item.DeconstructItems.Add(stackedItem);
                            }
#if CLIENT
                            HintManager.OnItemMarkedForDeconstruction(order.OrderGiver);
#endif
                        }
                        else
                        {
                            foreach (var stackedItem in item.GetStackedItems())
                            {
                                Item.DeconstructItems.Remove(stackedItem);
                            }
                        }
                    }
                }
                if (isIgnoreOrder && order.TargetEntity is Item targetItem)
                {
                    foreach (var stackedItem in targetItem.GetStackedItems())
                    {
                        ActiveOrders.Add(new ActiveOrder(order.WithTargetEntity(stackedItem), fadeOutTime));
                        stackedItem.OrderedToBeIgnored = true;
                    }
                }
                else
                {
                    ActiveOrders.Add(new ActiveOrder(order, fadeOutTime));
                }
#if CLIENT
                HintManager.OnActiveOrderAdded(order);
#endif
                return true;
            }

            static bool MatchesTarget(Entity existingTarget, Entity newTarget)
            {
                if (existingTarget == newTarget) { return true; }
                if (existingTarget is Hull existingHullTarget && newTarget is Hull newHullTarget)
                {
                    return existingHullTarget.linkedTo.Contains(newHullTarget);
                }
                return false;
            }

            return false;
        }

        public void LoadCharacterElements(XElement element)
        {
            foreach (var characterElement in element.Elements())
            {
                if (!characterElement.Name.ToString().Equals("character", StringComparison.OrdinalIgnoreCase)) { continue; }
                CharacterInfo characterInfo = new CharacterInfo(new ContentXElement(contentPackage: null, characterElement));
                if (characterElement.GetAttributeBool(nameof(CharacterInfo.IsOnReserveBench), false))
                {
                    reserveBench.Add(characterInfo);
                }
                else
                {
                    characterInfos.Add(characterInfo);
                }
            }
        }
        
        /// <summary>
        /// Remove info of a selected character. The character will not be visible in any menus or the round summary.
        /// </summary>
        /// <param name="characterInfo"></param>
        public void RemoveCharacterInfo(CharacterInfo characterInfo)
        {
            if (characterInfo is { IsOnReserveBench: true })
            {
                reserveBench.Remove(characterInfo);
            }
            characterInfos.Remove(characterInfo);
#if CLIENT
            GameMain.GameSession?.DeathPrompt?.UpdateBotList();
#endif
        }

        /// <param name="changeItemTeams"><see cref="ChangeItemTeam"/> will be called on the character, changing the team of all items in their inventory (e.g. making their ID cards work in the team's sub and headsets communicate with their team).</param>
        public void AddCharacter(Character character, bool changeItemTeams = false, bool createNetworkEvent = false)
        {
            if (character.Removed)
            {
                DebugConsole.ThrowError("Tried to add a removed character to CrewManager!\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }
            if (character.IsDead)
            {
                DebugConsole.ThrowError("Tried to add a dead character to CrewManager!\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }
            if (character.Info == null)
            {
                if (character.Prefab.ContentPackage == GameMain.VanillaContent)
                {
                    DebugConsole.ThrowError($"Added a character with no {nameof(CharacterInfo)} to the crew." + Environment.StackTrace.CleanupStackTrace());
                }
                else
                {
                    DebugConsole.ThrowError($"Added add a character with no {nameof(CharacterInfo)} to the crew. This may lead to issues: consider adding {nameof(CharacterPrefab.HasCharacterInfo)}=\"True\" to the character config.");
                }
            }

            if (!characters.Contains(character))
            {
                characters.Add(character);
            }
            if (!characterInfos.Contains(character.Info))
            {
                characterInfos.Add(character.Info);
            }
#if CLIENT
            var characterComponent = AddCharacterToCrewList(character);
            if (character.CurrentOrders != null)
            {
                foreach (var order in character.CurrentOrders)
                {
                    AddCurrentOrderIcon(character, order);
                }
            }
            
            foreach (var crewChangedHook in PluginData.GetHooks<PluginCrewChangedDelegate>())
            {
                crewChangedHook(character, isAdded: true);
            }
#endif
            if (character.AIController is HumanAIController humanAI)
            {
                var idleObjective = humanAI.ObjectiveManager.GetObjective<AIObjectiveIdle>();
                if (idleObjective != null)
                {
                    idleObjective.Behavior = character.Info.Job.Prefab.IdleBehavior;
                }
            }

            if (changeItemTeams)
            {
                ChangeItemTeam(character, Submarine.MainSub ?? Submarine.Loaded.FirstOrDefault(s => s.TeamID == character.TeamID), allowStealing: true);
            }
            if (createNetworkEvent && GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                GameMain.NetworkMember.CreateEntityEvent(character, new Character.AddToCrewEventData(character.TeamID, character.Inventory.FindAllItems(recursive: true)));                
            }
        }

        /// <summary>
        /// Changes the team of all the items in the character's inventory to the character's current team.
        /// Thus, callers must change the character's team before calling this.
        /// </summary>
        /// <param name="sub">The submarine whose waypoints are used for <see cref="Character.GiveIdCardTags"/>.</param>
        public static void ChangeItemTeam(Character character, Submarine sub, bool allowStealing)
        {
            foreach (Item item in character.Inventory.FindAllItems(recursive: true))
            {
                item.AllowStealing = allowStealing;
                ApplyTeamChangeToItem(item, character.TeamID);
            }
            WayPoint subWaypoint =
                WayPoint.WayPointList.Find(wp => wp.Submarine == sub && wp.SpawnType == SpawnType.Human && wp.AssignedJob == character.Info?.Job?.Prefab) ??
                WayPoint.WayPointList.Find(wp => wp.Submarine == sub && wp.SpawnType == SpawnType.Human);
            if (subWaypoint != null)
            {
                character.GiveIdCardTags(subWaypoint, createNetworkEvent: true);
            }
        }

        /// <summary>
        /// Updates an item (ie. its wifi and ID card components) to the given team.
        /// </summary>
        public static void ApplyTeamChangeToItem(Item item, CharacterTeamType teamID)
        {
            if (item.GetComponent<Items.Components.WifiComponent>() is { } wifiComponent)
            {
                wifiComponent.TeamID = teamID;
            }
            if (item.GetComponent<Items.Components.IdCard>() is { } idCard)
            {
                idCard.TeamID = teamID;
                idCard.SubmarineSpecificID = 0;
            }
        }

        public bool IsFired(Character character)
        {
            return !GetCharacterInfos().Contains(character.Info);
        }

        /// <summary>
        /// Remove the character from the crew (and crew menus).
        /// </summary>
        /// <param name="character">The character to remove</param>
        /// <param name="removeInfo">If the character info is also removed, the character will not be visible in the round summary.</param>
        /// <param name="changeItemTeams"><see cref="ChangeItemTeam"/> will be called on the character, changing the team of all items in their inventory (e.g. making their ID cards work in the team's sub and headsets communicate with their team).</param>
        public void RemoveCharacter(Character character, bool removeInfo = false, bool resetCrewListIndex = true, bool changeItemTeams = false, bool createNetworkEvent = false)
        {
            if (character == null)
            {
                DebugConsole.ThrowError("Tried to remove a null character from CrewManager.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }
            characters.Remove(character);
            if (removeInfo)
            {
                characterInfos.Remove(character.Info);
#if CLIENT
                RemoveCharacterFromCrewList(character);
#endif
            }
#if CLIENT
            if (resetCrewListIndex)
            {
                ResetCrewListIndex(character);
            }
            
            foreach (var crewChangedHook in PluginData.GetHooks<PluginCrewChangedDelegate>())
            {
                crewChangedHook(character, isAdded: false);
            }
#endif
            if (changeItemTeams)
            {
                ChangeItemTeam(character, Submarine.MainSub ?? Submarine.Loaded.FirstOrDefault(s => s.TeamID == character.TeamID), allowStealing: false);
            }
            if (createNetworkEvent)
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    GameMain.NetworkMember.CreateEntityEvent(character, new Character.RemoveFromCrewEventData(character.TeamID, character.Inventory.FindAllItems(recursive: true)));
                }
            }            
        }

        public void AddCharacterInfo(CharacterInfo characterInfo)
        {
            if (GameMain.GameSession?.Campaign is MultiPlayerCampaign)
            {
                Debug.Assert(characterInfo.BotStatus == BotStatus.ActiveService);
                if (characterInfo.BotStatus != BotStatus.ActiveService)
                {
                    DebugConsole.ThrowError($"CrewManager.AddCharacterInfo called on a bot ({characterInfo.DisplayName}) with the wrong status ({characterInfo.BotStatus})");
                }
            }
            
            if (characterInfos.Contains(characterInfo))
            {
                DebugConsole.ThrowError("Tried to add the same character info to CrewManager twice.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            characterInfos.Add(characterInfo);
#if CLIENT
            GameMain.GameSession?.DeathPrompt?.UpdateBotList();
#endif
        }

        public void ClearCharacterInfos()
        {
            characterInfos.Clear();
            reserveBench.Clear();
        }

        public void InitRound()
        {
#if CLIENT
            GUIContextMenu.CurrentContextMenu = null;
#endif
            
            characters.Clear();

            List<WayPoint> spawnWaypoints = null;
            List<WayPoint> mainSubWaypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub).ToList();
            
            if (Level.Loaded != null && Level.Loaded.ShouldSpawnCrewInsideOutpost(CharacterTeamType.Team1, isInitialSpawn: true))
            {
                spawnWaypoints = GetOutpostSpawnpoints(CharacterTeamType.Team1).ToList();
                while (spawnWaypoints.Count > characterInfos.Count)
                {
                    spawnWaypoints.RemoveAt(Rand.Int(spawnWaypoints.Count));
                }
                while (spawnWaypoints.Any() && spawnWaypoints.Count < characterInfos.Count)
                {
                    spawnWaypoints.Add(spawnWaypoints[Rand.Int(spawnWaypoints.Count)]);
                }
            }
            if (spawnWaypoints == null || !spawnWaypoints.Any())
            {
                spawnWaypoints = mainSubWaypoints;
            }

            System.Diagnostics.Debug.Assert(spawnWaypoints.Count == mainSubWaypoints.Count);

            for (int i = 0; i < spawnWaypoints.Count; i++)
            {
                var info = characterInfos[i];
                info.TeamID = CharacterTeamType.Team1;
                Character character = Character.Create(info, spawnWaypoints[i].WorldPosition, info.Name);
                InitializeCharacter(character, mainSubWaypoints[i], spawnWaypoints[i], createNetworkEvent: true);

                AddCharacter(character);
#if CLIENT
                if (IsSinglePlayer && (Character.Controlled == null || character.Info.LastControlled)) { Character.Controlled = character; }
#endif
            }

#if CLIENT
            if (IsSinglePlayer) { SortCrewList(); }
#endif

            //longer delay in multiplayer to prevent the server from triggering NPC conversations while the players are still loading the round
            conversationTimer = IsSinglePlayer ? Rand.Range(5.0f, 10.0f) : Rand.Range(45.0f, 60.0f);
        }

        /// <summary>
        /// Returns the potential crew spawnpositions for the crew in the loaded outpost
        /// </summary>
        public IEnumerable<WayPoint> GetOutpostSpawnpoints(CharacterTeamType team)
        {
            if (Level.Loaded.StartOutpost == null) { return Enumerable.Empty<WayPoint>(); }

            var outpostWaypoints = WayPoint.WayPointList.Where(wp =>
                    wp.SpawnType == SpawnType.Human &&
                    wp.Submarine == Level.Loaded.StartOutpost &&
                    wp.CurrentHull != null);
            var airlockWaypoints = outpostWaypoints.Where(wp =>
                    wp.CurrentHull.OutpostModuleTags.Contains(Tags.Airlock) || wp.Tags.Contains(Tags.Airlock));

            //If we are spawning in another team's outpost, prefer airlock waypoints if they're available
            if (team != Level.Loaded.StartOutpost.TeamID && airlockWaypoints.Any())
            {
                return airlockWaypoints;
            }
            else
            {
                //no airlock waypoints -> return waypoints with no specific tags
                //(tagged ones might be some special spawnpoints in e.g. jail cells)
                return outpostWaypoints.Where(wp => wp.Tags.None());
            }
        }

        public static void InitializeCharacter(Character character, WayPoint mainSubWaypoint, WayPoint spawnWaypoint, Rand.RandSync randSync = Rand.RandSync.Unsynced, bool createNetworkEvent = false)
        {
            if (character.Info != null)
            {
                if (!character.Info.StartItemsGiven && character.Info.InventoryData != null)
                {
                    DebugConsole.AddWarning($"Error when initializing a round: character \"{character.Name}\" has not been given their initial items but has saved inventory data. Using the saved inventory data instead of giving the character new items.");
                }

                if (character.Info.HealthData != null)
                {
                    CharacterInfo.ApplyHealthData(character, character.Info.HealthData);
                }

                if (character.Info.InventoryData != null)
                {
                    character.SpawnInventoryItems(character.Inventory, character.Info.InventoryData.FromPackage(null), createNetworkEvent: createNetworkEvent);
                }
                else if (!character.Info.StartItemsGiven)
                {
                    character.GiveJobItems(isPvPMode: GameMain.GameSession?.GameMode is PvPMode, mainSubWaypoint, randSync, createNetworkEvent: createNetworkEvent);
                    foreach (Item item in character.Inventory.AllItems)
                    {
                        //if the character is loaded from a human prefab with preconfigured items, its ID card gets assigned to the sub it spawns in
                        //we don't want that in this case, the crew's cards shouldn't be submarine-specific
                        var idCard = item.GetComponent<Items.Components.IdCard>();
                        if (idCard != null)
                        {
                            idCard.SubmarineSpecificID = 0;
                        }
                    }
                }

                character.LoadTalents();

                character.GiveIdCardTags(mainSubWaypoint);
                character.GiveIdCardTags(spawnWaypoint);
                character.Info.StartItemsGiven = true;
                if (character.Info.OrderData != null)
                {
                    character.Info.ApplyOrderData();
                }
            }
        }

        public void RenameCharacter(CharacterInfo characterInfo, string newName)
        {
            characterInfo.Rename(newName);
            RenameCharacterProjSpecific(characterInfo);
        }

        partial void RenameCharacterProjSpecific(CharacterInfo characterInfo);

        public void FireCharacter(CharacterInfo characterInfo)
        {
            RemoveCharacterInfo(characterInfo);
        }

        public void ClearCurrentOrders()
        {
            foreach (var characterInfo in characterInfos)
            {
                characterInfo?.ClearCurrentOrders();
            }
        }

        public void Update(float deltaTime)
        {
            foreach (ActiveOrder order in ActiveOrders)
            {
                if (order.FadeOutTime.HasValue) { order.FadeOutTime -= deltaTime; }
            }
            ActiveOrders.RemoveAll(o => (o.FadeOutTime.HasValue && o.FadeOutTime <= 0.0f) ||
                (o.Order.TargetEntity != null && o.Order.TargetEntity.Removed));

            UpdateConversations(deltaTime);
            UpdateProjectSpecific(deltaTime);
            ActiveReadyCheck?.Update(deltaTime);
            if (ActiveReadyCheck != null && ActiveReadyCheck.IsFinished)
            {
                ActiveReadyCheck = null;
            }
        }

        #region Dialog

        public void AddConversation(List<(Character speaker, string line)> conversationLines)
        {
            if (conversationLines == null || conversationLines.Count == 0) { return; }
            pendingConversationLines.AddRange(conversationLines);
        }

        partial void CreateRandomConversation();

        private void UpdateConversations(float deltaTime)
        {
            if (GameMain.GameSession?.GameMode?.Preset == GameModePreset.TestMode) { return; }
#if CLIENT
            if (GameMain.GameSession?.GameMode is TutorialMode tutorialMode && tutorialMode.Tutorial is Tutorial tutorial && tutorial.TutorialPrefab.DisableBotConversations) { return; }
#endif
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.ServerSettings.DisableBotConversations) { return; }

            conversationTimer -= deltaTime;
            if (conversationTimer <= 0.0f)
            {
                CreateRandomConversation();
                conversationTimer = Rand.Range(ConversationIntervalMin, ConversationIntervalMax);
                if (GameMain.NetworkMember != null)
                {
                    conversationTimer *= ConversationIntervalMultiplierMultiplayer;
                }
            }

            if (welcomeMessageNPC == null)
            {
                foreach (Character npc in Character.CharacterList)
                {
                    if ((npc.TeamID != CharacterTeamType.FriendlyNPC && npc.TeamID != CharacterTeamType.None) || npc.CurrentHull == null || npc.IsIncapacitated) { continue; }   
                    if (npc.AIController is HumanAIController humanAI && (humanAI.ObjectiveManager.IsCurrentObjective<AIObjectiveFindSafety>() || humanAI.ObjectiveManager.IsCurrentObjective<AIObjectiveCombat>()))
                    {
                        continue;
                    }
                    foreach (Character player in Character.CharacterList)
                    {
                        if (player.TeamID != npc.TeamID && !player.IsIncapacitated && player.CurrentHull == npc.CurrentHull)
                        {
                            List<Character> availableSpeakers = new List<Character>() { npc, player };
                            List<Identifier> dialogFlags = new List<Identifier>() { "OutpostNPC".ToIdentifier(), "EnterOutpost".ToIdentifier() };
                            if (npc.HumanPrefab != null)
                            {
                                foreach (var tag in npc.HumanPrefab.GetTags())
                                {
                                    dialogFlags.Add(tag);
                                }
                            }
                            if (GameMain.GameSession?.GameMode is CampaignMode campaignMode)
                            {
                                if (campaignMode.Map?.CurrentLocation?.Type?.Identifier == "abandoned")
                                {
                                    dialogFlags.Remove("OutpostNPC".ToIdentifier());
                                }
                                else if (campaignMode.Map?.CurrentLocation?.Reputation != null)
                                {
                                    float normalizedReputation = MathUtils.InverseLerp(
                                        campaignMode.Map.CurrentLocation.Reputation.MinReputation,
                                        campaignMode.Map.CurrentLocation.Reputation.MaxReputation,
                                        campaignMode.Map.CurrentLocation.Reputation.Value);
                                    if (normalizedReputation < 0.2f)
                                    {
                                        dialogFlags.Add("LowReputation".ToIdentifier());
                                    }
                                    else if (normalizedReputation > 0.8f)
                                    {
                                        dialogFlags.Add("HighReputation".ToIdentifier());
                                    }
                                }
                            }
                            foreach (var action in PluginData.GetHooks<PluginModifyRequiredNPCConversationFlags>())
                            {
                                action(dialogFlags);
                            }
                            pendingConversationLines.AddRange(NPCConversation.CreateRandom(availableSpeakers, dialogFlags));
                            welcomeMessageNPC = npc;
                            break;
                        }
                    }
                    if (welcomeMessageNPC != null) { break; }
                }
            }
            else if (welcomeMessageNPC.Removed)
            {
                welcomeMessageNPC = null;
            }

            if (pendingConversationLines.Count > 0)
            {
                conversationLineTimer -= deltaTime;
                if (conversationLineTimer <= 0.0f)
                {
                    //speaker of the next line can't speak, interrupt the conversation
                    if (pendingConversationLines[0].speaker.SpeechImpediment >= 100.0f)
                    {
                        pendingConversationLines.Clear();
                        return;
                    }

                    pendingConversationLines[0].speaker.Speak(pendingConversationLines[0].line, null);
                    if (pendingConversationLines.Count > 1)
                    {
                        conversationLineTimer = MathHelper.Clamp(pendingConversationLines[0].line.Length * 0.1f, 1.0f, 5.0f);
                    }
                    pendingConversationLines.RemoveAt(0);
                }
            }
        }

#endregion

        public static Character GetCharacterForQuickAssignment(Order order, Character controlledCharacter, IEnumerable<Character> characters, bool includeSelf = false)
        {
            bool isControlledCharacterNull = controlledCharacter == null;
#if !DEBUG
            if (isControlledCharacterNull) { return null; }
#endif
            if (order.Category == OrderCategory.Operate && HumanAIController.IsItemTargetedBySomeone(order.TargetItemComponent, controlledCharacter != null ? controlledCharacter.TeamID : CharacterTeamType.Team1, out Character operatingCharacter) &&
                (isControlledCharacterNull || operatingCharacter.CanHearCharacter(controlledCharacter)))
            {
                return operatingCharacter;
            }
            return GetCharactersSortedForOrder(order, characters, controlledCharacter, includeSelf).FirstOrDefault(c => isControlledCharacterNull || c.CanHearCharacter(controlledCharacter)) ?? controlledCharacter;
        }

        public static IEnumerable<Character> GetCharactersSortedForOrder(Order order, IEnumerable<Character> characters, Character controlledCharacter, bool includeSelf, IEnumerable<Character> extraCharacters = null)
        {
            var filteredCharacters = characters.Where(c => c.Info != null && (controlledCharacter == null || ((includeSelf || c != controlledCharacter) && c.TeamID == controlledCharacter.TeamID)));
            if (extraCharacters != null)
            {
                filteredCharacters = filteredCharacters.Union(extraCharacters);
            }
            return filteredCharacters
                    // Prioritize those who are on the same submarine as the controlled character
                    .OrderByDescending(c => Character.Controlled == null || c.Submarine == Character.Controlled.Submarine)
                    // Prioritize those who are already ordered to operate the device
                    .ThenByDescending(c
                        => order.Category == OrderCategory.Operate
                           && c.CurrentOrders.Any(o
                               => o != null
                                  && o.Identifier == order.Identifier
                                  && o.TargetEntity == order.TargetEntity))
                    // Prioritize those with the appropriate job for the order
                    .ThenByDescending(order.HasAppropriateJob)
                    // Prioritize those who don't yet have the same order (which allows quick-assigning the order to different characters)
                    .ThenByDescending(c => c.CurrentOrders.None(o => o != null && o.Identifier == order.Identifier))
                    // Prioritize those with the preferred job for the order
                    .ThenByDescending(order.HasPreferredJob)
                    // Prioritize bots over player-controlled characters
                    .ThenByDescending(c => c.IsBot)
                    // Prioritize those with a lower current objective priority
                    .ThenBy(c => c.AIController is HumanAIController humanAI ? humanAI.ObjectiveManager.CurrentObjective?.Priority : 0)
                    // Prioritize those with a higher order skill level
                    .ThenByDescending(c => c.GetSkillLevel(order.AppropriateSkill));
        }

        partial void UpdateProjectSpecific(float deltaTime);

        /// <summary>
        /// Restores the crew-wide orders (e.g. ignore orders) after loading a campaign save. There's no corresponding save method, since the 
        /// ignored status is saved on the entities themselves (<see cref="Item.OrderedToBeIgnored"/>, <see cref="WallSection.OrderedToBeIgnored"/>).
        /// Character-specific orders are saved and loaded separately in <see cref="CharacterInfo.SaveOrderData()"/>.
        /// </summary>
        /// <param name="legacyElement">Crew-wide orders saved by older versions in the campaign save, if any.</param>
        public void LoadCrewWideOrders(XElement legacyElement)
        {
            if (legacyElement != null)
            {
                // Load crew-wide orders saved by older versions (before the ignored status was saved on the entities themselves)
                LoadLegacyCrewWideOrders(legacyElement);
            }
            RestoreIgnoreOrders();
        }

        /// <summary>
        /// Recreate the ignore orders for loaded entities, so they're displayed and can be unignored.
        /// </summary>
        private void RestoreIgnoreOrders()
        {
            if (!OrderPrefab.Prefabs.TryGet(Tags.IgnoreThis, out OrderPrefab ignorePrefab)) { return; }
            foreach (Item item in Item.ItemList.ToList())
            {
                if (!item.OrderedToBeIgnored || item.Removed) 
                { 
                    continue; 
                }

                AddOrder(new Order(ignorePrefab, item, targetItem: null), null);
            }
            foreach (Structure wall in Structure.WallList)
            {
                for (int i = 0; i < wall.SectionCount; i++)
                {
                    if (!wall.Sections[i].OrderedToBeIgnored) 
                    { 
                        continue; 
                    }

                    AddOrder(new Order(ignorePrefab, wall, i), null);
                }
            }
        }

        private void LoadLegacyCrewWideOrders(XElement element)
        {
            foreach (var orderInfo in CharacterInfo.LoadOrders(element))
            {
                IIgnorable ignoreTarget = null;
                if (orderInfo.IsIgnoreOrder)
                {
                    switch (orderInfo.TargetType)
                    {
                        case Order.OrderTargetType.Entity:
                            ignoreTarget = orderInfo.TargetEntity as IIgnorable;
                            break;
                        case Order.OrderTargetType.WallSection when orderInfo.TargetEntity is Structure s && orderInfo.WallSectionIndex.HasValue:
                            ignoreTarget = s.GetSection(orderInfo.WallSectionIndex.Value);
                            break;
                        default:
                            DebugConsole.ThrowError("Error loading an ignore order - can't find a proper ignore target");
                            continue;
                    }
                }
                if (orderInfo.TargetEntity == null || (orderInfo.IsIgnoreOrder && ignoreTarget == null))
                {
                    // The order target doesn't exist anymore, just discard the loaded order
                    continue;
                }
                if (ignoreTarget != null)
                {
                    ignoreTarget.OrderedToBeIgnored = true;
                }
                AddOrder(orderInfo, null);
            }
        }
    }
}
