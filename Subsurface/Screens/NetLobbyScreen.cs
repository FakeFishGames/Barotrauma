using System;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;
using FarseerPhysics;
using FarseerPhysics.Factories;
using FarseerPhysics.Dynamics;
using System.IO;

namespace Subsurface
{
    class NetLobbyScreen : Screen
    {
        GUIFrame menu;
        GUIFrame infoFrame;
        GUIListBox playerList;

        GUIListBox mapList, modeList, chatBox;
        GUITextBox textBox;

        GUIScrollBar durationBar;

        GUIFrame playerFrame;

        float camAngle;

        Body previewPlatform;
        Hull previewHull;

        public bool isServer;

        public Map SelectedMap
        {
            get { return mapList.SelectedData as Map; }
        }


        public GameModePreset SelectedMode
        {
            get { return modeList.SelectedData as GameModePreset; }
        }

        public TimeSpan GameDuration
        {
            get 
            {
                int minutes = (int)(durationBar.BarScroll* 60.0f);
                return new TimeSpan(0, minutes, 0);
            }
        }

        public string DurationText()
        {
            return "Game duration: "+GameDuration+" min";
        }
                
        public NetLobbyScreen()
        {
            Rectangle panelRect = new Rectangle(
                (int)GUI.style.largePadding.X,
                (int)GUI.style.largePadding.Y,
                (int)(Game1.GraphicsWidth - GUI.style.largePadding.X * 2.0f),
                (int)(Game1.GraphicsHeight - GUI.style.largePadding.Y * 2.0f));

            menu = new GUIFrame(panelRect, Color.Transparent);
            //menu.Padding = GUI.style.smallPadding;

            //server info panel ------------------------------------------------------------

            infoFrame = new GUIFrame(new Rectangle(0, 0, (int)(panelRect.Width * 0.6f), (int)(panelRect.Height * 0.6f)), GUI.style.backGroundColor, menu);
            infoFrame.Padding = GUI.style.smallPadding;
            
            //chatbox ----------------------------------------------------------------------
            GUIFrame chatFrame = new GUIFrame(
                new Rectangle(0, (int)(panelRect.Height * 0.6f + GUI.style.smallPadding.W),
                    (int)(panelRect.Width * 0.6f),
                    (int)(panelRect.Height * 0.4f - GUI.style.smallPadding.W)),
                GUI.style.backGroundColor, menu);
            chatFrame.Padding = GUI.style.smallPadding;

            chatBox = new GUIListBox(new Rectangle(0,0,0,chatFrame.Rect.Height-80), Color.White, chatFrame);            
            textBox = new GUITextBox(new Rectangle(0, 0, 0, 25), Color.White, Color.Black, Alignment.Bottom, Alignment.Left, chatFrame);
            textBox.OnEnter = EnterChatMessage;

            //player info panel ------------------------------------------------------------

            playerFrame = new GUIFrame(
                new Rectangle((int)(panelRect.Width * 0.6f + GUI.style.smallPadding.Z), 0,
                    (int)(panelRect.Width * 0.4f - GUI.style.smallPadding.Z), (int)(panelRect.Height * 0.6f)),
                GUI.style.backGroundColor, menu);
            playerFrame.Padding = GUI.style.smallPadding;

            //player list ------------------------------------------------------------------

            GUIFrame playerListFrame = new GUIFrame(
                new Rectangle((int)(panelRect.Width * 0.6f + GUI.style.smallPadding.Z), (int)(panelRect.Height * 0.6f + GUI.style.smallPadding.W),
                    (int)(panelRect.Width * 0.4f - GUI.style.smallPadding.Z), (int)(panelRect.Height * 0.4f - GUI.style.smallPadding.W)),
                GUI.style.backGroundColor, menu);
            playerListFrame.Padding = GUI.style.smallPadding;

            playerList = new GUIListBox(new Rectangle(0,0,0,0), Color.White, playerListFrame);
        }

        public override void Deselect()
        {
            textBox.Deselect();

            if (previewPlatform!=null)
            {
                Game1.world.RemoveBody(previewPlatform);
                previewPlatform = null;
            }

            if (previewHull!=null)
            {
                previewHull.Remove();
                previewHull = null;
            }
        }

