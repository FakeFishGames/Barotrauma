using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

        public CrewManager(GameSession session)
        {
            characters = new List<Character>();
            characterInfos = new List<CharacterInfo>();
            
            guiFrame = new GUIFrame(new Rectangle(0, 50, 150, 450), Color.Transparent);

            listBox = new GUIListBox(new Rectangle(0, 0, 150, 400), Color.Transparent, guiFrame);
            listBox.ScrollBarEnabled = false;
            listBox.OnSelected = SelectCharacter;

            money = 10000;
        }

        private string CreateSaveFile(string mapName)
        {
            string path = "Content/Data/Saves/";

            string name = Path.GetFileNameWithoutExtension(mapName);

            int i = 0;
            while (File.Exists(path+name + i))
            {
                i++;
            }

            return path + name + i;
        }
        
        public bool SelectCharacter(object selection)
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
            if (!characterInfos.Contains(character.info))
            {
                characterInfos.Add(character.info);
            }

            GUIFrame frame = new GUIFrame(new Rectangle(0,0,0,40), Color.Transparent, listBox);
            frame.UserData = character;
            frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            frame.HoverColor = Color.LightGray * 0.5f;
            frame.SelectedColor = Color.Gold * 0.5f;

            string name = character.info.name.Replace(' ','\n');

            GUITextBlock textBlock = new GUITextBlock(
                new Rectangle(40,0,0,25), 
                name,
                Color.Transparent, Color.Black,
                Alignment.Left,
                Alignment.Left,
                frame);
            textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

            GUIImage face = new GUIImage(new Rectangle(-10,-10,0,0), character.animController.limbs[0].sprite, Alignment.Left, frame);
        }

        public void KillCharacter(Character killedCharacter)
        {
            GUIComponent characterBlock = listBox.GetChild(killedCharacter) as GUIComponent;
            if (characterBlock != null) characterBlock.Color = Color.DarkRed * 0.5f;
            
        }

        public void StartShift()
        {
            foreach (CharacterInfo ci in characterInfos)
            {
                WayPoint randomWayPoint = WayPoint.GetRandom(WayPoint.SpawnType.Human);
                Vector2 position = (randomWayPoint == null) ? Vector2.Zero : randomWayPoint.SimPosition;

                Character character = new Character(ci.file, position, ci);
                Character.Controlled = character;
                AddCharacter(character);
            }

            if (characters.Count>0) SelectCharacter(characters[0]);
        }

        public void EndShift()
        {
            foreach (Character c in characters)
            {
                if (!c.IsDead) continue;

                CharacterInfo deadInfo = characterInfos.Find(x => c.info == x);
                if (deadInfo != null) characterInfos.Remove(deadInfo);
            }

            characters.Clear();
            listBox.ClearChildren();
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            guiFrame.Draw(spriteBatch);
        }
    }
}
