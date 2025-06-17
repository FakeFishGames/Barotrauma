#nullable enable
using Barotrauma.Steam;
using System;
using System.Collections.Generic;
using System.Linq;
using Steamworks.Data;
using Steamworks;

namespace Barotrauma
{
    internal static class SteamTimelineManager
    {
        private static Screen? prevScreen;
        private static TimelineGameMode gameMode = TimelineGameMode.LoadingScreen;
        /// <summary>
        /// The current submarine that the controlled character is in (and has been for at least the delay amount).
        /// </summary>
        private static Submarine? currentSubmarine = null;
        /// <summary>
        /// For tracking the instantaneous switch of submarines, to reset the delay timer
        /// </summary>
        private static Submarine? previousTrackedSubmarine;
        private static Character? trackedCharacter = null;
        
        /// <summary>
        /// Delay in seconds before the submarine state change is considered valid, triggering events.
        /// </summary>
        private const float SubmarineStateChangeDelay = 2.0f;
        private static float submarineStateChangeTimer = 0.0f;

        public enum TimelineGameMode
        {
            Playing,
            Staging,
            Menus,
            LoadingScreen
        }

        public static void Initialize()
        {
            SetTimelineGameMode(TimelineGameMode.LoadingScreen);
        }
        
        public static void Update(float deltaTime)
        {
            PollScreenChange();
            PollCharacterChange(deltaTime);
            PollSubmarineChange(deltaTime);
        }

        private static void PollScreenChange()
        {
            if (!SteamManager.IsInitialized) { return; }
            if (Screen.Selected == prevScreen) { return; }

            TimelineGameMode newMode = Screen.Selected switch
            {
                GameScreen _ => TimelineGameMode.Playing,
                NetLobbyScreen _ => TimelineGameMode.Staging, 
                EditorScreen _ => TimelineGameMode.Playing,
                MainMenuScreen _ => TimelineGameMode.Menus,
                _ => TimelineGameMode.LoadingScreen // Default to Menus for other screens for now
            };
            
            if (GameMain.Instance != null && GameMain.Instance.LoadingScreenOpen)
            {
                newMode = TimelineGameMode.LoadingScreen;
            }
            
            if (newMode == gameMode) { return; }

            SetTimelineGameMode(newMode);
            gameMode = newMode;
            
            DebugConsole.NewMessage($"Timeline game mode set to {newMode}");

            prevScreen = Screen.Selected;
        }

        private static void PollCharacterChange(float deltaTime)
        {
            Character? controlledCharacter = Character.Controlled;
            
            // reset current sub state if character changes
            if (controlledCharacter != trackedCharacter)
            {
                InstantlySetCurrentSubmarine(controlledCharacter?.Submarine ?? null);
                trackedCharacter = controlledCharacter;
            }
        }
        
        private static void PollSubmarineChange(float deltaTime)
        {
            if (!SteamManager.IsInitialized) { return; }
            if (trackedCharacter == null) { return; }
            if (Screen.Selected is not GameScreen) { return; }

            Submarine? trackedCharacterSubmarine = trackedCharacter.Submarine;

            // timer makes sure only time-stable state changes are registered
            if (submarineStateChangeTimer > 0f)
            {
                submarineStateChangeTimer -= deltaTime;
                
                if (submarineStateChangeTimer <= 0f)
                {
                    // actually register our pending state change
                    CharacterSubChanged(trackedCharacter, trackedCharacterSubmarine);
                }
            }

            // detect instantaneous submarine change and start the delay timer
            if (previousTrackedSubmarine != trackedCharacterSubmarine)
            {
                submarineStateChangeTimer = SubmarineStateChangeDelay;
            }
            previousTrackedSubmarine = trackedCharacterSubmarine;
        }
        
        private static void InstantlySetCurrentSubmarine(Submarine? submarine)
        {
            currentSubmarine = submarine;
            previousTrackedSubmarine = submarine;
            submarineStateChangeTimer = 0f;
        }
        
