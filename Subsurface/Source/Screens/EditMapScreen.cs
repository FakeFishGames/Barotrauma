using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace Subsurface
{
    class EditMapScreen : Screen
    {
        Camera cam;

        GUIComponent GUIpanel;

        GUIComponent[] GUItabs;
        int selectedTab;

        //a character used for picking up and manipulating items
        Character dummyCharacter;
        
        bool characterMode;

        public Camera Cam
        {
            get { return cam; }
        }

        public EditMapScreen()
        {
            cam = new Camera(); 
            cam.Translate(new Vector2(-10.0f, 50.0f));

            selectedTab = -1;

            GUIpanel = new GUIFrame(new Rectangle(0, 0, 150, Game1.GraphicsHeight), GUI.Style);
            GUIpanel.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
            //GUIListBox constructionList = new GUIListBox(new Rectangle(0, 0, 0, 300), Color.White * 0.7f, GUIpanel);
            //constructionList.OnSelected = MapEntityPrefab.SelectPrefab;
            //constructionList.CheckSelected = MapEntityPrefab.GetSelected;

            GUIButton button = new GUIButton(new Rectangle(0, 50, 100, 20), "Items", Alignment.Left, GUI.Style, GUIpanel);
            button.UserData = 0;
            button.OnClicked = SelectTab;

            button = new GUIButton(new Rectangle(0, 80, 100, 20), "Structures", Alignment.Left, GUI.Style, GUIpanel);
            button.UserData = 1;
            button.OnClicked = SelectTab;

            button = new GUIButton(new Rectangle(0, 140, 100, 20), "Character mode", Alignment.Left, GUI.Style, GUIpanel);
            button.OnClicked = ToggleCharacterMode;
            
            GUItabs = new GUIComponent[2];
            int width = 400, height = 400;
            GUItabs[0] = new GUIFrame(new Rectangle(Game1.GraphicsWidth/2-width/2, Game1.GraphicsHeight/2-height/2, width, height), GUI.Style);
            GUItabs[0].Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
            GUIListBox itemList = new GUIListBox(new Rectangle(0, 0, 0, 0), Color.White * 0.7f, GUI.Style, GUItabs[0]);
            itemList.OnSelected = SelectPrefab;
            itemList.CheckSelected = MapEntityPrefab.GetSelected;

            GUItabs[1] = new GUIFrame(new Rectangle(Game1.GraphicsWidth / 2 - width / 2, Game1.GraphicsHeight / 2 - height / 2, width, height), GUI.Style);
            GUItabs[1].Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
            GUIListBox structureList = new GUIListBox(new Rectangle(0, 0, 0, 300), Color.White * 0.7f, GUI.Style, GUItabs[1]);
            structureList.OnSelected = SelectPrefab;
            structureList.CheckSelected = MapEntityPrefab.GetSelected;

            foreach (MapEntityPrefab ep in MapEntityPrefab.list)
            {
                GUIListBox parent = ((ep as ItemPrefab) == null) ? structureList : itemList;
                Color color = ((parent.CountChildren % 2) == 0) ? Color.Transparent : Color.White * 0.1f;
                
                GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 50), Color.Transparent, null, parent);
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

                if (ep.sprite != null)
                {
                    GUIImage img = new GUIImage(new Rectangle(0, 0, 40, 40), ep.sprite, Alignment.Left, frame);
                    img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
                }
            }
        }

        public override void Select()
        {
            base.Select();
            
            GUIComponent.MouseOn = null;
            characterMode = false;
             //CreateDummyCharacter();
        }

        public override void Deselect()
        {
            base.Deselect();

            GUIComponent.MouseOn = null;

            if (dummyCharacter != null)
            {
                dummyCharacter.Remove();
                dummyCharacter = null;
                Game1.World.ProcessChanges();
            }
        }

        private void CreateDummyCharacter()
        {
            if (dummyCharacter != null) dummyCharacter.Remove();

            dummyCharacter = new Character(Character.HumanConfigFile, Vector2.Zero);
            Character.Controlled = dummyCharacter;
            Game1.World.ProcessChanges();
        }

        private bool SelectTab(GUIButton button, object obj)
        {
            selectedTab = (int)obj;
            return true;
        }
        
        private bool ToggleCharacterMode(GUIButton button, object obj)
        {
            selectedTab = 0;

            characterMode = !characterMode;         
            button.Color = (characterMode) ? Color.Gold : Color.White;

            if (characterMode)
            {
                CreateDummyCharacter();
            }
            else if (dummyCharacter != null)
            {     
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

        private bool SelectPrefab(GUIComponent component, object obj)
        {
            MapEntityPrefab.SelectPrefab(obj);
            selectedTab = -1;
            GUIComponent.MouseOn = null;
            return true;
        }


        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        public override void Update(double deltaTime)
        {
            //Vector2 mousePosition = new Vector2(PlayerInput.GetMouseState.X, PlayerInput.GetMouseState.Y);
            //mousePosition = cam.ScreenToWorld(mousePosition);

            //if (!Character.characterList.Contains(dummyCharacter))
            //{
            //    CreateDummyCharacter();
            //}

            if (GUIComponent.MouseOn==null) cam.MoveCamera((float)deltaTime);
            cam.Zoom = MathHelper.Clamp(cam.Zoom + PlayerInput.ScrollWheelSpeed/1000.0f,0.1f, 2.0f);

            if (characterMode)
            {
                foreach (MapEntity me in MapEntity.mapEntityList)
                {
                    me.IsHighlighted = false;
                }

                if (dummyCharacter.SelectedConstruction==null)
                {
                    Vector2 mouseSimPos = FarseerPhysics.ConvertUnits.ToSimUnits(cam.ScreenToWorld(PlayerInput.MousePosition));
                    foreach (Limb limb in dummyCharacter.AnimController.limbs)
                    {
                        limb.body.SetTransform(mouseSimPos, 0.0f);
                    }
                }

                dummyCharacter.ControlLocalPlayer(cam, false);
                dummyCharacter.Control((float)deltaTime, cam);
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
            //cam.UpdateTransform();

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
            
            GUI.Draw((float)deltaTime, spriteBatch, cam);

            //EntityPrefab.DrawList(spriteBatch, new Vector2(20,50));



            if (characterMode)
            {
                if (dummyCharacter != null)                     
                {
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

                if (PlayerInput.GetMouseState.LeftButton != ButtonState.Pressed)
                {
                    //if (Inventory.draggingItem!=null)
                    //{
                    //    Inventory.draggingItem.see
                    //}
                    Inventory.draggingItem = null;
                }
            }
            else
            {
                MapEntity.Edit(spriteBatch, cam);
            }


                                              
            spriteBatch.End();

        }
    }
}
