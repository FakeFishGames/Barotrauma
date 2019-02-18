using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    class ScenarioTutorial : Tutorial
    {
        private Character character;
        private SpawnType spawnPointType;
        private string submarinePath;
        private string levelSeed;

        public ScenarioTutorial(XElement element) : base(element)
        {
            submarinePath = element.GetAttributeString("submarinepath", "");
            levelSeed = element.GetAttributeString("levelseed", "tuto");
            Enum.TryParse(element.GetAttributeString("spawnpointtype", "Human"), true, out spawnPointType);
        }

        public override void Initialize()
        {
            base.Initialize();
            GameMain.Instance.ShowLoading(Loading());
        }

        public override void Start()
        {
            base.Start();

            WayPoint wayPoint = WayPoint.GetRandom(spawnPointType, null);
            if (wayPoint == null)
            {
                DebugConsole.ThrowError("A waypoint with the spawntype \"" + spawnPointType + "\" is required for the tutorial event");
                return;
            }

            CharacterInfo charInfo = configElement.Element("Character") == null ?
                new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "engineer")) :
                new CharacterInfo(configElement.Element("Character"));

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

        private IEnumerable<object> Loading()
        {
            Submarine.MainSub = Submarine.Load(submarinePath, "", true);
            yield return CoroutineStatus.Running;

            GameMain.GameSession = new GameSession(Submarine.MainSub, "",
                GameModePreset.List.Find(g => g.Identifier == "tutorial"));
            (GameMain.GameSession.GameMode as TutorialMode).tutorial = this;
            GameMain.GameSession.StartRound(levelSeed);
            GameMain.GameSession.EventManager.Events.Clear();
            GameMain.GameScreen.Select();

            yield return CoroutineStatus.Success;
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
