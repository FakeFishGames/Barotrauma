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
                DebugConsole.ThrowError("Attempted to add an order with no target entity to CrewManager!\n" + Environment.StackTrace.CleanupStackTrace());
                return false;
            }

            Pair<Order, float?> existingOrder =
                ActiveOrders.Find(o => o.First.Prefab == order.Prefab && o.First.TargetEntity == order.TargetEntity &&
                                       (o.First.TargetType != Order.OrderTargetType.WallSection || o.First.WallSectionIndex == order.WallSectionIndex));
            
            if (existingOrder != null)
            {
                existingOrder.Second = fadeOutTime;
                return false;
            }
            else
            {
                ActiveOrders.Add(new Pair<Order, float?>(order, fadeOutTime));
                return true;
            }
        }

        public void AddCharacterElements(XElement element)
        {
            foreach (XElement characterElement in element.Elements())
            {
                if (!characterElement.Name.ToString().Equals("character", StringComparison.OrdinalIgnoreCase)) { continue; }

                CharacterInfo characterInfo = new CharacterInfo(characterElement);
#if CLIENT
                if (characterElement.GetAttributeBool("lastcontrolled", false)) { characterInfo.LastControlled = true; }
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
        
        public void AddCharacter(Character character)
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
            AddCharacterToCrewList(character);
            AddCurrentOrderIcon(character, character.CurrentOrder, character.CurrentOrderOption);
#endif
            var idleObjective = character.AIController?.ObjectiveManager?.GetObjective<AIObjectiveIdle>();
            if (idleObjective != null)
            {
                idleObjective.Behavior = character.Info.Job.Prefab.IdleBehavior;
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
            characters.Clear();

            List<WayPoint> spawnWaypoints = null;
            List<WayPoint> mainSubWaypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub).ToList();

            if (Level.IsLoadedOutpost)
            {
                spawnWaypoints = WayPoint.WayPointList.FindAll(wp => 
                    wp.SpawnType == SpawnType.Human &&
                    wp.Submarine == Level.Loaded.StartOutpost && 
                    wp.CurrentHull?.OutpostModuleTags != null && 
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
                info.TeamID = Character.TeamType.Team1;
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
                        character.Info.ApplyHealthData(character, character.Info.HealthData);
                    }
                    character.GiveIdCardTags(spawnWaypoints[i]);
                    character.Info.StartItemsGiven = true;
                }
                
                AddCharacter(character);
#if CLIENT
                if (IsSinglePlayer && (Character.Controlled == null || character.Info.LastControlled)) { Character.Controlled = character; }
#endif
            }

            //longer delay in multiplayer to prevent the server from triggering NPC conversations while the players are still loading the round
            conversationTimer = IsSinglePlayer ? Rand.Range(5.0f, 10.0f) : Rand.Range(45.0f, 60.0f);
        }

        public void FireCharacter(CharacterInfo characterInfo)
        {
            RemoveCharacterInfo(characterInfo);
        }

        public void Update(float deltaTime)
        {
            foreach (Pair<Order, float?> order in ActiveOrders)
            {
                if (order.Second.HasValue) { order.Second -= deltaTime; }
            }
            ActiveOrders.RemoveAll(o => o.Second.HasValue && o.Second <= 0.0f);

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
                    if (npc.TeamID != Character.TeamType.FriendlyNPC || npc.CurrentHull == null || npc.IsIncapacitated) { continue; }   
                    if (npc.AIController?.ObjectiveManager != null && (npc.AIController.ObjectiveManager.IsCurrentObjective<AIObjectiveFindSafety>() || npc.AIController.ObjectiveManager.IsCurrentObjective<AIObjectiveCombat>()))
                    {
                        continue;
                    }
                    foreach (Character player in Character.CharacterList)
                    {
                        if (player.TeamID != npc.TeamID && !player.IsIncapacitated && player.CurrentHull == npc.CurrentHull)
                        {
                            List<Character> availableSpeakers = new List<Character>() { npc, player };
                            List<string> dialogFlags = new List<string>() { "OutpostNPC", "EnterOutpost" };
                            if (GameMain.GameSession?.GameMode is CampaignMode campaignMode && campaignMode.Map?.CurrentLocation?.Reputation != null)
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

        partial void UpdateProjectSpecific(float deltaTime);
    }
}
