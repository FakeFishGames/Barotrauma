using Barotrauma.Extensions;
using Barotrauma.Networking;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace Barotrauma
{
    class ServerListScreen : Screen
    {
        //how often the client is allowed to refresh servers
        private TimeSpan AllowedRefreshInterval = new TimeSpan(0, 0, 3);

        private readonly GUIFrame menu;

        private readonly GUIListBox serverList;
        private readonly GUIFrame serverPreview;

        private readonly GUIButton joinButton;

        private readonly GUITextBox clientNameBox, ipBox;

        private bool masterServerResponded;
        private IRestResponse masterServerResponse;
        
        private readonly float[] columnRelativeWidth = new float[] { 0.1f, 0.1f, 0.7f, 0.12f, 0.08f, 0.08f };
        private readonly string[] columnLabel = new string[] { "ServerListCompatible", "ServerListHasPassword", "ServerListName", "ServerListRoundStarted", "ServerListPlayers", "ServerListPing" };
        
        private readonly GUILayoutGroup labelHolder;
        private readonly List<GUITextBlock> labelTexts = new List<GUITextBlock>();

        //filters
        private readonly GUITextBox searchBox;
        private readonly GUITickBox filterPassword;
        private readonly GUITickBox filterIncompatible;
        private readonly GUITickBox filterFull;
        private readonly GUITickBox filterEmpty;

        private string sortedBy;

        private readonly GUIButton serverPreviewToggleButton;

        //a timer for preventing the client from spamming the refresh button faster than AllowedRefreshInterval
        private DateTime refreshDisableTimer;
        private bool waitingForRefresh;

        public ServerListScreen()
        {
            GameMain.Instance.OnResolutionChanged += OnResolutionChanged;

            menu = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.85f), GUI.Canvas, Anchor.Center) { MinSize = new Point(GameMain.GraphicsHeight, 0) });

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.97f, 0.95f), menu.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            //-------------------------------------------------------------------------------------
            //Top row
            //-------------------------------------------------------------------------------------

            var topRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform)) { Stretch = true };

            var title = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.33f), topRow.RectTransform), TextManager.Get("JoinServer"), font: GUI.LargeFont)
            {
                Padding = Vector4.Zero,
                ForceUpperCase = true,
                AutoScale = true
            };

            var infoHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.33f), topRow.RectTransform), isHorizontal: true) { RelativeSpacing = 0.05f, Stretch = true };

            var clientNameHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), infoHolder.RectTransform)) { RelativeSpacing = 0.05f };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), clientNameHolder.RectTransform), TextManager.Get("YourName"));
            clientNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), clientNameHolder.RectTransform), "")
            {
                Text = GameMain.Config.DefaultPlayerName,
                MaxTextLength = Client.MaxNameLength,
                OverflowClip = true
            };

            if (string.IsNullOrEmpty(clientNameBox.Text))
            {
                clientNameBox.Text = SteamManager.GetUsername();
            }

            var ipBoxHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), infoHolder.RectTransform)) { RelativeSpacing = 0.05f };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), ipBoxHolder.RectTransform), TextManager.Get("ServerIP"));
            ipBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), ipBoxHolder.RectTransform), "");
            ipBox.OnTextChanged += (textBox, text) => { joinButton.Enabled = !string.IsNullOrEmpty(text); return true; };
            ipBox.OnSelected += (sender, key) =>
            {
                if (sender.UserData is ServerInfo)
                {
                    sender.Text = "";
                    sender.UserData = null;
                }
            };

            //-------------------------------------------------------------------------------------
            // Bottom row
            //-------------------------------------------------------------------------------------

            var bottomRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f - topRow.RectTransform.RelativeSize.Y),
                paddedFrame.RectTransform, Anchor.CenterRight))
            {
                Stretch = true
            };

            var serverListHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), bottomRow.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };

            // filters -------------------------------------------

            var filters = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f), serverListHolder.RectTransform, Anchor.Center), style: null)
            {
                Color = new Color(12, 14, 15, 255) * 0.5f,
                OutlineColor = Color.Black
            };
            var filterToggle = new GUIButton(new RectTransform(new Vector2(0.02f, 1.0f), serverListHolder.RectTransform, Anchor.CenterRight) { MinSize = new Point(20, 0) }, style: "UIToggleButton")
            {
                OnClicked = (btn, userdata) =>
                {
                    filters.RectTransform.RelativeSize = new Vector2(0.25f, 1.0f);
                    filters.Visible = !filters.Visible;
                    filters.IgnoreLayoutGroups = !filters.Visible;
                    serverListHolder.Recalculate();
                    btn.Children.ForEach(c => c.SpriteEffects = !filters.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
                    return true;
                }
            };
            filterToggle.Children.ForEach(c => c.SpriteEffects = SpriteEffects.FlipHorizontally);

            var filterContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.99f), filters.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            var filterTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), filterContainer.RectTransform), TextManager.Get("FilterServers"), font: GUI.LargeFont)
            {
                Padding = Vector4.Zero,
                AutoScale = true
            };

            float elementHeight = 0.05f;

            var searchHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, elementHeight), filterContainer.RectTransform), isHorizontal: true) { Stretch = true };

            var searchTitle = new GUITextBlock(new RectTransform(new Vector2(0.001f, 1.0f), searchHolder.RectTransform), TextManager.Get("Search") + "...");
            searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 1.0f), searchHolder.RectTransform), "");
            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = true; };
            searchBox.OnTextChanged += (txtBox, txt) => { FilterServers(); return true; };

            var filterHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), filterContainer.RectTransform)) { RelativeSpacing = 0.005f };

            List<GUITextBlock> filterTextList = new List<GUITextBlock>();
            filterPassword = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filterHolder.RectTransform), TextManager.Get("FilterPassword"))
            {
                ToolTip = TextManager.Get("FilterPassword"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterPassword.TextBlock);
            filterIncompatible = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filterHolder.RectTransform), TextManager.Get("FilterIncompatibleServers"))
            {
                ToolTip = TextManager.Get("FilterIncompatibleServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterIncompatible.TextBlock);
            filterFull = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filterHolder.RectTransform), TextManager.Get("FilterFullServers"))
            {
                ToolTip = TextManager.Get("FilterFullServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterFull.TextBlock);
            filterEmpty = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filterHolder.RectTransform), TextManager.Get("FilterEmptyServers"))
            {
                ToolTip = TextManager.Get("FilterEmptyServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterEmpty.TextBlock);

            filterContainer.RectTransform.SizeChanged += () =>
            {
                filterContainer.RectTransform.RecalculateChildren(true, true);
                filterTextList.ForEach(t => t.Text = t.ToolTip);
                GUITextBlock.AutoScaleAndNormalize(filterTextList);
                if (filterTextList[0].TextScale < 0.8f)
                {
                    filterTextList.ForEach(t => t.TextScale = 1.0f);
                    filterTextList.ForEach(t => t.Text = ToolBox.LimitString(t.Text, t.Font, (int)(filterContainer.Rect.Width * 0.8f)));
                }
            };

            // server list ---------------------------------------------------------------------

            var serverListContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), serverListHolder.RectTransform)) { Stretch = true };

            labelHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.99f, 0.05f), serverListContainer.RectTransform) { MinSize = new Point(0, 15) },
                isHorizontal: true)
            {
                Stretch = true
            };
            
            for (int i = 0; i < columnRelativeWidth.Length; i++)
            {
                var btn = new GUIButton(new RectTransform(new Vector2(columnRelativeWidth[i], 1.0f), labelHolder.RectTransform),
                    text: TextManager.Get(columnLabel[i]), textAlignment: Alignment.Center, style: null)
                {
                    Color = new Color(12, 14, 15, 255) * 0.5f,
                    HoverColor = new Color(12, 14, 15, 255) * 2.5f,
                    SelectedColor = Color.Gray * 0.7f,
                    PressedColor = Color.Gray * 0.7f,
                    OutlineColor = Color.Black,
                    ToolTip = TextManager.Get(columnLabel[i]),
                    ForceUpperCase = true,
                    UserData = columnLabel[i],
                    OnClicked = SortList
                };
                labelTexts.Add(btn.TextBlock);
                
                new GUIImage(new RectTransform(new Vector2(0.5f, 0.3f), btn.RectTransform, Anchor.BottomCenter, scaleBasis: ScaleBasis.BothHeight), style: "GUIButtonVerticalArrow", scaleToFit: true)
                {
                    CanBeFocused = false,
                    UserData = "arrowup",
                    Visible = false
                };
                new GUIImage(new RectTransform(new Vector2(0.5f, 0.3f), btn.RectTransform, Anchor.BottomCenter, scaleBasis: ScaleBasis.BothHeight), style: "GUIButtonVerticalArrow", scaleToFit: true)
                {
                    CanBeFocused = false,
                    UserData = "arrowdown",
                    SpriteEffects = SpriteEffects.FlipVertically,
                    Visible = false
                };
            }

            serverList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), serverListContainer.RectTransform, Anchor.Center))
            {
                ScrollBarVisible = true,
                OnSelected = (btn, obj) =>
                {
                    if (obj is ServerInfo serverInfo)
                    {
                        joinButton.Enabled = true;
                        ipBox.UserData = serverInfo;
                        ipBox.Text = serverInfo.ServerName;
                        if (!serverPreview.Visible)
                        {
                            serverPreview.RectTransform.RelativeSize = new Vector2(0.3f, 1.0f);
                            serverPreviewToggleButton.Visible = true;
                            serverPreviewToggleButton.IgnoreLayoutGroups = false;
                            serverPreview.Visible = true;
                            serverPreview.IgnoreLayoutGroups = false;
                            serverListHolder.Recalculate();
                        }
                        serverInfo.CreatePreviewWindow(serverPreview);
                        btn.Children.ForEach(c => c.SpriteEffects = serverPreview.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
                    }
                    return true;
                }
            };

            //server preview panel --------------------------------------------------

            serverPreviewToggleButton = new GUIButton(new RectTransform(new Vector2(0.02f, 1.0f), serverListHolder.RectTransform, Anchor.CenterRight) { MinSize = new Point(20, 0) }, style: "UIToggleButton")
            {
                Visible = false,
                OnClicked = (btn, userdata) =>
                {
                    serverPreview.RectTransform.RelativeSize = new Vector2(0.25f, 1.0f);
                    serverPreview.Visible = !serverPreview.Visible;
                    serverPreview.IgnoreLayoutGroups = !serverPreview.Visible;
                    serverListHolder.Recalculate();
                    btn.Children.ForEach(c => c.SpriteEffects = serverPreview.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
                    return true;
                }
            };

            serverPreview = new GUIFrame(new RectTransform(new Vector2(0.3f, 1.0f), serverListHolder.RectTransform, Anchor.Center), style: null)
            {
                Color = new Color(12, 14, 15, 255) * 0.5f,
                OutlineColor = Color.Black,
                IgnoreLayoutGroups = true,
                Visible = false
            };

            // Spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), bottomRow.RectTransform), style: null);

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.075f), bottomRow.RectTransform, Anchor.Center), isHorizontal: true)
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("Back"), style: "GUIButtonLarge")
            {
                OnClicked = GameMain.MainMenuScreen.ReturnToMainMenu
            };

            new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("ServerListRefresh"), style: "GUIButtonLarge")
            {
				OnClicked = (btn, userdata) => { RefreshServers(); return true; }
			};

            /*var directJoinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("serverlistdirectjoin"), style: "GUIButtonLarge")
            {
                OnClicked = (btn, userdata) => { ShowDirectJoinPrompt(); return true; }
            };*/

            joinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("ServerListJoin"), style: "GUIButtonLarge")
            {
                OnClicked = (btn, userdata) =>
                {
                    if (ipBox.UserData is ServerInfo selectedServer)
                    {
                        if (selectedServer.SteamID == 0)
                        {
                            JoinServer(selectedServer.IP + ":" + selectedServer.Port, selectedServer.ServerName);
                        }
                        else
                        {
                            JoinServer(selectedServer.SteamID, selectedServer.ServerName);
                        }
                    }
                    else if (!string.IsNullOrEmpty(ipBox.Text))
                    {
                        JoinServer(ipBox.Text, "");
                    }
                    return true;
                },
                Enabled = false
            };

            //--------------------------------------------------------

            bottomRow.Recalculate();
            serverListHolder.Recalculate();
            serverListContainer.Recalculate();
            labelHolder.RectTransform.MaxSize = new Point(serverList.Content.Rect.Width, int.MaxValue);
            labelHolder.Recalculate();

            serverList.Content.RectTransform.SizeChanged += () =>
            {
                labelHolder.RectTransform.MaxSize = new Point(serverList.Content.Rect.Width, int.MaxValue);
                labelHolder.Recalculate();
                foreach (GUITextBlock labelText in labelTexts)
                {
                    labelText.Text = ToolBox.LimitString(labelText.ToolTip, labelText.Font, labelText.Rect.Width);
                }
            };

            button.SelectedColor = button.Color;
            refreshDisableTimer = DateTime.Now;
        }

        private void OnResolutionChanged()
        {
            menu.RectTransform.MinSize = new Point(GameMain.GraphicsHeight, 0);
            labelHolder.RectTransform.MaxSize = new Point(serverList.Content.Rect.Width, int.MaxValue);
            foreach (GUITextBlock labelText in labelTexts)
            {
                labelText.Text = ToolBox.LimitString(labelText.ToolTip, labelText.Font, labelText.Rect.Width);
            }
        }

        private bool SortList(GUIButton button, object obj)
        {
            if (!(obj is string sortBy)) { return false; }
            SortList(sortBy, toggle: true);
            return true;
        }

        private void SortList(string sortBy, bool toggle)
        {
            GUIButton button = labelHolder.GetChildByUserData(sortBy) as GUIButton;
            if (button == null) { return; }

            sortedBy = sortBy;

            var arrowUp = button.GetChildByUserData("arrowup");
            var arrowDown = button.GetChildByUserData("arrowdown");
            
            //disable arrow buttons in other labels
            foreach (var child in button.Parent.Children)
            {
                if (child != button)
                {
                    child.GetChildByUserData("arrowup").Visible = false;
                    child.GetChildByUserData("arrowdown").Visible = false;
                }
            }

            bool ascending = arrowUp.Visible;
            if (toggle)
            {
                ascending = !ascending;
            }

            arrowUp.Visible = ascending;
            arrowDown.Visible = !ascending;
            serverList.Content.RectTransform.SortChildren((c1, c2) => 
            {
                ServerInfo s1 = c1.GUIComponent.UserData as ServerInfo;
                ServerInfo s2 = c2.GUIComponent.UserData as ServerInfo;

                if (s1 == null && s2 == null)
                {
                    return 0;
                }
                else if (s1 == null)
                {
                    return ascending ? 1 : -1;
                }
                else if (s2 == null)
                {
                    return ascending ? -1 : 1;
                }

                switch (sortBy)
                {
                    case "ServerListCompatible":                        
                        bool? s1Compatible = NetworkMember.IsCompatible(GameMain.Version.ToString(), s1.GameVersion);
                        if (!s1.ContentPackageHashes.Any()) { s1Compatible = null; }
                        if (s1Compatible.HasValue) { s1Compatible = s1Compatible.Value && s1.ContentPackagesMatch(GameMain.SelectedPackages); };

                        bool? s2Compatible = NetworkMember.IsCompatible(GameMain.Version.ToString(), s2.GameVersion);
                        if (!s2.ContentPackageHashes.Any()) { s2Compatible = null; }
                        if (s2Compatible.HasValue) { s2Compatible = s2Compatible.Value && s2.ContentPackagesMatch(GameMain.SelectedPackages); };

                        //convert to int to make sorting easier
                        //1 Compatible
                        //0 Unknown 
                        //-1 Incompatible
                        int s1CompatibleInt = s1Compatible.HasValue ?
                            (s1Compatible.Value ? 1 : -1) :
                            0;
                        int s2CompatibleInt = s2Compatible.HasValue ?
                            (s2Compatible.Value ? 1 : -1) :
                            0;
                        return s2CompatibleInt.CompareTo(s1CompatibleInt) * (ascending ? 1 : -1);
                    case "ServerListHasPassword":
                        if (s1.HasPassword == s2.HasPassword) { return 0; }
                        return (s1.HasPassword ? 1 : -1) * (ascending ? 1 : -1);
                    case "ServerListName":
                        return string.Compare(s1.ServerName, s2.ServerName) * (ascending ? 1 : -1);
                    case "ServerListRoundStarted":
                        if (s1.GameStarted == s2.GameStarted) { return 0; }
                        return (s1.GameStarted ? 1 : -1) * (ascending ? 1 : -1);
                    case "ServerListPlayers":
                        return s2.PlayerCount.CompareTo(s1.PlayerCount) * (ascending ? 1 : -1);
                    case "ServerListPing":
                        return s2.Ping.CompareTo(s1.Ping) * (ascending ? 1 : -1);
                    default:
                        return 0;
                }
            });
        }
        
        public override void Select()
        {
            base.Select();
            RefreshServers();
        }

        private void FilterServers()
        {
            serverList.Content.RemoveChild(serverList.Content.FindChild("noresults"));
            
            foreach (GUIComponent child in serverList.Content.Children)
            {
                if (!(child.UserData is ServerInfo)) continue;
                ServerInfo serverInfo = (ServerInfo)child.UserData;

                Version remoteVersion = null;
                if (!string.IsNullOrEmpty(serverInfo.GameVersion))
                {
                    Version.TryParse(serverInfo.GameVersion, out remoteVersion);
                }

                bool incompatible =
                    (!serverInfo.ContentPackageHashes.Any() && serverInfo.ContentPackagesMatch(GameMain.Config.SelectedContentPackages)) ||
                    (remoteVersion != null && !NetworkMember.IsCompatible(GameMain.Version, remoteVersion));

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

            serverList.UpdateScrollBarSize();
        }

        /*private void ShowDirectJoinPrompt()
        {
            var msgBox = new GUIMessageBox(TextManager.Get("ServerListDirectJoin"), "", new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") },
                relativeSize: new Vector2(0.25f, 0.2f), minSize: new Point(400, 150));

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.3f), msgBox.InnerFrame.RectTransform, Anchor.Center) { MinSize = new Point(0, 50) })
            {
                IgnoreLayoutGroups = true,
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform), TextManager.Get("ServerIP"));
            var ipBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform));

            var okButton = msgBox.Buttons[0];
            okButton.Enabled = false;
            okButton.OnClicked = (btn, userdata) =>
            {
                JoinServer(ipBox.Text, "");
                msgBox.Close();
                return true;
            };

            var cancelButton = msgBox.Buttons[1];
            cancelButton.OnClicked = msgBox.Close;

            ipBox.OnTextChanged += (textBox, text) =>
            {
                okButton.Enabled = !string.IsNullOrEmpty(text);
                return true;
            };
        }*/

        private void RefreshServers()
        {
            if (waitingForRefresh) { return; }
            serverList.ClearChildren();
            serverPreview.ClearChildren();
            joinButton.Enabled = false;
            ipBox.UserData = null;
            ipBox.Text = "";

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), serverList.Content.RectTransform),
                TextManager.Get("RefreshingServerList"), textAlignment: Alignment.Center)
            {
                CanBeFocused = false
            };
            
            CoroutineManager.StartCoroutine(WaitForRefresh());
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
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), serverList.Content.RectTransform),
                        TextManager.Get("ServerListNoSteamConnection"), textAlignment: Alignment.Center)
                    {
                        CanBeFocused = false
                    };
                }
            }
            else
            {
                CoroutineManager.StartCoroutine(SendMasterServerRequest());
                waitingForRefresh = false;
            }

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
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), serverList.Content.RectTransform),
                    TextManager.Get("NoServers"), textAlignment: Alignment.Center)
                {
                    CanBeFocused = false
                };
                return;
            }
            foreach (ServerInfo serverInfo in serverInfos)
            {
                AddToServerList(serverInfo);
            }
        }

        private void AddToServerList(ServerInfo serverInfo)
        {
            var serverFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.06f), serverList.Content.RectTransform) { MinSize = new Point(0, 35) },
                style: "InnerFrame", color: Color.White * 0.5f)
            {
                UserData = serverInfo
            };
            new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 1.0f), serverFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true,
                //RelativeSpacing = 0.02f
            };
            UpdateServerInfo(serverInfo);

            SortList(sortedBy, toggle: false);
            FilterServers();
        }

        private void UpdateServerInfo(ServerInfo serverInfo)
        {
            var serverFrame = serverList.Content.FindChild(serverInfo);
            if (serverFrame == null) return;

            var serverContent = serverFrame.Children.First() as GUILayoutGroup;
            serverContent.ClearChildren();

            var compatibleBox = new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[0], 0.9f), serverContent.RectTransform, Anchor.Center), label: "")
            {
                Enabled = false,
                Selected =
                    serverInfo.GameVersion == GameMain.Version.ToString() &&
                    serverInfo.ContentPackagesMatch(GameMain.SelectedPackages),
                UserData = "compatible"
            };
            
            var passwordBox = new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[1], 0.5f), serverContent.RectTransform, Anchor.Center), label: "", style: "GUIServerListPasswordTickBox")
            {
				ToolTip = TextManager.Get((serverInfo.HasPassword) ? "ServerListHasPassword" : "FilterPassword"),
				Selected = serverInfo.HasPassword,
                Enabled = false,
                UserData = "password"
            };

			var serverName = new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[2] * 1.1f, 1.0f), serverContent.RectTransform), serverInfo.ServerName, style: "GUIServerListTextBox");

            new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[3], 0.9f), serverContent.RectTransform, Anchor.Center), label: "")
            {
				ToolTip = TextManager.Get((serverInfo.GameStarted) ? "ServerListRoundStarted" : "ServerListRoundNotStarted"),
				Selected = serverInfo.GameStarted,
				Enabled = false
			};

            var serverPlayers = new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[4], 1.0f), serverContent.RectTransform),
                serverInfo.PlayerCount + "/" + serverInfo.MaxPlayers, style: "GUIServerListTextBox", textAlignment: Alignment.Right)
            {
                ToolTip = TextManager.Get("ServerListPlayers")
            };

            var serverPingText = new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[5], 1.0f), serverContent.RectTransform), "?", 
                style: "GUIServerListTextBox", textColor: Color.White * 0.5f, textAlignment: Alignment.Right)
            {
                ToolTip = TextManager.Get("ServerListPing")
            };

            if (serverInfo.PingChecked)
            {
                serverPingText.Text = serverInfo.Ping > -1 ? serverInfo.Ping.ToString() : "?";
                serverPingText.TextColor = GetPingTextColor(serverInfo.Ping);
            }
            else if (!string.IsNullOrEmpty(serverInfo.IP))
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

            if (GameMain.Config.UseSteamMatchmaking && serverInfo.RespondedToSteamQuery.HasValue && serverInfo.RespondedToSteamQuery.Value == false)
            {
                string toolTip = TextManager.Get("ServerListNoSteamQueryResponse");
                compatibleBox.Selected = false;
                serverContent.Children.ForEach(c => c.ToolTip = toolTip);
                serverName.TextColor *= 0.8f;
                serverPlayers.TextColor *= 0.8f;
            }
            else if (string.IsNullOrEmpty(serverInfo.GameVersion) || !serverInfo.ContentPackageHashes.Any())
            {
                compatibleBox.Selected = false;
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), compatibleBox.Box.RectTransform, Anchor.Center), " ? ", Color.Yellow * 0.85f, textAlignment: Alignment.Center)
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
                    toolTip = TextManager.GetWithVariable("ServerListIncompatibleVersion", "[version]", serverInfo.GameVersion);

                for (int i = 0; i < serverInfo.ContentPackageNames.Count; i++)
                {
                    if (!GameMain.SelectedPackages.Any(cp => cp.MD5hash.Hash == serverInfo.ContentPackageHashes[i]))
                    {
                        if (toolTip != "") toolTip += "\n";
                        toolTip += TextManager.GetWithVariables("ServerListIncompatibleContentPackage", new string[2] { "[contentpackage]", "[hash]" },
                            new string[2] { serverInfo.ContentPackageNames[i], Md5Hash.GetShortHash(serverInfo.ContentPackageHashes[i]) });
                    }
                }
                
                serverContent.Children.ForEach(c => c.ToolTip = toolTip);

                serverName.TextColor *= 0.5f;
                serverPlayers.TextColor *= 0.5f;
            }

            serverContent.Recalculate();
            SortList(sortedBy, toggle: false);
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
            waitingForRefresh = false;
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
                new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), TextManager.GetWithVariable("MasterServerErrorException", "[error]", masterServerResponse.ErrorException.ToString()));
            }
            else if (masterServerResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                serverList.ClearChildren();
                
                switch (masterServerResponse.StatusCode)
                {
                    case System.Net.HttpStatusCode.NotFound:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"),
                           TextManager.GetWithVariable("MasterServerError404", "[masterserverurl]", NetConfig.MasterServerUrl));
                        break;
                    case System.Net.HttpStatusCode.ServiceUnavailable:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), 
                            TextManager.Get("MasterServerErrorUnavailable"));
                        break;
                    default:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"),
                            TextManager.GetWithVariables("MasterServerErrorDefault", new string[2] { "[statuscode]", "[statusdescription]" }, 
                            new string[2] { masterServerResponse.StatusCode.ToString(), masterServerResponse.StatusDescription }));
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

        private bool JoinServer(string ip, string serverName)
        {
            if (string.IsNullOrWhiteSpace(clientNameBox.Text))
            {
                clientNameBox.Flash();
                return false;
            }

            GameMain.Config.DefaultPlayerName = clientNameBox.Text;
            GameMain.Config.SaveNewPlayerConfig();

            CoroutineManager.StartCoroutine(ConnectToServer(ip, serverName));

            return true;
        }
        
        private IEnumerable<object> ConnectToServer(string endpoint, string serverName)
        {
            string serverIP = null;
            UInt64 serverSteamID = SteamManager.SteamIDStringToUInt64(endpoint);
            if (serverSteamID == 0) { serverIP = endpoint; }

#if !DEBUG
            try
            {
#endif
                GameMain.Client = new GameClient(clientNameBox.Text, serverIP, serverSteamID, serverName);
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
                    if (serverInfo.Ping != -1)
                    {
                        serverPingText.TextColor = GetPingTextColor(serverInfo.Ping);
					}
                    serverPingText.Text = serverInfo.Ping > -1 ? serverInfo.Ping.ToString() : "?";
                    yield return CoroutineStatus.Success;
                }

                yield return CoroutineStatus.Running;
            }
            yield return CoroutineStatus.Success;
        }

        private Color GetPingTextColor(int ping)
        {
            if (ping < 0) { return Color.DarkRed; }
            return ToolBox.GradientLerp(ping / 200.0f, Color.LightGreen, Color.Yellow * 0.8f, Color.Red * 0.75f);
        }

        public void PingServer(ServerInfo serverInfo, int timeOut)
        {
            if (serverInfo?.IP == null)
            {
                serverInfo.PingChecked = true;
                serverInfo.Ping = -1;
                return;
            }

            long rtt = -1;
            IPAddress address = IPAddress.Parse(serverInfo.IP);
            if (address != null)
            {
                //don't attempt to ping if the address is IPv6 and it's not supported
                if (address.AddressFamily != AddressFamily.InterNetworkV6 || Socket.OSSupportsIPv6)
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
                        string errorMsg = "Failed to ping a server (" + serverInfo.ServerName + ", " + serverInfo.IP + ") - " + (ex?.InnerException?.Message ?? ex.Message);
                        GameAnalyticsManager.AddErrorEventOnce("ServerListScreen.PingServer:PingException" + serverInfo.IP, GameAnalyticsSDK.Net.EGAErrorSeverity.Warning, errorMsg);
#if DEBUG
                        DebugConsole.NewMessage(errorMsg, Color.Red);
#endif
                    }
                }
            }

            serverInfo.PingChecked = true;
            serverInfo.Ping = (int)rtt;
        }
        
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            GameMain.TitleScreen.DrawLoadingText = false;
            GameMain.MainMenuScreen.DrawBackground(graphics, spriteBatch);

            spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, GameMain.ScissorTestEnable);
            
            GUI.Draw(Cam, spriteBatch);

            spriteBatch.End();
        }

        public override void AddToGUIUpdateList()
        {
            menu.AddToGUIUpdateList();
        }
        
    }
}
