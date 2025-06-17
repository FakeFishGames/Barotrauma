using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class EscortMission : Mission
    {
        private readonly ContentXElement terroristItemConfig;
        
        private readonly Dictionary<HumanPrefab, List<StatusEffect>> characterStatusEffects = new Dictionary<HumanPrefab, List<StatusEffect>>();

        /// <summary>
        /// Number of escorted characters by default.
        /// </summary>
        private readonly int baseEscortedCharacters;
        /// <summary>
        /// A scaling factor for the number of escorted characters, relative to the recommended crew size of the sub. The total amount of escorted characters is calculated as 
        /// baseEscortedCharacters + scalingEscortedCharacters * (RecommendedCrewSizeMin + RecommendedCrewSizeMax) / 2
        /// </summary>
        private readonly float scalingEscortedCharacters;
        /// <summary>
        /// The probability for the escorted characters to be "terrorists" (turning them hostile when the sub has progressed enough in the level).
        /// A value of 0.5 would mean about half of the characters are terrorist, 1 would mean they all are. There's 20% of randomness applied to the value to make it less predictable.
        /// </summary>
        private readonly float terroristChance;

        /// <summary>
        /// Dialog tag the terrorists use in their dialog when they become hostile.
        /// </summary>
        private readonly string terroristAnnounceDialogTag;

        private int calculatedReward;
        private Submarine missionSub;

        private Character vipCharacter;

        private readonly List<Character> terroristCharacters = new List<Character>();
        private bool terroristsShouldAct = false;
        private float terroristDistanceSquared;
        private const string TerroristTeamChangeIdentifier = "terrorist";

        public EscortMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            missionSub = sub;
            baseEscortedCharacters = prefab.ConfigElement.GetAttributeInt("baseescortedcharacters", 1);
            scalingEscortedCharacters = prefab.ConfigElement.GetAttributeFloat("scalingescortedcharacters", 0);
            terroristChance = prefab.ConfigElement.GetAttributeFloat("terroristchance", 0);
            terroristItemConfig = prefab.ConfigElement.GetChildElement("TerroristItems");
            terroristAnnounceDialogTag = 
                prefab.ConfigElement.GetAttributeString("dialogterroristannounce", 
                    prefab.ConfigElement.GetAttributeString("terroristAnnounceDialogTag", string.Empty));
            CalculateReward();
        }

        private void CalculateReward()
        {
            if (missionSub == null)
            {
                calculatedReward = Prefab.Reward;
                return;
            }

            // Disabled for now, because they make balancing the missions a pain.
            int multiplier = 1;//CalculateScalingEscortedCharacterCount();
            calculatedReward = Prefab.Reward * multiplier;

            string rewardText = $"‖color:gui.orange‖{string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N0}", GetReward(missionSub))}‖end‖";
            if (descriptionWithoutReward != null) { description = descriptionWithoutReward.Replace("[reward]", rewardText); }
        }

        public override float GetBaseReward(Submarine sub)
        {
            if (sub != missionSub)
            {
                missionSub = sub;
                CalculateReward();
            }
            return calculatedReward;
        }

        int CalculateScalingEscortedCharacterCount(bool inMission = false)
        {
            if (missionSub == null || missionSub.Info == null) // UI logic failing to get the correct value is not important, but the mission logic must succeed
            {
                if (inMission)
                {
                    DebugConsole.ThrowError("MainSub was null when trying to retrieve submarine size for determining escorted character count!",
                        contentPackage: Prefab.ContentPackage);
                }
                return 1;
            }
            return (int)Math.Round(baseEscortedCharacters + scalingEscortedCharacters * (missionSub.Info.RecommendedCrewSizeMin + missionSub.Info.RecommendedCrewSizeMax) / 2);
        }

        private void InitEscort()
        {
            characters.Clear();
            characterItems.Clear();

            WayPoint explicitStayInHullPos = WayPoint.GetRandom(SpawnType.Human, null, Submarine.MainSub);
            Rand.RandSync randSync = Rand.RandSync.ServerAndClient;

            if (terroristChance > 0f)
            {
                // in terrorist missions, reroll characters each retry to avoid confusion as to who the terrorists are
                randSync = Rand.RandSync.Unsynced; 
            }

            List<(HumanPrefab humanPrefab, List<StatusEffect> statusEffects)> humanPrefabsToSpawn = new List<(HumanPrefab humanPrefab, List<StatusEffect> statusEffects)>();
            foreach (ContentXElement characterElement in characterConfig.Elements())
            {
                int count = CalculateScalingEscortedCharacterCount(inMission: true);
                var humanPrefab = GetHumanPrefabFromElement(characterElement);
                for (int i = 0; i < count; i++)
                {
                    List<StatusEffect> characterStatusEffects = new List<StatusEffect>();
                    foreach (var element in characterElement.Elements())
                    {
                        if (element.NameAsIdentifier() == "statuseffect")
                        {
                            var newEffect = StatusEffect.Load(element, parentDebugName: Prefab.Name.Value);
                            if (newEffect == null) { continue; }
                            characterStatusEffects.Add(newEffect);               
                        }
                    }
                    humanPrefabsToSpawn.Add((humanPrefab, characterStatusEffects));
                }
            }

            //if any of the escortees have a job defined, try to use a spawnpoint designated for that job
            foreach ((var humanPrefab, var statusEffectList) in humanPrefabsToSpawn)
            {
                if (humanPrefab == null || humanPrefab.Job.IsEmpty || humanPrefab.Job == "any") { continue; }
                var jobPrefab = humanPrefab.GetJobPrefab(randSync);
                if (jobPrefab != null)
                {
                    var jobSpecificSpawnPos = WayPoint.GetRandom(SpawnType.Human, jobPrefab, Submarine.MainSub);
                    if (jobSpecificSpawnPos != null)
                    {
                        explicitStayInHullPos = jobSpecificSpawnPos;
                        break;
                    }
                }
            }
            foreach ((var humanPrefab, var statusEffectList) in humanPrefabsToSpawn)
            {
                Character spawnedCharacter = CreateHuman(humanPrefab, characters, characterItems, Submarine.MainSub, CharacterTeamType.FriendlyNPC, explicitStayInHullPos, humanPrefabRandSync: randSync);
                if (spawnedCharacter.AIController is HumanAIController humanAI)
                {
                    humanAI.InitMentalStateManager();
                }
                foreach (var statusEffect in statusEffectList)
                {
                    statusEffect.Apply(statusEffect.type, 1.0f, spawnedCharacter, spawnedCharacter);
                }                
            }

            if (terroristChance > 0f)
            {
                int terroristCount = (int)Math.Ceiling(terroristChance * Rand.Range(0.8f, 1.2f) * characters.Count); 
                terroristCount = Math.Clamp(terroristCount, 1, characters.Count);

                terroristCharacters.Clear();
                characters.GetRange(0, terroristCount).ForEach(c => terroristCharacters.Add(c));
                terroristCharacters.ForEach(c => c.IsHostileEscortee = true);
                terroristDistanceSquared = Vector2.DistanceSquared(Level.Loaded.StartPosition, Level.Loaded.EndPosition) * Rand.Range(0.35f, 0.65f);
#if DEBUG
                DebugConsole.AddWarning("Terrorists will trigger at range  " + Math.Sqrt(terroristDistanceSquared));
                foreach (Character character in terroristCharacters)
                {
                    DebugConsole.AddWarning(character.Name + " is a terrorist.");
                }
#endif
            }
        }

        private void InitCharacters()
        {
            int scalingCharacterCount = CalculateScalingEscortedCharacterCount(inMission: true);

            if (scalingCharacterCount * characterConfig.Elements().Count() != characters.Count)
            {
                DebugConsole.AddWarning("Character count did not match expected character count in InitCharacters of EscortMission",
                    Prefab.ContentPackage);
                return;
            }
            int i = 0;

            foreach (ContentXElement element in characterConfig.Elements())
            {
                string escortIdentifier = element.GetAttributeString("escortidentifier", string.Empty);
                for (int k = 0; k < scalingCharacterCount; k++)
                {
                    // for each element defined, we need to initialize that type of character equal to the scaling escorted character count
                    characters[k + i].IsEscorted = true;
                    if (escortIdentifier != string.Empty)
                    {
                        if (escortIdentifier == "vip")
                        {
                            vipCharacter = characters[k + i];
                        }
                    }
                    characters[k + i].UniqueNameColor = element.GetAttributeColor("color", Color.LightGreen);
                }
                i++;
            }
        }

        protected override void StartMissionSpecific(Level level)
        {
            if (characters.Count > 0)
            {
#if DEBUG
                throw new Exception($"characters.Count > 0 ({characters.Count})");
#else
                DebugConsole.AddWarning("Character list was not empty at the start of a escort mission. The mission instance may not have been ended correctly on previous rounds.");
                characters.Clear();            
#endif
            }

            if (characterConfig == null)
            {
                DebugConsole.ThrowError("Failed to initialize characters for escort mission (characterConfig == null)",
                    contentPackage: Prefab.ContentPackage);
                return;
            }

            // to ensure single missions run without issues, default to mainsub
            if (missionSub == null)
            {
                missionSub = Submarine.MainSub;
                CalculateReward();
            }

            if (!IsClient)
            {
                InitEscort();
                InitCharacters();
            }
        }

        void TryToTriggerTerrorists()
        {
            if (terroristsShouldAct)
            {
                // decoupled from range check to prevent from weirdness if players handcuff a terrorist and move backwards
                foreach (Character character in terroristCharacters)
                {
                    character.IsHostileEscortee = true;
                    if (character.HasTeamChange(TerroristTeamChangeIdentifier))
                    {
                        // already triggered
                        continue;
                    }

                    if (IsAlive(character) && !character.IsIncapacitated && !character.LockHands)
                    {
                        character.TryAddNewTeamChange(TerroristTeamChangeIdentifier, new ActiveTeamChange(CharacterTeamType.None, ActiveTeamChange.TeamChangePriorities.Willful, aggressiveBehavior: true));
                        if (!string.IsNullOrEmpty(terroristAnnounceDialogTag))
                        {
                            character.Speak(TextManager.Get(terroristAnnounceDialogTag).Value, null, Rand.Range(0.5f, 3f));
                        }
                        foreach (var itemElement in terroristItemConfig.Elements())
                        {
                            float levelDifficulty = Level.Loaded?.Difficulty ?? 0.0f;
                            var selectedItemElement = itemElement;
                            if (itemElement.NameAsIdentifier() == "chooserandom".ToIdentifier())
                            {
                                selectedItemElement = itemElement.Elements().GetRandomUnsynced(e => e.GetAttributeFloat(0f, "mindifficulty") <= levelDifficulty);
                            }
                            if (selectedItemElement != null)
                            {
                                if (levelDifficulty < selectedItemElement.GetAttributeFloat(0f, "mindifficulty")) { continue; }
                                HumanPrefab.InitializeItem(character, selectedItemElement, character.Submarine, humanPrefab: null, createNetworkEvents: true);
                            }
                        }

                    }
                }
            }
            else if (Vector2.DistanceSquared(Submarine.MainSub.WorldPosition, Level.Loaded.EndPosition) < terroristDistanceSquared)
            {
                foreach (Character character in terroristCharacters)
                {
                    if (character.AIController is HumanAIController humanAI)
                    {
                        humanAI.ObjectiveManager.AddObjective(new AIObjectiveEscapeHandcuffs(character, humanAI.ObjectiveManager, shouldSwitchTeams: false, beginInstantly: true));
                    }
                }
                terroristsShouldAct = true;
            }
        }

        bool NonTerroristsStillAlive(IEnumerable<Character> characterList)
        {
            return characterList.All(c => terroristCharacters.Contains(c) || IsAlive(c));
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (!IsClient)
            {
                int newState = State;
                TryToTriggerTerrorists();
                switch (State)
                {
                    case 0: // base
                        if (!NonTerroristsStillAlive(characters))
                        {
                            newState = 1;
                        }
                        if (terroristCharacters.Any() && terroristCharacters.All(c => !IsAlive(c)))
                        {
                            newState = 2;
                        }
                        break;
                    case 1: // failure
                        break;
                    case 2: // terrorists killed
                        if (!NonTerroristsStillAlive(characters))
                        {
                            newState = 1;
                        }
                        break;
                }
                State = newState;
            }
        }

        private static bool Survived(Character character)
        {
            return IsAlive(character) && character.CurrentHull?.Submarine != null && 
                (character.CurrentHull.Submarine == Submarine.MainSub || Submarine.MainSub.DockedTo.Contains(character.CurrentHull.Submarine));
        }

        private static bool IsAlive(Character character)
        {
            return character != null && !character.Removed && !character.IsDead;
        }

        protected override bool DetermineCompleted()
        {
            if (Submarine.MainSub != null && Submarine.MainSub.AtEndExit)
            {
                bool friendliesSurvived = characters.Except(terroristCharacters).All(c => Survived(c));
                bool vipDied = false;

                // this logic is currently irrelevant, as the mission is failed regardless of who dies 
                if (vipCharacter != null)
                {
                    vipDied = !Survived(vipCharacter);
                }

                if (friendliesSurvived && !vipDied)
                {
                    return true;
                }
            }
            return false;
        }

        protected override void EndMissionSpecific(bool completed)
        {
            if (!IsClient)
            {
                foreach (Character character in characters)
                {
                    if (character.Inventory == null) { continue; }
                    foreach (Item item in character.Inventory.AllItemsMod)
                    {
                        //item didn't spawn with the characters -> drop it
                        if (!characterItems.Any(c => c.Value.Contains(item)))
                        {
                            item.Drop(character);
                        }
                    }
                }

                // characters that survived will take their items with them, in case players tried to be crafty and steal them
                // this needs to run here in case players abort the mission by going back home
                foreach (var characterItem in characterItems)
                {
                    if (Survived(characterItem.Key) || !completed)
                    {
                        foreach (Item item in characterItem.Value)
                        {
                            if (!item.Removed)
                            {
                                item.Remove();
                            }
                        }
                    }
                }
            }

            characters.Clear();
            characterItems.Clear();
            failed = !completed;
        }
    }
}
