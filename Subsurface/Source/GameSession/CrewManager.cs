using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;

namespace Subsurface
{
    class CrewManager
    {
        public List<Character> characters;
        public List<CharacterInfo> characterInfos;
        
        //public static string mapFile;
        //public string saveFile;

        private int money;
        
        private GUIFrame guiFrame;
        private GUIListBox listBox;


        public int Money
        {
            get { return money; }
            set { money = (int)Math.Max(value, 0.0f); }
        }

        public CrewManager()
        {
            characters = new List<Character>();
            characterInfos = new List<CharacterInfo>();
            
            guiFrame = new GUIFrame(new Rectangle(0, 50, 150, 450), Color.Transparent);

            listBox = new GUIListBox(new Rectangle(0, 0, 150, 0), Color.Transparent, null, guiFrame);
            listBox.ScrollBarEnabled = false;
            listBox.OnSelected = SelectCharacter;

            money = 10000;
        }

        public CrewManager(XElement element)
            : this()
        {
            money = ToolBox.GetAttributeInt(element, "money", 0);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower()!="character") continue;

                characterInfos.Add(new CharacterInfo(subElement));
            }
        }

        public bool SelectCharacter(GUIComponent component, object selection)
        {
            //listBox.Select(selection);
            Character character = selection as Character;

            if (character == null) return false;

            if (characters.Contains(character))
            {
                Character.Controlled = character;
                return true;
            }

            return false;
        }

        public void AddCharacter(Character character)
        {
            characters.Add(character);
            if (!characterInfos.Contains(character.Info))
            {
                characterInfos.Add(character.Info);
            }

            GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 40), Color.Transparent, null, listBox);
            frame.UserData = character;
            frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            frame.HoverColor = Color.LightGray * 0.5f;
            frame.SelectedColor = Color.Gold * 0.5f;

            string name = character.Info.Name.Replace(' ', '\n');

            GUITextBlock textBlock = new GUITextBlock(
                new Rectangle(40, 0, 0, 25),
                name,
                Color.Transparent, Color.White,
                Alignment.Left, Alignment.Left,
                null, frame);
            textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

            new GUIImage(new Rectangle(-10, -10, 0, 0), character.AnimController.limbs[0].sprite, Alignment.Left, frame);
        }

        public void Update(float deltaTime)
        {
            guiFrame.Update(deltaTime);
        }

        public void KillCharacter(Character killedCharacter)
        {
            GUIComponent characterBlock = listBox.GetChild(killedCharacter) as GUIComponent;
            if (characterBlock != null) characterBlock.Color = Color.DarkRed * 0.5f;

            //if (characters.Find(c => !c.IsDead)==null)
            //{
            //    Game1.GameSession.EndShift(null, null);
            //}            
        }

        public void StartShift()
        {
            listBox.ClearChildren();
            characters.Clear();

            WayPoint[] waypoints = WayPoint.SelectCrewSpawnPoints(characterInfos);

            for (int i = 0; i < waypoints.Length; i++)
            {
                //WayPoint randomWayPoint = WayPoint.GetRandom(SpawnType.Human);
                //Vector2 position = (randomWayPoint == null) ? Vector2.Zero : randomWayPoint.SimPosition;
                
                Character character = new Character(characterInfos[i], waypoints[i]);
                Character.Controlled = character;

                if (!character.Info.StartItemsGiven)
                {
                    character.GiveJobItems(waypoints[i]);
                    character.Info.StartItemsGiven = true;
                }

                AddCharacter(character);
            }

            if (characters.Count > 0) SelectCharacter(null, characters[0]);
        }

        public void EndShift()
        {
            foreach (Character c in characters)
            {
                if (!c.IsDead)
                {
                    c.Info.UpdateCharacterItems();
                    continue;
                }

                CharacterInfo deadInfo = characterInfos.Find(x => c.Info == x);
                if (deadInfo != null) characterInfos.Remove(deadInfo);
            }

            characters.Clear();
            listBox.ClearChildren();
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            guiFrame.Draw(spriteBatch);
        }

        public void Save(XElement parentElement)
        {
            XElement element = new XElement("crew");
                
            element.Add(new XAttribute("money", money));
            
            foreach (CharacterInfo ci in characterInfos)
            {
                ci.Save(element);
            }

            parentElement.Add(element);
        }
    }
}
