using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;

namespace Barotrauma
{
    class CrewManager
    {
        public List<Character> characters;
        public List<CharacterInfo> characterInfos;
        
        private int money;
        
        private GUIFrame guiFrame;
        private GUIListBox listBox, orderListBox;

        private bool crewFrameOpen;
        private GUIButton crewButton;
        protected GUIFrame crewFrame;

        private CrewCommander commander;
        
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

            listBox = new GUIListBox(new Rectangle(45, 30, 150, 0), Color.Transparent, null, guiFrame);
            listBox.ScrollBarEnabled = false;
            listBox.OnSelected = SelectCharacter;

            orderListBox = new GUIListBox(new Rectangle(5, 30, 30, 0), Color.Transparent, null, guiFrame);
            orderListBox.ScrollBarEnabled = false;
            orderListBox.OnSelected = SelectCharacterOrder;

            crewButton = new GUIButton(new Rectangle(5, 0, 100, 20), "Crew", GUI.Style, guiFrame);
            crewButton.OnClicked = ToggleCrewFrame;

            commander = new CrewCommander(this);

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

            if (character == null || character.IsDead) return false;

            if (characters.Contains(character))
            {
                Character.Controlled = character;
                return true;
            }

            return false;
        }

        public void SetCharacterOrder(Character character, Order order)
        {
            if (order == null) return;

            var characterFrame = listBox.FindChild(character);

            if (characterFrame == null) return;

            int characterIndex = listBox.children.IndexOf(characterFrame);

            orderListBox.children[characterIndex].ClearChildren();
            
            var img = new GUIImage(new Rectangle(0, 0, 30, 30), order.SymbolSprite, Alignment.Center, orderListBox.children[characterIndex]);
            img.Scale = 30.0f / img.SourceRect.Width;
            img.Color = order.Color;
            img.CanBeFocused = false;

            orderListBox.children[characterIndex].ToolTip = "Order: " + order.Name; 
        }

        public bool SelectCharacterOrder(GUIComponent component, object selection)
        {
            GameMain.GameSession.CrewManager.commander.ToggleGUIFrame();

            int orderIndex = orderListBox.children.IndexOf(component);
            if (orderIndex<0 || orderIndex >= listBox.children.Count) return false;

            var characterFrame = listBox.children[orderIndex];
            if (characterFrame == null) return false;

            GameMain.GameSession.CrewManager.commander.SelectCharacter(characterFrame.UserData as Character);

            return false;
        }

