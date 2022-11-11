using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Tutorials
{
    enum AutoPlayVideo { Yes, No };

    enum TutorialSegmentType { MessageBox, InfoBox, Objective };

    sealed class Tutorial
    {
        #region Constants

        private const SpawnType SpawnPointType = SpawnType.Human;
        private const float FadeOutTime = 3f;
        private const float WaitBeforeFade = 4f;

        #endregion

        #region Tutorial variables

        public readonly Identifier Identifier;
        public LocalizedString DisplayName { get; }
        public LocalizedString Description { get; }
 

        private bool completed;
        public bool Completed
        {
            get
            {
                return completed;
            }
            private set
            {
                if (completed == value) { return; }
                completed = value;
                if (value)
                {
                    CompletedTutorials.Instance.Add(Identifier);
                }
                GameSettings.SaveCurrentConfig();
            }
        }

        public readonly TutorialPrefab TutorialPrefab;
        private readonly EventPrefab eventPrefab;

        private CoroutineHandle tutorialCoroutine;
        private CoroutineHandle completedCoroutine;

        private Character character;

        private string SubmarinePath => TutorialPrefab.SubmarinePath.Value;
        private string StartOutpostPath => TutorialPrefab.OutpostPath.Value;
        private string LevelSeed => TutorialPrefab.LevelSeed;
        private string LevelParams => TutorialPrefab.LevelParams;

        private SubmarineInfo startOutpost = null;

        public readonly List<(Entity entity, Identifier iconStyle)> Icons = new List<(Entity entity, Identifier iconStyle)>();

        public bool Paused { get; private set; }

        #endregion

        #region Tutorial Controls

        public Tutorial(TutorialPrefab prefab)
        {
            Identifier = $"tutorial.{prefab.Identifier}".ToIdentifier();
            DisplayName = TextManager.Get(Identifier);
            Description = TextManager.Get($"tutorial.{prefab.Identifier}.description");
            TutorialPrefab = prefab;
            eventPrefab = EventSet.GetEventPrefab(prefab.EventIdentifier);
        }

        private IEnumerable<CoroutineStatus> Loading()
        {
            SubmarineInfo subInfo = new SubmarineInfo(SubmarinePath);

            LevelGenerationParams.LevelParams.TryGet(LevelParams, out LevelGenerationParams generationParams);

            yield return CoroutineStatus.Running;

            GameMain.GameSession = new GameSession(subInfo, GameModePreset.Tutorial, missionPrefabs: null);
            (GameMain.GameSession.GameMode as TutorialMode).Tutorial = this;

            if (generationParams is not null)
            {
                Biome biome =
                    Biome.Prefabs.FirstOrDefault(b => generationParams.AllowedBiomeIdentifiers.Contains(b.Identifier)) ??
                    Biome.Prefabs.First();

                if (!string.IsNullOrEmpty(StartOutpostPath))
                {
                    startOutpost = new SubmarineInfo(StartOutpostPath);
                }

                LevelData tutorialLevel = new LevelData(LevelSeed, 0, 0, generationParams, biome);
                GameMain.GameSession.StartRound(tutorialLevel, startOutpost: startOutpost);
            }
            else
            {
                GameMain.GameSession.StartRound(LevelSeed);
            }

            GameMain.GameSession.EventManager.ActiveEvents.Clear();
            GameMain.GameSession.EventManager.Enabled = true;
            GameMain.GameScreen.Select();

            if (Submarine.MainSub != null)
            {
                Submarine.MainSub.GodMode = true;
            }
            foreach (Structure wall in Structure.WallList)
            {
                if (wall.Submarine != null && wall.Submarine.Info.IsOutpost)
                {
                    wall.Indestructible = true;
                }
            }

            var charInfo = TutorialPrefab.GetTutorialCharacterInfo();

            var wayPoint = WayPoint.GetRandom(SpawnPointType, charInfo.Job?.Prefab, Level.Loaded.StartOutpost);

            if (wayPoint == null)
            {
                DebugConsole.ThrowError("A waypoint with the spawntype \"" + SpawnPointType + "\" is required for the tutorial event");
                yield return CoroutineStatus.Failure;
                yield break;
            }

            character = Character.Create(charInfo, wayPoint.WorldPosition, "", isRemotePlayer: false, hasAi: false);
            character.TeamID = CharacterTeamType.Team1;
            Character.Controlled = character;
            character.GiveJobItems(null);

            var idCard = character.Inventory.FindItemByTag("identitycard".ToIdentifier());
            if (idCard == null)
            {
                DebugConsole.ThrowError("Item prefab \"ID Card\" not found!");
                yield return CoroutineStatus.Failure;
                yield break;
            }
            idCard.AddTag("com");
            idCard.AddTag("eng");

            foreach (Item item in Item.ItemList)
            {
                Door door = item.GetComponent<Door>();
                if (door != null)
                {
                    door.CanBeWelded = false;
                }
            }

            tutorialCoroutine = CoroutineManager.StartCoroutine(UpdateState());

            GameMain.GameSession.CrewManager.AllowCharacterSwitch = TutorialPrefab.AllowCharacterSwitch;
            GameMain.GameSession.CrewManager.AutoHideCrewList();

            if (Character.Controlled?.Inventory is CharacterInventory inventory)
            {
                foreach (Item item in inventory.AllItemsMod)
                {
                    if (item.HasTag(TutorialPrefab.StartingItemTags)) { continue; }
                    item.Unequip(Character.Controlled);
                    Character.Controlled.Inventory.RemoveItem(item);
                }
            }

            yield return CoroutineStatus.Success;
        }

        public void Start()
        {
            GameMain.Instance.ShowLoading(Loading());
            ObjectiveManager.ResetObjectives();

            // Setup doors:  Clear all requirements, unless the door is setup as locked.
            foreach (var item in Item.ItemList)
            {
                var door = item.GetComponent<Door>();
                if (door != null)
                {
                    if (door.requiredItems.Values.None(ris => ris.None(ri => ri.Identifiers.None(i => i == "locked"))))
                    {
                        door.requiredItems.Clear();
                    }
                }
            }
        }

        public void Update()
        {
            if (character != null)
            {
                if (character.Oxygen < 1)
                {
                    character.Oxygen = 1;
                }
                if (character.IsDead)
                {
                    CoroutineManager.StartCoroutine(Dead());
                }
                else if (Character.Controlled == null)
                {
                    if (tutorialCoroutine != null)
                    {
                        CoroutineManager.StopCoroutines(tutorialCoroutine);
                    }
                    if (completedCoroutine == null && !CoroutineManager.IsCoroutineRunning(completedCoroutine))
                    {
                        GUI.PreventPauseMenuToggle = false;
                    }
                    ObjectiveManager.ClearContent();
                }
                else
                {
                    character = Character.Controlled;
                }
            }
        }

        private IEnumerable<CoroutineStatus> Dead()
        {
            GUI.PreventPauseMenuToggle = true;
            Character.Controlled = character = null;
            Stop();

            GameAnalyticsManager.AddDesignEvent("Tutorial:Died");

            yield return new WaitForSeconds(3.0f);

            var messageBox = new GUIMessageBox(TextManager.Get("Tutorial.TryAgainHeader"), TextManager.Get("Tutorial.TryAgain"), new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });

            messageBox.Buttons[0].OnClicked += Restart;
            messageBox.Buttons[0].OnClicked += messageBox.Close;


            messageBox.Buttons[1].OnClicked = GameMain.MainMenuScreen.ReturnToMainMenu;
            messageBox.Buttons[1].OnClicked += messageBox.Close;

            yield return CoroutineStatus.Success;
        }

        public IEnumerable<CoroutineStatus> UpdateState()
        {
            while (GameMain.Instance.LoadingScreenOpen || Level.Loaded == null || Level.Loaded.Generating)
            {
                yield return new WaitForSeconds(0.1f);
            }

            if (eventPrefab == null)
            {
                DebugConsole.LogError($"No tutorial event defined for the tutorial (identifier: \"{TutorialPrefab?.Identifier.ToString() ?? "null"})\"");
                yield return CoroutineStatus.Failure;
            }

            if (eventPrefab.CreateInstance() is Event eventInstance)
            {
                GameMain.GameSession.EventManager.QueuedEvents.Enqueue(eventInstance);
                while (!eventInstance.IsFinished)
                {
                    yield return CoroutineStatus.Running;
                }
            }
            else
            {
                DebugConsole.LogError($"Failed to create an instance for a tutorial event (identifier: \"{eventPrefab.Identifier}\"");
                yield return CoroutineStatus.Failure;
            }

            yield return CoroutineStatus.Success;
        }

        public void Complete()
        {
            GameAnalyticsManager.AddDesignEvent($"Tutorial:{Identifier}:Completed");
            completedCoroutine = CoroutineManager.StartCoroutine(TutorialCompleted());

            IEnumerable<CoroutineStatus> TutorialCompleted()
            {
                while (GUI.PauseMenuOpen) { yield return CoroutineStatus.Running; }

                GUI.PreventPauseMenuToggle = true;
                Character.Controlled.ClearInputs();
                Character.Controlled = null;
                GameAnalyticsManager.AddDesignEvent("Tutorial:Completed");

                yield return new WaitForSeconds(WaitBeforeFade);

                Action onEnd = () => GameMain.MainMenuScreen.ReturnToMainMenu(null, null);

                TutorialPrefab nextTutorialPrefab = null;
                bool displayEndMessage =
                    TutorialPrefab.EndMessage.EndType == TutorialPrefab.EndType.Restart ||
                    (TutorialPrefab.EndMessage.EndType == TutorialPrefab.EndType.Continue && TutorialPrefab.Prefabs.TryGet(TutorialPrefab.EndMessage.NextTutorialIdentifier, out nextTutorialPrefab));

                if (displayEndMessage)
                {
                    Paused = true;
                    var endingMessageBox = new GUIMessageBox(
                        headerText: "",
                        text: TextManager.Get($"{Identifier}.completed"),
                        buttons: new LocalizedString[]
                        {
                            TextManager.Get(nextTutorialPrefab is null ? "restart" : "campaigncontinue"),
                            TextManager.Get("pausemenuquit")
                        });

                    endingMessageBox.Buttons[0].OnClicked += (_, _) =>
                    {
                        if (nextTutorialPrefab is null)
                        {
                            onEnd = () => Restart(null, null);
                        }
                        else
                        {
                            onEnd = () =>
                            {
                                GameMain.MainMenuScreen.ReturnToMainMenu(null, null);
                                new Tutorial(nextTutorialPrefab).Start();
                            };
                        }
                        return true;
                    };
                    endingMessageBox.Buttons[0].OnClicked += endingMessageBox.Close;
                    endingMessageBox.Buttons[0].OnClicked += (_, _) => Paused = false;
                    endingMessageBox.Buttons[1].OnClicked += endingMessageBox.Close;
                    endingMessageBox.Buttons[1].OnClicked += (_, _) => Paused = false;
                }

                while (Paused) { yield return CoroutineStatus.Running; }

                var endCinematic = new CameraTransition(Submarine.MainSub, GameMain.GameScreen.Cam, null, Alignment.Center, panDuration: FadeOutTime);
                Completed = true;

                while (endCinematic.Running) { yield return CoroutineStatus.Running; }

                Stop();
                onEnd();
            }
        }

        private bool Restart(GUIButton button, object obj)
        {
            GUIMessageBox.MessageBoxes.Clear();
            GameMain.MainMenuScreen.ReturnToMainMenu(button, obj);
            Start();
            return true;
        }

        public void Stop()
        {
            if (tutorialCoroutine != null)
            {
                CoroutineManager.StopCoroutines(tutorialCoroutine);
            }
            ObjectiveManager.ResetUI();
        }

        #endregion
    }
}
