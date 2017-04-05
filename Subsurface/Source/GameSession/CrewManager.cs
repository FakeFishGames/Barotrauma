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

        public int WinningTeam = 1;
        
        private int money;
        
        private GUIFrame guiFrame;
        private GUIListBox listBox, orderListBox;

        private bool crewFrameOpen;
        //private GUIButton crewButton;
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

            commander = new CrewCommander(this);

            money = 10000;
        }

        public CrewManager(XElement element)
            : this()
        {
            money = ToolBox.GetAttributeInt(element, "money", 0);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "character") continue;

                characterInfos.Add(new CharacterInfo(subElement));
            }
        }

        public bool SelectCharacter(GUIComponent component, object selection)
        {
            //listBox.Select(selection);
            Character character = selection as Character;

            if (character == null || character.IsDead || character.IsUnconscious) return false;

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
            commander.ToggleGUIFrame();

            int orderIndex = orderListBox.children.IndexOf(component);
            if (orderIndex < 0 || orderIndex >= listBox.children.Count) return false;

            var characterFrame = listBox.children[orderIndex];
            if (characterFrame == null) return false;

            commander.SelectCharacter(characterFrame.UserData as Character);

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
            if (ai == null)
            {
                DebugConsole.ThrowError("Error in crewmanager - attempted to give orders to a character with no HumanAIController");
                return;
            }
            SetCharacterOrder(character, ai.CurrentOrder);
        }

        public void AddToGUIUpdateList()
        {
            guiFrame.AddToGUIUpdateList();
            if (commander.Frame != null) commander.Frame.AddToGUIUpdateList();
            if (crewFrameOpen) crewFrame.AddToGUIUpdateList();
        }

        public void Update(float deltaTime)
        {
            guiFrame.Update(deltaTime);
            
            //TODO: implement AI commands in multiplayer?
            if (GameMain.NetworkMember != null &&
                GameMain.Config.KeyBind(InputType.CrewOrders).IsHit())
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

        public void CreateCrewFrame(List<Character> crew, GUIFrame crewFrame)
        {
            List<byte> teamIDs = crew.Select(c => c.TeamID).Distinct().ToList();

            if (!teamIDs.Any()) teamIDs.Add(0);            

            int listBoxHeight = 300 / teamIDs.Count;

            int y = 20;
            for (int i = 0; i < teamIDs.Count; i++)
            {
                if (teamIDs.Count > 1)
                {
                    new GUITextBlock(new Rectangle(0, y - 20, 100, 20), CombatMission.GetTeamName(teamIDs[i]), GUI.Style, crewFrame);
                }

                GUIListBox crewList = new GUIListBox(new Rectangle(0, y, 280, listBoxHeight), Color.White * 0.7f, GUI.Style, crewFrame);
                crewList.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
                crewList.OnSelected = SelectCrewCharacter;
            
                foreach (Character character in crew.FindAll(c => c.TeamID == teamIDs[i]))
                {
                    GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 40), Color.Transparent, null, crewList);
                    frame.UserData = character;
                    frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                    frame.Color = (GameMain.NetworkMember != null && GameMain.NetworkMember.Character == character) ? Color.Gold * 0.2f : Color.Transparent;
                    frame.HoverColor = Color.LightGray * 0.5f;
                    frame.SelectedColor = Color.Gold * 0.5f;

                    GUITextBlock textBlock = new GUITextBlock(
                        new Rectangle(40, 0, 0, 25),
                        ToolBox.LimitString(character.Info.Name + " (" + character.Info.Job.Name + ")", GUI.Font, frame.Rect.Width-20),
                        Color.Transparent, Color.White,
                        Alignment.Left, Alignment.Left,
                        null, frame);
                    textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                    new GUIImage(new Rectangle(-10, 0, 0, 0), character.AnimController.Limbs[0].sprite, Alignment.Left, frame);
                }

                y += crewList.Rect.Height + 30;
            }


        }

        protected virtual bool SelectCrewCharacter(GUIComponent component, object obj)
        {
            Character character = obj as Character;
            if (character == null) return false;

            var crewFrame = component.Parent;
            while (crewFrame.Parent!=null)
            {
                crewFrame = crewFrame.Parent;
            }

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

        //private bool ToggleCrewFrame(GUIButton button, object obj)
        //{
        //    if (crewFrame == null) CreateCrewFrame(characters);

        //    crewFrameOpen = !crewFrameOpen;
        //    return true;
        //}

        public void StartShift()
        {
            listBox.ClearChildren();
            characters.Clear();

            WayPoint[] waypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub);

            for (int i = 0; i < waypoints.Length; i++)
            {
                Character character;

                if (characterInfos[i].HullID != null)
                {
                    var hull = Entity.FindEntityByID((ushort)characterInfos[i].HullID) as Hull;
                    if (hull == null) continue;
                    character = Character.Create(characterInfos[i], hull.WorldPosition);
                }
                else
                {
                    character = Character.Create(characterInfos[i], waypoints[i].WorldPosition);
                    Character.Controlled = character;

                    if (character.Info != null && !character.Info.StartItemsGiven)
                    {
                        character.GiveJobItems(waypoints[i]);
                        character.Info.StartItemsGiven = true;
                    }
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