        public void AddCharacter(Character character)
        {
            characters.Add(character);
            if (!characterInfos.Contains(character.Info))
            {
                characterInfos.Add(character.Info);
            }

            if (character is AICharacter)
            {
                commander.UpdateCharacters();
            }


            character.Info.CreateCharacterFrame(listBox, character.Info.Name.Replace(' ', '\n'), character);

            GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 40, 40), Color.Transparent, null, orderListBox);
            frame.UserData = character;
            //frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            frame.HoverColor = Color.LightGray * 0.5f;
            frame.SelectedColor = Color.Gold * 0.5f;

            var ai = character.AIController as HumanAIController;
            SetCharacterOrder(character, ai.CurrentOrder);

           

            //string name = character.Info.Name.Replace(' ', '\n');

            //GUITextBlock textBlock = new GUITextBlock(
            //    new Rectangle(40, 0, 0, 25),
            //    name,
            //    Color.Transparent, Color.White,
            //    Alignment.Left, Alignment.Left,
            //    null, frame, false);
            //textBlock.Font = GUI.SmallFont;
            //textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

            //new GUIImage(new Rectangle(-10, -5, 0, 0), character.AnimController.Limbs[0].sprite, Alignment.Left, frame);
        }

        public void Update(float deltaTime)
        {
            guiFrame.Update(deltaTime);


            if (GameMain.Config.KeyBind(InputType.CrewOrders).IsHit())
            {
                //deselect construction unless it's the ladders the character is climbing
                if (!commander.IsOpen && Character.Controlled != null && 
                    Character.Controlled.SelectedConstruction != null && 
                    Character.Controlled.SelectedConstruction.GetComponent<Items.Components.Ladder>() == null)
                {
                    Character.Controlled.SelectedConstruction = null;
                }

                //only allow opening the command UI if there are AICharacters in the crew
                if (commander.IsOpen || characters.Any(c => c is AICharacter)) commander.ToggleGUIFrame();                
            }

            if (commander.Frame != null) commander.Frame.Update(deltaTime);
            if (crewFrameOpen) crewFrame.Update(deltaTime);
        }

        public void ReviveCharacter(Character revivedCharacter)
        {
            GUIComponent characterBlock = listBox.GetChild(revivedCharacter) as GUIComponent;
            if (characterBlock != null) characterBlock.Color = Color.Transparent;

            if (revivedCharacter is AICharacter)
            {
                commander.UpdateCharacters();
            }
        }

        public void KillCharacter(Character killedCharacter)
        {
            GUIComponent characterBlock = listBox.GetChild(killedCharacter) as GUIComponent;
            if (characterBlock != null) characterBlock.Color = Color.DarkRed * 0.5f;

            if (killedCharacter is AICharacter)
            {
                commander.UpdateCharacters();
            }
            //if (characters.Find(c => !c.IsDead)==null)
            //{
            //    Game1.GameSession.EndShift(null, null);
            //}            
        }

        public void CreateCrewFrame(List<Character> crew)
        {
            int width = 600, height = 400;

            crewFrame = new GUIFrame(new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), GUI.Style);
            crewFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            GUIListBox crewList = new GUIListBox(new Rectangle(0, 0, 280, 300), Color.White * 0.7f, GUI.Style, crewFrame);
            crewList.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
            crewList.OnSelected = SelectCrewCharacter;

            foreach (Character character in crew)
            {
                GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 40), Color.Transparent, null, crewList);
                frame.UserData = character;
                frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                frame.Color = (GameMain.NetworkMember != null && GameMain.NetworkMember.Character == character) ? Color.Gold * 0.2f : Color.Transparent;
                frame.HoverColor = Color.LightGray * 0.5f;
                frame.SelectedColor = Color.Gold * 0.5f;

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(40, 0, 0, 25),
                    character.Info.Name + " (" + character.Info.Job.Name + ")",
                    Color.Transparent, Color.White,
                    Alignment.Left, Alignment.Left,
                    null, frame);
                textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                new GUIImage(new Rectangle(-10, 0, 0, 0), character.AnimController.Limbs[0].sprite, Alignment.Left, frame);
            }

            var closeButton = new GUIButton(new Rectangle(0, 0, 80, 20), "Close", Alignment.BottomCenter, GUI.Style, crewFrame);
            closeButton.OnClicked = ToggleCrewFrame;
        }

        protected virtual bool SelectCrewCharacter(GUIComponent component, object obj)
        {
            Character character = obj as Character;
            if (character == null) return false;

            GUIComponent existingFrame = crewFrame.FindChild("selectedcharacter");
            if (existingFrame != null) crewFrame.RemoveChild(existingFrame);

            var previewPlayer = new GUIFrame(
                new Rectangle(0, 0, 230, 300),
                new Color(0.0f, 0.0f, 0.0f, 0.8f), Alignment.TopRight, GUI.Style, crewFrame);
            previewPlayer.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            previewPlayer.UserData = "selectedcharacter";

            character.Info.CreateInfoFrame(previewPlayer);

            if (GameMain.NetworkMember != null) GameMain.NetworkMember.SelectCrewCharacter(component, obj);

            return true;
        }

        private bool ToggleCrewFrame(GUIButton button, object obj)
        {
            if (crewFrame == null) CreateCrewFrame(characters);

            crewFrameOpen = !crewFrameOpen;
            return true;
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
                
                Character character = Character.Create(characterInfos[i], waypoints[i].WorldPosition);
                Character.Controlled = character;

                if (!character.Info.StartItemsGiven)
                {
                    character.GiveJobItems(waypoints[i]);
                    character.Info.StartItemsGiven = true;
                }

                AddCharacter(character);
            }

            if (characters.Any()) listBox.Select(0);// SelectCharacter(null, characters[0]);
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
            orderListBox.ClearChildren();
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (commander.IsOpen)
            {
                commander.Draw(spriteBatch);
            }
            else
            {
                guiFrame.Draw(spriteBatch);
                if (crewFrameOpen) crewFrame.Draw(spriteBatch);
            }
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
