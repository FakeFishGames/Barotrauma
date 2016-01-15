using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace Barotrauma
{
    class EditMapScreen : Screen
    {
        private Camera cam;

        public GUIComponent GUIpanel;

        private GUIComponent[] GUItabs;
        private int selectedTab;

        private GUITextBox nameBox;

        //a Character used for picking up and manipulating items
        private Character dummyCharacter;
        
        private bool characterMode;

        private Tutorials.EditorTutorial tutorial;

        public Camera Cam
        {
            get { return cam; }
        }

        public int SelectedTab
        {
            get { return selectedTab; }
        }

        //public string GetSubName()
        //{
        //    return ((Submarine.Loaded == null) ? "" : Submarine.Loaded.Name);
        //}

        private string GetItemCount()
        {
            return "Items: " +Item.ItemList.Count;
        }

        private string GetStructureCount()
        {
            return "Structures: " + (MapEntity.mapEntityList.Count - Item.ItemList.Count);
        }

        private string GetPhysicsBodyCount()
        {
            return "Physics bodies: " + GameMain.World.BodyList.Count;
        }


        public EditMapScreen()
        {
            cam = new Camera(); 
            cam.Translate(new Vector2(-10.0f, 50.0f));

            selectedTab = -1;

            GUIpanel = new GUIFrame(new Rectangle(0, 0, 150, GameMain.GraphicsHeight), GUI.Style);
            GUIpanel.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
            //GUIListBox constructionList = new GUIListBox(new Rectangle(0, 0, 0, 300), Color.White * 0.7f, GUIpanel);
            //constructionList.OnSelected = MapEntityPrefab.SelectPrefab;
            //constructionList.CheckSelected = MapEntityPrefab.GetSelected;



            new GUITextBlock(new Rectangle(0, 20, 0, 20), "Submarine:", GUI.Style, GUIpanel);
            nameBox = new GUITextBox(new Rectangle(0, 40, 0, 20), GUI.Style, GUIpanel);
            nameBox.OnEnterPressed = ChangeSubName;
            //nameBlock.TextGetter = GetSubName;

            GUIButton button = new GUIButton(new Rectangle(0,70,0,20), "Save", GUI.Style, GUIpanel);
            button.OnClicked = SaveSub;

            GUITextBlock itemCount = new GUITextBlock(new Rectangle(0, 100, 0, 20), "", GUI.Style, GUIpanel);
            itemCount.TextGetter = GetItemCount;

            GUITextBlock structureCount = new GUITextBlock(new Rectangle(0, 120, 0, 20), "", GUI.Style, GUIpanel);
            structureCount.TextGetter = GetStructureCount;

            //GUITextBlock physicsBodyCount = new GUITextBlock(new Rectangle(0, 120, 0, 20), "", GUI.Style, GUIpanel);
            //physicsBodyCount.TextGetter = GetPhysicsBodyCount;
            

            //button = new GUIButton(new Rectangle(0, 180, 0, 20), "Structures", Alignment.Left, GUI.Style, GUIpanel);
            //button.UserData = 1;
            //button.OnClicked = SelectTab;

            
            GUItabs = new GUIComponent[Enum.GetValues(typeof(MapEntityCategory)).Length];

            int width = 400, height = 400;
            int y = 160;
            foreach (MapEntityCategory category in Enum.GetValues(typeof(MapEntityCategory)))
            {

                var catButton = new GUIButton(new Rectangle(0, y, 0, 20), category.ToString(), Alignment.Left, GUI.Style, GUIpanel);
                catButton.UserData = (int)category;
                catButton.OnClicked = SelectTab;
                y+=25;

                GUItabs[(int)category] = new GUIFrame(new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), GUI.Style);
                GUItabs[(int)category].Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

                GUIListBox itemList = new GUIListBox(new Rectangle(0, 0, 0, 0), Color.White * 0.7f, GUI.Style, GUItabs[(int)category]);
                itemList.OnSelected = SelectPrefab;
                itemList.CheckSelected = MapEntityPrefab.GetSelected;

                foreach (MapEntityPrefab ep in MapEntityPrefab.list)
                {
                    if (ep.Category != category) continue;

                    Color color = ((itemList.CountChildren % 2) == 0) ? Color.Transparent : Color.White * 0.1f;

                    GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 50), Color.Transparent, null, itemList);
                    frame.UserData = ep;
                    frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                    frame.Color = color;
                    frame.HoverColor = Color.Gold * 0.2f;
                    frame.SelectedColor = Color.Gold * 0.5f;

                    GUITextBlock textBlock = new GUITextBlock(
                        new Rectangle(40, 0, 0, 25),
                        ep.Name,
                        Color.Transparent, Color.White,
                        Alignment.Left, Alignment.Left,
                        null, frame);
                    textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                    if (!string.IsNullOrWhiteSpace(ep.Description))
                    {
                        textBlock.ToolTip = ep.Description;
                    }

                    if (ep.sprite != null)
                    {
                        GUIImage img = new GUIImage(new Rectangle(0, 0, 40, 40), ep.sprite, Alignment.Left, frame);
                        img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
                        img.Color = ep.SpriteColor;
                    }
                }

                itemList.children.Sort((i1, i2) => (i1.UserData as MapEntityPrefab).Name.CompareTo((i2.UserData as MapEntityPrefab).Name));

            }

            y+=50;
            button = new GUIButton(new Rectangle(0, y, 0, 20), "Character mode", Alignment.Left, GUI.Style, GUIpanel);
            button.ToolTip = "Allows you to pick up and use items. Useful for things such as placing items inside closets, turning devices on/off and doing the wiring.";
            button.OnClicked = ToggleCharacterMode;
            
            y+=50;
            button = new GUIButton(new Rectangle(0, y, 0, 20), "Generate waypoints", Alignment.Left, GUI.Style, GUIpanel);
            button.OnClicked = GenerateWaypoints;
            
            y+=50;

            new GUITextBlock(new Rectangle(0, y, 0, 20), "Show:", GUI.Style, GUIpanel);

            var tickBox = new GUITickBox(new Rectangle(0,y+20,20,20), "Waypoints", Alignment.TopLeft, GUIpanel);
            tickBox.OnSelected = (GUITickBox obj) => { WayPoint.ShowWayPoints = !WayPoint.ShowWayPoints; return true; };
            tickBox = new GUITickBox(new Rectangle(0, y + 40, 20, 20), "Spawnpoints", Alignment.TopLeft, GUIpanel);
            tickBox.OnSelected = (GUITickBox obj) => { WayPoint.ShowSpawnPoints = !WayPoint.ShowSpawnPoints; return true; };
            
        }

        public void StartTutorial()
        {
            tutorial = new Tutorials.EditorTutorial("EditorTutorial");

            CoroutineManager.StartCoroutine(tutorial.UpdateState());
        }

        public override void Select()
        {
            base.Select();
            
            GUIComponent.MouseOn = null;
            characterMode = false;

            if (Submarine.Loaded != null)
            {
                cam.Position = Submarine.Loaded.Position + Submarine.HiddenSubPosition;
                nameBox.Text = Submarine.Loaded.Name;
            }
             //CreateDummyCharacter();
        }

        public override void Deselect()
        {
            base.Deselect();

            GUIComponent.MouseOn = null;

            MapEntityPrefab.Selected = null;

            if (dummyCharacter != null)
            {
                dummyCharacter.Remove();
                dummyCharacter = null;
                GameMain.World.ProcessChanges();
            }
        }

        private void CreateDummyCharacter()
        {
            if (dummyCharacter != null) dummyCharacter.Remove();

            dummyCharacter = Character.Create(Character.HumanConfigFile, Vector2.Zero);
            Character.Controlled = dummyCharacter;
            GameMain.World.ProcessChanges();
        }

        private bool SaveSub(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                nameBox.Flash();
                return false;
            }

            if (nameBox.Text.Contains("../"))
            {
                DebugConsole.ThrowError("Illegal symbols in filename (../)");
                nameBox.Flash();
                return false;
            }

            if (Submarine.Loaded!=null)
            {
                Submarine.Loaded.Name = nameBox.Text;
            }

            Submarine.SaveCurrent(nameBox.Text + ".sub");

            GUI.AddMessage("Submarine saved to " + Submarine.Loaded.FilePath, Color.DarkGreen, 3.0f);
            

            return false;
        }

        private bool SelectTab(GUIButton button, object obj)
        {
            selectedTab = (int)obj;
            return true;
        }
        
        private bool ToggleCharacterMode(GUIButton button, object obj)
        {
            selectedTab = -1;

            characterMode = !characterMode;         
            //button.Color = (characterMode) ? Color.Gold : Color.White;

            if (characterMode)
            {
                CreateDummyCharacter();
            }
            else if (dummyCharacter != null)
            {     
                foreach (Item item in dummyCharacter.Inventory.Items)
                {
                    if (item == null) continue;

                    item.Remove();
                }

                dummyCharacter.Remove();
                dummyCharacter = null;
            }

            foreach (MapEntity me in MapEntity.mapEntityList)
            {
                me.IsHighlighted = false;
                me.IsSelected = false;
            }
            
            return true;
        }

        private bool ChangeSubName(GUITextBox textBox, string text)
        {
            if (Submarine.Loaded != null) Submarine.Loaded.Name = text;
            textBox.Deselect();

            textBox.Text = text;

            return true;
        }

        private bool SelectPrefab(GUIComponent component, object obj)
        {
            MapEntityPrefab.SelectPrefab(obj);
            selectedTab = -1;
            GUIComponent.MouseOn = null;
            return true;
        }

        private bool GenerateWaypoints(GUIButton button, object obj)
        {
            WayPoint.GenerateSubWaypoints();
            return true;
        }


        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        public override void Update(double deltaTime)
        {
            if (tutorial!=null) tutorial.Update((float)deltaTime);

            if (GUIComponent.MouseOn == null)
            {
                cam.MoveCamera((float)deltaTime);
                //cam.Zoom = MathHelper.Clamp(cam.Zoom + (PlayerInput.ScrollWheelSpeed / 1000.0f)*cam.Zoom, 0.1f, 2.0f);
            }

            if (characterMode)
            {
                if (Entity.FindEntityByID(dummyCharacter.ID)!=dummyCharacter)
                {
                    ToggleCharacterMode(null, null);
                }

                foreach (MapEntity me in MapEntity.mapEntityList)
                {
                    me.IsHighlighted = false;
                }

                if (dummyCharacter.SelectedConstruction==null)
                {
                    Vector2 mouseSimPos = FarseerPhysics.ConvertUnits.ToSimUnits(cam.ScreenToWorld(PlayerInput.MousePosition));
                    foreach (Limb limb in dummyCharacter.AnimController.Limbs)
                    {
                        limb.body.SetTransform(mouseSimPos, 0.0f);
                    }
                }

                dummyCharacter.ControlLocalPlayer((float)deltaTime, cam, false);
                dummyCharacter.Control((float)deltaTime, cam);
                cam.TargetPos = Vector2.Zero;
            }
            else
            {

                MapEntity.UpdateSelecting(cam);
            }


            GUIpanel.Update((float)deltaTime);
            if (selectedTab > -1)
            {
                GUItabs[selectedTab].Update((float)deltaTime);
                if (PlayerInput.RightButtonClicked()) selectedTab = -1;
            }
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            cam.UpdateTransform();

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                null, null, null, null,
                cam.Transform);

            graphics.Clear(new Color(0.051f, 0.149f, 0.271f, 1.0f));

            Submarine.Draw(spriteBatch, true);

            if (!characterMode)
            {
                if (MapEntityPrefab.Selected != null) MapEntityPrefab.Selected.UpdatePlacing(spriteBatch, cam);

                MapEntity.DrawSelecting(spriteBatch, cam);
            }
            
            spriteBatch.End();

            //-------------------- HUD -----------------------------

            spriteBatch.Begin();

            GUIpanel.Draw(spriteBatch);

            if (selectedTab > -1) GUItabs[selectedTab].Draw(spriteBatch);
            
            //EntityPrefab.DrawList(spriteBatch, new Vector2(20,50));
            
            if (characterMode)
            {
                if (dummyCharacter != null)                     
                {
                    dummyCharacter.AnimController.FindHull(); 

                    foreach (Item item in dummyCharacter.SelectedItems)
                    {
                        if (item == null) continue;
                        item.SetTransform(dummyCharacter.SimPosition, 0.0f);

                        item.Update(cam, (float)deltaTime);
                    }

                    if (dummyCharacter.SelectedConstruction != null)
                    {
                        if (dummyCharacter.SelectedConstruction == dummyCharacter.ClosestItem)
                        {
                            dummyCharacter.SelectedConstruction.DrawHUD(spriteBatch, dummyCharacter);

                        }
                        else
                        {
                            dummyCharacter.SelectedConstruction = null;
                        }
                    }

                }

            }
            else
            {
                MapEntity.Edit(spriteBatch, cam);
            }

            if (tutorial != null) tutorial.Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, cam);

            if (!PlayerInput.LeftButtonDown()) Inventory.draggingItem = null;
                                              
            spriteBatch.End();
        }
    }
}
