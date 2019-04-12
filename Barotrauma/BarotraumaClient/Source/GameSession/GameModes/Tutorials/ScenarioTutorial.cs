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
        
        // Colors
        protected Color highlightColor = Color.OrangeRed;
        protected Color uiHighlightColor = new Color(150, 50, 0);
        protected Color buttonHighlightColor = new Color(255, 100, 0);
        protected Color inaccessibleColor = Color.Red;
        protected Color accessibleColor = Color.Green;

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

        protected void SetHighlight(Item item, bool state)
        {
            item.SpriteColor = (state) ? highlightColor : Color.White;
            item.ExternalHighlight = state;
        }

        protected void SetHighlight(Structure structure, bool state)
        {
            structure.SpriteColor = (state) ? highlightColor : Color.White;
            structure.ExternalHighlight = state;
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
                if (character.IsDead)
                {
                    CoroutineManager.StartCoroutine(Dead());
                }
                else if (Character.Controlled == null)
                {
                    CoroutineManager.StopCoroutines("TutorialMode.UpdateState");
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
            CoroutineManager.StopCoroutines("TutorialMode.UpdateState");
            base.Stop();
        }

        private IEnumerable<object> Dead()
        {
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
    }
}
