using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class AbandonedOutpostMission : Mission
    {
        protected readonly HashSet<Character> requireKill = new HashSet<Character>();
        protected readonly HashSet<Character> requireRescue = new HashSet<Character>();

        private readonly Identifier itemTag;
        private readonly XElement itemConfig;
        private readonly List<Item> items = new List<Item>();

        protected const int HostagesKilledState = 5;

        private readonly LocalizedString hostagesKilledMessage;

        private const float EndDelay = 5.0f;
        private float endTimer;

        private readonly bool allowOrderingRescuees;

        public override bool AllowRespawning => false;

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
            allowOrderingRescuees = prefab.ConfigElement.GetAttributeBool(nameof(allowOrderingRescuees), true);

            string msgTag = prefab.ConfigElement.GetAttributeString("hostageskilledmessage", "");
            hostagesKilledMessage = TextManager.Get(msgTag).Fallback(msgTag);

            itemConfig = prefab.ConfigElement.GetChildElement("Items");
            itemTag = prefab.ConfigElement.GetAttributeIdentifier("targetitem", Identifier.Empty);
        }

        protected override void StartMissionSpecific(Level level)
        {
            failed = false;
            endTimer = 0.0f;
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
            if (!itemTag.IsEmpty)
            {
                var itemsToDestroy = Item.ItemList.FindAll(it => it.Submarine?.Info.Type != SubmarineType.Player && it.HasTag(itemTag));
                if (!itemsToDestroy.Any())
                {
                    DebugConsole.ThrowError($"Error in mission \"{Prefab.Identifier}\". Could not find an item with the tag \"{itemTag}\".",
                        contentPackage: Prefab.ContentPackage);
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
                    Identifier itemIdentifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
                    if (MapEntityPrefab.FindByIdentifier(itemIdentifier) is not ItemPrefab itemPrefab)
                    {
                        DebugConsole.ThrowError("Couldn't spawn item for outpost destroy mission: item prefab \"" + itemIdentifier + "\" not found",
                            contentPackage: Prefab.ContentPackage);
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
                    if (GameMain.GameSession.GameMode is not CampaignMode && GameMain.Server != null)
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
                    if (GameMain.GameSession.GameMode is not CampaignMode && GameMain.Server != null)
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

        protected override void InitCharacter(Character character, XElement element)
        {
            base.InitCharacter(character, element);
            if (element.GetAttributeBool("requirekill", false))
            {
                requireKill.Add(character);
            }
        }

        protected override Character LoadHuman(HumanPrefab humanPrefab, XElement element, Submarine submarine)
        {
            Character spawnedCharacter = base.LoadHuman(humanPrefab, element, submarine);
            bool requiresRescue = element.GetAttributeBool("requirerescue", false);
            if (requiresRescue)
            {
                requireRescue.Add(spawnedCharacter);
#if CLIENT
                if (allowOrderingRescuees)
                {
                    GameMain.GameSession.CrewManager?.AddCharacterToCrewList(spawnedCharacter);
                }
#endif
            }
            else if (TimesAttempted > 0 && spawnedCharacter.AIController is HumanAIController)
            {
                var order = OrderPrefab.Prefabs["fightintruders"]
                    .CreateInstance(OrderPrefab.OrderTargetType.Entity, orderGiver: spawnedCharacter)
                    .WithManualPriority(CharacterInfo.HighestManualOrderPriority);
                spawnedCharacter.SetOrder(order, isNewOrder: true, speak: false);
            }
            // Overrides the team change set in the base method.
            var teamId = element.GetAttributeEnum("teamid", requiresRescue ? CharacterTeamType.FriendlyNPC : CharacterTeamType.None);
            if (teamId != spawnedCharacter.TeamID)
            {
                spawnedCharacter.SetOriginalTeamAndChangeTeam(teamId);
            }
            return spawnedCharacter;
        }
    }
}