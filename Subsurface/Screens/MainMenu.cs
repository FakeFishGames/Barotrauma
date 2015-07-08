using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;
using System.IO;

namespace Subsurface
{
    class MainMenuScreen : Screen
    {
        enum Tabs { Main = 0, NewGame = 1, LoadGame = 2, JoinServer = 3 }

        private GUIFrame[] menuTabs;
        private GUIListBox mapList;

        private GUIListBox saveList;

        private GUITextBox nameBox;
        private GUITextBox ipBox;

        private Game1 game;

        int selectedTab;

        public MainMenuScreen(Game1 game)
        {
            menuTabs = new GUIFrame[Enum.GetValues(typeof(Tabs)).Length];

            Rectangle panelRect = new Rectangle(                
                Game1.GraphicsWidth / 2 - 250,
                Game1.GraphicsHeight/ 2 - 250,
                500, 500);

            menuTabs[(int)Tabs.Main] = new GUIFrame(panelRect, GUI.style);
            //menuTabs[(int)Tabs.Main].Padding = GUI.style.smallPadding;

            GUIButton button = new GUIButton(new Rectangle(0, 0, 0, 30), "New Game", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.UserData = (int)Tabs.NewGame;
            button.OnClicked = SelectTab;
            //button.Enabled = false;

            button = new GUIButton(new Rectangle(0, 60, 0, 30), "Load Game", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.UserData = (int)Tabs.LoadGame;
            button.OnClicked = SelectTab;

            button = new GUIButton(new Rectangle(0, 120, 0, 30), "Join Server", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.UserData = (int)Tabs.JoinServer;
            button.OnClicked = SelectTab;

            button = new GUIButton(new Rectangle(0, 180, 0, 30), "Host Server", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.OnClicked = HostServerClicked;
            //button.Enabled = false;

            button = new GUIButton(new Rectangle(0, 240, 0, 30), "Quit", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.OnClicked = QuitClicked;

            //----------------------------------------------------------------------

            menuTabs[(int)Tabs.NewGame] = new GUIFrame(panelRect, GUI.style);
            //menuTabs[(int)Tabs.NewGame].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "New Game", null, null, Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.NewGame]);

            new GUITextBlock(new Rectangle(0, 30, 0, 30), "Selected map:", null, null, Alignment.Left, GUI.style, menuTabs[(int)Tabs.NewGame]);
            mapList = new GUIListBox(new Rectangle(0, 60, 200, 400), GUI.style, menuTabs[(int)Tabs.NewGame]);

            foreach (Submarine map in Submarine.SavedSubmarines)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    map.Name, 
                    GUI.style,
                    Alignment.Left, Alignment.Left, mapList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = map;
            }
            if (Submarine.SavedSubmarines.Count > 0) mapList.Select(Submarine.SavedSubmarines[0]);


            button = new GUIButton(new Rectangle(0, 0, 100, 30), "Start",Alignment.Right | Alignment.Bottom, GUI.style,  menuTabs[(int)Tabs.NewGame]);
            button.OnClicked = StartGame;

            //----------------------------------------------------------------------

            menuTabs[(int)Tabs.LoadGame] = new GUIFrame(panelRect, GUI.style);
            //menuTabs[(int)Tabs.LoadGame].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "Load Game", Color.Transparent, Color.Black, Alignment.CenterX, null, menuTabs[(int)Tabs.LoadGame]);

            if (!Directory.Exists(SaveUtil.SaveFolder))
            {
                DebugConsole.ThrowError("Save folder ''"+SaveUtil.SaveFolder+" not found! Attempting to create a new folder");
                try
                {
                    Directory.CreateDirectory(SaveUtil.SaveFolder);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to create the folder ''"+SaveUtil.SaveFolder+"''!", e);
                }
            }

            string[] saveFiles = Directory.GetFiles(SaveUtil.SaveFolder, "*.save");

            //new GUITextBlock(new Rectangle(0, 30, 0, 30), "Selected map:", Color.Transparent, Color.Black, Alignment.Left, menuTabs[(int)Tabs.NewGame]);
            saveList = new GUIListBox(new Rectangle(0, 60, 200, 400), Color.White, GUI.style, menuTabs[(int)Tabs.LoadGame]);

            foreach (string saveFile in saveFiles)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    saveFile,
                    GUI.style,
                    Alignment.Left,
                    Alignment.Left,
                    saveList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = saveFile;
            }

            button = new GUIButton(new Rectangle(0, 0, 100, 30), "Start",Alignment.Right | Alignment.Bottom, GUI.style, menuTabs[(int)Tabs.LoadGame]);
            button.OnClicked = LoadGame;

            //----------------------------------------------------------------------

            menuTabs[(int)Tabs.JoinServer] = new GUIFrame(panelRect, GUI.style);
            //menuTabs[(int)Tabs.JoinServer].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "Join Server", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);

            new GUITextBlock(new Rectangle(0, 30, 0, 30), "Name:", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);
            nameBox = new GUITextBox(new Rectangle(0, 60, 200, 30), Color.White, Color.Black, Alignment.CenterX, Alignment.CenterX, null, menuTabs[(int)Tabs.JoinServer]);

            new GUITextBlock(new Rectangle(0, 100, 0, 30), "Server IP:", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);
            ipBox = new GUITextBox(new Rectangle(0, 130, 200, 30), Color.White, Color.Black, Alignment.CenterX, Alignment.CenterX, null, menuTabs[(int)Tabs.JoinServer]);

            GUIButton joinButton = new GUIButton(new Rectangle(0, 0, 200, 30), "Join", Alignment.BottomCenter, GUI.style, menuTabs[(int)Tabs.JoinServer]);
            joinButton.OnClicked = JoinServer;


            this.game = game;

        }

        private bool SelectTab(GUIButton button, object obj)
        {
            selectedTab = (int)obj;
            return true;
        }
        
        private bool HostServerClicked(GUIButton button, object obj)
        {
            Game1.NetLobbyScreen.IsServer = true;
            Game1.NetLobbyScreen.Select();
            return true;
        }

        private bool QuitClicked(GUIButton button, object obj)
        {
            game.Exit();
            return true;
        }

        public override void Update(double deltaTime)
        {
            menuTabs[selectedTab].Update((float)deltaTime);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            Game1.GameScreen.DrawMap(graphics, spriteBatch);
            
            spriteBatch.Begin();

            menuTabs[selectedTab].Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();
        }

        private bool StartGame(GUIButton button, object obj)
        {
            Submarine selectedMap = mapList.SelectedData as Submarine;
            if (selectedMap == null) return false;

            Game1.GameSession = new GameSession(selectedMap, GameModePreset.list.Find(gm => gm.Name == "Single Player"));

            Game1.LobbyScreen.Select();

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
                DebugConsole.ThrowError("Loading map ''"+saveFile+"'' failed", e);
                return false;
            }


            Game1.LobbyScreen.Select();

            return true;
        }


        private bool JoinServer(GUIButton button, object obj)
        {
            if (string.IsNullOrEmpty(nameBox.Text)) return false;
            if (string.IsNullOrEmpty(ipBox.Text)) return false;

            Game1.NetworkMember = new GameClient(nameBox.Text);
            Game1.Client.ConnectToServer(ipBox.Text);

            return true;
            //{
            //    Game1.NetLobbyScreen.Select();
            //    return true;
            //}
            //else
            //{
            //    Game1.NetworkMember = null;
            //    return false;
            //}
        }
        
    }
}
