using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using System.Xml.Linq;
using System.IO;

namespace Barotrauma
{
    class MainMenuScreen : Screen
    {
        public enum Tab { NewGame = 1, LoadGame = 2, HostServer = 3, Settings = 4 }

        GUIFrame buttonsTab;

        private GUIFrame[] menuTabs;
        private GUIListBox mapList;

        private GUIListBox saveList;

        private GUITextBox saveNameBox, seedBox;

        private GUITextBox serverNameBox, portBox, passwordBox, maxPlayersBox;
        private GUITickBox isPublicBox, useUpnpBox;

        private GameMain game;

        int selectedTab;

        public MainMenuScreen(GameMain game)
        {
            menuTabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length+1];



            buttonsTab = new GUIFrame(new Rectangle(0,0,0,0), Color.Transparent, Alignment.Left | Alignment.CenterY);
            buttonsTab.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);
            //menuTabs[(int)Tabs.Main].Padding = GUI.style.smallPadding;


            int y = 170;

            Rectangle panelRect = new Rectangle(
                290, y,
                500, 360);

            GUIButton button = new GUIButton(new Rectangle(50, y, 200, 30), "Tutorial", null, Alignment.TopLeft, Alignment.Left, GUI.Style, buttonsTab);
            button.Color = button.Color * 0.8f;
            button.OnClicked = TutorialButtonClicked;

            button = new GUIButton(new Rectangle(50, y + 60, 200, 30), "New Game", null, Alignment.TopLeft, Alignment.Left, GUI.Style, buttonsTab);
            button.Color = button.Color * 0.8f;
            button.UserData = Tab.NewGame;
            button.OnClicked = SelectTab;

            button = new GUIButton(new Rectangle(50, y + 100, 200, 30), "Load Game", null, Alignment.TopLeft, Alignment.Left, GUI.Style, buttonsTab);
            button.Color = button.Color * 0.8f;
            button.UserData = Tab.LoadGame;
            button.OnClicked = SelectTab;

            button = new GUIButton(new Rectangle(50, y + 160, 200, 30), "Join Server", null, Alignment.TopLeft, Alignment.Left, GUI.Style, buttonsTab);
            button.Color = button.Color * 0.8f;
            //button.UserData = (int)Tabs.JoinServer;
            button.OnClicked = JoinServerClicked;

            button = new GUIButton(new Rectangle(50, y + 200, 200, 30), "Host Server", null, Alignment.TopLeft, Alignment.Left, GUI.Style, buttonsTab);
            button.Color = button.Color * 0.8f;
            button.UserData = Tab.HostServer;
            button.OnClicked = SelectTab;

            button = new GUIButton(new Rectangle(50, y + 260, 200, 30), "Submarine Editor", null, Alignment.TopLeft, Alignment.Left, GUI.Style, buttonsTab);
            button.Color = button.Color * 0.8f;
            button.OnClicked = (GUIButton btn, object userdata) => { GameMain.EditMapScreen.Select(); return true; };

            button = new GUIButton(new Rectangle(50, y + 320, 200, 30), "Settings", null, Alignment.TopLeft, Alignment.Left, GUI.Style, buttonsTab);
            button.Color = button.Color * 0.8f;
            button.UserData = Tab.Settings;
            button.OnClicked = SelectTab;

            button = new GUIButton(new Rectangle(0, 0, 150, 30), "Quit", Alignment.BottomRight, GUI.Style, buttonsTab);
            button.Color = button.Color * 0.8f;
            button.OnClicked = QuitClicked;

            //----------------------------------------------------------------------

            menuTabs[(int)Tab.NewGame] = new GUIFrame(panelRect, GUI.Style);
            //menuTabs[(int)Tabs.NewGame].Padding = GUI.style.smallPadding;