        private static void CharacterSubChanged(Character character, Submarine newSubmarine)
        {
            if (newSubmarine == currentSubmarine) { return; }
            
            // currentSub to none
            if (currentSubmarine != null && newSubmarine == null)
            {
                OnCharacterLeftSubmarine(character, currentSubmarine);
            }
            // currentSub to newSub
            else if (currentSubmarine != null && newSubmarine != null)
            {
                OnCharacterMovedBetweenSubmarines(character, currentSubmarine, newSubmarine);
            }
            //none to newSub
            else if (currentSubmarine == null && newSubmarine != null)
            {
                OnCharacterEnteredSubmarine(character, newSubmarine);
            }
            
            currentSubmarine = newSubmarine;
        }
        
        public static void SetTimelineGameMode(TimelineGameMode mode)
        {
            if (!SteamManager.IsInitialized) { return; }
            
            Steamworks.TimelineGameMode steamMode = mode switch
            {
                TimelineGameMode.Playing => Steamworks.TimelineGameMode.Playing,
                TimelineGameMode.Staging => Steamworks.TimelineGameMode.Staging,
                TimelineGameMode.Menus => Steamworks.TimelineGameMode.Menus,
                TimelineGameMode.LoadingScreen => Steamworks.TimelineGameMode.LoadingScreen,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, message: null)
            };
            
            try
            {
                SteamTimeline.SetTimelineGameMode(steamMode); 
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Failed to set timeline game mode to {mode}", e);
            }
        }

