using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;

namespace Subsurface
{
    class MainMenuScreen : Screen
    {
        enum Tabs { Main = 0, NewGame = 1, JoinServer = 2}

        GUIFrame[] menuTabs;
        GUIListBox mapList;

        GUITextBox nameBox;
        GUITextBox ipBox;

        Game1 game;

        int selectedTab;

        public MainMenuScreen(Game1 game)
        {
            menuTabs = new GUIFrame[3];

            Rectangle panelRect = new Rectangle(                
                Game1.GraphicsWidth / 2 - 250,
                Game1.GraphicsHeight/ 2 - 250,
                500, 500);

            menuTabs[(int)Tabs.Main] = new GUIFrame(panelRect, GUI.style.backGroundColor);
            menuTabs[(int)Tabs.Main].Padding = GUI.style.smallPadding;

            GUIButton button = new GUIButton(new Rectangle(0, 0, 0, 30), "New Game", GUI.style, Alignment.CenterX, menuTabs[(int)Tabs.Main]);
            button.OnClicked = NewGameClicked;
            //button.Enabled = false;

            button = new GUIButton(new Rectangle(0, 60, 0, 30), "Join Server", GUI.style, Alignment.CenterX, menuTabs[(int)Tabs.Main]);
            button.OnClicked = JoinServerClicked;

            button = new GUIButton(new Rectangle(0, 120, 0, 30), "Host Server", GUI.style, Alignment.CenterX, menuTabs[(int)Tabs.Main]);
            button.OnClicked = HostServerClicked;
            //button.Enabled = false;

            button = new GUIButton(new Rectangle(0, 180, 0, 30), "Quit", GUI.style, Alignment.CenterX, menuTabs[(int)Tabs.Main]);
            button.OnClicked = QuitClicked;

            //----------------------------------------------------------------------

            menuTabs[(int)Tabs.NewGame] = new GUIFrame(panelRect, GUI.style.backGroundColor);
            menuTabs[(int)Tabs.NewGame].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "New Game", Color.Transparent, Color.Black, Alignment.CenterX, menuTabs[(int)Tabs.NewGame]);

            new GUITextBlock(new Rectangle(0, 30, 0, 30), "Selected map:", Color.Transparent, Color.Black, Alignment.Left, menuTabs[(int)Tabs.NewGame]);
            mapList = new GUIListBox(new Rectangle(0, 60, 200, 400), Color.White, menuTabs[1]);

            //string[] mapFilePaths = Map.GetMapFilePaths();
            //if (mapFilePaths!=null)
            //{
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
                if (Map.SavedMaps.Count > 0) mapList.Select(Map.SavedMaps[0]);
            //}

            button = new GUIButton(new Rectangle(0, 0, 100, 30), "Start", GUI.style, Alignment.Right | Alignment.Bottom, menuTabs[(int)Tabs.NewGame]);
            button.OnClicked = StartGame;


            //----------------------------------------------------------------------

            menuTabs[(int)Tabs.JoinServer] = new GUIFrame(panelRect, GUI.style.backGroundColor);
            menuTabs[(int)Tabs.JoinServer].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "Join Server", Color.Transparent, Color.Black, Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);
            
            new GUITextBlock(new Rectangle(0, 30, 0, 30), "Name:", Color.Transparent, Color.Black, Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);
            nameBox = new GUITextBox(new Rectangle(0, 60, 200, 30), Color.White, Color.Black, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);

            new GUITextBlock(new Rectangle(0, 100, 0, 30), "Server IP:", Color.Transparent, Color.Black, Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);
            ipBox = new GUITextBox(new Rectangle(0, 130, 200, 30), Color.White, Color.Black, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);

            GUIButton joinButton = new GUIButton(new Rectangle(0, 0, 200, 30), "Join", Color.White, Alignment.Bottom | Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);
            joinButton.OnClicked = JoinServer;


            this.game = game;

        }

        private bool NewGameClicked(GUIButton button, object obj)
        {
            selectedTab = (int)Tabs.NewGame;
            return true;
        }

        private bool JoinServerClicked(GUIButton button, object obj)
        {
            selectedTab = (int)Tabs.JoinServer;
            return true;
        }

        private bool HostServerClicked(GUIButton button, object obj)
        {
            Game1.netLobbyScreen.isServer = true;
            Game1.netLobbyScreen.Select();
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

            Game1.gameScreen.DrawMap(graphics, spriteBatch);
            
            spriteBatch.Begin();

            menuTabs[selectedTab].Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();
        }

        private bool StartGame(GUIButton button, object obj)
        {

            Map selectedMap = mapList.SelectedData as Map;
            if (selectedMap == null) return false;


            Game1.gameSession = new GameSession(selectedMap.FilePath, true, TimeSpan.Zero);

            Game1.lobbyScreen.Select();

            return true;
        }

        private bool JoinServer(GUIButton button, object obj)
        {
            if (string.IsNullOrEmpty(nameBox.Text)) return false;
            if (string.IsNullOrEmpty(ipBox.Text)) return false;

            Game1.client = new GameClient(nameBox.Text);
            if (Game1.client.ConnectToServer(ipBox.Text))
            {
                Game1.netLobbyScreen.Select();
                return true;
            }
            else
            {
                Game1.client = null;
                return false;
            }
        }
        
    }
}