        public override void Select()
        {
            infoFrame.ClearChildren();
            
            if (isServer && Game1.Server == null) Game1.Server = new GameServer();

            textBox.Select();

            //int oldMapIndex = 0;
            //if (mapList != null && mapList.SelectedData != null) oldMapIndex = mapList.SelectedIndex;

            new GUITextBlock(new Rectangle(0, 30, 0, 30), "Selected map:", Color.Transparent, Color.Black, Alignment.Left, infoFrame);
            mapList = new GUIListBox(new Rectangle(0, 60, 200, 200), Color.White, infoFrame);
            mapList.OnSelected = SelectMap;
            mapList.Enabled = (Game1.Server!=null);

            if (Map.SavedMaps.Count>0)
            {
                foreach (Map map in Map.SavedMaps)
                {
                    GUITextBlock textBlock = new GUITextBlock(
                        new Rectangle(0, 0, 0, 25),
                        map.Name,
                        GUI.style,
                        Alignment.Left,
                        Alignment.Left,
                        mapList);
                    textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                    textBlock.UserData = map;
                }
            }
            else
            {
                DebugConsole.ThrowError("No saved maps found!");
                return;
            }

            new GUITextBlock(new Rectangle(220, 30, 0, 30), "Selected game mode: ", Color.Transparent, Color.Black, Alignment.Left, infoFrame);
            modeList = new GUIListBox(new Rectangle(220, 60, 200, 200), Color.White, infoFrame);
            modeList.Enabled = (Game1.Server != null);
            //modeList.OnSelected = new GUIListBox.OnSelectedHandler(SelectEvent);

            foreach (GameModePreset mode in GameModePreset.list)
            {
                if (mode.IsSinglePlayer) continue;

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    mode.Name,
                    GUI.style,
                    Alignment.Left,
                    Alignment.Left,
                    modeList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = mode;
            }

            GUITextBlock durationText = new GUITextBlock(new Rectangle((int)(modeList.Rect.X + modeList.Rect.Width + GUI.style.smallPadding.X), modeList.Rect.Y, 100, 20),
                "Game duration: ", Color.Transparent, Color.Black, Alignment.Left, infoFrame);
            durationText.TextGetter = DurationText;

            durationBar = new GUIScrollBar(new Rectangle((int)(modeList.Rect.X + modeList.Rect.Width + GUI.style.smallPadding.X), modeList.Rect.Y + 30, 100, 20),
                Color.Gold, 0.1f, Alignment.Left, infoFrame);
            durationBar.BarSize = 0.1f;
            durationBar.Enabled = (Game1.Server != null);
            
            if (isServer && Game1.Server!=null)
            {
                GUIButton startButton = new GUIButton(new Rectangle(0,0,200,30), "Start", Color.White, Alignment.Right | Alignment.Bottom, infoFrame);
                startButton.OnClicked = Game1.Server.StartGame;

                //mapList.OnSelected = new GUIListBox.OnSelectedHandler(Game1.server.UpdateNetLobby);
                modeList.OnSelected = Game1.Server.UpdateNetLobby;
                durationBar.OnMoved = Game1.Server.UpdateNetLobby;

                if (mapList.CountChildren > 0) mapList.Select(0);
                if (GameModePreset.list.Count > 0) modeList.Select(0);                
            }
            else
            {
                int x = playerFrame.Rect.Width / 2;
                GUITextBox playerName = new GUITextBox(new Rectangle(x, 0, 0, 20), Color.White, Color.Black,
                    Alignment.Left | Alignment.Top, Alignment.Left, playerFrame);
                playerName.Text = Game1.Client.CharacterInfo.name;
                playerName.OnEnter += ChangeCharacterName;

                new GUITextBlock(new Rectangle(x,40,200, 30), "Gender: ", Color.Transparent, Color.Black,
                    Alignment.Left | Alignment.Top, Alignment.Left, playerFrame);

                GUIButton maleButton = new GUIButton(new Rectangle(x+70,50,70,20), "Male", GUI.style,
                    Alignment.Left | Alignment.Top, playerFrame);
                maleButton.UserData = Gender.Male;
                maleButton.OnClicked += SwitchGender;

                GUIButton femaleButton = new GUIButton(new Rectangle(x+150, 50, 70, 20), "Female", GUI.style,
                    Alignment.Left | Alignment.Top, playerFrame);
                femaleButton.UserData = Gender.Female;
                femaleButton.OnClicked += SwitchGender;

                new GUITextBlock(new Rectangle(0, 150, 200, 30), "Job preferences:", Color.Transparent, Color.Black, Alignment.Left, playerFrame);

                GUIListBox jobList = new GUIListBox(new Rectangle(0,180,200,0), Color.White, playerFrame);

                foreach (Job job in Job.jobList)
                {
                    GUITextBlock jobText = new GUITextBlock(new Rectangle(0,0,0,20), job.Name, Color.Transparent, Color.Black, Alignment.Left, jobList);
                    GUIButton upButton = new GUIButton(new Rectangle(jobText.Rect.Width - 40, 0, 20, 20), "u", Color.White, jobText);
                    upButton.UserData = -1;
                    upButton.OnClicked += ChangeJobPreference;

                    GUIButton downButton = new GUIButton(new Rectangle(jobText.Rect.Width - 20, 0, 20, 20), "d", Color.White, jobText);
                    downButton.UserData = 1;
                    downButton.OnClicked += ChangeJobPreference;
                }

                UpdateJobPreferences(jobList);

            }

            base.Select();
        }

