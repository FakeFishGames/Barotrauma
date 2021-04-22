using Barotrauma.Extensions;
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

        protected const int HostagesKilledState = 5;

        private readonly string hostagesKilledMessage;

        private const float EndDelay = 5.0f;
        private float endTimer;

        public override bool AllowRespawn => false;

        public override bool AllowUndocking
        {
            get 
            {
                if (GameMain.GameSession.GameMode is CampaignMode) { return true; }
                return state > 0;
            }
        }

        protected bool wasDocked;

        public AbandonedOutpostMission(MissionPrefab prefab, Location[] locations) : 
            base(prefab, locations)
        {
            characterConfig = prefab.ConfigElement.Element("Characters");

            string msgTag = prefab.ConfigElement.GetAttributeString("hostageskilledmessage", "");
            hostagesKilledMessage = TextManager.Get(msgTag, returnNull: true) ?? msgTag;
        }

        protected override void StartMissionSpecific(Level level)
        {
            failed = false;
            endTimer = 0.0f;
            characters.Clear();
            characterItems.Clear();
            requireKill.Clear();
            requireRescue.Clear();

            var submarine = Submarine.Loaded.Find(s => s.Info.Type == SubmarineType.Outpost) ?? Submarine.MainSub;
            if (!IsClient)
            {
                InitCharacters(submarine);
            }

            wasDocked = Submarine.MainSub.DockedTo.Contains(Level.Loaded.StartOutpost);
        }

        private void InitCharacters(Submarine submarine)
        {
            characters.Clear();
            characterItems.Clear();

            if (characterConfig == null) { return; }

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
                    string characterIdentifier = element.GetAttributeString("identifier", "");
                    string characterFrom = element.GetAttributeString("from", "");
                    HumanPrefab humanPrefab = NPCSet.Get(characterFrom, characterIdentifier);
                    if (humanPrefab == null)
                    {
                        DebugConsole.ThrowError("Couldn't spawn a character for abandoned outpost mission: character prefab \"" + characterIdentifier + "\" not found");
                        continue;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        LoadHuman(humanPrefab, element, submarine);
                    }
                }
                else
                {
                    string speciesName = element.GetAttributeString("character", element.GetAttributeString("identifier", ""));
                    var characterPrefab = CharacterPrefab.FindBySpeciesName(speciesName);
                    if (characterPrefab == null)
                    {
                        DebugConsole.ThrowError("Couldn't spawn a character for abandoned outpost mission: character prefab \"" + speciesName + "\" not found");
                        continue;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        LoadMonster(characterPrefab, element, submarine);
                    }
                }
            }
        }

        private void LoadHuman(HumanPrefab humanPrefab, XElement element, Submarine submarine)
        {
            string[] moduleFlags = element.GetAttributeStringArray("moduleflags", null);
            string[] spawnPointTags = element.GetAttributeStringArray("spawnpointtags", null);
            ISpatialEntity spawnPos = SpawnAction.GetSpawnPos(
                SpawnAction.SpawnLocationType.Outpost, SpawnType.Human,
                moduleFlags ?? humanPrefab.GetModuleFlags(),
                spawnPointTags ?? humanPrefab.GetSpawnPointTags(),
                element.GetAttributeBool("asfaraspossible", false));
            if (spawnPos == null)
            {
                spawnPos = submarine.GetHulls(alsoFromConnectedSubs: false).GetRandom();
            }

            var characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: humanPrefab.GetJobPrefab(Rand.RandSync.Server), randSync: Rand.RandSync.Server);
            Character spawnedCharacter = Character.Create(characterInfo.SpeciesName, spawnPos.WorldPosition, ToolBox.RandomSeed(8), characterInfo, createNetworkEvent: false);
            if (element.GetAttributeBool("requirerescue", false))
            {
                requireRescue.Add(spawnedCharacter);
                spawnedCharacter.TeamID = CharacterTeamType.FriendlyNPC;
#if CLIENT
                GameMain.GameSession.CrewManager.AddCharacterToCrewList(spawnedCharacter);
#endif
            }
            else
            {
                spawnedCharacter.TeamID = CharacterTeamType.None;
            }
            humanPrefab.InitializeCharacter(spawnedCharacter, spawnPos);
            humanPrefab.GiveItems(spawnedCharacter, Submarine.MainSub, Rand.RandSync.Server, createNetworkEvents: false);
            if (spawnPos is WayPoint wp)
            {
                spawnedCharacter.GiveIdCardTags(wp);
            }
            if (element.GetAttributeBool("requirekill", false))
            {
                requireKill.Add(spawnedCharacter);
            }
            characters.Add(spawnedCharacter);
            characterItems.Add(spawnedCharacter, spawnedCharacter.Inventory.FindAllItems(recursive: true));
        }

        private void LoadMonster(CharacterPrefab monsterPrefab, XElement element, Submarine submarine)
        {
            string[] moduleFlags = element.GetAttributeStringArray("moduleflags", null);
            string[] spawnPointTags = element.GetAttributeStringArray("spawnpointtags", null);
            ISpatialEntity spawnPos = SpawnAction.GetSpawnPos(SpawnAction.SpawnLocationType.Outpost, SpawnType.Enemy, moduleFlags, spawnPointTags, element.GetAttributeBool("asfaraspossible", false));
            if (spawnPos == null)
            {
                spawnPos = submarine.GetHulls(alsoFromConnectedSubs: false).GetRandom();
            }
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


        public override void Update(float deltaTime)
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

                    if (requireKill.All(c => c.Removed || c.IsDead) &&
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

        public override void End()
        {
            completed = State > 0 && State != HostagesKilledState;
            if (completed)
            {
                if (Prefab.LocationTypeChangeOnCompleted != null)
                {
                    ChangeLocationType(Prefab.LocationTypeChangeOnCompleted);
                }
                GiveReward();
            }
            else
            {
                failed = requireRescue.Any(r => r.Removed || r.IsDead);
            }
        }
    }
}