#nullable enable

using Barotrauma.Extensions;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    sealed partial class TraitorManager
   {
        const int MaxPreviousEventHistory = 10;

        const int StartDelayMin = 60;
        const int StartDelayMax = 200;

        private float startTimer;
        private bool started = false;

        private TraitorResults? results = null;

        public class PreviousTraitorEvent
        {
            public TraitorEventPrefab TraitorEvent { get; }
            public TraitorEvent.State State { get; }

            public Client Traitor =>
                GameMain.Server.ConnectedClients.Find(c => traitorAccountId.IsSome() && traitorAccountId == c.AccountId) ??
                GameMain.Server.ConnectedClients.Find(c => traitorAddress == c.Connection.Endpoint.Address);

            private readonly Address traitorAddress;
            private readonly Option<AccountId> traitorAccountId;

            public PreviousTraitorEvent(TraitorEventPrefab traitorEvent, TraitorEvent.State state, Client traitor)
            {
                TraitorEvent = traitorEvent;
                State = state;
                traitorAddress = traitor.Connection.Endpoint.Address;
                traitorAccountId = traitor.AccountId;
            }

            private PreviousTraitorEvent(TraitorEventPrefab traitorEvent, TraitorEvent.State state, Option<AccountId> accountId, Address address)
            {
                TraitorEvent = traitorEvent;
                State = state;
                traitorAddress = address;
                traitorAccountId = accountId;
            }

            public void Save(XElement parentElement)
            {
                parentElement.Add(
                    new XElement(nameof(PreviousTraitorEvent),
                        new XAttribute("id", TraitorEvent.Identifier),
                        new XAttribute("state", State),
                        new XAttribute("accountid", traitorAccountId),
                        new XAttribute("address", traitorAddress)));
            }

            public static PreviousTraitorEvent? Load(XElement subElement)
            {
                var traitorEventId = subElement.GetAttributeIdentifier("id", Identifier.Empty);
                var state = subElement.GetAttributeEnum("state", Barotrauma.TraitorEvent.State.Failed);
                var accountId = Networking.AccountId.Parse(
                    subElement.GetAttributeString("accountid", null)
                    ?? subElement.GetAttributeString("steamid", ""));
                var address = Address.Parse(
                        subElement.GetAttributeString("address", null)
                        ?? subElement.GetAttributeString("endpoint", ""))
                    .Fallback(new UnknownAddress());
                if (EventPrefab.Prefabs.TryGet(traitorEventId, out EventPrefab? prefab) && prefab is TraitorEventPrefab traitorEventPrefab)
                {
                    return new PreviousTraitorEvent(traitorEventPrefab, state, accountId, address);
                }
                else
                {
                    DebugConsole.ThrowError($"Error when loading {nameof(TraitorManager)}: could not find a traitor event prefab with the identifier \"{traitorEventId}\".");
                    return null;
                }
            }
        }

        public readonly record struct ActiveTraitorEvent(
            Client Traitor,
            TraitorEvent TraitorEvent);

        private readonly List<PreviousTraitorEvent> previousTraitorEvents = new List<PreviousTraitorEvent>();

        private readonly List<ActiveTraitorEvent> activeEvents = new List<ActiveTraitorEvent>();

        public IEnumerable<ActiveTraitorEvent> ActiveEvents => activeEvents;

        private readonly GameServer server;

        private EventManager? eventManager;
        private Level? level;

        public bool Enabled;

        public bool IsTraitor(Character character)
        {
            return activeEvents.Any(e => e.Traitor.Character == character);
        }

        public TraitorManager(GameServer server)
        {
            this.server = server;
        }

        public void Initialize(EventManager eventManager, Level level)
        {
            this.eventManager = eventManager;
            this.level = level;
            startTimer = Rand.Range(StartDelayMin, StartDelayMax); 
            started = false;
            results = null;
        }

        private bool TryCreateTraitorEvents(EventManager eventManager, Level level)
        {
            var eventPrefabs = EventPrefab.Prefabs.Where(p => p is TraitorEventPrefab).Cast<TraitorEventPrefab>();
            if (!eventPrefabs.Any())
            {
                DebugConsole.AddWarning("No traitor event available in any of the enabled content packages.");
                return false;
            }

            if (server.ConnectedClients.Count(IsClientViableTraitor) < server.ServerSettings.TraitorsMinPlayerCount)
            {
                DebugConsole.AddWarning("Not enough clients to create a traitor event. Active traitor events: " + activeEvents.Count);
#if DEBUG
                DebugConsole.AddWarning("Starting a traitor event anyway because this is a debug build.");
#else
                return false;
#endif

            }

            int maxDangerLevel = server.ServerSettings.TraitorDangerLevel;

            int playerCount = server.ConnectedClients.Count(c => c.Character != null && !c.Character.Removed);

            var campaign = GameMain.GameSession?.Campaign;
            var suitablePrefabs = eventPrefabs
                .Where(e => EventManager.IsLevelSuitable(e, level))
                .Where(e => e.ReputationRequirementsMet(campaign))
                .Where(e => e.MissionRequirementsMet(GameMain.GameSession))
                .Where(e => e.LevelRequirementsMet(level))
                .Where(e => e.DangerLevel <= maxDangerLevel)
                .Where(e => playerCount >= e.MinPlayerCount)
                .ToList();

            if (!suitablePrefabs.Any())
            {
                //this is normal, there e.g. might be no missions for an abandoned outpost or end level
                DebugConsole.Log("No suitable traitor missions available for the level.");
                return false;
            }

            foreach (var previousEvent in previousTraitorEvents.Reverse<PreviousTraitorEvent>().DistinctBy(e => e.Traitor))
            {
                if (previousEvent.State == TraitorEvent.State.Completed &&
                    previousEvent.TraitorEvent.IsChainable &&
                    IsClientViableTraitor(previousEvent.Traitor))
                {
                    GameServer.Log($"{NetworkMember.ClientLogName(previousEvent.Traitor)} successfully completed a traitor event ({previousEvent.TraitorEvent.Identifier}) on a previous round. Attempting to give choose them a new, more dangerous event...", ServerLog.MessageType.Traitors);
                    
                    var suitablePrefab = 
                        //try finding an event that's continuation from the previous one (= requires the previous one to be completed 1st)
                        suitablePrefabs.GetRandomUnsynced(p => p.RequiredCompletedTags.Any(t => 
                            previousEvent.TraitorEvent.Identifier == t || previousEvent.TraitorEvent.Tags.Contains(t)));

                    suitablePrefab ??=
                        //otherwise try finding some higher-difficult event for the same faction
                        suitablePrefabs.GetRandomUnsynced(p =>
                            p.RequiredCompletedTags.None() &&
                            p.DangerLevel > previousEvent.TraitorEvent.DangerLevel && 
                            p.Faction == previousEvent.TraitorEvent.Faction);

                    if (suitablePrefab != null)
                    {
                        CreateTraitorEvent(eventManager, suitablePrefab, previousEvent.Traitor);
                        return true;
                    }
                    else
                    {
                        GameServer.Log($"Could not find a suitable, more difficult traitor event for {NetworkMember.ClientLogName(previousEvent.Traitor)}.", ServerLog.MessageType.Traitors);
                    }
                }
            }

            TraitorEventPrefab selectedPrefab;
            if (GameMain.GameSession?.Campaign == null)
            {
                selectedPrefab = suitablePrefabs.GetRandomByWeight(GetTraitorEventPrefabCommonness, Rand.RandSync.Unsynced);
            }
            else
            {
                //events that are suitable as initial traitor events (not requiring some other event to be completed first)
                var suitableInitialPrefabs = suitablePrefabs.FindAll(e => e.RequiredCompletedTags.None() && IsSuitableDangerLevel(e));

                bool IsSuitableDangerLevel(TraitorEventPrefab prefab)
                {
                    if (prefab.DangerLevel == TraitorEventPrefab.MinDangerLevel) { return true; }
                    //events that require another event to be completed are handled earlier in the method
                    if (prefab.RequirePreviousDangerLevelCompleted) { return false; }     
                    return 
                        prefab.RequiredPreviousDangerLevel < TraitorEventPrefab.MinDangerLevel || 
                        previousTraitorEvents.Any(e2 => e2.TraitorEvent.DangerLevel >= prefab.RequiredPreviousDangerLevel);                    
                }

                selectedPrefab = suitableInitialPrefabs.GetRandomByWeight(GetTraitorEventPrefabCommonness, Rand.RandSync.Unsynced);
                if (selectedPrefab == null)
                {
                    GameServer.Log($"Could not find a suitable danger level {TraitorEventPrefab.MinDangerLevel} traitor event. Choosing a random event instead.", ServerLog.MessageType.Traitors);
                    selectedPrefab = suitablePrefabs.GetRandomByWeight(GetTraitorEventPrefabCommonness, Rand.RandSync.Unsynced);
                }
            }

            if (selectedPrefab != null)
            {
                var selectedTraitor = SelectRandomTraitor();
                if (selectedTraitor == null)
                {
                    DebugConsole.ThrowError($"Could not find a suitable traitor for the event \"{selectedPrefab.Identifier}\".",
                        contentPackage: selectedPrefab.ContentPackage);
                    return false;
                }
                CreateTraitorEvent(eventManager, selectedPrefab, selectedTraitor);
                return true;
            }

            return false;
        }

        private Client? SelectRandomTraitor()
        {
            if (GameSettings.CurrentConfig.VerboseLogging)
            {
                GameServer.Log(
                        $"Choosing a random traitor... Available traitors:"
                        + string.Concat(server.ConnectedClients.Where(IsClientViableTraitor).Select(c => $"\n  - {c.Name} ({(int)(GetTraitorProbability(c) * 100)}%)")),
                    ServerLog.MessageType.Traitors);
            }
            return server.ConnectedClients.Where(IsClientViableTraitor).GetRandomByWeight(GetTraitorProbability, Rand.RandSync.Unsynced);
        }

        private IEnumerable<Client> SelectSecondaryTraitors(TraitorEvent traitorEvent, Client mainTraitor)
        {
            if (traitorEvent.Prefab.SecondaryTraitorPercentage <= 0.0f && 
                traitorEvent.Prefab.SecondaryTraitorAmount <= 0) 
            { 
                return Enumerable.Empty<Client>(); 
            }

            var viableTraitors = server.ConnectedClients.Where(c => c != mainTraitor && IsClientViableTraitor(c)).ToList();

            int amountToChoose = (int)Math.Ceiling(viableTraitors.Count * (traitorEvent.Prefab.SecondaryTraitorPercentage / 100.0f));
            amountToChoose = Math.Max(amountToChoose, traitorEvent.Prefab.SecondaryTraitorAmount);

            if (amountToChoose > viableTraitors.Count)
            {
                DebugConsole.ThrowError(
                    $"Error in traitor event {traitorEvent.Prefab.Identifier}. Not enough players to choose {amountToChoose} secondary traitors. " +
                    $"Make sure the {nameof(traitorEvent.Prefab.MinPlayerCount)} of the event is high enough to support to desired amount of secondary traitors.",
                        contentPackage: traitorEvent.Prefab.ContentPackage);
                amountToChoose = viableTraitors.Count;
            }

            List<Client> traitors = new List<Client>();
            for (int i = 0; i < amountToChoose; i++)
            {
                var traitor = viableTraitors.GetRandomUnsynced();
                viableTraitors.Remove(traitor);
                traitors.Add(traitor);
            }
            return traitors;
        }

        private bool IsClientViableTraitor(Client client)
        {
            return 
                client != null && 
                server.ConnectedClients.Contains(client) && 
                client.Character != null && 
                !client.Character.IsIncapacitated && !client.Character.Removed &&
                activeEvents.None(e => e.Traitor == client);
        }

        private float GetTraitorEventPrefabCommonness(TraitorEventPrefab prefab)
        {
            int? roundsSinceLastSelected = GetRoundsSinceLastSelected(e => e.TraitorEvent == prefab);
            if (roundsSinceLastSelected.HasValue)
            {
                float normalizedRoundsSinceLastSelected = MathUtils.InverseLerp(0, MaxPreviousEventHistory, roundsSinceLastSelected.Value);
                //exponentially decreasing commonness the closer the last time the event was selected
                return prefab.Commonness * normalizedRoundsSinceLastSelected * normalizedRoundsSinceLastSelected;
            }
            else
            {
                return prefab.Commonness;
            }
        }

        private float GetTraitorProbability(Client client)
        {
            int? roundsSinceLastSelected = GetRoundsSinceLastSelected(e => e.Traitor == client);
            if (roundsSinceLastSelected.HasValue)
            {
                float normalizedRoundsSinceLastSelected = MathUtils.InverseLerp(0, MaxPreviousEventHistory, roundsSinceLastSelected.Value);
                //exponentially decreasing commonness the closer the last time the event was selected
                return normalizedRoundsSinceLastSelected * normalizedRoundsSinceLastSelected;
            }
            else
            {
                return 1.0f;
            }
        }

        private int? GetRoundsSinceLastSelected(Func<PreviousTraitorEvent, bool> condition)
        {
            //most recent events are at the end of the list, start from there
            for (int i = previousTraitorEvents.Count - 1; i >= 0; i--)
            {
                if (condition(previousTraitorEvents[i]))
                {
                    return previousTraitorEvents.Count - i;
                }
            }
            return null;
        }

        private void CreateTraitorEvent(EventManager eventManager, TraitorEventPrefab selectedPrefab, Client traitor) 
        {
            if (selectedPrefab.TryCreateInstance<TraitorEvent>(eventManager.RandomSeed, out var newEvent))
            {
                var secondaryTraitors = SelectSecondaryTraitors(newEvent, traitor);

                string logMessage = $"{NetworkMember.ClientLogName(traitor)} was selected as a traitor. Selected event: {selectedPrefab.Name}";
                if (secondaryTraitors.Any())
                {
                    logMessage += $", secondary traitors: {string.Join(", ", secondaryTraitors.Select(c => NetworkMember.ClientLogName(c)))}";
                }
                GameServer.Log(logMessage, ServerLog.MessageType.Traitors);

                newEvent.OnStateChanged += () => SendCurrentState(newEvent);
                activeEvents.Add(new ActiveTraitorEvent(traitor, newEvent));
                newEvent.SetTraitor(traitor);
                newEvent.SetSecondaryTraitors(secondaryTraitors);
                eventManager.ActivateEvent(newEvent);
                SendCurrentState(newEvent);
            }
            else
            {
                DebugConsole.ThrowError($"Failed to create an instance of the traitor event prefab \"{selectedPrefab.Identifier}\"!",
                    contentPackage: selectedPrefab.ContentPackage);
            }
        }

        public void ForceTraitorEvent(TraitorEventPrefab traitorEventPrefab)
        {
            if (eventManager == null)
            {
                throw new InvalidOperationException("EventManager was null. TraitorManager may not have been initialized properly.");
            }
            var traitor = SelectRandomTraitor();
            if (traitor == null)
            {
                DebugConsole.ThrowError($"Could not find a suitable traitor for the event \"{traitorEventPrefab.Identifier}\".",
                    contentPackage: traitorEventPrefab.ContentPackage);
                return;
            }
            CreateTraitorEvent(eventManager, traitorEventPrefab, traitor);
        }

        public void SkipStartDelay()
        {
            startTimer = 0.0f;
            if (activeEvents.Any()) { started = true; }
        }

        public void Update(float deltaTime)
        {
            if (!Enabled) { return; }
            if (!started)
            {
                if (level?.LevelData is { Type: LevelData.LevelType.LocationConnection })
                {
                    if (Submarine.MainSub.WorldPosition.X > level.Size.X / 2)
                    {
                        //try starting ASAP if the submarine is already half-way through the level
                        //(brief delay regardless, because otherwise we might retry every frame if finding a suitable event fails below)
                        startTimer = Math.Min(startTimer, 10.0f);
                    }
                }
                startTimer -= deltaTime;
                if (startTimer >= 0.0f) { return; }
                if (eventManager == null)
                {
                    throw new InvalidOperationException("EventManager was null. TraitorManager may not have been initialized properly.");
                }
                if (level == null)
                {
                    throw new InvalidOperationException("Level was null. TraitorManager may not have been initialized properly.");
                }
                if (TryCreateTraitorEvents(eventManager, level))
                {
                    started = true;
                }
                else
                {
                    //restart timer, we might be able to start a mission later if more clients join
                    startTimer = Rand.Range(StartDelayMin, StartDelayMax);
                }                
            }
        }

        public void EndRound()
        {
            Client? votedAsTraitor = GetClientAccusedAsTraitor();
            foreach (var activeEvent in activeEvents)
            {
                if (results != null)
                {
                    DebugConsole.AddWarning("Multiple traitor events active during the round, only displaying the results for the last one.");
                }
                results = new TraitorResults(votedAsTraitor, activeEvent.TraitorEvent);
                if (results.Value.MoneyPenalty > 0)
                {
                    GameMain.GameSession?.Campaign?.Bank?.TryDeduct(results.Value.MoneyPenalty);
                }

                if (activeEvent.TraitorEvent.CurrentState != TraitorEvent.State.Completed)
                {
                    activeEvent.TraitorEvent.CurrentState = TraitorEvent.State.Failed;
                }
                GameServer.Log(
                    NetworkMember.ClientLogName(activeEvent.Traitor) +
                    (activeEvent.TraitorEvent.CurrentState == TraitorEvent.State.Completed ?
                    " completed their traitor objective successfully." : " failed to complete their traitor objective."), 
                    ServerLog.MessageType.Traitors);

                //consider the event failed if the traitor was identifier correctly,
                //so the traitor doesn't get rewards or get assigned a follow-up event
                if (results.Value.VotedCorrectTraitor)
                {
                    GameServer.Log(
                        NetworkMember.ClientLogName(activeEvent.Traitor) + " was correctly identified as the traitor, and will not receive any rewards.",
                        ServerLog.MessageType.Traitors);
                    activeEvent.TraitorEvent.CurrentState = TraitorEvent.State.Failed;
                }
                previousTraitorEvents.Add(new PreviousTraitorEvent(
                    activeEvent.TraitorEvent.Prefab,
                    activeEvent.TraitorEvent.CurrentState,
                    activeEvent.Traitor));

                if (activeEvent.TraitorEvent.CurrentState == TraitorEvent.State.Completed)
                {
                    AchievementManager.OnTraitorWin(activeEvent.TraitorEvent.Traitor?.Character);
                    foreach (var secondaryTraitor in activeEvent.TraitorEvent.SecondaryTraitors)
                    {
                        AchievementManager.OnTraitorWin(secondaryTraitor?.Character);
                    }
                }
            }
            if (previousTraitorEvents.Count > MaxPreviousEventHistory)
            {
                previousTraitorEvents.RemoveRange(0, previousTraitorEvents.Count - MaxPreviousEventHistory);
            }
            activeEvents.Clear();
        }

        public Client? GetClientAccusedAsTraitor()
        {
            Client? votedAsTraitor = Voting.HighestVoted<Client>(VoteType.Traitor, server.ConnectedClients.Where(c => c.Character is { IsDead: false }), out int voteCount);
            if (voteCount < server.ConnectedClients.Count * server.ServerSettings.MinPercentageOfPlayersForTraitorAccusation / 100.0f)
            {
                //at least x% of the players must've voted for the same player
                votedAsTraitor = null;
            }
            return votedAsTraitor;
        }

        public TraitorResults? GetEndResults()
        {
            return results;
        }

        public XElement Save()
        {
            var element = new XElement(nameof(TraitorManager),
                new XAttribute("version", GameMain.Version.ToString()));
            foreach (var previousEvent in previousTraitorEvents)
            {
                previousEvent.Save(element);
            }
            return element;
        }

        public void Load(XElement traitorManagerElement)
        {
            previousTraitorEvents.Clear();
            foreach (XElement subElement in traitorManagerElement.Elements())
            {
                if (subElement.Name.ToIdentifier() == nameof(PreviousTraitorEvent))
                {
                    var previousTraitorEvent = PreviousTraitorEvent.Load(subElement);
                    if (previousTraitorEvent != null)
                    {
                        previousTraitorEvents.Add(previousTraitorEvent);
                    }
                }
            }
        }

        public void SendCurrentState(TraitorEvent ev)
        {
            if (ev?.Traitor == null) { return; }
            var msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ServerPacketHeader.TRAITOR_MESSAGE);
            msg.WriteByte((byte)ev.CurrentState);
            msg.WriteIdentifier(ev.Prefab.Identifier);
            server.SendTraitorMessage(msg, ev.Traitor);
        }
    }
}