        private bool SelectMap(object obj)
        {
            if (Game1.Server != null) Game1.Server.UpdateNetLobby(obj);

            Map map = (Map)obj;

            //map already loaded
            if (Map.Loaded!=null && map.FilePath == Map.Loaded.FilePath) return true;

            map.Load();

            return true;
        }


        public void AddPlayer(string name)
        {
            GUITextBlock textBlock = new GUITextBlock(
                new Rectangle(0, 0, 0, 25),
                name,
                GUI.style,
                Alignment.Left,
                Alignment.Left,
                playerList);
            textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
            textBlock.UserData = name;          
        }

        public void RemovePlayer(string name)
        {
            playerList.RemoveChild(playerList.GetChild(name));
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            Game1.GameScreen.Cam.MoveCamera((float)deltaTime);

            Vector2 pos = new Vector2(
                Map.Borders.X + Map.Borders.Width / 2,
                Map.Borders.Y - Map.Borders.Height / 2);

            camAngle += (float)deltaTime / 10.0f;
            Vector2 offset = (new Vector2(
                (float)Math.Cos(camAngle) * (Map.Borders.Width / 2.0f),
                (float)Math.Sin(camAngle) * (Map.Borders.Height / 2.0f)));
            
            pos += offset * 0.8f;
            
            Game1.GameScreen.Cam.TargetPos = pos;

            menu.Update((float)deltaTime);
                        
            durationBar.BarScroll = Math.Max(durationBar.BarScroll, 1.0f / 60.0f);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            Game1.GameScreen.DrawMap(graphics, spriteBatch);

            spriteBatch.Begin();

            menu.Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();


            if (Game1.Client != null)
            {
                if (Game1.Client.Character != null)
                {
                    Vector2 position = new Vector2(playerFrame.Rect.X + playerFrame.Rect.Width * 0.25f, playerFrame.Rect.Y + 25.0f);

                    Vector2 pos = Game1.Client.Character.Position;
                    pos.Y = -pos.Y;
                    Matrix transform = Matrix.CreateTranslation(new Vector3(-pos + position, 0.0f));

                    spriteBatch.Begin(SpriteSortMode.BackToFront, null, null, null, null, null, transform);
                    Game1.Client.Character.Draw(spriteBatch);
                    spriteBatch.End();
                }
                else
                {
                    CreatePreviewCharacter();
                }
            }

        }

        public void NewChatMessage(string message, Color color)
        {
            GUITextBlock msg = new GUITextBlock(new Rectangle(0, 0, 0, 20),
                message, 
                ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black*0.1f, color, 
                Alignment.Left, null, true);

            msg.Padding = new Vector4(GUI.style.smallPadding.X, 0, 0, 0);
            chatBox.AddChild(msg);
        }


        public bool StartGame(object obj)
        {
            Game1.Server.StartGame(null, obj);
            return true;
        }

        public bool EnterChatMessage(GUITextBox textBox, string message)
        {
            if (String.IsNullOrEmpty(message)) return false;

            if (isServer)
            {
                Game1.Server.SendChatMessage("Server: " + message);
            }
            else
            {
                Game1.Client.SendChatMessage(Game1.Client.Name + ": " + message);
            }

            return true;
        }

