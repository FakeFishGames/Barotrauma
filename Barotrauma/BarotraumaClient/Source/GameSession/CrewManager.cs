using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CrewManager
    {
        private List<CharacterInfo> characterInfos;
        private List<Character> characters;

        //orders that have not been issued to a specific character
        private List<Pair<Order, float>> activeOrders = new List<Pair<Order, float>>();

        public int WinningTeam = 1;
        
        private GUIFrame guiFrame;
        private GUIListBox listBox, orderListBox;
        
        private CrewCommander commander;

        private bool isSinglePlayer;

        public CrewCommander CrewCommander
        {
            get { return commander; }
        }

        public List<Pair<Order, float>> ActiveOrders
        {
            get { return activeOrders; }
        }
                
        public CrewManager(bool isSinglePlayer)
        {
            characters = new List<Character>();
            characterInfos = new List<CharacterInfo>();
            
            guiFrame = new GUIFrame(new Rectangle(0, 50, 150, 450), Color.Transparent);
            guiFrame.Padding = Vector4.One * 5.0f;

            listBox = new GUIListBox(new Rectangle(45, 30, 150, 0), Color.Transparent, null, guiFrame);
            listBox.ScrollBarEnabled = false;
            listBox.OnSelected = SelectCharacter;
            listBox.Visible = isSinglePlayer;

            orderListBox = new GUIListBox(new Rectangle(5, 30, 30, 0), Color.Transparent, null, guiFrame);
            orderListBox.ScrollBarEnabled = false;
            orderListBox.OnSelected = SelectCharacterOrder;
            orderListBox.Visible = isSinglePlayer;

            commander = new CrewCommander(this);
        }

        public CrewManager(XElement element, bool isSinglePlayer)
            : this(isSinglePlayer)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "character") continue;

                characterInfos.Add(new CharacterInfo(subElement));
            }
        }

        public List<Character> GetCharacters()
        {
            return characters;
        }

        public List<CharacterInfo> GetCharacterInfos()
        {
            return characterInfos;
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

        public bool AddOrder(Order order, float fadeOutTime)
        {
            Pair<Order, float> existingOrder = activeOrders.Find(o => o.First.Prefab == order.Prefab && o.First.TargetEntity == order.TargetEntity);
            if (existingOrder != null)
            {
                existingOrder.Second = fadeOutTime;
                return false;
            }
            else
            {
                activeOrders.Add(new Pair<Order, float>() { First = order, Second = fadeOutTime });
                return true;
            }
        }

        public void RemoveOrder(Order order)
        {
            activeOrders.RemoveAll(o => o.First == order);
        }

        public void SetCharacterOrder(Character character, Order order)
        {
            var characterFrame = listBox.FindChild(character);
            if (characterFrame == null) return;

            int characterIndex = listBox.children.IndexOf(characterFrame);
            orderListBox.children[characterIndex].ClearChildren();
            
            if (order == null) return;

            var img = new GUIImage(new Rectangle(0, 0, 30, 30), order.SymbolSprite, Alignment.Center, orderListBox.children[characterIndex]);
            img.Scale = 30.0f / img.SourceRect.Width;
            img.Color = order.Color;
            img.CanBeFocused = false;

            orderListBox.children[characterIndex].ToolTip = TextManager.Get("Order") + ": " + order.Name;
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
                character.Info.CreateCharacterFrame(listBox, character.Info.Name.Replace(' ', '\n'), character);
                GUIFrame orderFrame = new GUIFrame(new Rectangle(0, 0, 40, 40), Color.Transparent, "ListBoxElement", orderListBox);
                orderFrame.UserData = character;

                var ai = character.AIController as HumanAIController;
                if (ai == null)
                {
                    DebugConsole.ThrowError("Error in crewmanager - attempted to give orders to a character with no HumanAIController");
                    return;
                }
                SetCharacterOrder(character, ai.CurrentOrder);
            }
        }

        public void RemoveCharacter(Character character)
        {
            characters.Remove(character);
        }

        public void AddCharacterInfo(CharacterInfo characterInfo)
        {
            if (characterInfos.Contains(characterInfo))
            {
                DebugConsole.ThrowError("Tried to add the same character info to CrewManager twice.\n" + Environment.StackTrace);
                return;
            }

            characterInfos.Add(characterInfo);
        }

        public void AddToGUIUpdateList()
        {
            guiFrame.AddToGUIUpdateList();
            if (commander.Frame != null) commander.Frame.AddToGUIUpdateList();
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
                
                commander.ToggleGUIFrame();                
            }

            foreach (Pair<Order, float> order in activeOrders)
            {
                order.Second -= deltaTime;
            }
            activeOrders.RemoveAll(o => o.Second <= 0.0f);

            if (commander.Frame != null) commander.Frame.Update(deltaTime);
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
                    new GUITextBlock(new Rectangle(0, y - 20, 100, 20), CombatMission.GetTeamName(teamIDs[i]), "", crewFrame);
                }

                GUIListBox crewList = new GUIListBox(new Rectangle(0, y, 280, listBoxHeight), Color.White * 0.7f, "", crewFrame);
                crewList.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
                crewList.OnSelected = (component, obj) =>
                {
                    SelectCrewCharacter(component.UserData as Character, crewList);
                    return true;
                };
            
                foreach (Character character in crew.FindAll(c => c.TeamID == teamIDs[i]))
                {
                    GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 40), Color.Transparent, "ListBoxElement", crewList);
                    frame.UserData = character;
                    frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                    frame.Color = (GameMain.NetworkMember != null && GameMain.NetworkMember.Character == character) ? Color.Gold * 0.2f : Color.Transparent;

                    GUITextBlock textBlock = new GUITextBlock(
                        new Rectangle(40, 0, 0, 25),
                        ToolBox.LimitString(character.Info.Name + " (" + character.Info.Job.Name + ")", GUI.Font, frame.Rect.Width-20),
                        null,null,
                        Alignment.Left, Alignment.Left,
                        "", frame);
                    textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                    new GUIImage(new Rectangle(-10, 0, 0, 0), character.AnimController.Limbs[0].sprite, Alignment.Left, frame);
                }

                y += crewList.Rect.Height + 30;
            }
        }

        protected virtual bool SelectCrewCharacter(Character character, GUIComponent crewList)
        {
            if (character == null) return false;

            GUIComponent existingFrame = crewList.Parent.FindChild("SelectedCharacter");
            if (existingFrame != null) crewList.Parent.RemoveChild(existingFrame);

            var previewPlayer = new GUIFrame(
                new Rectangle(0, 0, 230, 300),
                new Color(0.0f, 0.0f, 0.0f, 0.8f), Alignment.TopRight, "", crewList.Parent);
            previewPlayer.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            previewPlayer.UserData = "SelectedCharacter";

            character.Info.CreateInfoFrame(previewPlayer);

            if (GameMain.NetworkMember != null) GameMain.NetworkMember.SelectCrewCharacter(character, previewPlayer);

            return true;
        }
        

        public void StartRound()
        {
            listBox.ClearChildren();
            characters.Clear();

            WayPoint[] waypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub, false);

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

        public void EndRound()
        {
            foreach (Character c in characters)
            {
                if (!c.IsDead)
                {
                    c.Info.UpdateCharacterItems();
                    continue;
                }

                characterInfos.Remove(c.Info);
            }

            //remove characterinfos whose character doesn't exist anymore
            //(i.e. character was removed during the round)
            characterInfos.RemoveAll(c => c.Character == null);
            
            characters.Clear();
            listBox.ClearChildren();
            orderListBox.ClearChildren();
        }

        public void Reset()
        {
            characters.Clear();
            characterInfos.Clear();
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
            }
        }

        public void Save(XElement parentElement)
        {
            XElement element = new XElement("crew");

            foreach (CharacterInfo ci in characterInfos)
            {
                ci.Save(element);
            }

            parentElement.Add(element);
        }
    }
}
