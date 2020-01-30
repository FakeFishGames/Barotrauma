using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    class ScenarioTutorial : Tutorial
    {
        private CoroutineHandle tutorialCoroutine;

        private Character character;
        private string spawnSub;
        private SpawnType spawnPointType;
        private string submarinePath;
        private string startOutpostPath;
        private string endOutpostPath;
        private string levelSeed;
        private string levelParams;

        private Submarine startOutpost = null;
        private Submarine endOutpost = null;
        private bool currentTutorialCompleted = false;
        private float fadeOutTime = 3f;
        protected float waitBeforeFade = 4f;

        // Colors
        protected Color highlightColor = Color.OrangeRed;
        protected Color uiHighlightColor = new Color(150, 50, 0);
        protected Color buttonHighlightColor = new Color(255, 100, 0);
        protected Color inaccessibleColor = GUI.Style.Red;
        protected Color accessibleColor = GUI.Style.Green;

        public ScenarioTutorial(XElement element) : base(element)
        {
            submarinePath = element.GetAttributeString("submarinepath", "");
            startOutpostPath = element.GetAttributeString("startoutpostpath", "");
            endOutpostPath = element.GetAttributeString("endoutpostpath", "");

            levelSeed = element.GetAttributeString("levelseed", "tuto");
            levelParams = element.GetAttributeString("levelparams", "");

            spawnSub = element.GetAttributeString("spawnsub", "");
            Enum.TryParse(element.GetAttributeString("spawnpointtype", "Human"), true, out spawnPointType);        
        }

        public override void Initialize()
        {
            base.Initialize();
            currentTutorialCompleted = false;
            GameMain.Instance.ShowLoading(Loading());
        }

        private IEnumerable<object> Loading()
        {
            Submarine.MainSub = Submarine.Load(submarinePath, "", true);

            LevelGenerationParams generationParams = LevelGenerationParams.LevelParams.Find(p => p.Name == levelParams);

            yield return CoroutineStatus.Running;

            GameMain.GameSession = new GameSession(Submarine.MainSub, "",
                GameModePreset.List.Find(g => g.Identifier == "tutorial"));
            (GameMain.GameSession.GameMode as TutorialMode).Tutorial = this;

            if (generationParams != null)
            {
                Biome biome = LevelGenerationParams.GetBiomes().Find(b => generationParams.AllowedBiomes.Contains(b));

                if (startOutpostPath != string.Empty)
                {
                    startOutpost = Submarine.Load(startOutpostPath, "", false);
                }

                if (endOutpostPath != string.Empty)
                {
                    endOutpost = Submarine.Load(endOutpostPath, "", false);
                }

                Level tutorialLevel = new Level(levelSeed, 0, 0, generationParams, biome, startOutpost, endOutpost);
                GameMain.GameSession.StartRound(tutorialLevel);
            }
            else
            {
                GameMain.GameSession.StartRound(levelSeed);
            }

            GameMain.GameSession.EventManager.ActiveEvents.Clear();
            GameMain.GameSession.EventManager.Enabled = false;
            GameMain.GameScreen.Select();

            yield return CoroutineStatus.Success;
        }

        public override void Start()
        {
            base.Start();

            Submarine.MainSub.GodMode = true;

            CharacterInfo charInfo = configElement.Element("Character") == null ?
                new CharacterInfo(CharacterPrefab.HumanSpeciesName, "", JobPrefab.Get("engineer")) :
                new CharacterInfo(configElement.Element("Character"));

            WayPoint wayPoint = GetSpawnPoint(charInfo);

            if (wayPoint == null)
            {
                DebugConsole.ThrowError("A waypoint with the spawntype \"" + spawnPointType + "\" is required for the tutorial event");
                return;
            }

            character = Character.Create(charInfo, wayPoint.WorldPosition, "", false, false);
            Character.Controlled = character;
            character.GiveJobItems(null);

            var idCard = character.Inventory.FindItemByIdentifier("idcard");
            if (idCard == null)
            {
                DebugConsole.ThrowError("Item prefab \"ID Card\" not found!");
                return;
            }
            idCard.AddTag("com");
            idCard.AddTag("eng");

            List<Entity> entities = Entity.GetEntityList();

            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] is Item)
                {
                    Door door = (entities[i] as Item).GetComponent<Door>();
                    if (door != null)
                    {
                        door.CanBeWelded = false;
                    }
                }
            }

            tutorialCoroutine = CoroutineManager.StartCoroutine(UpdateState());
        }

        public override void AddToGUIUpdateList()
        {
            if (!currentTutorialCompleted)
            {
                base.AddToGUIUpdateList();
            }
        }

        private WayPoint GetSpawnPoint(CharacterInfo charInfo)
        {
            Submarine spawnSub = null;

            if (this.spawnSub != string.Empty)
            {
                switch (this.spawnSub)
                {
                    case "startoutpost":
                        spawnSub = startOutpost;
                        break;

                    case "endoutpost":
                        spawnSub = endOutpost;
                        break;

                    default:
                        spawnSub = Submarine.MainSub;
                        break;
                }
            }

            return WayPoint.GetRandom(spawnPointType, charInfo.Job, spawnSub);
        }

        protected bool HasOrder(Character character, string identifier, string option = null)
        {
            if (character.CurrentOrder?.Identifier == identifier)
            {
                if (option == null)
                {
                    return true;
                }
                else
                {
                    HumanAIController humanAI = character.AIController as HumanAIController;
                    return humanAI.CurrentOrderOption == option;
                }
            }

            return false;
        }

        protected void SetHighlight(Item item, bool state)
        {
            if (item.ExternalHighlight == state) return;
            item.SpriteColor = (state) ? highlightColor : Color.White;
            item.ExternalHighlight = state;
        }

        protected void SetHighlight(Structure structure, bool state)
        {
            structure.SpriteColor = (state) ? highlightColor : Color.White;
            structure.ExternalHighlight = state;
        }

        protected void SetHighlight(Character character, bool state)
        {
            character.ExternalHighlight = state;
        }

        protected void SetDoorAccess(Door door, LightComponent light, bool state)
        {
            if (state && door != null) door.requiredItems.Clear();
            if (light != null) light.LightColor = (state) ? accessibleColor : inaccessibleColor;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
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
                    infoBox = null;
                }
                else if (Character.Controlled.IsDead)
                {
                    CoroutineManager.StartCoroutine(Dead());
                }
            }
        }

        public override void Stop()
        {
            if (tutorialCoroutine != null)
            {
                CoroutineManager.StopCoroutines(tutorialCoroutine);
            }
            base.Stop();
        }

        private IEnumerable<object> Dead()
        {
            GUI.PreventPauseMenuToggle = true;
            Character.Controlled = character = null;
            Stop();

            yield return new WaitForSeconds(3.0f);

            var messageBox = new GUIMessageBox(TextManager.Get("Tutorial.TryAgainHeader"), TextManager.Get("Tutorial.TryAgain"), new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

            messageBox.Buttons[0].OnClicked += Restart;
            messageBox.Buttons[0].OnClicked += messageBox.Close;


            messageBox.Buttons[1].OnClicked = GameMain.MainMenuScreen.ReturnToMainMenu;
            messageBox.Buttons[1].OnClicked += messageBox.Close;

            yield return CoroutineStatus.Success;
        }

        protected IEnumerable<object> TutorialCompleted()
        {
            GUI.PreventPauseMenuToggle = true;

            Character.Controlled.ClearInputs();
            Character.Controlled = null;

            yield return new WaitForSeconds(waitBeforeFade);

            var endCinematic = new RoundEndCinematic(Submarine.MainSub, GameMain.GameScreen.Cam, fadeOutTime);
            currentTutorialCompleted = Completed = true;
            while (endCinematic.Running) yield return null;
            Stop();
            GameMain.MainMenuScreen.ReturnToMainMenu(null, null);
        }

        protected void Heal(Character character)
        {
            character.SetAllDamage(0.0f, 0.0f, 0.0f);
            character.Oxygen = 100.0f;
            character.Bloodloss = 0.0f;
            character.SetStun(0.0f, true);
        }

        protected Item FindOrGiveItem(Character character, string identifier)
        {
            var item = character.Inventory.FindItemByIdentifier(identifier);
            if (item != null) { return item; }

            ItemPrefab itemPrefab = MapEntityPrefab.Find(name: null, identifier: identifier) as ItemPrefab;
            item = new Item(itemPrefab, Vector2.Zero, submarine: null);
            character.Inventory.TryPutItem(item, character);
            return item;
        }
    }
}