            //new GUITextBlock(new Rectangle(0, -20, 0, 30), "New Game", null, null, Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.NewGame]);

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "Selected submarine:", null, null, Alignment.Left, GUI.Style, menuTabs[(int)Tab.NewGame]);
            mapList = new GUIListBox(new Rectangle(0, 30, 200, panelRect.Height-100), GUI.Style, menuTabs[(int)Tab.NewGame]);

            foreach (Submarine sub in Submarine.SavedSubmarines)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    sub.Name, 
                    GUI.Style,
                    Alignment.Left, Alignment.Left, mapList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = sub;
            }
            if (Submarine.SavedSubmarines.Count > 0) mapList.Select(Submarine.SavedSubmarines[0]);

            new GUITextBlock(new Rectangle((int)(mapList.Rect.Width + 20), 0, 100, 20),
                "Save name: ", GUI.Style, Alignment.Left, Alignment.Left, menuTabs[(int)Tab.NewGame]);

            saveNameBox = new GUITextBox(new Rectangle((int)(mapList.Rect.Width + 20), 30, 180, 20),
                Alignment.TopLeft, GUI.Style, menuTabs[(int)Tab.NewGame]);
            saveNameBox.Text = SaveUtil.CreateSavePath();

            new GUITextBlock(new Rectangle((int)(mapList.Rect.Width + 20), 60, 100, 20),
                "Map Seed: ", GUI.Style, Alignment.Left, Alignment.Left, menuTabs[(int)Tab.NewGame]);

            seedBox = new GUITextBox(new Rectangle((int)(mapList.Rect.Width + 20), 90, 180, 20),
                Alignment.TopLeft, GUI.Style, menuTabs[(int)Tab.NewGame]);
            seedBox.Text = ToolBox.RandomSeed(8);


            button = new GUIButton(new Rectangle(0, 0, 100, 30), "Start", Alignment.BottomRight, GUI.Style,  menuTabs[(int)Tab.NewGame]);
            button.OnClicked = StartGame;

            //----------------------------------------------------------------------

            menuTabs[(int)Tab.LoadGame] = new GUIFrame(panelRect, GUI.Style);
            //menuTabs[(int)Tabs.LoadGame].Padding = GUI.style.smallPadding;


            menuTabs[(int)Tab.HostServer] = new GUIFrame(panelRect, GUI.Style);
            //menuTabs[(int)Tabs.JoinServer].Padding = GUI.style.smallPadding;

            //new GUITextBlock(new Rectangle(0, -25, 0, 30), "Host Server", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.HostServer], false, GUI.LargeFont);

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "Server Name:", GUI.Style, Alignment.TopLeft, Alignment.Left, menuTabs[(int)Tab.HostServer]);
            serverNameBox = new GUITextBox(new Rectangle(160, 0, 200, 30), null, null, Alignment.TopLeft, Alignment.Left, GUI.Style, menuTabs[(int)Tab.HostServer]);

            new GUITextBlock(new Rectangle(0, 50, 0, 30), "Server port:", GUI.Style, Alignment.TopLeft, Alignment.Left, menuTabs[(int)Tab.HostServer]);
            portBox = new GUITextBox(new Rectangle(160, 50, 200, 30), null, null, Alignment.TopLeft, Alignment.Left, GUI.Style, menuTabs[(int)Tab.HostServer]);
            portBox.Text = NetConfig.DefaultPort.ToString();
            portBox.ToolTip = "Server port";

            new GUITextBlock(new Rectangle(0, 100, 100, 30), "Max players:", GUI.Style, Alignment.TopLeft, Alignment.Left, menuTabs[(int)Tab.HostServer]);
            maxPlayersBox = new GUITextBox(new Rectangle(195, 100, 30, 30), null, null, Alignment.TopLeft, Alignment.Center, GUI.Style, menuTabs[(int)Tab.HostServer]);
            maxPlayersBox.Text = "8";
            maxPlayersBox.Enabled = false;

            var minusPlayersBox = new GUIButton(new Rectangle(160, 100, 30, 30), "-", GUI.Style, menuTabs[(int)Tab.HostServer]);
            minusPlayersBox.UserData = -1;
            minusPlayersBox.OnClicked = ChangeMaxPlayers;

            var plusPlayersBox = new GUIButton(new Rectangle(230, 100, 30, 30), "+", GUI.Style, menuTabs[(int)Tab.HostServer]);
            plusPlayersBox.UserData = 1;
            plusPlayersBox.OnClicked = ChangeMaxPlayers;
            
            new GUITextBlock(new Rectangle(0, 150, 0, 30), "Password (optional):", GUI.Style, Alignment.TopLeft, Alignment.Left, menuTabs[(int)Tab.HostServer]);
            passwordBox = new GUITextBox(new Rectangle(160, 150, 200, 30), null, null, Alignment.TopLeft, Alignment.Left, GUI.Style, menuTabs[(int)Tab.HostServer]);
            
            isPublicBox = new GUITickBox(new Rectangle(10, 200, 20, 20), "Public server", Alignment.TopLeft, menuTabs[(int)Tab.HostServer]);
            isPublicBox.ToolTip = "Public servers are shown in the list of available servers in the ''Join Server'' -tab";


            useUpnpBox = new GUITickBox(new Rectangle(10, 250, 20, 20), "Attempt UPnP port forwarding", Alignment.TopLeft, menuTabs[(int)Tab.HostServer]);
            useUpnpBox.ToolTip = "UPnP can be used for forwarding ports on your router to allow players join the server."
            + " However, UPnP isn't supported by all routers, so you may need to setup port forwards manually"
            +" if players are unable to join the server (see the readme for instructions).";
            
            GUIButton hostButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Start", Alignment.BottomRight, GUI.Style, menuTabs[(int)Tab.HostServer]);
            hostButton.OnClicked = HostServerClicked;

            this.game = game;
        }

        public override void Select()
        {
            base.Select();

            if (GameMain.NetworkMember != null)
            {
                GameMain.NetworkMember.Disconnect();
                GameMain.NetworkMember = null;
            }

            Submarine.Unload();

            SelectTab(null, 0);
            //selectedTab = 0;
        }
        
        public bool SelectTab(GUIButton button, object obj)
        {
            try
            {
                SelectTab((Tab)obj);
            }
            catch
            {
                selectedTab = 0;
            }

            if (button != null) button.Selected = true;
            
            foreach (GUIComponent child in buttonsTab.children)
            {
                GUIButton otherButton = child as GUIButton;
                if (otherButton == null || otherButton == button) continue;

                otherButton.Selected = false;
            }

            if (Screen.Selected != this) Select();

            return true;
        }

        public void SelectTab(Tab tab)
        {
            int oldTab = selectedTab;


            if (GameMain.Config.UnsavedSettings)
            {
                var applyBox = new GUIMessageBox("Apply changes?", "Do you want to apply the settings or discard the changes?", 
                    new string[] {"Apply", "Discard"});
                applyBox.Buttons[0].OnClicked += applyBox.Close;
                applyBox.Buttons[0].OnClicked += ApplySettings;
                applyBox.Buttons[0].UserData = tab;
                applyBox.Buttons[1].OnClicked += applyBox.Close;
                applyBox.Buttons[1].OnClicked += DiscardSettings;
                applyBox.Buttons[1].UserData = tab;

                return;
            }

            selectedTab = (int)tab;

            switch (selectedTab)
            {
                case (int)Tab.LoadGame:
                    UpdateLoadScreen();
                    break;
                case (int)Tab.Settings:
                    GameMain.Config.ResetSettingsFrame();
                    menuTabs[(int)Tab.Settings] = GameMain.Config.SettingsFrame;
                    break;
            }
        }

        private bool ApplySettings(GUIButton button, object obj)
        {
            GameMain.Config.Save("config.xml");
            selectedTab = 0;

            if (GameMain.GraphicsWidth != GameMain.Config.GraphicsWidth || GameMain.GraphicsHeight != GameMain.Config.GraphicsHeight)
            {
                new GUIMessageBox("Restart required", "You need to restart the game for the resolution changes to take effect.");
            }



            return true;
        }

        private bool DiscardSettings(GUIButton button, object obj)
        {
            GameMain.Config.Load("config.xml");
            selectedTab = (int)obj;

            return true;
        }


        private bool TutorialButtonClicked(GUIButton button, object obj)
        {
            //!!!!!!!!!!!!!!!!!! placeholder
            TutorialMode.StartTutorial(Tutorials.TutorialType.TutorialTypes[0]);

            return true;
        }

        private bool JoinServerClicked(GUIButton button, object obj)
        {            
            GameMain.ServerListScreen.Select();
            return true;
        }

        private bool ChangeMaxPlayers(GUIButton button, object obj)
        {
            int currMaxPlayers = 10;

            int.TryParse(maxPlayersBox.Text, out currMaxPlayers);
            currMaxPlayers = (int)MathHelper.Clamp(currMaxPlayers+(int)button.UserData, 1, 10);

            maxPlayersBox.Text = currMaxPlayers.ToString();

            return true;
        }

        private bool HostServerClicked(GUIButton button, object obj)
        {
            string name = serverNameBox.Text;
            if (string.IsNullOrEmpty(name))
            {
                serverNameBox.Flash();
                return false;
            }

            int port;
            if (!int.TryParse(portBox.Text, out port) || port < 0 || port > 65535)
            {
                portBox.Text = NetConfig.DefaultPort.ToString();
                portBox.Flash();

                return false;
            }

            GameMain.NetLobbyScreen = new NetLobbyScreen();

            try
            {
                GameMain.NetworkMember = new GameServer(name, port, isPublicBox.Selected, passwordBox.Text, useUpnpBox.Selected, int.Parse(maxPlayersBox.Text));                  
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to start server", e);
            }

            GameMain.NetLobbyScreen.IsServer = true;
            //Game1.NetLobbyScreen.Select();
            return true;
        }


        private bool QuitClicked(GUIButton button, object obj)
        {
            game.Exit();
            return true;
        }

        private void UpdateLoadScreen()
        {
            menuTabs[(int)Tab.LoadGame].ClearChildren();

            string[] saveFiles = SaveUtil.GetSaveFiles();

            saveList = new GUIListBox(new Rectangle(0, 0, 200, menuTabs[(int)Tab.LoadGame].Rect.Height - 80), Color.White, GUI.Style, menuTabs[(int)Tab.LoadGame]);
            saveList.OnSelected = SelectSaveFile;

            foreach (string saveFile in saveFiles)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    saveFile,
                    GUI.Style,
                    Alignment.Left,
                    Alignment.Left,
                    saveList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = saveFile;
            }

            var button = new GUIButton(new Rectangle(0, 0, 100, 30), "Start", Alignment.Right | Alignment.Bottom, GUI.Style, menuTabs[(int)Tab.LoadGame]);
            button.OnClicked = LoadGame;
        }

        private bool SelectSaveFile(GUIComponent component, object obj)
        {
            string fileName = (string)obj;
            
            XDocument doc = SaveUtil.LoadGameSessionDoc(fileName);

            if (doc==null)
            {
                DebugConsole.ThrowError("Error loading save file ''"+fileName+"''. The file may be corrupted.");
                return false;
            }

            RemoveSaveFrame();

            string subName = ToolBox.GetAttributeString(doc.Root, "submarine", "");

            string saveTime = ToolBox.GetAttributeString(doc.Root, "savetime", "unknown");

            XElement modeElement = null;
            foreach (XElement element in doc.Root.Elements())
            {
                if (element.Name.ToString().ToLower() != "gamemode") continue;

                modeElement = element;
                break;
            }

            string mapseed = ToolBox.GetAttributeString(modeElement, "mapseed", "unknown");

            GUIFrame saveFileFrame = new GUIFrame(new Rectangle((int)(saveList.Rect.Width + 20), 0, 200, 230), Color.Black*0.4f, GUI.Style, menuTabs[(int)Tab.LoadGame]);
            saveFileFrame.UserData = "savefileframe";
            saveFileFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            new GUITextBlock(new Rectangle(0,0,0,20), fileName, GUI.Style, Alignment.TopLeft, Alignment.TopLeft, saveFileFrame, false, GUI.LargeFont);

            new GUITextBlock(new Rectangle(0, 35, 0, 20), "Submarine: ", GUI.Style, saveFileFrame).Font = GUI.SmallFont;
            new GUITextBlock(new Rectangle(15, 52, 0, 20), subName, GUI.Style, saveFileFrame).Font = GUI.SmallFont;

            new GUITextBlock(new Rectangle(0, 70, 0, 20), "Last saved: ", GUI.Style, saveFileFrame).Font = GUI.SmallFont;
            new GUITextBlock(new Rectangle(15, 85, 0, 20), saveTime, GUI.Style, saveFileFrame).Font = GUI.SmallFont;

            new GUITextBlock(new Rectangle(0, 105, 0, 20), "Map seed: ", GUI.Style, saveFileFrame).Font = GUI.SmallFont;
            new GUITextBlock(new Rectangle(15, 120, 0, 20), mapseed, GUI.Style, saveFileFrame).Font = GUI.SmallFont;

            var deleteSaveButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Delete", Alignment.BottomCenter, GUI.Style, saveFileFrame);
            deleteSaveButton.UserData = fileName;
            deleteSaveButton.OnClicked = DeleteSave;

            return true;
        }

        private bool DeleteSave(GUIButton button, object obj)
        {
            string saveFile = obj as string;

            if (obj == null) return false;

            SaveUtil.DeleteSave(saveFile);

            UpdateLoadScreen();

            return true;
        }

        private void RemoveSaveFrame()
		{
            GUIComponent prevFrame = null;
            foreach (GUIComponent child in menuTabs[(int)Tab.LoadGame].children)
            {
                if (child.UserData as string != "savefileframe") continue;

                prevFrame = child;
                break;
            }
            menuTabs[(int)Tab.LoadGame].RemoveChild(prevFrame);
        }

        public override void Update(double deltaTime)
        {
            buttonsTab.Update((float)deltaTime);
            if (selectedTab>0) menuTabs[selectedTab].Update((float)deltaTime);

            GameMain.TitleScreen.TitlePosition =
                Vector2.Lerp(GameMain.TitleScreen.TitlePosition, new Vector2(
                    GameMain.TitleScreen.TitleSize.X / 2.0f * GameMain.TitleScreen.Scale + 30.0f,
                    GameMain.TitleScreen.TitleSize.Y / 2.0f * GameMain.TitleScreen.Scale + 30.0f), 
                    0.1f);
                
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            GameMain.TitleScreen.DrawLoadingText = false;
            GameMain.TitleScreen.Draw(spriteBatch, graphics, (float)deltaTime);

            //Game1.GameScreen.DrawMap(graphics, spriteBatch);
            
            spriteBatch.Begin();

            buttonsTab.Draw(spriteBatch);
            if (selectedTab>0) menuTabs[selectedTab].Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.DrawString(GUI.Font, "Barotrauma v"+GameMain.Version, new Vector2(10, GameMain.GraphicsHeight-20), Color.White);

            spriteBatch.End();
        }

        private bool StartGame(GUIButton button, object obj)
        {
            if (string.IsNullOrEmpty(saveNameBox.Text)) return false;

            string[] existingSaveFiles = SaveUtil.GetSaveFiles();

            if (Array.Find(existingSaveFiles, s => s == saveNameBox.Text)!=null)
            {
                new GUIMessageBox("Save name already in use", "Please choose another name for the save file");
                return false;
            }

            Submarine selectedSub = mapList.SelectedData as Submarine;
            if (selectedSub == null) return false;

            if (!Directory.Exists(SaveUtil.TempPath))
            {
                Directory.CreateDirectory(SaveUtil.TempPath);
            }

            File.Copy(selectedSub.FilePath, Path.Combine(SaveUtil.TempPath, selectedSub.Name+".sub"), true);

            selectedSub = new Submarine(Path.Combine(SaveUtil.TempPath, selectedSub.Name + ".sub"), "");
            
            GameMain.GameSession = new GameSession(selectedSub, saveNameBox.Text, GameModePreset.list.Find(gm => gm.Name == "Single Player"));
            (GameMain.GameSession.gameMode as SinglePlayerMode).GenerateMap(seedBox.Text);

            GameMain.LobbyScreen.Select();

            //new GUIMessageBox("Welcome to Barotrauma!", "Please note that the single player mode is very unfinished at the moment; "+
            //"for example, the NPCs don't have an AI yet and there are only a couple of different quests to complete. The multiplayer "+
            //"mode should be much more enjoyable to play at the moment, so my recommendation is to try out and get a hang of the game mechanics "+
            //"in the single player mode and then move on to multiplayer. Have fun!\n - Regalis, the main dev of Subsurface", 400, 350);

            return true;
        }

        private bool PreviousTab(GUIButton button, object obj)
        {
            //selectedTab = (int)Tabs.Main;

            return true;
        }

        private bool LoadGame(GUIButton button, object obj)
        {
            string saveFile = saveList.SelectedData as string;
            if (string.IsNullOrWhiteSpace(saveFile)) return false;

            try
            {
                SaveUtil.LoadGame(saveFile);                
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading save ''"+saveFile+"'' failed", e);
                return false;
            }


            GameMain.LobbyScreen.Select();

            return true;
        }

    }
}
