using Barotrauma.Extensions;
using Barotrauma.Networking;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Barotrauma
{
    class ServerListScreen : Screen
    {
        //how often the client is allowed to refresh servers
        private TimeSpan AllowedRefreshInterval = new TimeSpan(0, 0, 3);

        private GUIFrame menu;

        private GUIListBox serverList;

        private GUIButton joinButton;

        private GUITextBox clientNameBox, ipBox;

        private bool masterServerResponded;
        private IRestResponse masterServerResponse;

        private float[] columnRelativeWidth;

        //filters
        private GUITextBox searchBox;
        private GUITickBox filterPassword;
        private GUITickBox filterIncompatible;
        private GUITickBox filterFull;
        private GUITickBox filterEmpty;

        //a timer for 
        private DateTime refreshDisableTimer;
        private bool waitingForRefresh;
                
        public ServerListScreen()
        {
            int width = Math.Min(GameMain.GraphicsWidth - 160, 1000);
            int height = Math.Min(GameMain.GraphicsHeight - 160, 700);

            Rectangle panelRect = new Rectangle(0, 0, width, height);

            menu = new GUIFrame(new RectTransform(new Point(width, height), GUI.Canvas, Anchor.Center));

            new GUITextBlock(new RectTransform(new Vector2(0.95f, 0.133f), menu.RectTransform, Anchor.TopCenter),
                TextManager.Get("JoinServer"), textAlignment: Alignment.Left, font: GUI.LargeFont);

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), menu.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.0f, 0.03f) }, style: null);

            //-------------------------------------------------------------------------------------
            //left column
            //-------------------------------------------------------------------------------------

            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.35f, 0.95f), paddedFrame.RectTransform, Anchor.TopLeft));

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.03f), leftColumn.RectTransform), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("YourName"));
            clientNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.045f), leftColumn.RectTransform), "")
            {
                Text = GameMain.Config.DefaultPlayerName
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("ServerIP"));
            ipBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.045f), leftColumn.RectTransform), "");

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), leftColumn.RectTransform), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("FilterServers"));
            searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), "");

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.03f), leftColumn.RectTransform), style: null);

            searchBox.OnTextChanged += (txtBox, txt) => { FilterServers(); return true; };
            filterPassword = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("FilterPassword"));
            filterPassword.OnSelected += (tickBox) => { FilterServers(); return true; };
            filterIncompatible = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("FilterIncompatibleServers"));
            filterIncompatible.OnSelected += (tickBox) => { FilterServers(); return true; };

            filterFull = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("FilterFullServers"));
            filterFull.OnSelected += (tickBox) => { FilterServers(); return true; };
            filterEmpty = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("FilterEmptyServers"));
            filterEmpty.OnSelected += (tickBox) => { FilterServers(); return true; };

            //-------------------------------------------------------------------------------------
            //right column
            //-------------------------------------------------------------------------------------

            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(1.0f - leftColumn.RectTransform.RelativeSize.X - 0.017f, 1.0f),
                paddedFrame.RectTransform, Anchor.TopRight))
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            var columnHeaderContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.03f), rightColumn.RectTransform), isHorizontal: true);

			serverList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.85f), rightColumn.RectTransform, Anchor.Center))
            {
                OnSelected = SelectServer
            };

            columnHeaderContainer.RectTransform.NonScaledSize = new Point(serverList.Content.Rect.Width, columnHeaderContainer.Rect.Height);

            columnRelativeWidth = new float[] { 0.04f, 0.04f, 0.7f, 0.08f, 0.05f, 0.04f, 0.07f };
            string[] columnHeaders = new string[]
            {
                TextManager.Get("ServerListCompatible"),
                TextManager.Get("Password"),
                TextManager.Get("ServerListName"),
                TextManager.Get("ServerListPlayers"),
                TextManager.Get("ServerListPing"),
                TextManager.Get("ServerListRoundStarted"),
                ""
            };
            Sprite[] columnIcons = new Sprite[]
            {
                GUI.CheckmarkIcon,
                GUI.LockIcon,
                null,
                null,
                null,
                null,
                null
            };

            System.Diagnostics.Debug.Assert(columnRelativeWidth.Length == columnHeaders.Length);
            System.Diagnostics.Debug.Assert(columnRelativeWidth.Length == columnIcons.Length);

            for (int i = 0; i < columnHeaders.Length; i++)
            {
                if (columnIcons[i] != null)
                {
                    new GUIImage(new RectTransform(new Vector2(columnRelativeWidth[i], 1.0f), columnHeaderContainer.RectTransform),
                        columnIcons[i], scaleToFit: true)
                    {
                        ToolTip = columnHeaders[i]
                    };
                    continue;
                }

                new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[i], 1.0f), columnHeaderContainer.RectTransform),
                    columnHeaders[i], font: GUI.SmallFont, wrap: true)
                {
                    Padding = Vector4.Zero
                };
            }

            var buttonContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.075f), rightColumn.RectTransform), style: null);

            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform, Anchor.TopLeft),
                TextManager.Get("Back"), style: "GUIButtonLarge")
            {
                OnClicked = GameMain.MainMenuScreen.SelectTab
            };

			new GUIButton(new RectTransform(new Vector2(0.1f, 0.9f), buttonContainer.RectTransform, Anchor.Center),
				"", style: "GUIButtonRefresh") {

				ToolTip = TextManager.Get("ServerListRefresh"),
				OnClicked = RefreshServers
			};

			joinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform, Anchor.TopRight),
                TextManager.Get("ServerListJoin"), style: "GUIButtonLarge")
            {
                OnClicked = JoinServer
            };

            //--------------------------------------------------------

            button.SelectedColor = button.Color;

            refreshDisableTimer = DateTime.Now;
        }

        public override void Select()
        {
            base.Select();
            RefreshServers(null, null);
        }

        private void FilterServers()
        {
            serverList.Content.RemoveChild(serverList.Content.FindChild("noresults"));
            
            foreach (GUIComponent child in serverList.Content.Children)
            {
                if (!(child.UserData is ServerInfo)) continue;
                ServerInfo serverInfo = (ServerInfo)child.UserData;

                bool incompatible =
                    (!serverInfo.ContentPackageHashes.Any() && serverInfo.ContentPackagesMatch(GameMain.Config.SelectedContentPackages)) ||
                    (!string.IsNullOrEmpty(serverInfo.GameVersion) && serverInfo.GameVersion != GameMain.Version.ToString());

                child.Visible =
                    serverInfo.ServerName.ToLowerInvariant().Contains(searchBox.Text.ToLowerInvariant()) &&
                    (!filterPassword.Selected || !serverInfo.HasPassword) &&
                    (!filterIncompatible.Selected || !incompatible) &&
                    (!filterFull.Selected || serverInfo.PlayerCount < serverInfo.MaxPlayers) &&
                    (!filterEmpty.Selected || serverInfo.PlayerCount > 0);
            }

            if (serverList.Content.Children.All(c => !c.Visible))
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), serverList.Content.RectTransform),
                    TextManager.Get("NoMatchingServers"))
                {
                    UserData = "noresults"
                };
            }
        }

        private bool SelectServer(GUIComponent component, object obj)
        {
            if (obj == null || waitingForRefresh) return false;

            ServerInfo serverInfo;
            try
            {
                serverInfo = (ServerInfo)obj;
            }
            catch (InvalidCastException)
            {
                return false;
            }

            ipBox.Text = serverInfo.IP + ":" + serverInfo.Port;

            return true;
        }

        private bool RefreshServers(GUIButton button, object obj)
        {
            if (waitingForRefresh) return false;
            serverList.ClearChildren();

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), serverList.Content.RectTransform),
                TextManager.Get("RefreshingServerList"));
            
            CoroutineManager.StartCoroutine(WaitForRefresh());

            return true;
        }

        private IEnumerable<object> WaitForRefresh()
        {
            waitingForRefresh = true;
            if (refreshDisableTimer > DateTime.Now)
            {
                yield return new WaitForSeconds((float)(refreshDisableTimer - DateTime.Now).TotalSeconds);
            }
            
            if (GameMain.Config.UseSteamMatchmaking)
            {
                serverList.ClearChildren();
                if (!SteamManager.GetServers(AddToServerList, UpdateServerInfo, ServerQueryFinished))
                {
                    serverList.ClearChildren();
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), serverList.Content.RectTransform),
                        TextManager.Get("ServerListNoSteamConnection"));
                }
            }
            else
            {
                CoroutineManager.StartCoroutine(SendMasterServerRequest());
            }

            waitingForRefresh = false;

            refreshDisableTimer = DateTime.Now + AllowedRefreshInterval;

            yield return CoroutineStatus.Success;
        }

        private void UpdateServerList(string masterServerData)
        {
            serverList.ClearChildren();
                        
            if (masterServerData.Substring(0, 5).ToLowerInvariant() == "error")
            {
                DebugConsole.ThrowError("Error while connecting to master server (" + masterServerData + ")!");
                return;
            }

            string[] lines = masterServerData.Split('\n');
            List<ServerInfo> serverInfos = new List<ServerInfo>();
            for (int i = 0; i < lines.Length; i++)
            {
                string[] arguments = lines[i].Split('|');
                if (arguments.Length < 3) continue;

                string ip =                 arguments[0];
                string port =               arguments[1];
                string serverName =         arguments[2];
                bool gameStarted =          arguments.Length > 3 && arguments[3] == "1";
                string currPlayersStr =     arguments.Length > 4 ? arguments[4] : "";
                string maxPlayersStr =      arguments.Length > 5 ? arguments[5] : "";
                bool hasPassWord =          arguments.Length > 6 && arguments[6] == "1";
                string gameVersion =        arguments.Length > 7 ? arguments[7] : "";
                string contentPackageNames = arguments.Length > 8 ? arguments[8] : "";
                string contentPackageHashes = arguments.Length > 9 ? arguments[9] : "";

                int.TryParse(currPlayersStr, out int playerCount);
                int.TryParse(maxPlayersStr, out int maxPlayers);

                var serverInfo = new ServerInfo()
                {
                    IP = ip,
                    Port = port,
                    ServerName = serverName,
                    GameStarted = gameStarted,
                    PlayerCount = playerCount,
                    MaxPlayers = maxPlayers,
                    HasPassword = hasPassWord,
                    GameVersion = gameVersion
                };
                foreach (string contentPackageName in contentPackageNames.Split(','))
                {
                    if (string.IsNullOrEmpty(contentPackageName)) continue;
                    serverInfo.ContentPackageNames.Add(contentPackageName);
                }
                foreach (string contentPackageHash in contentPackageHashes.Split(','))
                {
                    if (string.IsNullOrEmpty(contentPackageHash)) continue;
                    serverInfo.ContentPackageHashes.Add(contentPackageHash);
                }

                serverInfos.Add(serverInfo);
            }

            serverList.Content.ClearChildren();
            if (serverInfos.Count() == 0)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), serverList.Content.RectTransform),
                    TextManager.Get("NoServers"));
                return;
            }
            foreach (ServerInfo serverInfo in serverInfos)
            {
                AddToServerList(serverInfo);
            }
        }

        private void AddToServerList(ServerInfo serverInfo)
        {
            var serverFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.06f), serverList.Content.RectTransform) { MinSize = new Point(0, 20) },
                style: "InnerFrame", color: Color.White * 0.5f)
            {
                UserData = serverInfo
            };
            var serverContent = new GUILayoutGroup(new RectTransform(Vector2.One, serverFrame.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            UpdateServerInfo(serverInfo);
        }

        private void UpdateServerInfo(ServerInfo serverInfo)
        {

            var serverFrame = serverList.Content.FindChild(serverInfo);
            if (serverFrame == null) return;

            var serverContent = serverFrame.Children.First();
            serverContent.ClearChildren();

                var compatibleBox = new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[0], 0.6f), serverContent.RectTransform, Anchor.Center), label: "", style: "GUIServerListTickBox")
            {
                Selected =
                    serverInfo.GameVersion == GameMain.Version.ToString() &&
                    serverInfo.ContentPackagesMatch(GameMain.SelectedPackages),
                Enabled = false,
                UserData = "compatible"
            };
            
            var passwordBox = new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[1], 0.6f), serverContent.RectTransform, Anchor.Center), label: "", style: "GUIServerListTickBox")
            {
                Selected = serverInfo.HasPassword,
                Enabled = false,
                UserData = "password"
            };
                        
            var serverName = new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[2], 1.0f), serverContent.RectTransform), serverInfo.ServerName, style: "GUIServerListTextBox");
            var serverPlayers = new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[3], 1.0f), serverContent.RectTransform),
                serverInfo.PlayerCount + "/" + serverInfo.MaxPlayers, style: "GUIServerListTextBox");

            var serverPingText = new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[4], 1.0f), serverContent.RectTransform), "?", style: "GUIServerListTextBox");

            if (serverInfo.IP != null)
            {
                try
                {
                    GetServerPing(serverInfo, serverPingText);
                }
                catch (NullReferenceException ex)
                {
                    DebugConsole.ThrowError("Ping is null", ex);
                }
            }

            var gameStartedBox = new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[5], 0.6f), serverContent.RectTransform, Anchor.Center),
                label: "", style: "GUIServerListTickBox")
            {
                Selected = serverInfo.GameStarted,
                Enabled = false
            };

            if (string.IsNullOrEmpty(serverInfo.GameVersion) || !serverInfo.ContentPackageHashes.Any())
            {
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), compatibleBox.Box.RectTransform, Anchor.Center), " ? ", Color.Yellow, textAlignment: Alignment.Center)
                {
                    ToolTip = TextManager.Get(string.IsNullOrEmpty(serverInfo.GameVersion) ?
                        "ServerListUnknownVersion" :
                        "ServerListUnknownContentPackage")
                };
            }
            else if (!compatibleBox.Selected)
            {
                string toolTip = "";
                if (serverInfo.GameVersion != GameMain.Version.ToString())
                    toolTip = TextManager.Get("ServerListIncompatibleVersion").Replace("[version]", serverInfo.GameVersion);

                for (int i = 0; i < serverInfo.ContentPackageNames.Count; i++)
                {
                    if (!GameMain.SelectedPackages.Any(cp => cp.MD5hash.Hash == serverInfo.ContentPackageHashes[i]))
                    {
                        if (toolTip != "") toolTip += "\n";
                        toolTip += TextManager.Get("ServerListIncompatibleContentPackage")
                            .Replace("[contentpackage]", serverInfo.ContentPackageNames[i])
                            .Replace("[hash]", Md5Hash.GetShortHash(serverInfo.ContentPackageHashes[i]));
                    }
                }
                
                serverContent.Children.ForEach(c => c.ToolTip = toolTip);

                serverName.TextColor *= 0.5f;
                serverPlayers.TextColor *= 0.5f;
            }

            new GUIButton(new RectTransform(new Vector2(columnRelativeWidth[5], 0.8f), serverContent.RectTransform, Anchor.CenterRight), TextManager.Get("ServerListInfo"), textAlignment: Alignment.Center, style: "GUIButtonServerListInfo")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (btn, obj) =>
                {
                    SelectServer(null, serverInfo);
                    var msgBox = new GUIMessageBox("", "", new string[] { TextManager.Get("Cancel"), TextManager.Get("ServerListJoin") }, 550, 400);
                    msgBox.Buttons[0].OnClicked += msgBox.Close;
                    msgBox.Buttons[1].OnClicked += JoinServer;
                    msgBox.Buttons[1].OnClicked += msgBox.Close;
                    serverInfo.CreatePreviewWindow(msgBox);
                    return true;
                }
            };

            FilterServers();
        }

        private void ServerQueryFinished()
        {
            if (serverList.Content.Children.All(c => !c.Visible))
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), serverList.Content.RectTransform),
                    TextManager.Get("NoMatchingServers"))
                {
                    UserData = "noresults"
                };
            }
        }

        private IEnumerable<object> SendMasterServerRequest()
        {
            RestClient client = null;
            try
            {
                client = new RestClient(NetConfig.MasterServerUrl);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error while connecting to master server", e);                
            }

            if (client == null) yield return CoroutineStatus.Success;

            var request = new RestRequest("masterserver2.php", Method.GET);
            request.AddParameter("gamename", "barotrauma");
            request.AddParameter("action", "listservers");
            
            // execute the request
            masterServerResponded = false;
            var restRequestHandle = client.ExecuteAsync(request, response => MasterServerCallBack(response));

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 8);
            while (!masterServerResponded)
            {
                if (DateTime.Now > timeOut)
                {
                    serverList.ClearChildren();
                    restRequestHandle.Abort();
                    new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), TextManager.Get("MasterServerTimeOutError"));
                    yield return CoroutineStatus.Success;
                }
                yield return CoroutineStatus.Running;
            }

            if (masterServerResponse.ErrorException != null)
            {
                serverList.ClearChildren();
                new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), TextManager.Get("MasterServerErrorException").Replace("[error]", masterServerResponse.ErrorException.ToString()));
            }
            else if (masterServerResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                serverList.ClearChildren();
                
                switch (masterServerResponse.StatusCode)
                {
                    case System.Net.HttpStatusCode.NotFound:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"),
                           TextManager.Get("MasterServerError404")
                                .Replace("[masterserverurl]", NetConfig.MasterServerUrl)
                                .Replace("[statuscode]", masterServerResponse.StatusCode.ToString())
                                .Replace("[statusdescription]", masterServerResponse.StatusDescription));
                        break;
                    case System.Net.HttpStatusCode.ServiceUnavailable:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), 
                            TextManager.Get("MasterServerErrorUnavailable")
                                .Replace("[masterserverurl]", NetConfig.MasterServerUrl)
                                .Replace("[statuscode]", masterServerResponse.StatusCode.ToString())
                                .Replace("[statusdescription]", masterServerResponse.StatusDescription));
                        break;
                    default:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"),
                            TextManager.Get("MasterServerError404")
                                .Replace("[masterserverurl]", NetConfig.MasterServerUrl)
                                .Replace("[statuscode]", masterServerResponse.StatusCode.ToString())
                                .Replace("[statusdescription]", masterServerResponse.StatusDescription));
                        break;
                }
                
            }
            else
            {
                UpdateServerList(masterServerResponse.Content);
            }

            yield return CoroutineStatus.Success;

        }

        private void MasterServerCallBack(IRestResponse response)
        {
            masterServerResponse = response;
            masterServerResponded = true;
        }

        private bool JoinServer(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(clientNameBox.Text))
            {
                clientNameBox.Flash();
                return false;
            }

            GameMain.Config.DefaultPlayerName = clientNameBox.Text;

            string ip = ipBox.Text;

            if (string.IsNullOrWhiteSpace(ip))
            {
                ipBox.Flash();
                return false;
            }

            CoroutineManager.StartCoroutine(ConnectToServer(ip));

            return true;
        }

        /*public void JoinServer(string ip, bool hasPassword, string msg = "Password required")
        {
            CoroutineManager.StartCoroutine(ConnectToServer(ip));
        }*/

        private IEnumerable<object> ConnectToServer(string ip)
        {
#if !DEBUG
            try
            {
#endif
                GameMain.Client = new GameClient(clientNameBox.Text);
                GameMain.Client.ConnectToServer(ip);
#if !DEBUG
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to start the client", e);
            }
#endif

            yield return CoroutineStatus.Success;
        }

        public void GetServerPing(ServerInfo serverInfo, GUITextBlock serverPingText)
        {
            serverInfo.PingChecked = false;
            serverInfo.Ping = -1;

            var pingThread = new Thread(() => { PingServer(serverInfo, 1000); })
            {
                IsBackground = true
            };
            pingThread.Start();

            CoroutineManager.StartCoroutine(UpdateServerPingText(serverInfo, serverPingText, 1000));
        }

        private IEnumerable<object> UpdateServerPingText(ServerInfo serverInfo, GUITextBlock serverPingText, int timeOut)
        {
            DateTime timeOutTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: timeOut);
            while (DateTime.Now < timeOutTime)
            {
                if (serverInfo.PingChecked)
                {
                    serverPingText.Text = serverInfo.Ping > -1 ? serverInfo.Ping.ToString() : "?";
                    yield return CoroutineStatus.Success;
                }

                yield return CoroutineStatus.Running;
            }
            yield return CoroutineStatus.Success;
        }

        public void PingServer(ServerInfo serverInfo, int timeOut)
        {
            long rtt = -1;
            IPAddress address = IPAddress.Parse(serverInfo.IP);
            if (address != null)
            {
                Ping ping = new Ping();
                byte[] buffer = new byte[32];
                try
                {
                    PingReply pingReply = ping.Send(address, timeOut, buffer, new PingOptions(128, true));

                    if (pingReply != null)
                    {
                        switch (pingReply.Status)
                        {
                            case IPStatus.Success:
                                rtt = pingReply.RoundtripTime;
                                break;
                            default:
                                rtt = -1;
                                break;
                        }
                    }
                }
                catch (PingException ex)
                {
                    DebugConsole.ThrowError("Ping Error", ex);
                }
            }

            serverInfo.PingChecked = true;
            serverInfo.Ping = (int)rtt;
        }
        
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            GameMain.TitleScreen.DrawLoadingText = false;
            GameMain.TitleScreen.Draw(spriteBatch, graphics, (float)deltaTime);

            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, GameMain.ScissorTestEnable);
            
            GUI.Draw(Cam, spriteBatch);

            spriteBatch.End();
        }

        public override void AddToGUIUpdateList()
        {
            menu.AddToGUIUpdateList();
        }
        
    }
}
