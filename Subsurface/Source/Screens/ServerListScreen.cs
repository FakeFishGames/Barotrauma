using System;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;
using FarseerPhysics;
using FarseerPhysics.Factories;
using FarseerPhysics.Dynamics;
using System.IO;
using System.Collections.Generic;
using RestSharp;

namespace Subsurface
{
    class ServerListScreen : Screen
    {
        private GUIFrame menu;

        private GUIListBox serverList;

        private GUIButton joinButton;

        private GUITextBox clientNameBox, ipBox;

        public ServerListScreen()
        {
            int width = Math.Min(Game1.GraphicsWidth - 160, 1000);
            int height = Math.Min(Game1.GraphicsHeight - 160, 700);

            Rectangle panelRect = new Rectangle(0, 0, width, height);

            menu = new GUIFrame(panelRect, null, Alignment.Center, GUI.style);
            
            new GUITextBlock(new Rectangle(0, 0, 0, 30), "Join Server", GUI.style, Alignment.CenterX, Alignment.CenterX, menu);

            new GUITextBlock(new Rectangle(0, 30, 0, 30), "Your Name:", GUI.style, menu);
            clientNameBox = new GUITextBox(new Rectangle(0, 60, 200, 30), GUI.style, menu);

            new GUITextBlock(new Rectangle(0, 100, 0, 30), "Server IP:", GUI.style, menu);
            ipBox = new GUITextBox(new Rectangle(0, 130, 200, 30), GUI.style, menu);
            
            int middleX = (int)(width * 0.4f);

            serverList = new GUIListBox(new Rectangle(middleX,60,0,(int)(height*0.7f)), GUI.style, menu);
            serverList.OnSelected = SelectServer;

            new GUITextBlock(new Rectangle(middleX, 30, 0, 30), "Name", GUI.style, menu);
            new GUITextBlock(new Rectangle(middleX, 30, 0, 30), "Players", GUI.style, Alignment.TopLeft, Alignment.TopCenter, menu);
            new GUITextBlock(new Rectangle(middleX, 30, 0, 30), "Game running", GUI.style, Alignment.TopLeft, Alignment.TopRight, menu);

            joinButton = new GUIButton(new Rectangle(-170, 0, 150, 30), "Refresh", Alignment.BottomRight, GUI.style, menu);
            joinButton.OnClicked = RefreshServers;

            joinButton = new GUIButton(new Rectangle(0,0,150,30), "Join", Alignment.BottomRight, GUI.style, menu);
            joinButton.OnClicked = JoinServer;
            //joinButton.Enabled = false;
        }

        public override void Select()
        {
            base.Select();

            UpdateServerList();
        }

        private bool SelectServer(object obj)
        {
            string ip = obj as string;
            if (string.IsNullOrWhiteSpace(ip)) return false;

            ipBox.Text = ip;

            return true;
        }

        private bool RefreshServers(GUIButton button, object obj)
        {
            UpdateServerList();

            return true;
        }

        private void UpdateServerList()
        {
            serverList.ClearChildren();

            string masterServerData = GetMasterServerData();

            if (string.IsNullOrWhiteSpace(masterServerData))
            {
                var nameText = new GUITextBlock(new Rectangle(0, 0, 0, 20), "Couldn't find any servers", GUI.style, serverList);

                return;
            }

            if (masterServerData.Substring(0,5).ToLower()=="error")
            {
                DebugConsole.ThrowError("Error while connecting to master server ("+masterServerData+")!");
                return;
            }

            string[] lines = masterServerData.Split('\n');

            for (int i = 0; i<lines.Length; i++)
            {
                string[] arguments = lines[i].Split('|');
                if (arguments.Length < 3) continue;

                string IP = arguments[0];
                string port = arguments[1];
                string serverName = arguments[2];
                string gameStarted = (arguments.Length > 3) ? arguments[3] : "";
                string playerCountStr = (arguments.Length > 4) ? arguments[4] : "";

                var serverFrame = new GUIFrame(new Rectangle(0,0,0,20), (i%2 == 0) ? Color.Transparent : Color.White*0.2f, null, serverList);
                serverFrame.UserData = IP+":"+port;
                serverFrame.HoverColor = Color.Gold * 0.2f;
                serverFrame.SelectedColor = Color.Gold * 0.5f;

                var nameText = new GUITextBlock(new Rectangle(0,0,0,0), serverName, GUI.style, serverFrame);

                int playerCount, maxPlayers;
                playerCount = GameClient.ByteToPlayerCount((byte)int.Parse(playerCountStr), out maxPlayers);

                var playerCountText = new GUITextBlock(new Rectangle(0, 0, 0, 0), playerCount+"/"+maxPlayers, GUI.style, Alignment.Left, Alignment.TopCenter, serverFrame);
                var gameStartedText = new GUITextBlock(new Rectangle(0, 0, 0, 0), gameStarted=="1" ? "Yes" : "No", GUI.style, Alignment.Left, Alignment.TopRight, serverFrame);
            
            }
        }

        private string GetMasterServerData()
        {
            RestClient client = null;
            try
            {
                client = new RestClient(NetworkMember.MasterServerUrl);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error while connecting to master server", e);
                return "";
            }


            var request = new RestRequest("masterserver.php", Method.GET);
            request.AddParameter("gamename", "subsurface"); // adds to POST or URL querystring based on Method
            request.AddParameter("action", "listservers"); // adds to POST or URL querystring based on Method


            // easily add HTTP Headers
            //request.AddHeader("header", "value");

            //// add files to upload (works with compatible verbs)
            //request.AddFile(path);

            // execute the request
            RestResponse response = (RestResponse)client.Execute(request);


            if (response.StatusCode!= System.Net.HttpStatusCode.OK)
            {
                DebugConsole.ThrowError("Error while connecting to master server (" +response.StatusCode+": "+response.StatusDescription+")");
                return "";
            }

            return response.Content; // raw content as string

        }

        private bool JoinServer(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(clientNameBox.Text))
            {
                clientNameBox.Flash();
                return false;
            }

            string ip = ipBox.Text;

            if (string.IsNullOrWhiteSpace(ip))
            {
                ipBox.Flash();
                return false;
            }

            Game1.NetworkMember = new GameClient(clientNameBox.Text);
            Game1.Client.ConnectToServer(ip);

            return true;
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            Game1.GameScreen.DrawMap(graphics, spriteBatch);

            spriteBatch.Begin();

            menu.Draw(spriteBatch);

            //if (previewPlayer!=null) previewPlayer.Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();
        }

        public override void Update(double deltaTime)
        {
            menu.Update((float)deltaTime);

            GUI.Update((float)deltaTime);
        }
    }
}
