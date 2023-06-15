﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class EventManager
    {
        public enum NetworkEventType
        {
            CONVERSATION,
            CONVERSATION_SELECTED_OPTION,
            STATUSEFFECT,
            MISSION,
            UNLOCKPATH
        }

        const float IntensityUpdateInterval = 5.0f;

        const float CalculateDistanceTraveledInterval = 5.0f;

        const int MaxEventHistory = 20;

        private Level level;

        private readonly List<Sprite> preloadedSprites = new List<Sprite>();

        //The "intensity" of the current situation (a value between 0.0 - 1.0).
        //High when a disaster has struck, low when nothing special is going on.
        private float currentIntensity;
        //The exact intensity of the current situation, current intensity is lerped towards this value
        private float targetIntensity;
        //follows targetIntensity a bit faster than currentIntensity to prevent e.g. combat musing staying on very long after the monsters are dead
        private float musicIntensity;

        //How low the intensity has to be for an event to be triggered. 
        //Gradually increases with time, so additional problems can still appear eventually even if
        //the sub is laying broken on the ocean floor or if the players are trying to abuse the system
        //by intentionally keeping the intensity high by causing breaches, damaging themselves or such
        private float eventThreshold = 0.2f;

        //New events can't be triggered when the cooldown is active.
        private float eventCoolDown;

        private float intensityUpdateTimer;

        private PathFinder pathFinder;
        private float totalPathLength;
        private float calculateDistanceTraveledTimer;
        private float distanceTraveled;

        private float avgCrewHealth, avgHullIntegrity, floodingAmount, fireAmount, enemyDanger, monsterStrength;
        public float CumulativeMonsterStrengthMain;
        public float CumulativeMonsterStrengthRuins;
        public float CumulativeMonsterStrengthWrecks;
        public float CumulativeMonsterStrengthCaves;

        private float roundDuration;

        private bool isCrewAway;
        //how long it takes after the crew returns for the event manager to resume normal operation
        const float CrewAwayResetDelay = 60.0f;
        private float crewAwayResetTimer;
        private float crewAwayDuration;

        private readonly List<EventSet> pendingEventSets = new List<EventSet>();

        private readonly Dictionary<EventSet, List<Event>> selectedEvents = new Dictionary<EventSet, List<Event>>();

        private readonly List<Event> activeEvents = new List<Event>();

        private readonly HashSet<Event> finishedEvents = new HashSet<Event>();
        private readonly HashSet<Identifier> nonRepeatableEvents = new HashSet<Identifier>();


#if DEBUG && SERVER
        private DateTime nextIntensityLogTime;
#endif

        private EventManagerSettings settings;

        private readonly bool isClient;
        
        public float CurrentIntensity
        {
            get { return currentIntensity; }
        }
        public float MusicIntensity
        {
            get { return musicIntensity; }
        }

        public List<Event> ActiveEvents
        {
            get { return activeEvents; }
        }
        
        public readonly Queue<Event> QueuedEvents = new Queue<Event>();

        public readonly Queue<Identifier> QueuedEventsForNextRound = new Queue<Identifier>();

        private readonly struct TimeStamp
        {
            public readonly double Time;
            public readonly Event Event;

            public TimeStamp(Event e)
            {
                Event = e;
                Time = Timing.TotalTime;
            }
        }

        private readonly List<TimeStamp> timeStamps = new List<TimeStamp>();
        public void AddTimeStamp(Event e) => timeStamps.Add(new TimeStamp(e));

        public EventManager()
        {
            isClient = GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;
        }

        public bool Enabled = true;

        private MTRandom random;
        private int randomSeed;

        public void StartRound(Level level)
        {
            this.level = level;

            if (isClient) { return; }

            timeStamps.Clear();
            pendingEventSets.Clear();
            selectedEvents.Clear();
            activeEvents.Clear();
#if SERVER
            MissionAction.ResetMissionsUnlockedThisRound();
#endif
            pathFinder = new PathFinder(WayPoint.WayPointList, false);
            totalPathLength = 0.0f;
            if (level != null)
            {
                var steeringPath = pathFinder.FindPath(ConvertUnits.ToSimUnits(level.StartPosition), ConvertUnits.ToSimUnits(level.EndPosition));
                totalPathLength = steeringPath.TotalLength;
            }

            SelectSettings();
           
            if (level != null)
            {
                randomSeed = ToolBox.StringToInt(level.Seed);
                foreach (var previousEvent in level.LevelData.EventHistory)
                {
                    randomSeed ^= ToolBox.IdentifierToInt(previousEvent);
                }
            }
            random = new MTRandom(randomSeed);

            bool playingCampaign = GameMain.GameSession?.GameMode is CampaignMode;
            EventSet initialEventSet = SelectRandomEvents(
                EventSet.Prefabs.ToList(),
                requireCampaignSet: playingCampaign,
                random: random);
            EventSet additiveSet = null;
            if (initialEventSet != null && initialEventSet.Additive)
            {
                additiveSet = initialEventSet;
                initialEventSet = SelectRandomEvents(
                    EventSet.Prefabs.Where(e => !e.Additive).ToList(),
                    requireCampaignSet: playingCampaign,
                    random: random);
            }
            if (initialEventSet != null)
            {
                pendingEventSets.Add(initialEventSet);
                CreateEvents(initialEventSet);
            }
            if (additiveSet != null)
            {
                pendingEventSets.Add(additiveSet);
                CreateEvents(additiveSet);
            }

            if (level?.LevelData != null)
            {
                if (level.LevelData.Type == LevelData.LevelType.Outpost)
                {
                    //if the outpost is connected to a locked connection, create an event to unlock it
                    if (level.StartLocation?.Connections.Any(c => c.Locked && level.StartLocation.MapPosition.X < c.OtherLocation(level.StartLocation).MapPosition.X) ?? false)
                    {
                        var unlockPathEventPrefab = EventPrefab.GetUnlockPathEvent(level.LevelData.Biome.Identifier, level.StartLocation.Faction);
                        if (unlockPathEventPrefab != null)
                        {
                            var newEvent = unlockPathEventPrefab.CreateInstance();
                            ActiveEvents.Add(newEvent);
                        }
                        else
                        {
                            //if no event that unlocks the path can be found, unlock it automatically
                            level.StartLocation.Connections.ForEach(c => c.Locked = false);
                        }
                    }
                }
                RegisterNonRepeatableChildEvents(initialEventSet);
                void RegisterNonRepeatableChildEvents(EventSet eventSet)
                {
                    if (eventSet == null) { return; }
                    if (eventSet.OncePerLevel)
                    {
                        foreach (EventPrefab ep in eventSet.EventPrefabs.SelectMany(e => e.EventPrefabs))
                        {
                            nonRepeatableEvents.Add(ep.Identifier);                                
                        }
                    }
                    foreach (EventSet childSet in eventSet.ChildSets)
                    {
                        RegisterNonRepeatableChildEvents(childSet);
                    }
                }                
            }

            while (QueuedEventsForNextRound.TryDequeue(out var id))
            {
                var eventPrefab = EventSet.GetEventPrefab(id);
                if (eventPrefab == null)
                {
                    DebugConsole.ThrowError($"Error in EventManager.StartRound - could not find an event with the identifier {id}.");
                    continue;
                }
                var ev = eventPrefab.CreateInstance();
                if (ev != null)
                {
                    QueuedEvents.Enqueue(ev);
                }
            }

            PreloadContent(GetFilesToPreload());

            roundDuration = 0.0f;
            eventsInitialized = false;
            isCrewAway = false;
            crewAwayDuration = 0.0f;
            crewAwayResetTimer = 0.0f;
            intensityUpdateTimer = 0.0f;
            CalculateCurrentIntensity(0.0f);
            currentIntensity = musicIntensity = targetIntensity;
            eventCoolDown = 0.0f;
            CumulativeMonsterStrengthMain = 0;
            CumulativeMonsterStrengthRuins = 0;
            CumulativeMonsterStrengthWrecks = 0;
            CumulativeMonsterStrengthCaves = 0;
        }
        
        private void SelectSettings()
        {
            if (!EventManagerSettings.Prefabs.Any())
            {
                throw new InvalidOperationException("Could not select EventManager settings (no settings loaded).");
            }
            var orderedByDifficulty = EventManagerSettings.OrderedByDifficulty.ToArray();
            if (level == null)
            {
#if CLIENT
                if (GameMain.GameSession.GameMode is TestGameMode)
                {
                    settings = orderedByDifficulty.GetRandom(Rand.RandSync.ServerAndClient);
                    if (settings != null)
                    {
                        eventThreshold = settings.DefaultEventThreshold;
                    }
                    return;
                }
#endif
                throw new InvalidOperationException("Could not select EventManager settings (level not set).");
            }

            float extraDifficulty = 0;
            if (GameMain.GameSession.Campaign?.Settings != null)
            {
                extraDifficulty = GameMain.GameSession.Campaign.Settings.ExtraEventManagerDifficulty;
            }
            float modifiedDifficulty = Math.Clamp(level.Difficulty + extraDifficulty, 0, 100);
            var suitableSettings = EventManagerSettings.OrderedByDifficulty.Where(s =>
                modifiedDifficulty >= s.MinLevelDifficulty &&
                modifiedDifficulty <= s.MaxLevelDifficulty).ToArray();

            if (suitableSettings.Length == 0)
            {
                DebugConsole.ThrowError("No suitable event manager settings found for the selected level (difficulty " + level.Difficulty + ")");
                settings = orderedByDifficulty.GetRandom(Rand.RandSync.ServerAndClient);
            }
            else
            {
                settings = suitableSettings.GetRandom(Rand.RandSync.ServerAndClient);
            }
            if (settings != null)
            {
                eventThreshold = settings.DefaultEventThreshold;
            }
        }

        public IEnumerable<ContentFile> GetFilesToPreload()
        {
            foreach (List<Event> eventList in selectedEvents.Values)
            {
                foreach (Event ev in eventList)
                {
                    foreach (ContentFile contentFile in ev.GetFilesToPreload())
                    {
                        yield return contentFile;
                    }
                }
            }
        }

        public void PreloadContent(IEnumerable<ContentFile> contentFiles)
        {
            var filesToPreload = contentFiles.ToList();
            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub.WreckAI == null) { continue; }

                if (!sub.WreckAI.Config.DefensiveAgent.IsEmpty)
                {
                    var prefab = CharacterPrefab.FindBySpeciesName(sub.WreckAI.Config.DefensiveAgent);
                    if (prefab != null && !filesToPreload.Any(f => f.Path == prefab.FilePath))
                    {
                        filesToPreload.Add(prefab.ContentFile);
                    }
                }
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine != sub) { continue; }
                    foreach (Items.Components.ItemComponent component in item.Components)
                    {
                        if (component.statusEffectLists == null) { continue; }
                        foreach (var statusEffectList in component.statusEffectLists.Values)
                        {
                            foreach (StatusEffect statusEffect in statusEffectList)
                            {
                                foreach (var spawnInfo in statusEffect.SpawnCharacters)
                                {
                                    var prefab = CharacterPrefab.FindBySpeciesName(spawnInfo.SpeciesName);
                                    if (prefab != null && !filesToPreload.Contains(prefab.ContentFile))
                                    {
                                        filesToPreload.Add(prefab.ContentFile);
                                    }
                                }
                            }
                        }
                    }
                }                
            }

            foreach (ContentFile file in filesToPreload)
            {
                file.Preload(preloadedSprites.Add);
            }
        }

        public void EndRound()
        {
            pendingEventSets.Clear();
            selectedEvents.Clear();
            activeEvents.Clear();
            QueuedEvents.Clear();
            finishedEvents.Clear();
            nonRepeatableEvents.Clear();

            preloadedSprites.ForEach(s => s.Remove());
            preloadedSprites.Clear();


            pathFinder = null;
        }

        /// <summary>
        /// Registers the exhaustible events in the level as exhausted, and adds the current events to the event history
        /// </summary>
        public void RegisterEventHistory(bool registerFinishedOnly = false)
        {
            if (level?.LevelData == null) { return; }

            level.LevelData.EventsExhausted = !registerFinishedOnly;

            if (level.LevelData.Type == LevelData.LevelType.Outpost)
            {
                if (registerFinishedOnly)
                {
                    foreach (var finishedEvent in finishedEvents)
                    {
                        var key = finishedEvent.ParentSet;
                        if (key == null) { continue; }
                        if (level.LevelData.FinishedEvents.ContainsKey(key))
                        {
                            level.LevelData.FinishedEvents[key] += 1;
                        }
                        else
                        {
                            level.LevelData.FinishedEvents.Add(key, 1);
                        }
                    }
                }

                level.LevelData.EventHistory.AddRange(selectedEvents.Values
                    .SelectMany(v => v)
                    .Select(e => e.Prefab.Identifier)
                    .Where(eventId => Register(eventId) && !level.LevelData.EventHistory.Contains(eventId)));

                if (level.LevelData.EventHistory.Count > MaxEventHistory)
                {
                    level.LevelData.EventHistory.RemoveRange(0, level.LevelData.EventHistory.Count - MaxEventHistory);
                }
            }
            level.LevelData.NonRepeatableEvents.AddRange(nonRepeatableEvents.Where(eventId => Register(eventId) && !level.LevelData.NonRepeatableEvents.Contains(eventId)));

            if (!registerFinishedOnly)
            {
                level.LevelData.FinishedEvents.Clear();
            }

            bool Register(Identifier eventId) => !registerFinishedOnly || finishedEvents.Any(fe => fe.Prefab.Identifier == eventId);
        }

        public void SkipEventCooldown()
        {
            eventCoolDown = 0.0f;
        }

        private float CalculateCommonness(EventPrefab eventPrefab, float baseCommonness)
        {
            if (level.LevelData.NonRepeatableEvents.Contains(eventPrefab.Identifier)) { return 0.0f; }
            float retVal = baseCommonness;
            if (level.LevelData.EventHistory.Contains(eventPrefab.Identifier)) { retVal *= 0.1f; }
            return retVal;
        }

        private void CreateEvents(EventSet eventSet)
        {
            selectedEvents.Remove(eventSet);
            if (level == null) { return; }
            if (level.LevelData.HasHuntingGrounds && eventSet.DisableInHuntingGrounds) { return; }
            if (eventSet.Exhaustible && level.LevelData.EventsExhausted) { return; }

            DebugConsole.NewMessage($"Loading event set {eventSet.Identifier}", Color.LightBlue, debugOnly: true);

            int applyCount = 1;
            List<Func<Level.InterestingPosition, bool>> spawnPosFilter = new List<Func<Level.InterestingPosition, bool>>();
            if (eventSet.PerRuin)
            {
                applyCount = level.Ruins.Count;
                foreach (var ruin in level.Ruins)
                {
                    spawnPosFilter.Add(pos => pos.Ruin == ruin);
                }
            }
            else if (eventSet.PerCave)
            {
                applyCount = level.Caves.Count;
                foreach (var cave in level.Caves)
                {
                    spawnPosFilter.Add(pos => pos.Cave == cave);
                }
            }
            else if (eventSet.PerWreck)
            {
                var wrecks = Submarine.Loaded.Where(s => s.Info.IsWreck && (s.WreckAI == null || !s.WreckAI.IsAlive));
                applyCount = wrecks.Count();
                foreach (var wreck in wrecks)
                {
                    spawnPosFilter.Add(pos => pos.Submarine == wreck);
                }
            }

            bool isPrefabSuitable(EventPrefab e) =>
                (e.BiomeIdentifier.IsEmpty || e.BiomeIdentifier == level.LevelData?.Biome?.Identifier) &&
                !level.LevelData.NonRepeatableEvents.Contains(e.Identifier) &&
                isFactionSuitable(e.Faction);

            bool isFactionSuitable(Identifier factionId) =>
                factionId.IsEmpty || factionId == level.StartLocation?.Faction?.Prefab.Identifier || factionId == level.StartLocation?.SecondaryFaction?.Prefab.Identifier;

            foreach (var subEventPrefab in eventSet.EventPrefabs)
            {
                foreach (Identifier missingId in subEventPrefab.GetMissingIdentifiers())
                {
                    DebugConsole.ThrowError($"Error in event set \"{eventSet.Identifier}\" ({eventSet.ContentFile?.ContentPackage?.Name ?? "null"}) - could not find an event prefab with the identifier \"{missingId}\".");
                }
            }

            var suitablePrefabSubsets = eventSet.EventPrefabs.Where(
                e => isFactionSuitable(e.Faction) && e.EventPrefabs.Any(isPrefabSuitable)).ToArray();

            for (int i = 0; i < applyCount; i++)
            {
                if (eventSet.ChooseRandom)
                {
                    if (suitablePrefabSubsets.Any())
                    {
                        var unusedEvents = suitablePrefabSubsets.ToList();
                        int eventCount = eventSet.GetEventCount(level);
                        for (int j = 0; j < eventCount; j++)
                        {
                            if (unusedEvents.All(e => e.EventPrefabs.All(p => CalculateCommonness(p, e.Commonness) <= 0.0f))) { break; }
                            EventSet.SubEventPrefab subEventPrefab = ToolBox.SelectWeightedRandom(unusedEvents, e => e.EventPrefabs.Max(p => CalculateCommonness(p, e.Commonness)), random);
                            (IEnumerable<EventPrefab> eventPrefabs, float commonness, float probability) = subEventPrefab;
                            if (eventPrefabs != null && random.NextDouble() <= probability)
                            {
                                var eventPrefab = ToolBox.SelectWeightedRandom(eventPrefabs.Where(isPrefabSuitable), e => e.Commonness, random);
                                var newEvent = eventPrefab.CreateInstance();
                                if (newEvent == null) { continue; }
                                newEvent.RandomSeed = randomSeed;
                                if (i < spawnPosFilter.Count) { newEvent.SpawnPosFilter = spawnPosFilter[i]; }
                                DebugConsole.NewMessage($"Initialized event {newEvent}", debugOnly: true);
                                if (!selectedEvents.ContainsKey(eventSet))
                                {
                                    selectedEvents.Add(eventSet, new List<Event>());
                                }
                                selectedEvents[eventSet].Add(newEvent);
                                unusedEvents.Remove(subEventPrefab);
                            }
                        }
                    }
                    if (eventSet.ChildSets.Any())
                    {
                        var newEventSet = SelectRandomEvents(eventSet.ChildSets, random: random);
                        if (newEventSet != null)
                        {
                            CreateEvents(newEventSet);
                        }
                    }
                }
                else
                {
                    foreach ((IEnumerable<EventPrefab> eventPrefabs, float commonness, float probability) in suitablePrefabSubsets)
                    {
                        if (random.NextDouble() > probability) { continue; }

                        var eventPrefab = ToolBox.SelectWeightedRandom(eventPrefabs.Where(isPrefabSuitable), e => e.Commonness, random);
                        var newEvent = eventPrefab.CreateInstance();
                        if (newEvent == null) { continue; }
                        if (!selectedEvents.ContainsKey(eventSet))
                        {
                            selectedEvents.Add(eventSet, new List<Event>());
                        }
                        selectedEvents[eventSet].Add(newEvent);
                    }

                    var location = GetEventLocation();
                    foreach (EventSet childEventSet in eventSet.ChildSets)
                    {
                        if (!IsValidForLevel(childEventSet, level)) { continue; }
                        if (!IsValidForLocation(childEventSet, location)) { continue; }
                        CreateEvents(childEventSet);
                    }
                }
            }
        }

        private EventSet SelectRandomEvents(IReadOnlyList<EventSet> eventSets, bool? requireCampaignSet = null, Random random = null)
        {
            if (level == null) { return null; }
            Random rand = random ?? new MTRandom(ToolBox.StringToInt(level.Seed));

            var allowedEventSets = 
                eventSets.Where(set => IsValidForLevel(set, level));

            if (requireCampaignSet.HasValue)
            {
                if (requireCampaignSet.Value)
                {
                    if (allowedEventSets.Any(es => es.IsCampaignSet))
                    {
                        allowedEventSets =
                            allowedEventSets.Where(es => es.IsCampaignSet);
                    }
                    else
                    {
                        DebugConsole.AddWarning("No campaign event sets available. Using a non-campaign-specific set instead.");
                    }
                }
                else
                {
                    allowedEventSets =
                        allowedEventSets.Where(es => !es.IsCampaignSet);
                }
            }

            var location = GetEventLocation();
            allowedEventSets = allowedEventSets.Where(set => IsValidForLocation(set, location));

            allowedEventSets = allowedEventSets.Where(set => !set.CampaignTutorialOnly ||
                (GameMain.IsSingleplayer && GameMain.GameSession?.Campaign?.Settings is { TutorialEnabled: true }));

            int? discoveryIndex = GameMain.GameSession?.Map?.GetDiscoveryIndex(location);
            int? visitIndex = GameMain.GameSession?.Map?.GetVisitIndex(location);
            if (discoveryIndex is not null && discoveryIndex >= 0 && allowedEventSets.Any(set => set.ForceAtDiscoveredNr == discoveryIndex))
            {
                allowedEventSets = allowedEventSets.Where(set => set.ForceAtDiscoveredNr == discoveryIndex);
            }
            else if (visitIndex is not null && visitIndex >= 0 && allowedEventSets.Any(set => set.ForceAtVisitedNr == visitIndex))
            {
                allowedEventSets = allowedEventSets.Where(set => set.ForceAtVisitedNr == visitIndex);
            }
            else
            {
                // When there are no forced sets, only allow sets that aren't forced at any specific location
                allowedEventSets = allowedEventSets.Where(set => set.ForceAtDiscoveredNr < 0 && set.ForceAtVisitedNr < 0);
            }

            if (allowedEventSets.Count() == 1)
            {
                // When there's only a single set available, just select it directly
                return allowedEventSets.First();
            }

            float totalCommonness = allowedEventSets.Sum(e => e.GetCommonness(level));
            float randomNumber = (float)rand.NextDouble();
            randomNumber *= totalCommonness;
            foreach (EventSet eventSet in allowedEventSets)
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

        private static bool IsValidForLevel(EventSet eventSet, Level level)
        {
            return
                level.Difficulty >= eventSet.MinLevelDifficulty && level.Difficulty <= eventSet.MaxLevelDifficulty &&
                level.LevelData.Type == eventSet.LevelType &&
                (eventSet.BiomeIdentifier.IsEmpty || eventSet.BiomeIdentifier == level.LevelData.Biome.Identifier);
        }

        private bool IsValidForLocation(EventSet eventSet, Location location)
        {
            if (location is null) { return true; }
            if (!eventSet.Faction.IsEmpty)
            {
                if (eventSet.Faction != location.Faction?.Prefab.Identifier && eventSet.Faction != location.SecondaryFaction?.Prefab.Identifier) { return false; }
            }
            var locationType = location.GetLocationType();
            bool includeGenericEvents = level.Type == LevelData.LevelType.LocationConnection || !locationType.IgnoreGenericEvents;
            if (includeGenericEvents && eventSet.LocationTypeIdentifiers == null) { return true; }
            return eventSet.LocationTypeIdentifiers != null && eventSet.LocationTypeIdentifiers.Any(identifier => identifier == locationType.Identifier);
        }

        private Location GetEventLocation()
        {
            return GameMain.GameSession?.Campaign?.Map?.CurrentLocation ?? level?.StartLocation;
        }

        private bool CanStartEventSet(EventSet eventSet)
        {
            ISpatialEntity refEntity = GetRefEntity();
            float distFromStart = (float)Math.Sqrt(MathUtils.LineSegmentToPointDistanceSquared(level.StartExitPosition.ToPoint(), level.StartPosition.ToPoint(), refEntity.WorldPosition.ToPoint()));
            float distFromEnd = (float)Math.Sqrt(MathUtils.LineSegmentToPointDistanceSquared(level.EndExitPosition.ToPoint(), level.EndPosition.ToPoint(), refEntity.WorldPosition.ToPoint()));

            //don't create new events if within 50 meters of the start/end of the level
            if (!eventSet.AllowAtStart)
            {
                if (distanceTraveled <= 0.0f ||
                    distFromStart * Physics.DisplayToRealWorldRatio < 50.0f ||
                    distFromEnd * Physics.DisplayToRealWorldRatio < 50.0f)
                {
                    return false;
                }
            }

            if (eventSet.DelayWhenCrewAway)
            {
                if ((isCrewAway && crewAwayDuration < settings.FreezeDurationWhenCrewAway) || crewAwayResetTimer > 0.0f)
                {
                    return false;
                }
            }

            if ((Submarine.MainSub == null || distanceTraveled < eventSet.MinDistanceTraveled) &&
                roundDuration < eventSet.MinMissionTime)
            {
                return false;
            }

            if (CurrentIntensity < eventSet.MinIntensity || CurrentIntensity > eventSet.MaxIntensity)
            {
                return false;
            }

            return true;
        }

        private bool eventsInitialized;
        
        public void Update(float deltaTime)
        {
            if (!Enabled || level == null) { return; }
            if (GameMain.GameSession.Campaign?.DisableEvents ?? false) { return; }

            if (!eventsInitialized)
            {
                foreach (var eventSet in selectedEvents.Keys)
                {
                    foreach (var ev in selectedEvents[eventSet])
                    {
                        ev.Init(eventSet);
                    }
                }
                eventsInitialized = true;
            }

            //clients only calculate the intensity but don't create any events
            //(the intensity is used for controlling the background music)
            CalculateCurrentIntensity(deltaTime);

#if DEBUG && SERVER
            if (DateTime.Now > nextIntensityLogTime)
            {
                DebugConsole.NewMessage("EventManager intensity: " + (int)Math.Round(currentIntensity * 100) + " %");
                nextIntensityLogTime = DateTime.Now + new TimeSpan(0, minutes: 1, seconds: 0);
            }
#endif

            if (isClient) { return; }

            roundDuration += deltaTime;

            if (settings == null)
            {
                DebugConsole.ThrowError("Event settings not set before updating EventManager. Attempting to select...");
                SelectSettings();
                if (settings == null)
                {
                    DebugConsole.ThrowError("Could not select EventManager settings. Disabling EventManager for the round...");
#if SERVER
                    GameMain.Server?.SendChatMessage("Could not select EventManager settings. Disabling EventManager for the round...", Networking.ChatMessageType.Error);
#endif
                    Enabled = false;
                    return;
                }
            }

            if (IsCrewAway())
            {
                isCrewAway = true;
                crewAwayResetTimer = CrewAwayResetDelay;
                crewAwayDuration += deltaTime;
            }
            else if (crewAwayResetTimer > 0.0f)
            {
                isCrewAway = false;
                crewAwayResetTimer -= deltaTime;
            }
            else
            {
                isCrewAway = false;
                crewAwayDuration = 0.0f;
                eventThreshold += settings.EventThresholdIncrease * deltaTime;
                eventThreshold = Math.Min(eventThreshold, 1.0f);
                eventCoolDown -= deltaTime;
            }

            calculateDistanceTraveledTimer -= deltaTime;
            if (calculateDistanceTraveledTimer <= 0.0f)
            {
                distanceTraveled = CalculateDistanceTraveled();
                calculateDistanceTraveledTimer = CalculateDistanceTraveledInterval;
            }

            bool recheck = false;
            do
            {
                recheck = false;
                //activate pending event sets that can be activated
                for (int i = pendingEventSets.Count - 1; i >= 0; i--)
                {
                    var eventSet = pendingEventSets[i];
                    if (eventCoolDown > 0.0f && !eventSet.IgnoreCoolDown) { continue; }
                    if (currentIntensity > eventThreshold && !eventSet.IgnoreIntensity) { continue; }
                    if (!CanStartEventSet(eventSet)) { continue; }

                    pendingEventSets.RemoveAt(i);

                    if (selectedEvents.ContainsKey(eventSet))
                    {
                        //start events in this set
                        foreach (Event ev in selectedEvents[eventSet])
                        {
                            activeEvents.Add(ev);
                            eventThreshold = settings.DefaultEventThreshold;
                            if (eventSet.TriggerEventCooldown && selectedEvents[eventSet].Any(e => e.Prefab.TriggerEventCooldown))
                            {
                                eventCoolDown = settings.EventCooldown;
                            }
                            if (eventSet.ResetTime > 0)
                            {
                                ev.Finished += () =>
                                {
                                    pendingEventSets.Add(eventSet);
                                    CreateEvents(eventSet);
                                };
                            }
                        }
                    }

                    //add child event sets to pending
                    foreach (EventSet childEventSet in eventSet.ChildSets)
                    {
                        pendingEventSets.Add(childEventSet);
                        recheck = true;
                    }
                }
            } while (recheck);

            foreach (Event ev in activeEvents)
            {
                if (!ev.IsFinished) 
                { 
                    ev.Update(deltaTime); 
                }
                else if (ev.Prefab != null && !finishedEvents.Any(e => e.Prefab == ev.Prefab))
                {
                    if (level?.LevelData != null && level.LevelData.Type == LevelData.LevelType.Outpost)
                    {
                        if (!level.LevelData.EventHistory.Contains(ev.Prefab.Identifier)) { level.LevelData.EventHistory.Add(ev.Prefab.Identifier); }
                    }
                    finishedEvents.Add(ev);
                }
            }

            if (QueuedEvents.Count > 0)
            {
                activeEvents.Add(QueuedEvents.Dequeue());
            }
        }
                
        private void CalculateCurrentIntensity(float deltaTime)
        {
            intensityUpdateTimer -= deltaTime;
            if (intensityUpdateTimer > 0.0f) { return; }
            intensityUpdateTimer = IntensityUpdateInterval;

            // crew health --------------------------------------------------------

            avgCrewHealth = 0.0f;
            int characterCount = 0;
            foreach (Character character in Character.CharacterList)
            {
                if (character.IsDead || character.TeamID == CharacterTeamType.FriendlyNPC) { continue; }
                if (character.AIController is HumanAIController || character.IsRemotePlayer)
                {
                    avgCrewHealth += character.Vitality / character.MaxVitality * (character.IsUnconscious ? 0.5f : 1.0f);
                    characterCount++;
                }
            }
            if (characterCount > 0)
            {
                avgCrewHealth /= characterCount;
            }
            else
            {
                avgCrewHealth = 0.5f;
            }

            // enemy amount --------------------------------------------------------

            enemyDanger = 0.0f;
            monsterStrength = 0;
            foreach (Character character in Character.CharacterList)
            {
                if (character.IsIncapacitated || character.IsArrested || !character.Enabled || character.IsPet) { continue; }

                if (character.AIController is EnemyAIController enemyAI)
                {
                    if (!enemyAI.AIParams.StayInAbyss)
                    {
                        // Ignore abyss monsters because they can stay active for quite great distances. They'll be taken into account when they target the sub.
                        monsterStrength += enemyAI.CombatStrength;
                    }

                    if (character.CurrentHull?.Submarine?.Info != null &&
                        (character.CurrentHull.Submarine == Submarine.MainSub || Submarine.MainSub.DockedTo.Contains(character.CurrentHull.Submarine)) &&
                        character.CurrentHull.Submarine.Info.Type == SubmarineType.Player)
                    {
                        // Enemy onboard -> Crawler inside the sub adds 0.2 to enemy danger, Mudraptor 0.42
                        enemyDanger += enemyAI.CombatStrength / 500.0f;
                    }
                    else if (enemyAI.SelectedAiTarget?.Entity?.Submarine != null)
                    {
                        // Enemy outside targeting the sub or something in it
                        // -> One Crawler adds 0.02, a Mudraptor 0.042, a Hammerhead 0.1, and a Moloch 0.25.
                        enemyDanger += enemyAI.CombatStrength / 5000.0f;
                    }
                }
                else if (character.AIController is HumanAIController humanAi && !character.IsOnFriendlyTeam(CharacterTeamType.Team1))
                {
                    if (character.Submarine != null &&
                        character.Submarine.PhysicsBody is { BodyType: BodyType.Dynamic } &&
                        Vector2.DistanceSquared(character.Submarine.WorldPosition, Submarine.MainSub.WorldPosition) < Sonar.DefaultSonarRange * Sonar.DefaultSonarRange)
                    {
                        //we have no easy way to define the strength of a human enemy (depends more on the sub and it's state than the character),
                        //so let's just go with a fixed value.
                        //5 living enemy characters in an enemy sub in sonar range is enough to bump the intensity to max
                        enemyDanger += 0.2f;
                    }
                }
            }

            // Add a portion of the total strength of active monsters to the enemy danger so that we don't spawn too many monsters around the sub.
            // On top of the existing value, so if 10 crawlers are targeting the sub simultaneously from outside, the final value would be: 0.02 x 10 + 0.2 = 0.4.
            // And if they get inside, we add 0.1 per crawler on that.
            // So, in practice the danger per enemy that is attacking the sub is half of what it would be when the enemy is not targeting the sub.
            // 10 Crawlers -> +0.2 (0.4 in total if all target the sub from outside).
            // 5 Mudraptors -> +0.21 (0.42 in total, before they get inside).
            // 3 Hammerheads -> +0.3 (0.6 in total, if they all target the sub).
            // 2 Molochs -> +0.5 (1.0 in total, if both target the sub).
            enemyDanger += monsterStrength / 5000f;
            enemyDanger = MathHelper.Clamp(enemyDanger, 0.0f, 1.0f);

            // The definitions above aim for that we never spawn more monsters that the player (and the performance) can handle.
            // Some examples that result in the max intensity even when the creatures would just idle around.
            // The values are theoretical, because in practice many of the monsters are targeting the sub, which will double the danger of those monster and effectively halve the max monster count.
            // In practice we don't use the max intensity. For example on level 50 we use max intensity 50, which would mean that we'd halve the numbers below.
            // There's no hard cap for the monster count, but if the amount of monsters is higher than this, we don't spawn more monsters from the events:
            // 50 Crawlers (We shouldn't actually ever spawn that many. 12 is the max per event, but theoretically 25 crawlers would result in max intensity).
            // 25 Tigerthreshers (Max 9 per event. 12 targeting the sub at the same time results in max intensity).
            // 10 Hammerheads (Max 3 per event. 5 targeting the sub at the same time results in max intensity).
            // 4 Molochs (Max 2 per event and 2 targeting the sub at the same time results in max intensity).

            // hull status (gaps, flooding, fire) --------------------------------------------------------

            float holeCount = 0.0f;
            float waterAmount = 0.0f;
            float dryHullVolume = 0.0f;
            foreach (Hull hull in Hull.HullList)
            {
                if (hull.Submarine == null || hull.Submarine.Info.Type != SubmarineType.Player) { continue; } 
                if (GameMain.GameSession?.GameMode is PvPMode)
                {
                    if (hull.Submarine.TeamID != CharacterTeamType.Team1 && hull.Submarine.TeamID != CharacterTeamType.Team2) { continue; }
                }
                else
                {
                    if (hull.Submarine.TeamID != CharacterTeamType.Team1) { continue; }
                }
                fireAmount += hull.FireSources.Sum(fs => fs.Size.X);
                if (hull.IsWetRoom) { continue; }
                foreach (Gap gap in hull.ConnectedGaps)
                {
                    if (!gap.IsRoomToRoom)
                    {
                        holeCount += gap.Open;
                    }
                }
                waterAmount += hull.WaterVolume;
                dryHullVolume += hull.Volume;
            }
            if (dryHullVolume > 0)
            {
                floodingAmount = waterAmount / dryHullVolume;
            }

            //hull integrity at 0.0 if there are 10 or more wide-open holes
            avgHullIntegrity = MathHelper.Clamp(1.0f - holeCount / 10.0f, 0.0f, 1.0f);
            
            //a fire of any size bumps up the fire amount to 20%
            //if the total width of the fires is 1000 or more, the fire amount is considered to be at 100%
            fireAmount = MathHelper.Clamp(fireAmount / 1000.0f, fireAmount > 0.0f ? 0.2f : 0.0f, 1.0f);

            //flooding less than 10% of the sub is ignored 
            //to prevent ballast tanks from affecting the intensity
            if (floodingAmount < 0.1f) 
            {
                floodingAmount = 0.0f;
            }
            else 
            {
                floodingAmount *= 1.5f;
            }

            // calculate final intensity --------------------------------------------------------

            targetIntensity = 
                ((1.0f - avgCrewHealth) + (1.0f - avgHullIntegrity) + floodingAmount) / 3.0f;
            targetIntensity += fireAmount * 0.5f;
            targetIntensity += enemyDanger;
            targetIntensity = MathHelper.Clamp(targetIntensity, 0.0f, 1.0f);

            if (targetIntensity > currentIntensity)
            {
                //25 seconds for intensity to go from 0.0 to 1.0
                currentIntensity = Math.Min(currentIntensity + 0.04f * IntensityUpdateInterval, targetIntensity);
                //20 seconds for intensity to go from 0.0 to 1.0
                musicIntensity = Math.Min(musicIntensity + 0.05f * IntensityUpdateInterval, targetIntensity);
            }
            else
            {
                //400 seconds for intensity to go from 1.0 to 0.0
                currentIntensity = Math.Max(currentIntensity - 0.0025f * IntensityUpdateInterval, targetIntensity);
                //20 seconds for intensity to go from 1.0 to 0.0
                musicIntensity = Math.Max(musicIntensity - 0.05f * IntensityUpdateInterval, targetIntensity);
            }
        }

        private float CalculateDistanceTraveled()
        {
            if (level == null || pathFinder == null) { return 0.0f; }
            var refEntity = GetRefEntity();
            if (refEntity == null) { return 0.0f; }
            Vector2 target = ConvertUnits.ToSimUnits(level.EndPosition);
            var steeringPath = pathFinder.FindPath(ConvertUnits.ToSimUnits(refEntity.WorldPosition), target);
            if (steeringPath.Unreachable || float.IsPositiveInfinity(totalPathLength))
            {
                //use horizontal position in the level as a fallback if a path can't be found
                return MathHelper.Clamp((refEntity.WorldPosition.X - level.StartPosition.X) / (level.EndPosition.X - level.StartPosition.X), 0.0f, 1.0f);
            }
            else
            {
                return MathHelper.Clamp(1.0f - steeringPath.TotalLength / totalPathLength, 0.0f, 1.0f);
            }
        }

        /// <summary>
        /// Finds all actions in a ScriptedEvent
        /// </summary>
        private static List<Tuple<int, EventAction>> FindActions(ScriptedEvent scriptedEvent)
        {
            var list = new List<Tuple<int, EventAction>>();
            foreach (EventAction eventAction in scriptedEvent.Actions)
            {
                list.AddRange(FindActionsRecursive(eventAction));
            }

            return list;

            static List<Tuple<int, EventAction>> FindActionsRecursive(EventAction eventAction, int ident = 1)
            {
                var eventActions = new List<Tuple<int, EventAction>> { Tuple.Create(ident, eventAction) };

                ident++;
                
                foreach (var action in eventAction.GetSubActions())
                {
                    eventActions.AddRange(FindActionsRecursive(action, ident));
                }

                return eventActions;
            }
        }


        /// <summary>
        /// Get the entity that should be used in determining how far the player has progressed in the level.
        /// = The submarine or player character that has progressed the furthest. 
        /// </summary>
        public static ISpatialEntity GetRefEntity()
        {
            ISpatialEntity refEntity = Submarine.MainSub;
#if CLIENT
            if (Character.Controlled != null)
            {
                if (Character.Controlled.Submarine != null && 
                    Character.Controlled.Submarine.Info.Type == SubmarineType.Player)
                {
                    refEntity = Character.Controlled.Submarine;
                }
                else
                {
                    refEntity = Character.Controlled;
                }
            }
#else
            foreach (Barotrauma.Networking.Client client in GameMain.Server.ConnectedClients)
            {
                if (client.Character == null) { continue; }
                //only take the players inside a player sub into account. 
                //Otherwise the system could be abused by for example making a respawned player wait
                //close to the destination outpost
                if (client.Character.Submarine != null && 
                    client.Character.Submarine.Info.Type == SubmarineType.Player)
                {
                    if (client.Character.Submarine.WorldPosition.X > refEntity.WorldPosition.X)
                    {
                        refEntity = client.Character.Submarine;
                    }
                }
            }
#endif
            return refEntity;
        }

        private bool IsCrewAway()
        {
#if CLIENT
            return Character.Controlled != null && IsCharacterAway(Character.Controlled);
#else
            int playerCount = 0;
            int awayPlayerCount = 0;
            foreach (Barotrauma.Networking.Client client in GameMain.Server.ConnectedClients)
            {
                if (client.Character == null || client.Character.IsDead || client.Character.IsIncapacitated) { continue; }
                
                playerCount++;
                if (IsCharacterAway(client.Character)) { awayPlayerCount++; }
            }
            return playerCount > 0 && awayPlayerCount / (float)playerCount > 0.5f;
#endif
        }

        private bool IsCharacterAway(Character character)
        {
            if (character.Submarine != null)
            {
                switch (character.Submarine.Info.Type)
                {
                    case SubmarineType.Player:
                    case SubmarineType.Outpost:
                    case SubmarineType.OutpostModule:
                        return false;
                    case SubmarineType.Wreck:
                    case SubmarineType.BeaconStation:
                    case SubmarineType.Ruin:
                        return true;
                }
            }

            const int maxDist = 1000;

            if (level != null && !level.Removed)
            {
                foreach (var ruin in level.Ruins)
                {
                    Rectangle area = ruin.Area;
                    area.Inflate(maxDist, maxDist);
                    if (area.Contains(character.WorldPosition)) { return true; }
                }
                foreach (var cave in level.Caves)
                {
                    Rectangle area = cave.Area;
                    area.Inflate(maxDist, maxDist);
                    if (area.Contains(character.WorldPosition)) { return true; }
                }
            }

            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub.Info.Type != SubmarineType.BeaconStation && sub.Info.Type != SubmarineType.Wreck) { continue; }
                Rectangle worldBorders = new Rectangle(
                    sub.Borders.X + (int)sub.WorldPosition.X - maxDist,
                    sub.Borders.Y + (int)sub.WorldPosition.Y + maxDist,
                    sub.Borders.Width + maxDist * 2,
                    sub.Borders.Height + maxDist * 2);
                if (Submarine.RectContains(worldBorders, character.WorldPosition))
                {
                    return true;
                }
            }

            return false;
        }

        public void Load(XElement element)
        {
            foreach (var id in element.GetAttributeIdentifierArray(nameof(QueuedEventsForNextRound), Array.Empty<Identifier>()))
            {
                QueuedEventsForNextRound.Enqueue(id);
            }
        }

        public XElement Save()
        {
            return new XElement("eventmanager",
                new XAttribute(nameof(QueuedEventsForNextRound),
                    string.Join(',', QueuedEventsForNextRound)));
        }
    }
}
