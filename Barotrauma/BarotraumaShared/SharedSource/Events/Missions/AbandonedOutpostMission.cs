using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class AbandonedOutpostMission : Mission
    {
        private readonly XElement characterConfig;

        protected readonly List<Character> characters = new List<Character>();
        private readonly Dictionary<Character, List<Item>> characterItems = new Dictionary<Character, List<Item>>();
        protected readonly HashSet<Character> requireKill = new HashSet<Character>();
        protected readonly HashSet<Character> requireRescue = new HashSet<Character>();

        private readonly string itemTag;
        private readonly XElement itemConfig;
        private readonly List<Item> items = new List<Item>();

        protected const int HostagesKilledState = 5;

        private readonly LocalizedString hostagesKilledMessage;

        private const float EndDelay = 5.0f;
        private float endTimer;

        private bool allowOrderingRescuees;

        public override bool AllowRespawn => false;

        public override bool AllowUndocking
        {
            get 
            {
                if (GameMain.GameSession.GameMode is CampaignMode) { return true; }
                return state > 0;
            }
        }

        public override IEnumerable<(LocalizedString Label, Vector2 Position)> SonarLabels
        {
            get
            {
                if (State == 0)
                {
                    return Targets.Select(t => (Prefab.SonarLabel, t.WorldPosition));
                }
                else
                {
                    return Enumerable.Empty<(LocalizedString Label, Vector2 Position)>();
                }
            }
        }

        private IEnumerable<Entity> Targets
        {
            get
            {
                if (State > 0)
                {
                    return Enumerable.Empty<Entity>();
                }
                else
                {
                    if (items.Any())
                    {
                        return items.Where(it => !it.Removed && it.Condition > 0.0f).Cast<Entity>().Concat(requireKill.Where(c => !c.Removed && !c.IsDead)).Concat(requireRescue);
                    }
                    else
                    {
                        return requireKill.Concat(requireRescue);
                    }
                }
            }
        }

        protected bool wasDocked;

        public AbandonedOutpostMission(MissionPrefab prefab, Location[] locations, Submarine sub) : 
            base(prefab, locations, sub)
        {
            characterConfig = prefab.ConfigElement.GetChildElement("Characters");

            allowOrderingRescuees = prefab.ConfigElement.GetAttributeBool(nameof(allowOrderingRescuees), true);

            string msgTag = prefab.ConfigElement.GetAttributeString("hostageskilledmessage", "");
            hostagesKilledMessage = TextManager.Get(msgTag).Fallback(msgTag);

            itemConfig = prefab.ConfigElement.GetChildElement("Items");
            itemTag = prefab.ConfigElement.GetAttributeString("targetitem", "");
        }

        protected override void StartMissionSpecific(Level level)
        {
            failed = false;
            endTimer = 0.0f;
            characters.Clear();
            characterItems.Clear();
            requireKill.Clear();
            requireRescue.Clear();
            items.Clear();
#if SERVER
            spawnedItems.Clear();
#endif

            var submarine = Submarine.Loaded.Find(s => s.Info.Type == SubmarineType.Outpost) ?? Submarine.MainSub;
            InitItems(submarine);
            if (!IsClient)
            {
                InitCharacters(submarine);
            }

            wasDocked = Submarine.MainSub.DockedTo.Contains(Level.Loaded.StartOutpost);
        }

        private void InitItems(Submarine submarine)
        {
            if (!string.IsNullOrEmpty(itemTag))
            {
                var itemsToDestroy = Item.ItemList.FindAll(it => it.Submarine?.Info.Type != SubmarineType.Player && it.HasTag(itemTag));
                if (!itemsToDestroy.Any())
                {
                    DebugConsole.ThrowError($"Error in mission \"{Prefab.Identifier}\". Could not find an item with the tag \"{itemTag}\".");
                }
                else
                {
                    items.AddRange(itemsToDestroy);
                }
            }

            if (itemConfig != null && !IsClient)
            {
                foreach (XElement element in itemConfig.Elements())
                {
                    string itemIdentifier = element.GetAttributeString("identifier", "");
                    if (!(MapEntityPrefab.Find(null, itemIdentifier) is ItemPrefab itemPrefab))
                    {
                        DebugConsole.ThrowError("Couldn't spawn item for outpost destroy mission: item prefab \"" + itemIdentifier + "\" not found");
                        continue;
                    }

                    Identifier[] moduleFlags = element.GetAttributeIdentifierArray("moduleflags", null);
                    Identifier[] spawnPointTags = element.GetAttributeIdentifierArray("spawnpointtags", null);
                    ISpatialEntity spawnPoint = SpawnAction.GetSpawnPos(
                         SpawnAction.SpawnLocationType.Outpost, SpawnType.Human | SpawnType.Enemy,
                         moduleFlags, spawnPointTags, element.GetAttributeBool("asfaraspossible", false));
                    spawnPoint ??= submarine.GetHulls(alsoFromConnectedSubs: false).GetRandomUnsynced();
                    Vector2 spawnPos = spawnPoint.WorldPosition;
                    if (spawnPoint is WayPoint wp && wp.CurrentHull != null && wp.CurrentHull.Rect.Width > 100)
                    {
                        spawnPos = new Vector2(
                            MathHelper.Clamp(wp.WorldPosition.X + Rand.Range(-200, 201), wp.CurrentHull.WorldRect.X + 50, wp.CurrentHull.WorldRect.Right - 50),
                            wp.CurrentHull.WorldRect.Y - wp.CurrentHull.Rect.Height + 16.0f);
                    }
                    var item = new Item(itemPrefab, spawnPos, null);
                    items.Add(item);
#if SERVER
                    spawnedItems.Add(item);
#endif
                }
            }
        }

        private void InitCharacters(Submarine submarine)
        {
            characters.Clear();
            characterItems.Clear();

            if (characterConfig != null) 
            { 
                foreach (XElement element in characterConfig.Elements())
                {
                    if (GameMain.NetworkMember == null && element.GetAttributeBool("multiplayeronly", false)) { continue; }

                    int defaultCount = element.GetAttributeInt("count", -1);
                    if (defaultCount < 0)
                    {
                        defaultCount = element.GetAttributeInt("amount", 1);
                    }
                    int min = Math.Min(element.GetAttributeInt("min", defaultCount), 255);
                    int max = Math.Min(Math.Max(min, element.GetAttributeInt("max", defaultCount)), 255);
                    int count = Rand.Range(min, max + 1);

                    if (element.Attribute("identifier") != null && element.Attribute("from") != null)
                    {
                        HumanPrefab humanPrefab = GetHumanPrefabFromElement(element); 
                        if (humanPrefab == null)
                        {
                            DebugConsole.ThrowError($"Couldn't spawn a human character for abandoned outpost mission: human prefab \"{element.GetAttributeString("identifier", string.Empty)}\" not found");
                            continue;
                        }
                        for (int i = 0; i < count; i++)
                        {
                            LoadHuman(humanPrefab, element, submarine);
                        }
                    }
                    else
                    {
                        Identifier speciesName = element.GetAttributeIdentifier("character", element.GetAttributeIdentifier("identifier", Identifier.Empty));
                        var characterPrefab = CharacterPrefab.FindBySpeciesName(speciesName);
                        if (characterPrefab == null)
                        {
                            DebugConsole.ThrowError($"Couldn't spawn a character for abandoned outpost mission: character prefab \"{speciesName}\" not found");
                            continue;
                        }
                        for (int i = 0; i < count; i++)
                        {
                            LoadMonster(characterPrefab, element, submarine);
                        }
                    }
                }
            }            
        }

        private void LoadHuman(HumanPrefab humanPrefab, XElement element, Submarine submarine)
        {
            Identifier[] moduleFlags = element.GetAttributeIdentifierArray("moduleflags", null);
            Identifier[] spawnPointTags = element.GetAttributeIdentifierArray("spawnpointtags", null);
            var spawnPointType = element.GetAttributeEnum("spawnpointtype", SpawnType.Human);
            ISpatialEntity spawnPos = SpawnAction.GetSpawnPos(
                SpawnAction.SpawnLocationType.Outpost, spawnPointType,
                moduleFlags ?? humanPrefab.GetModuleFlags(),
                spawnPointTags ?? humanPrefab.GetSpawnPointTags(),
                element.GetAttributeBool("asfaraspossible", false));
            spawnPos ??= submarine.GetHulls(alsoFromConnectedSubs: false).GetRandomUnsynced();

            bool requiresRescue = element.GetAttributeBool("requirerescue", false);
            var teamId = element.GetAttributeEnum("teamid", requiresRescue ? CharacterTeamType.FriendlyNPC : CharacterTeamType.None);
            Character spawnedCharacter = CreateHuman(humanPrefab, characters, characterItems, submarine, teamId, spawnPos);
            if (Level.Loaded?.StartOutpost?.Info is { } outPostInfo)
            {
                outPostInfo.AddOutpostNPCIdentifierOrTag(spawnedCharacter, humanPrefab.Identifier);
                foreach (Identifier tag in humanPrefab.GetTags())
                {
                    outPostInfo.AddOutpostNPCIdentifierOrTag(spawnedCharacter, tag);
                }
            }

            if (spawnPos is WayPoint wp)
            {
                spawnedCharacter.GiveIdCardTags(wp);
            }

            if (requiresRescue)
            {
                requireRescue.Add(spawnedCharacter);
#if CLIENT
                if (allowOrderingRescuees)
                {
                    GameMain.GameSession.CrewManager.AddCharacterToCrewList(spawnedCharacter);
                }
#endif
            }
            else if (TimesAttempted > 0 && spawnedCharacter.AIController is HumanAIController humanAi)
            {
                var order = OrderPrefab.Prefabs["fightintruders"]
                    .CreateInstance(OrderPrefab.OrderTargetType.Entity, orderGiver: spawnedCharacter)
                    .WithManualPriority(CharacterInfo.HighestManualOrderPriority);
                spawnedCharacter.SetOrder(order, isNewOrder: true, speak: false);
            }

            if (element.GetAttributeBool("requirekill", false))
            {
                requireKill.Add(spawnedCharacter);
            }
        }

        private void LoadMonster(CharacterPrefab monsterPrefab, XElement element, Submarine submarine)
        {
            Identifier[] moduleFlags = element.GetAttributeIdentifierArray("moduleflags", null);
            Identifier[] spawnPointTags = element.GetAttributeIdentifierArray("spawnpointtags", null);
            ISpatialEntity spawnPos = SpawnAction.GetSpawnPos(SpawnAction.SpawnLocationType.Outpost, SpawnType.Enemy, moduleFlags, spawnPointTags, element.GetAttributeBool("asfaraspossible", false));
            spawnPos ??= submarine.GetHulls(alsoFromConnectedSubs: false).GetRandomUnsynced();
            Character spawnedCharacter = Character.Create(monsterPrefab.Identifier, spawnPos.WorldPosition, ToolBox.RandomSeed(8), createNetworkEvent: false);
            characters.Add(spawnedCharacter);
            if (element.GetAttributeBool("requirekill", false))
            {
                requireKill.Add(spawnedCharacter);
            }
            if (spawnedCharacter.Inventory != null)
            {
                characterItems.Add(spawnedCharacter, spawnedCharacter.Inventory.FindAllItems(recursive: true));
            }
            if (submarine != null && spawnedCharacter.AIController is EnemyAIController enemyAi)
            {
                enemyAi.UnattackableSubmarines.Add(submarine);
                enemyAi.UnattackableSubmarines.Add(Submarine.MainSub);
                foreach (Submarine sub in Submarine.MainSub.DockedTo)
                {
                    enemyAi.UnattackableSubmarines.Add(sub);
                }
            }
        }


        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (State != HostagesKilledState)
            {
                if (requireRescue.Any(r => r.Removed || r.IsDead))
                {
                    State = HostagesKilledState;
                    return;
                }
            }
            else
            {
                endTimer += deltaTime;
                if (endTimer > EndDelay)
                {
#if SERVER
                    if (!(GameMain.GameSession.GameMode is CampaignMode) && GameMain.Server != null)
                    {
                        GameMain.Server.EndGame();                        
                    }
#endif
                }
            }

            switch (state)
            {
                case 0:

                    if (items.All(it => it.Removed || it.Condition <= 0.0f) &&
                        requireKill.All(c => c.Removed || c.IsDead || (c.LockHands && c.Submarine == Submarine.MainSub)) &&
                        requireRescue.All(c => c.Submarine?.Info.Type == SubmarineType.Player))
                    {
                        State = 1;
                    }                    
                    break;
#if SERVER
                case 1:
                    if (!(GameMain.GameSession.GameMode is CampaignMode) && GameMain.Server != null)
                    {
                        if (!Submarine.MainSub.AtStartExit || (wasDocked && !Submarine.MainSub.DockedTo.Contains(Level.Loaded.StartOutpost)))
                        {
                            GameMain.Server.EndGame();
                            State = 2;
                        }
                    }
                    break;
#endif
            }

        }

        protected override bool DetermineCompleted()
        {
            return State > 0 && State != HostagesKilledState;
        }

        protected override void EndMissionSpecific(bool completed)
        {
            failed = !completed && requireRescue.Any(r => r.Removed || r.IsDead);
        }
    }
}