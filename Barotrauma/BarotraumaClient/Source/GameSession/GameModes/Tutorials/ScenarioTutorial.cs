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

            GameMain.GameSession.EventManager.Events.Clear();
            GameMain.GameScreen.Select();

            yield return CoroutineStatus.Success;
        }

        public override void Start()
        {
            base.Start();

            CharacterInfo charInfo = configElement.Element("Character") == null ?
                new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "engineer")) :
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

            CoroutineManager.StartCoroutine(UpdateState());
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

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (character != null)
            {
                if (Character.Controlled == null)
                {
                    CoroutineManager.StopCoroutines("TutorialMode.UpdateState");
                    infoBox = null;
                }
                else if (Character.Controlled.IsDead)
                {
                    Character.Controlled = null;

                    CoroutineManager.StopCoroutines("TutorialMode.UpdateState");
                    infoBox = null;
                    CoroutineManager.StartCoroutine(Dead());
                }
            }
        }

        private IEnumerable<object> Dead()
        {
            yield return new WaitForSeconds(3.0f);

            var messageBox = new GUIMessageBox("You have died", "Do you want to try again?", new string[] { "Yes", "No" });

            messageBox.Buttons[0].OnClicked += Restart;
            messageBox.Buttons[0].OnClicked += messageBox.Close;


            messageBox.Buttons[1].OnClicked = GameMain.MainMenuScreen.ReturnToMainMenu;
            messageBox.Buttons[1].OnClicked += messageBox.Close;

            yield return CoroutineStatus.Success;
        }
    }
}