        private void CreatePreviewCharacter()
        {
            if (Game1.Client.Character != null) Game1.Client.Character.Remove();

            Vector2 pos = new Vector2(1000.0f, 1000.0f);

            Character character = new Character(Game1.Client.CharacterInfo, pos);

            Game1.Client.Character = character;

            character.animController.isStanding = true;
            
            if (previewPlatform==null)
            {
                Body platform = BodyFactory.CreateRectangle(Game1.world, 3.0f, 1.0f, 5.0f);
                platform.SetTransform(new Vector2(pos.X, pos.Y - 2.5f), 0.0f);
                platform.IsStatic = true;
            }

            if (previewHull==null)
            {
                pos = ConvertUnits.ToDisplayUnits(pos);
                previewHull = new Hull(new Rectangle((int)pos.X - 100, (int)pos.Y + 100, 200, 200));
            }
            
            Physics.Alpha = 1.0f;

            for (int i = 0; i < 500; i++)
            {
                character.animController.Update((float)Physics.step);
                character.animController.UpdateAnim((float)Physics.step);
                Game1.world.Step((float)Physics.step);
            }
        }

        private bool SwitchGender(GUIButton button, object obj)
        {
            try
            {
                Gender gender = (Gender)obj;
                Game1.Client.CharacterInfo.gender = gender;
                Game1.Client.SendCharacterData();
                CreatePreviewCharacter();
            }
            catch
            {
                return false;
            }
            return true;
        }

        private bool ChangeCharacterName(GUITextBox textBox, string newName)
        {
            if (string.IsNullOrEmpty(newName)) return false;

            Game1.Client.CharacterInfo.name = newName;
            Game1.Client.Name = newName;
            Game1.Client.SendCharacterData();

            textBox.Text = newName;

            return true;
        }

        private bool ChangeJobPreference(GUIButton button, object obj)
        {
            GUIComponent jobText = button.Parent;
            GUIListBox jobList = jobText.Parent as GUIListBox;

            int index = jobList.children.IndexOf(jobText);
            int newIndex = index + (int)obj;
            if (newIndex<0 || newIndex>jobList.children.Count-1) return false;

            GUIComponent temp = jobList.children[newIndex];
            jobList.children[newIndex] = jobText;
            jobList.children[index] = temp;

            UpdateJobPreferences(jobList);

            return true;
        }

        private void UpdateJobPreferences(GUIListBox listBox)
        {
            listBox.Deselect();
            for (int i = 0; i<listBox.children.Count; i++)
            {
                float a = (float)i/listBox.children.Count;
                Color color = new Color(a, 1.0f - a, 0.5f, 1.0f);

                listBox.children[i].Color = color;
            }
        }


        public void WriteData(NetOutgoingMessage msg)
        {
            Map selectedMap = mapList.SelectedData as Map;

            if (selectedMap==null)
            {
                msg.Write(" ");
                msg.Write(" ");
            }
            else
            {
                msg.Write(Path.GetFileName(selectedMap.Name));
                msg.Write(selectedMap.MapHash.MD5Hash);
            }

            msg.Write(modeList.SelectedIndex);
            msg.Write(durationBar.BarScroll);
        }

        public bool TrySelectMap(string mapName, string md5Hash)
        {

            Map map = Map.SavedMaps.Find(m => m.Name == mapName);
            if (map==null)
            {
                DebugConsole.ThrowError("The map ''" + mapName + "'' has been selected by the server.");
                DebugConsole.ThrowError("Matching map not found in your map folder.");
                return false;
            }
            else
            {
                if (map.MapHash.MD5Hash!=md5Hash)
                {
                    DebugConsole.ThrowError("Your version of the map file ''"+map.Name+"'' doesn't match the server's version!");
                    DebugConsole.ThrowError("Your file: "+map.Name+"(MD5 hash : "+map.MapHash.MD5Hash+")");
                    DebugConsole.ThrowError("Server's file: " + mapName + "(MD5 hash : " + md5Hash + ")");
                    return false;
                }
                else
                {
                    mapList.Select(map);
                    //map.Load();
                    return true;
                }
            }
        }

        public void ReadData(NetIncomingMessage msg)
        {
            string mapName = msg.ReadString();
            string md5Hash = msg.ReadString();

            TrySelectMap(mapName, md5Hash);
            

            //mapList.Select(msg.ReadInt32());
            modeList.Select(msg.ReadInt32());

            durationBar.BarScroll = msg.ReadFloat();
        }
    }
}
