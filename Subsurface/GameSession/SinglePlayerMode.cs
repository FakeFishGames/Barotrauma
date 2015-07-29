using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface
{
    class SinglePlayerMode : GameMode
    {
        //private const int StartCharacterAmount = 3;

        public readonly CrewManager crewManager;
        //public readonly HireManager hireManager;

        private GUIButton endShiftButton;

        public readonly CargoManager CargoManager;

        private Quest quest;
        
        public Map map;

        private bool crewDead;
        private float endTimer;

        private bool savedOnStart;

        public int Money
        {
            get { return crewManager.Money; }
            set { crewManager.Money = value; }
        }

        public SinglePlayerMode(GameModePreset preset)
            : base(preset)
        {
            crewManager = new CrewManager();

            CargoManager = new CargoManager();

            endShiftButton = new GUIButton(new Rectangle(Game1.GraphicsWidth - 220, 20, 200, 25), "End shift", Alignment.TopLeft, GUI.style);
            endShiftButton.OnClicked = EndShift;

            for (int i = 0; i < 3; i++)
            {
                JobPrefab jobPrefab = null;
                switch (i)
                {
                    case 0:
                        jobPrefab = JobPrefab.List.Find(jp => jp.Name == "Captain");
                        break;
                    case 1:
                        jobPrefab = JobPrefab.List.Find(jp => jp.Name == "Engineer");
                        break;
                    case 2:
                        jobPrefab = JobPrefab.List.Find(jp => jp.Name == "Mechanic");
                        break;
                }

                CharacterInfo characterInfo =
                    new CharacterInfo(Character.HumanConfigFile, "", Gender.None, jobPrefab);
                crewManager.characterInfos.Add(characterInfo);
            }
  
        }

        public SinglePlayerMode(XElement element)
            : this(GameModePreset.list.Find(gm => gm.Name == "Single Player"))
        {
            string mapSeed = ToolBox.GetAttributeString(element, "mapseed", "a");

            GenerateMap(mapSeed);

            map.SetLocation(ToolBox.GetAttributeInt(element, "currentlocation", 0));

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "crew") continue;
                
                crewManager = new CrewManager(subElement);                
            }
        }

        public void GenerateMap(string seed)
        {
            map = new Map(seed, 500);
        }

        public override void Start(TimeSpan duration)
        {
            CargoManager.CreateItems();

            Game1.GameSession.Map.SelectedConnection.Quest.Start(Level.Loaded);

            if (!savedOnStart)
            {
                SaveUtil.SaveGame(Game1.GameSession.SavePath);
                savedOnStart = true;
            }

            endTimer = 5.0f;

            crewManager.StartShift();
        }

        public bool TryHireCharacter(HireManager hireManager, CharacterInfo characterInfo)
        {
            if (crewManager.Money < characterInfo.Salary) return false;

            hireManager.availableCharacters.Remove(characterInfo);
            crewManager.characterInfos.Add(characterInfo);

            crewManager.Money -= characterInfo.Salary;

            return true;
        }

        public string GetMoney()
        {
            return ("Money: " + crewManager.Money);
        }


        public override void Draw(SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);

            crewManager.Draw(spriteBatch);

            if (Level.Loaded.AtEndPosition)
            {
                endShiftButton.Text = "Enter " + map.SelectedLocation.Name;
                endShiftButton.Draw(spriteBatch);
            }
            else if (Level.Loaded.AtStartPosition)
            {
                endShiftButton.Text = "Enter " + map.CurrentLocation.Name;
                endShiftButton.Draw(spriteBatch);
            }

            //chatBox.Draw(spriteBatch);
            //textBox.Draw(spriteBatch);

            //timerBar.Draw(spriteBatch);

            //if (Game1.Client == null) endShiftButton.Draw(spriteBatch);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            crewManager.Update(deltaTime);

            endShiftButton.Update(deltaTime);

            if (!crewDead)
            {
                if (crewManager.characters.Find(c => !c.IsDead) == null)
                {
                    crewDead = true;
                }  
            }
            else
            {
                endTimer -= deltaTime;

                if (endTimer <= 0.0f) End("");
            }  
        }

        public override void End(string endMessage = "")
        {
            base.End(endMessage);

            quest.End();

            StringBuilder sb = new StringBuilder();
            List<Character> casualties = crewManager.characters.FindAll(c => c.IsDead);

            if (casualties.Count == crewManager.characters.Count)
            {
                sb.Append("Your entire crew has died!");

                var msgBox = new GUIMessageBox("GG", sb.ToString(), new string[] { "Load game", "Quit" });
                msgBox.Buttons[0].OnClicked += Game1.GameSession.LoadPrevious;
                msgBox.Buttons[0].OnClicked += msgBox.Close;
                msgBox.Buttons[1].OnClicked = Game1.LobbyScreen.QuitToMainMenu;
                msgBox.Buttons[1].OnClicked += msgBox.Close;
            }
            else
            {
                if (casualties.Any())
                {
                    sb.Append("Casualties: \n");
                    foreach (Character c in casualties)
                    {
                        sb.Append("    - " + c.Info.Name + "\n");
                    }
                }
                else
                {
                    sb.Append("No casualties!");
                }

                if (Level.Loaded.AtEndPosition)
                {
                    map.MoveToNextLocation();
                }

                SaveUtil.SaveGame(Game1.GameSession.SavePath);
            }

            crewManager.EndShift();
            for (int i = Character.CharacterList.Count - 1; i >= 0; i--)
            {
                Character.CharacterList[i].Remove();
            }

            Game1.GameSession.EndShift("");

        }

        private bool EndShift(GUIButton button, object obj)
        {
            End("");
            return true;
        }

        public void Save(XElement element)
        {
            //element.Add(new XAttribute("day", day));
            XElement modeElement = new XElement("gamemode");

            modeElement.Add(new XAttribute("currentlocation", map.CurrentLocationIndex));
            modeElement.Add(new XAttribute("mapseed", map.Seed));

            crewManager.Save(modeElement);

            element.Add(modeElement);
            
        }
    }
}
