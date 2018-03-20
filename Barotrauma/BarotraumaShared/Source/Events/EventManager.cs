using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class EventManager
    {
        private List<ScriptedEvent> events;

        private Level level;

        //The "intensity" of the current situation (a value between 0.0 - 1.0).
        //High when a disaster has struck, low when nothing special is going on.
        private float currentIntensity;
        //The exact intensity of the current situation, current intensity is lerped towards this value
        private float targetIntensity;

        //How low the intensity has to be for an event to be triggered. 
        //Gradually increases with time, so additional problems can still appear eventually even if
        //the sub is laying broken on the ocean floor or if the players are trying to abuse the system
        //by intentionally keeping the intensity high by causing breaches, damaging themselves or such
        private float eventThreshold = 0.2f;
        
        //New events can't be triggered when the cooldown is active.
        private float eventCoolDown;

        private float intensityUpdateTimer;

        private float avgCrewHealth, avgHullIntegrity, floodingAmount, fireAmount, enemyDanger;

        private float roundDuration;

        private List<ScriptedEventSet> selectedEventSets;

        private EventManagerSettings settings;
        
        public float CurrentIntensity
        {
            get { return currentIntensity; }
        }

        public List<ScriptedEvent> Events
        {
            get { return events; }
        }
        
        public EventManager(GameSession session)
        {
            events = new List<ScriptedEvent>();
            selectedEventSets = new List<ScriptedEventSet>();
        }

        public bool Enabled = true;

        public void StartRound(Level level)
        {
            if (GameMain.Client != null) return;

            var suitableSettings = EventManagerSettings.List.FindAll(s =>
                level.Difficulty > s.MinLevelDifficulty &&
                level.Difficulty < s.MaxLevelDifficulty);

            if (suitableSettings.Count == 0)
            {
                DebugConsole.ThrowError("No suitable event manager settings found for the selected level (difficulty " + level.Difficulty + ")");
                settings = EventManagerSettings.List[Rand.Int(EventManagerSettings.List.Count, Rand.RandSync.Server)];
            }
            else
            {
                settings = suitableSettings[Rand.Int(suitableSettings.Count, Rand.RandSync.Server)];
            }

            this.level = level;
            var initialEventSet = SelectRandomEvents(ScriptedEventSet.List);
            if (initialEventSet != null) selectedEventSets.Add(initialEventSet);
            /*CreateInitialEvents();
            foreach (ScriptedEvent ev in events)
            {
                ev.Init(false);
            }*/

            roundDuration = 0.0f;
            intensityUpdateTimer = 0.0f;
            CalculateCurrentIntensity(0.0f);
            currentIntensity = targetIntensity;
            eventThreshold = settings.DefaultEventThreshold;
            eventCoolDown = 0.0f;
        }

        public void EndRound()
        {
            selectedEventSets.Clear();
            events.Clear();
        }

        private ScriptedEventSet SelectRandomEvents(List<ScriptedEventSet> eventSets)
        {
            MTRandom rand = new MTRandom(ToolBox.StringToInt(level.Seed));

            var allowedEventSets = 
                eventSets.Where(es => level.Difficulty >= es.MinLevelDifficulty && level.Difficulty <= es.MaxLevelDifficulty);

            float totalCommonness = allowedEventSets.Sum(e => e.GetCommonness(level));
            float randomNumber = (float)rand.NextDouble() * totalCommonness;
            foreach (ScriptedEventSet eventSet in allowedEventSets)
            {
                float commonness = eventSet.GetCommonness(level);
                if (randomNumber <= commonness)
                {
                    return eventSet;
                }
                randomNumber -= commonness;
            }

            return null;
        }

        private void CreateEvents()
        {
            for (int i = selectedEventSets.Count - 1; i >= 0; i--)
            {
                ScriptedEventSet eventSet = selectedEventSets[i];

                if ((Submarine.MainSub == null || Submarine.MainSub.WorldPosition.X / level.Size.X < eventSet.MinDistanceTraveled) &&
                    roundDuration < eventSet.MinMissionTime)
                {
                    continue;
                }

                selectedEventSets.RemoveAt(i);

                if (eventSet.ChooseRandom)
                {
                    if (eventSet.EventPrefabs.Count > 0)
                    {
                        MTRandom rand = new MTRandom(ToolBox.StringToInt(level.Seed));
                        var newEvent = eventSet.EventPrefabs[rand.NextInt32() % eventSet.EventPrefabs.Count].CreateInstance();
                        newEvent.Init(true);
                        events.Add(newEvent);
                    }
                    if (eventSet.ChildSets.Count > 0)
                    {
                        MTRandom rand = new MTRandom(ToolBox.StringToInt(level.Seed));
                        var newEventSet = SelectRandomEvents(eventSet.ChildSets);
                        if (newEventSet != null) selectedEventSets.Add(newEventSet);
                    }
                }
                else
                {
                    foreach (ScriptedEventPrefab eventPrefab in eventSet.EventPrefabs)
                    {
                        var newEvent = eventPrefab.CreateInstance();
                        newEvent.Init(true);
                        events.Add(newEvent);
                    }

                    selectedEventSets.AddRange(eventSet.ChildSets);
                }
            }
        }

        /*private void CreateInitialEvents()
        {
            if (GameMain.Client != null) return;

            System.Diagnostics.Debug.Assert(events.Count == 0);

            MTRandom rand = new MTRandom(ToolBox.StringToInt(level.Seed));

            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Generating events (seed: " + level.Seed + ")", Color.White);
            }

            events.AddRange(ScriptedEvent.GenerateInitialEvents(rand, level));
        }*/

        /*private void CreateRandomEvent()
        {
            List<ScriptedEventPrefab> allowedEvents = new List<ScriptedEventPrefab>();

            foreach (ScriptedEventPrefab prefab in ScriptedEventPrefab.List)
            {
                float commonness = prefab.GetMidRoundCommonness(level);
                if (commonness <= 0.0f) continue;
                
                allowedEvents.Add(prefab);
            }

            allowedEvents.RemoveAll(e => 
                e.Difficulty < settings.MinEventDifficulty || 
                e.Difficulty > settings.MaxEventDifficulty);

            if (allowedEvents.Count == 0)
            {
                DebugConsole.ThrowError("EventManager failed to create a random event - no allowed events found for the level type \"" + level.GenerationParams.Name + "\"!");
                return;
            }
            
            allowedEvents.RemoveAll(e =>
            {
                var tempInstance = e.CreateInstance();
                return !tempInstance.CanAffectSubImmediately(level);
            });

            float totalCommonness = allowedEvents.Sum(e => e.GetMidRoundCommonness(level));
            float randomNumber = Rand.Range(0.0f, totalCommonness, Rand.RandSync.Server);
            ScriptedEventPrefab selectedEvent = null;
            foreach (ScriptedEventPrefab prefab in allowedEvents)
            {
                float commonness = prefab.GetMidRoundCommonness(level);
                if (randomNumber <= commonness)
                {
                    selectedEvent = prefab;
                    break;
                }
                randomNumber -= commonness;
            }

            if (selectedEvent == null)
            {
#if DEBUG
                DebugConsole.ThrowError("EventManager failed to create a random event - no event could be made to affect the submarine immediately (no spawnpositions nearby?)");
#endif
                return;
            }

            ScriptedEvent eventInstance = selectedEvent.CreateInstance();
            eventInstance.Init(true);
            events.Add(eventInstance);
        }*/
        
        public void Update(float deltaTime)
        {
            if (GameMain.Client != null || !Enabled) return;

            roundDuration += deltaTime;

            CalculateCurrentIntensity(deltaTime);

            eventThreshold += settings.EventThresholdIncrease * deltaTime;
            if (eventCoolDown > 0.0f)
            {
                eventCoolDown -= deltaTime;
            }
            else if (currentIntensity < eventThreshold)
            {
                CreateEvents();
                eventThreshold = settings.DefaultEventThreshold;
                eventCoolDown = settings.EventCooldown;
            }

            events.RemoveAll(t => t.IsFinished);
            foreach (ScriptedEvent ev in events)
            {
                if (!ev.IsFinished)
                {
                    ev.Update(deltaTime);
                }
            }
        }
                
        private void CalculateCurrentIntensity(float deltaTime)
        {
            intensityUpdateTimer -= deltaTime;
            if (intensityUpdateTimer > 0.0f) return;
            intensityUpdateTimer = settings.IntensityUpdateInterval;

            // crew health --------------------------------------------------------

            avgCrewHealth = 0.0f;
            int characterCount = 0;
            foreach (Character character in Character.CharacterList)
            {
                if (character.IsDead) continue;
                if (character.AIController is HumanAIController || character.IsRemotePlayer || character == Character.Controlled)
                {
                    avgCrewHealth += character.Vitality / character.MaxVitality * (character.IsUnconscious ? 0.5f : 1.0f);
                    characterCount++;
                }
            }
            if (characterCount > 0)
            {
                avgCrewHealth = avgCrewHealth / characterCount;
            }

            // enemy amount --------------------------------------------------------

            enemyDanger = 0.0f;
            foreach (Character character in Character.CharacterList)
            {
                if (character.IsDead || character.IsUnconscious || !character.Enabled) continue;

                EnemyAIController enemyAI = character.AIController as EnemyAIController;
                if (enemyAI == null) continue;
                
                if (character.CurrentHull != null)
                {
                    //crawler inside the sub adds 0.1f to enemy danger, mantis 0.25f
                    enemyDanger += enemyAI.CombatStrength / 1000.0f;
                }
                else if (enemyAI.SelectedAiTarget?.Entity?.Submarine != null)
                {
                    //enemy outside and targeting the sub or something in it
                    //moloch adds 0.24 to enemy danger, a crawler 0.02
                    enemyDanger += enemyAI.CombatStrength / 5000.0f;
                }
            }
            enemyDanger = MathHelper.Clamp(enemyDanger, 0.0f, 1.0f);

            // hull status (gaps, flooding, fire) --------------------------------------------------------

            float holeCount = 0.0f;
            floodingAmount = 0.0f;
            foreach (Hull hull in Hull.hullList)
            {
                foreach (Gap gap in hull.ConnectedGaps)
                {
                    if (!gap.IsRoomToRoom) holeCount += gap.Open;
                }
                floodingAmount += hull.WaterVolume / hull.Volume / Hull.hullList.Count;
                fireAmount += hull.FireSources.Sum(fs => fs.Size.X);
            }
            //hull integrity at 0.0 if there are 10 or more wide-open holes
            avgHullIntegrity = MathHelper.Clamp(1.0f - holeCount / 10.0f, 0.0f, 1.0f);
            
            //a fire of any size bumps up the fire amount to 20%
            //if the total width of the fires is 1000 or more, the fire amount is considered to be at 100%
            fireAmount = MathHelper.Clamp(fireAmount / 1000.0f, fireAmount > 0.0f ? 0.2f : 0.0f, 1.0f);

            //flooding less than 10% of the sub is ignored 
            //to prevent ballast tanks from affecting the intensity
            if (floodingAmount < 0.1f) floodingAmount = 0.0f;

            // calculate final intensity --------------------------------------------------------

            targetIntensity = 
                ((1.0f - avgCrewHealth) + (1.0f - avgHullIntegrity) + floodingAmount) / 3.0f;
            targetIntensity += fireAmount * 0.5f;
            targetIntensity += enemyDanger;
            targetIntensity = MathHelper.Clamp(targetIntensity, 0.0f, 1.0f);

            if (targetIntensity > currentIntensity)
            {
                //50 seconds for intensity to go from 0.0 to 1.0
                currentIntensity = MathHelper.Min(currentIntensity + 0.02f * settings.IntensityUpdateInterval, targetIntensity);
            }
            else
            {
                //400 seconds for intensity to go from 1.0 to 0.0
                currentIntensity = MathHelper.Max(0.0025f * settings.IntensityUpdateInterval, targetIntensity);
            }
        }
    }
}
