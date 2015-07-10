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
        public readonly CrewManager crewManager;
        public readonly HireManager hireManager;

        private GUIButton endShiftButton;

        //private int day;

        //public int Day
        //{
        //    get { return day; }
        //}

        bool crewDead;
        private float endTimer;

        public SinglePlayerMode(GameModePreset preset)
            : base(preset)
        {
            crewManager = new CrewManager();
            hireManager = new HireManager();

            endShiftButton = new GUIButton(new Rectangle(Game1.GraphicsWidth - 220, 20, 200, 25), "End shift", Alignment.TopLeft, GUI.style);
            endShiftButton.OnClicked = EndShift;

            hireManager.GenerateCharacters("Content/Characters/Human/human.xml", 10);

            //day = 1;  
        }

        public SinglePlayerMode(XElement element)
            : this(GameModePreset.list.Find(gm => gm.Name == "Single Player"))
        {
            //day = ToolBox.GetAttributeInt(element,"day",1);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "crew") continue;
                
                crewManager = new CrewManager(subElement);                
            }
        }

        public override void Start(TimeSpan duration)
        {
            endTimer = 5.0f;

            crewManager.StartShift();
        }

        public bool TryHireCharacter(CharacterInfo characterInfo)
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
                endShiftButton.Text = "Enter " + Game1.GameSession.map.SelectedLocation.Name;
                endShiftButton.Draw(spriteBatch);
            }
            else if (Level.Loaded.AtStartPosition)
            {
                endShiftButton.Text = "Enter " + Game1.GameSession.map.CurrentLocation.Name;
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

        private bool EndShift(GUIButton button, object obj)
        {
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
                    Game1.GameSession.map.MoveToNextLocation();
                }

            }

            crewManager.EndShift();
            for (int i = Character.CharacterList.Count - 1; i >= 0; i--)
            {
                Character.CharacterList[i].Remove();
            }

            Game1.GameSession.EndShift("");

            return true;
        }

        public void Save(XElement element)
        {
            //element.Add(new XAttribute("day", day));

            crewManager.Save(element);
            
        }
    }
}
