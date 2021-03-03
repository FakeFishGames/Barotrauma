using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class AbandonedOutpostMission : Mission
    {
        private readonly XElement characterConfig;

        private readonly List<Character> characters = new List<Character>();
        private readonly Dictionary<Character, List<Item>> characterItems = new Dictionary<Character, List<Item>>();

        private readonly string itemTag;

        private Item itemToDestroy;

        public AbandonedOutpostMission(MissionPrefab prefab, Location[] locations) : 
            base(prefab, locations)
        {
            characterConfig = prefab.ConfigElement.Element("Characters");

            itemTag = prefab.ConfigElement.GetAttributeString("targetitem", "");
            if (string.IsNullOrEmpty(itemTag))
            {
                DebugConsole.ThrowError($"Error in mission prefab \"{prefab.Identifier}\". Target item not defined.");
            }
        }

        protected override void StartMissionSpecific(Level level)
        {
            itemToDestroy = null;
            itemToDestroy = Item.ItemList.Find(it => it.Submarine?.Info.Type != SubmarineType.Player && it.HasTag(itemTag));
            if (itemToDestroy == null)
            {
                DebugConsole.ThrowError($"Error in mission \"{Prefab.Identifier}\". Could not find an item with the tag \"{itemTag}\".");
            }

            if (!IsClient)
            {
                InitCharacters();
            }
        }

        private void InitCharacters()
        {
            characters.Clear();
            characterItems.Clear();

            if (characterConfig == null) { return; }

            var submarine = Submarine.Loaded.Find(s => s.Info.Type == SubmarineType.Outpost) ?? Submarine.MainSub;
            if (submarine.Info.Type == SubmarineType.Outpost)
            {
                submarine.TeamID = CharacterTeamType.None;
            }

            foreach (XElement element in characterConfig.Elements())
            {
                string characterIdentifier = element.GetAttributeString("identifier", "");
                string characterFrom = element.GetAttributeString("from", "");
                HumanPrefab humanPrefab = NPCSet.Get(characterFrom, characterIdentifier);
                if (humanPrefab == null)
                {
                    DebugConsole.ThrowError("Couldn't spawn character for abandoned outpost mission: character prefab \"" + characterIdentifier + "\" not found");
                    return;
                }

                string[] moduleFlags = element.GetAttributeStringArray("moduleflags", null);
                string[] spawnPointTags = element.GetAttributeStringArray("spawnpointtags", null);
                ISpatialEntity spawnPos = SpawnAction.GetSpawnPos(
                    SpawnAction.SpawnLocationType.Outpost, SpawnType.Human,
                    moduleFlags ?? humanPrefab.GetModuleFlags(),
                    spawnPointTags ?? humanPrefab.GetSpawnPointTags());
                if (spawnPos == null)
                {
                    spawnPos = submarine.GetHulls(alsoFromConnectedSubs: false).GetRandom();
                }

                var characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: humanPrefab.GetJobPrefab(Rand.RandSync.Server), randSync: Rand.RandSync.Server);
                Character spawnedCharacter = Character.Create(characterInfo.SpeciesName, spawnPos.WorldPosition, ToolBox.RandomSeed(8), characterInfo, createNetworkEvent: false);
                spawnedCharacter.TeamID = CharacterTeamType.None;
                humanPrefab.InitializeCharacter(spawnedCharacter, spawnPos);
                humanPrefab.GiveItems(spawnedCharacter, Submarine.MainSub, Rand.RandSync.Server, createNetworkEvents: false);

                characters.Add(spawnedCharacter);
                characterItems.Add(spawnedCharacter, spawnedCharacter.Inventory.FindAllItems(recursive: true));
            }
        }

        public override void Update(float deltaTime)
        {
            if (State == 0 && itemToDestroy != null && itemToDestroy.Condition <= 0.0f)
            {
                State = 1;
            }
        }

        public override void End()
        {
            completed = itemToDestroy == null || itemToDestroy.Condition <= 0.0f;
            if (completed)
            {
                if (Prefab.LocationTypeChangeOnCompleted != null)
                {
                    ChangeLocationType(Prefab.LocationTypeChangeOnCompleted);
                }
                GiveReward();
            }
        }
    }
}