        public static void OnPlayerDied(Character victim, CauseOfDeath causeOfDeath)
        {
            if (victim == null || causeOfDeath == null) { return; }

            string eventTitle = $"{victim.DisplayName} died";
            string causeOfDeathText = causeOfDeath.Affliction != null ?
                causeOfDeath.Affliction.CauseOfDeathDescription.Value :
                causeOfDeath.Type.ToString();
            string eventDescription = $"{victim.DisplayName} died: {causeOfDeathText}";

            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Death, 1);
        }

        public static void OnSignificantEnemyDied(Character victim, CauseOfDeath causeOfDeath)
        {
            string eventTitle = $"{victim.DisplayName} has died!";
            string causeOfDeathText = causeOfDeath.Affliction != null ?
                causeOfDeath.Affliction.CauseOfDeathDescription.Value :
                causeOfDeath.Type.ToString();
            string eventDescription = $"{victim.DisplayName} died: {causeOfDeathText}";
            if (causeOfDeath.Killer != null)
            {
                eventDescription = $"{victim.DisplayName} was killed by {causeOfDeath.Killer.DisplayName}";
            }

            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Attack, 2);
        }

        public static void OnRoundStarted()
        {
            string eventTitle = "Round Started";
            string eventDescription = "The round has started";

            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Marker, 0);
        }

        public static void OnRoundEnded()
        {
            string eventTitle = "Round Ended";
            string eventDescription = "The round has ended";

            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Completed, 0);
        }

        public static void OnCharacterLeftSubmarine(Character character, Submarine submarine)
        {
            string eventTitle = $"{character.Name} Went Diving Outside";
            string eventDescription = $"{character.Name} left {submarine.Info.Name}";
            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Transfer, 1);
        }
        
        public static void OnCharacterMovedBetweenSubmarines(Character character, Submarine oldSubmarine, Submarine newSubmarine)
        {
            string eventTitle = $"{character.Name} Moved Between Locations";
            string eventDescription = $"{character.Name} moved from {oldSubmarine.Info.Name} to {newSubmarine.Info.Name}";
            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Transfer, 1);
        }

        public static void OnCharacterEnteredSubmarine(Character character, Submarine submarine)
        {
            string eventTitle = $"{character.Name} Entered Hull";
            string eventDescription = $"{character.Name} has entered {submarine.Info.Name}";
            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Transfer, 1);
        }

        public static void OnError(string errorMessage, Exception? e = null)
        {
            // these don't have localization support yet, use hardcoded strings
            string eventTitle = "Error Occurred";
            string eventDescription = $"An error was logged: {errorMessage}";
            if (e != null) { eventDescription += $"\n{e.GetType().Name}"; }
            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Bug, 3); // Higher priority for errors
        }

        public static void OnClientDisconnect(string disconnectInfo)
        {
             // these don't have localization support yet, use hardcoded strings
            string eventTitle = $"Client Disconnected";
            string eventDescription = $"{disconnectInfo}";
            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Bug, 2); // Maybe slightly lower priority than code errors
        }

        public static void OnMonsterMissionTargetsKilled(MonsterMission mission)
        {
            // these don't have localization support yet, use hardcoded strings
            string eventTitle = $"Monsters Dispatched";
            string eventDescription = $"{mission.Name}: All targets were eliminated."; 
            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Attack, 2); 
        }

        public static void OnScanSuccessful(ScanMission mission)
        {
             // these don't have localization support yet, use hardcoded strings
            string eventTitle = "Scan Successful";
            string eventDescription = $"{mission.Name}: A scanner has successfully scanned a target.";
            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Marker, 1);
        }

        public static void OnOutpostTargetEliminated(AbandonedOutpostMission mission)
        {
            // these don't have localization support yet, use hardcoded strings
            string eventTitle = $"Target Character Eliminated";
            string eventDescription = $"{mission.Name}: A target was eliminated.";
            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Attack, 2);
        }

        /// <summary>
        /// How often can hull breach events be created? There's often multiple breaches very close to each other, not necessary to track all of them.
        /// </summary>
        const float HullBreachEventInterval = 10.0f;
        private static double LastHullBreachTime;

        public static void OnHullBreached(Structure structure)
        {
            if (LastHullBreachTime > Timing.TotalTime - HullBreachEventInterval) { return; }
            // only trigger this event for player subs, since beacon stations can fill the requirements at level start
            if (structure.Submarine?.Info is not { IsPlayer: true }) { return; }
            
            string eventTitle = "Major Hull Breach";
            string eventDescription = $"The hull of {structure.Submarine?.Info.Name ?? "Unknown Submarine"} suffered a major breach.";
            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Caution, 2);
            LastHullBreachTime = Timing.TotalTime;
        }

        public static void OnMissionTargetRetrieved(Item item, Mission mission)
        {
            string eventTitle = $"Target Retrieved: {item.Name}";
            string eventDescription = $"{mission.Name}: A target item {item.Name} was retrieved.";
            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Checkmark, 1);
        }
        
        public static void OnMissionTargetPickedUp(Item item, Mission mission)
        {
            string eventTitle = $"Target Picked Up: {item.Name}";
            string eventDescription = $"{mission.Name}: A target item {item.Name} was picked up.";
            AddTimelineEvent(eventTitle, eventDescription, SteamIcons.Checkmark, 1);
        }
        
        public static void AddTimelineEvent(string title, string description, string icon, uint priority = 1, Submarine? submarine = null)
        {
            if (!SteamManager.IsInitialized) { return; }

            // exit early if title, description or icon is empty
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(icon))
            {
                DebugConsole.ThrowError("Failed to add timeline event: title, description or icon is empty");
                return;
            }
            
            if (submarine != null)
            {
                string submarineName = submarine.Info?.DisplayName.Value ?? "Unknown Submarine";
                title = title.Replace("[sub]", submarineName);
                description = description.Replace("[sub]", submarineName);
            }

            try
            {
                var eventHandle = Steamworks.SteamTimeline.AddInstantaneousTimelineEvent(
                    title,
                    description,
                    icon,
                    priority,
                    0.0f,
                    Steamworks.TimelineEventClipPriority.Standard);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Failed to add timeline event", e);
            }
        }
    }
} 