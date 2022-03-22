using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CrewManager
    {
        const float ConversationIntervalMin = 100.0f;
        const float ConversationIntervalMax = 180.0f;
        const float ConversationIntervalMultiplierMultiplayer = 5.0f;
        private float conversationTimer, conversationLineTimer;
        private readonly List<Pair<Character, string>> pendingConversationLines = new List<Pair<Character, string>>();

        public const int MaxCrewSize = 16;

        private readonly List<CharacterInfo> characterInfos = new List<CharacterInfo>();
        private readonly List<Character> characters = new List<Character>();

        private Character welcomeMessageNPC;

        public List<CharacterInfo> CharacterInfos => characterInfos;

        public bool HasBots { get; set; }

        public List<Pair<Order, float?>> ActiveOrders { get; } = new List<Pair<Order, float?>>();
        public bool IsSinglePlayer { get; private set; }

        public ReadyCheck ActiveReadyCheck;

        public XElement ActiveOrdersElement { get; set; }

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
            var isUnignoreOrder = order.Identifier == "unignorethis";
            var orderPrefab = !isUnignoreOrder ? order.Prefab : Order.GetPrefab("ignorethis");
            Pair<Order, float?> existingOrder = ActiveOrders.Find(o =>
                    o.First.Prefab == orderPrefab && MatchesTarget(o.First.TargetEntity, order.TargetEntity) &&
                    (o.First.TargetType != Order.OrderTargetType.WallSection || o.First.WallSectionIndex == order.WallSectionIndex));

            if (existingOrder != null)
            {
                if (!isUnignoreOrder)
                {
                    existingOrder.Second = fadeOutTime;
                    return false;
                }
                else
                {
                    ActiveOrders.Remove(existingOrder);
                    return true;
                }
            }
            else if (!isUnignoreOrder)
            {
                ActiveOrders.Add(new Pair<Order, float?>(order, fadeOutTime));
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

        public void AddCharacterElements(XElement element)
        {
            foreach (XElement characterElement in element.Elements())
            {
                if (!characterElement.Name.ToString().Equals("character", StringComparison.OrdinalIgnoreCase)) { continue; }
                CharacterInfo characterInfo = new CharacterInfo(characterElement);
#if CLIENT
                if (characterElement.GetAttributeBool("lastcontrolled", false)) { characterInfo.LastControlled = true; }
                characterInfo.CrewListIndex = characterElement.GetAttributeInt("crewlistindex", -1);
#endif
                characterInfos.Add(characterInfo);
                foreach (XElement subElement in characterElement.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "inventory":
                            characterInfo.InventoryData = subElement;
                            break;
                        case "health":
                            characterInfo.HealthData = subElement;
                            break;
                        case "orders":
                            characterInfo.OrderData = subElement;
                            break;
                    }
                }
            }
        }
        
        /// <summary>
        /// Remove info of a selected character. The character will not be visible in any menus or the round summary.
        /// </summary>
        /// <param name="characterInfo"></param>
        public void RemoveCharacterInfo(CharacterInfo characterInfo)
        {
            characterInfos.Remove(characterInfo);
        }
        
        public void AddCharacter(Character character, bool sortCrewList = true)
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
            if (sortCrewList)
            {
                SortCrewList();
            }
            if (character.CurrentOrders != null)
            {
                foreach (var order in character.CurrentOrders)
                {
                    AddCurrentOrderIcon(character, order);
                }
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
        }

        public void AddCharacterInfo(CharacterInfo characterInfo)
        {
            if (characterInfos.Contains(characterInfo))
            {
                DebugConsole.ThrowError("Tried to add the same character info to CrewManager twice.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            characterInfos.Add(characterInfo);
        }

        public void InitRound()
        {
#if CLIENT
            GUIContextMenu.CurrentContextMenu = null;
#endif
            
            characters.Clear();

            List<WayPoint> spawnWaypoints = null;
            List<WayPoint> mainSubWaypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub).ToList();

            if (Level.IsLoadedOutpost && Submarine.Loaded.Any(s => s.Info.Type == SubmarineType.Outpost && (s.Info.OutpostGenerationParams?.SpawnCrewInsideOutpost ?? false)))
            {
                spawnWaypoints = WayPoint.WayPointList.FindAll(wp => 
                    wp.SpawnType == SpawnType.Human &&
                    wp.Submarine == Level.Loaded.StartOutpost && 
                    wp.CurrentHull != null &&
                    wp.CurrentHull.OutpostModuleTags.Contains("airlock"));
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
                if (character.Info != null)
                {
                    if (!character.Info.StartItemsGiven && character.Info.InventoryData != null)
                    {
                        DebugConsole.AddWarning($"Error when initializing a round: character \"{character.Name}\" has not been given their initial items but has saved inventory data. Using the saved inventory data instead of giving the character new items.");
                    }
                    if (character.Info.InventoryData != null)
                    {
                        character.SpawnInventoryItems(character.Inventory, character.Info.InventoryData);
                    }
                    else if (!character.Info.StartItemsGiven)
                    {
                        character.GiveJobItems(mainSubWaypoints[i]);
                    }
                    if (character.Info.HealthData != null)
                    {
                        CharacterInfo.ApplyHealthData(character, character.Info.HealthData);
                    }

                    character.LoadTalents();

                    character.GiveIdCardTags(spawnWaypoints[i]);
                    character.Info.StartItemsGiven = true;
                    if (character.Info.OrderData != null)
                    {
                        character.Info.ApplyOrderData();
                    }
                }
                
                AddCharacter(character, sortCrewList: false);
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

        public void RenameCharacter(CharacterInfo characterInfo, string newName)
        {
            int identifier = characterInfo.GetIdentifierUsingOriginalName();
            var match = characterInfos.FirstOrDefault(ci => ci.GetIdentifierUsingOriginalName() == identifier);
            if (match == null)
            {
                DebugConsole.ThrowError($"Tried to rename an invalid crew member ({identifier})");
                return;
            }
            match.Rename(newName);
            RenameCharacterProjSpecific(match);
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
            foreach (Pair<Order, float?> order in ActiveOrders)
            {
                if (order.Second.HasValue) { order.Second -= deltaTime; }
            }
            ActiveOrders.RemoveAll(o => (o.Second.HasValue && o.Second <= 0.0f) ||
                (o.First.TargetEntity != null && o.First.TargetEntity.Removed));

            UpdateConversations(deltaTime);
            UpdateProjectSpecific(deltaTime);
            ActiveReadyCheck?.Update(deltaTime);
            if (ActiveReadyCheck != null && ActiveReadyCheck.IsFinished)
            {
                ActiveReadyCheck = null;
            }
        }

        #region Dialog

        public void AddConversation(List<Pair<Character, string>> conversationLines)
        {
            if (conversationLines == null || conversationLines.Count == 0) { return; }
            pendingConversationLines.AddRange(conversationLines);
        }

        partial void CreateRandomConversation();

        private void UpdateConversations(float deltaTime)
        {
            if (GameMain.GameSession?.GameMode?.Preset == GameModePreset.TestMode) { return; }
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
                            List<string> dialogFlags = new List<string>() { "OutpostNPC", "EnterOutpost" };
                            if (GameMain.GameSession?.GameMode is CampaignMode campaignMode)
                            {
                                if (campaignMode.Map?.CurrentLocation?.Type?.Identifier.Equals("abandoned", StringComparison.OrdinalIgnoreCase) ?? false)
                                {
                                    if (npc.TeamID == CharacterTeamType.None)
                                    {
                                        dialogFlags.Remove("OutpostNPC");
                                        dialogFlags.Add("Bandit");
                                    }
                                    else if (npc.TeamID == CharacterTeamType.FriendlyNPC)
                                    {
                                        dialogFlags.Remove("OutpostNPC");
                                        dialogFlags.Add("Hostage");
                                    }
                                }
                                else if (campaignMode.Map?.CurrentLocation?.Reputation != null)
                                {
                                    float normalizedReputation = MathUtils.InverseLerp(
                                        campaignMode.Map.CurrentLocation.Reputation.MinReputation,
                                        campaignMode.Map.CurrentLocation.Reputation.MaxReputation,
                                        campaignMode.Map.CurrentLocation.Reputation.Value);
                                    if (normalizedReputation < 0.2f)
                                    {
                                        dialogFlags.Add("LowReputation");
                                    }
                                    else if (normalizedReputation > 0.8f)
                                    {
                                        dialogFlags.Add("HighReputation");
                                    }
                                }
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
                    if (pendingConversationLines[0].First.SpeechImpediment >= 100.0f)
                    {
                        pendingConversationLines.Clear();
                        return;
                    }

                    pendingConversationLines[0].First.Speak(pendingConversationLines[0].Second, null);
                    if (pendingConversationLines.Count > 1)
                    {
                        conversationLineTimer = MathHelper.Clamp(pendingConversationLines[0].Second.Length * 0.1f, 1.0f, 5.0f);
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
            var filteredCharacters = characters.Where(c => controlledCharacter == null || ((includeSelf || c != controlledCharacter) && c.TeamID == controlledCharacter.TeamID));
            if (extraCharacters != null)
            {
                filteredCharacters = filteredCharacters.Union(extraCharacters);
            }
            return filteredCharacters
                    // Prioritize those who are on the same submarine as the controlled character
                    .OrderByDescending(c => Character.Controlled == null || c.Submarine == Character.Controlled.Submarine)
                    // Prioritize those who are already ordered to operate the device
                    .ThenByDescending(c => order.Category == OrderCategory.Operate && c.CurrentOrders.Any(o => o.Order != null && o.Order.Identifier == order.Identifier && o.Order.TargetEntity == order.TargetEntity))
                    // Prioritize those with the appropriate job for the order
                    .ThenByDescending(c => order.HasAppropriateJob(c))
                    // Prioritize those who don't yet have the same order (which allows quick-assigning the order to different characters)
                    .ThenByDescending(c => c.CurrentOrders.None(o => o.Order != null && o.Order.Identifier == order.Identifier))
                    // Prioritize those with the preferred job for the order
                    .ThenByDescending(c => order.HasPreferredJob(c))
                    // Prioritize bots over player-controlled characters
                    .ThenByDescending(c => c.IsBot)
                    // Prioritize those with a lower current objective priority
                    .ThenBy(c => c.AIController is HumanAIController humanAI ? humanAI.ObjectiveManager.CurrentObjective?.Priority : 0)
                    // Prioritize those with a higher order skill level
                    .ThenByDescending(c => c.GetSkillLevel(order.AppropriateSkill));
        }

        partial void UpdateProjectSpecific(float deltaTime);

        private void SaveActiveOrders(XElement parentElement)
        {
            ActiveOrdersElement = new XElement("activeorders");
            // Only save orders with no fade out time (e.g. ignore orders)
            var ordersToSave = new List<OrderInfo>();
            foreach (var activeOrder in ActiveOrders)
            {
                var order = activeOrder?.First;
                if (order == null || activeOrder.Second.HasValue) { continue; }
                ordersToSave.Add(new OrderInfo(order, null, CharacterInfo.HighestManualOrderPriority));
            }
            CharacterInfo.SaveOrders(ActiveOrdersElement, ordersToSave.ToArray());
            parentElement?.Add(ActiveOrdersElement);
        }

        public void LoadActiveOrders()
        {
            if (ActiveOrdersElement == null) { return; }
            foreach (var orderInfo in CharacterInfo.LoadOrders(ActiveOrdersElement))
            {
                IIgnorable ignoreTarget = null;
                if (orderInfo.Order.IsIgnoreOrder)
                {
                    switch (orderInfo.Order.TargetType)
                    {
                        case Order.OrderTargetType.Entity:
                            ignoreTarget = orderInfo.Order.TargetEntity as IIgnorable;
                            break;
                        case Order.OrderTargetType.WallSection when orderInfo.Order.TargetEntity is Structure s && orderInfo.Order.WallSectionIndex.HasValue:
                            ignoreTarget = s.GetSection(orderInfo.Order.WallSectionIndex.Value) as IIgnorable;
                            break;
                        default:
                            DebugConsole.ThrowError("Error loading an ignore order - can't find a proper ignore target");
                            continue;
                    }
                }
                if (orderInfo.Order.TargetEntity == null || (orderInfo.Order.IsIgnoreOrder && ignoreTarget == null))
                {
                    // The order target doesn't exist anymore, just discard the loaded order
                    continue;
                }
                if (ignoreTarget != null)
                {
                    ignoreTarget.OrderedToBeIgnored = true;
                }
                AddOrder(orderInfo.Order, null);
            }
        }
    }
}
