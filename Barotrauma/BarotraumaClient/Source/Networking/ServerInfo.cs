using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    class ServerInfo
    {
        public string IP;
        public string Port;

        public UInt64 LobbyID;

        public string ServerName;
        public string ServerMessage;
        public bool GameStarted;
        public int PlayerCount;
        public int MaxPlayers;
        public bool HasPassword;

        public bool PingChecked;
        public int Ping = -1;

        //null value means that the value isn't known (the server may be using 
        //an old version of the game that didn't report these values or the FetchRules query to Steam may not have finished yet)
        public bool? UsingWhiteList;
        public SelectionMode? ModeSelectionMode;
        public SelectionMode? SubSelectionMode;
        public bool? AllowSpectating;
        public bool? VoipEnabled;
        public bool? AllowRespawn;
        public YesNoMaybe? TraitorsEnabled;
        public string GameMode;

        public bool? RespondedToSteamQuery = null;

        public string GameVersion;
        public List<string> ContentPackageNames
        {
            get;
            private set;
        } = new List<string>();
        public List<string> ContentPackageHashes
        {
            get;
            private set;
        } = new List<string>();
        public List<string> ContentPackageWorkshopUrls
        {
            get;
            private set;
        } = new List<string>();
        
        public bool ContentPackagesMatch(IEnumerable<ContentPackage> myContentPackages)
        {
            //make sure we have all the packages the server requires
            foreach (string hash in ContentPackageHashes)
            {
                if (!myContentPackages.Any(myPackage => myPackage.MD5hash.Hash == hash)) { return false; }
            }            

            //make sure the server isn't missing any of our packages that cause multiplayer incompatibility
            foreach (ContentPackage myPackage in myContentPackages)
            {
                if (myPackage.HasMultiplayerIncompatibleContent)
                {
                    if (!ContentPackageHashes.Any(hash => hash == myPackage.MD5hash.Hash)) { return false; }
                }
            }

            return true;
        }

        public bool ContentPackagesMatch(IEnumerable<string> myContentPackageHashes)
        {
            HashSet<string> contentPackageHashes = new HashSet<string>(ContentPackageHashes);
            return contentPackageHashes.SetEquals(myContentPackageHashes);
        }

        public void CreatePreviewWindow(GUIFrame frame)
        {
            frame.ClearChildren();

            if (frame == null) return;

            var previewContainer =  new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 1.0f), frame.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            var columnContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), previewContainer.RectTransform))
            {
                Stretch = true
            };

            float elementHeight = 0.075f;

            var title = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), columnContainer.RectTransform), ServerName, font: GUI.LargeFont);
            title.Text = ToolBox.LimitString(title.Text, title.Font, title.Rect.Width);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), columnContainer.RectTransform),
                TextManager.AddPunctuation(':', TextManager.Get("ServerListVersion"), string.IsNullOrEmpty(GameVersion) ? TextManager.Get("Unknown") : GameVersion));

            // left column -----------------------------------------------------------------------------

            //new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnLeft.RectTransform), IP + ":" + Port);

            var serverMsg = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.5f), columnContainer.RectTransform)) { ScrollBarVisible = true };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), serverMsg.Content.RectTransform), ServerMessage, wrap: true) { CanBeFocused = false };

            // right column -----------------------------------------------------------------------------

            /*var playerCount = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListPlayers"));
            new GUITextBlock(new RectTransform(Vector2.One, playerCount.RectTransform), PlayerCount + "/" + MaxPlayers, textAlignment: Alignment.Right);


            new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnRight.RectTransform), "Round running")
            {
                Selected = GameStarted,
                CanBeFocused = false
            };*/

            var gameMode = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnContainer.RectTransform), TextManager.Get("GameMode"));
            new GUITextBlock(new RectTransform(Vector2.One, gameMode.RectTransform),
                TextManager.Get(string.IsNullOrEmpty(GameMode) ? "Unknown" : "GameMode." + GameMode, returnNull: true) ?? GameMode,
                textAlignment: Alignment.Right);

            var traitors = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnContainer.RectTransform), TextManager.Get("Traitors"));

            new GUITextBlock(new RectTransform(Vector2.One, traitors.RectTransform), TextManager.Get(!TraitorsEnabled.HasValue ? "Unknown" : TraitorsEnabled.Value.ToString()), textAlignment: Alignment.Right);

            var subSelection = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnContainer.RectTransform), TextManager.Get("ServerListSubSelection"));
            new GUITextBlock(new RectTransform(Vector2.One, subSelection.RectTransform), TextManager.Get(!SubSelectionMode.HasValue ? "Unknown" : SubSelectionMode.Value.ToString()), textAlignment: Alignment.Right);

            var modeSelection = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnContainer.RectTransform), TextManager.Get("ServerListModeSelection"));
            new GUITextBlock(new RectTransform(Vector2.One, modeSelection.RectTransform), TextManager.Get(!ModeSelectionMode.HasValue ? "Unknown" : ModeSelectionMode.Value.ToString()), textAlignment: Alignment.Right);

            var allowSpectating = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnContainer.RectTransform), TextManager.Get("ServerListAllowSpectating"))
            {
                CanBeFocused = false
            };
            if (!AllowSpectating.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), allowSpectating.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                allowSpectating.Selected = AllowSpectating.Value;

            var allowRespawn = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnContainer.RectTransform), TextManager.Get("ServerSettingsAllowRespawning"))
            {
                CanBeFocused = false
            };
            if (!AllowRespawn.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), allowRespawn.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                allowRespawn.Selected = AllowRespawn.Value;

            var voipEnabledTickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), columnContainer.RectTransform), TextManager.Get("serversettingsvoicechatenabled"))
            {
                CanBeFocused = false
            };
            if (!VoipEnabled.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), voipEnabledTickBox.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                voipEnabledTickBox.Selected = VoipEnabled.Value;

            var usingWhiteList = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnContainer.RectTransform), TextManager.Get("ServerListUsingWhitelist"))
            {
                CanBeFocused = false
            };
            if (!UsingWhiteList.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), usingWhiteList.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                usingWhiteList.Selected = UsingWhiteList.Value;


            columnContainer.RectTransform.SizeChanged += () =>
            {
                GUITextBlock.AutoScaleAndNormalize(allowSpectating.TextBlock, allowRespawn.TextBlock, voipEnabledTickBox.TextBlock, usingWhiteList.TextBlock);
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), columnContainer.RectTransform),
                TextManager.Get("ServerListContentPackages"));

            var contentPackageList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), columnContainer.RectTransform)) { ScrollBarVisible = true };
            if (ContentPackageNames.Count == 0)
            {
                new GUITextBlock(new RectTransform(Vector2.One, contentPackageList.Content.RectTransform), TextManager.Get("Unknown"), textAlignment: Alignment.Center)
                {
                    CanBeFocused = false
                };
            }
            else
            {
                List<string> availableWorkshopUrls = new List<string>();
                for (int i = 0; i < ContentPackageNames.Count; i++)
                {
                    var packageText = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.15f), contentPackageList.Content.RectTransform) { MinSize = new Point(0, 15) },
                        ContentPackageNames[i])
                    {
                        Enabled = false
                    };
                    if (i < ContentPackageHashes.Count)
                    {
                        if (GameMain.Config.SelectedContentPackages.Any(cp => cp.MD5hash.Hash == ContentPackageHashes[i]))
                        {
                            packageText.Selected = true;
                            continue;
                        }

                        //matching content package found, but it hasn't been enabled
                        if (ContentPackage.List.Any(cp => cp.MD5hash.Hash == ContentPackageHashes[i]))
                        {
                            packageText.TextColor = Color.Orange;
                            packageText.ToolTip = TextManager.GetWithVariable("ServerListContentPackageNotEnabled", "[contentpackage]", ContentPackageNames[i]);
                        }
                        //workshop download link found
                        else if (i < ContentPackageWorkshopUrls.Count && !string.IsNullOrEmpty(ContentPackageWorkshopUrls[i]))
                        {
                            availableWorkshopUrls.Add(ContentPackageWorkshopUrls[i]);
                            packageText.TextColor = Color.Yellow;
                            packageText.ToolTip = TextManager.GetWithVariable("ServerListIncompatibleContentPackageWorkshopAvailable", "[contentpackage]", ContentPackageNames[i]);
                        }
                        else //no package or workshop download link found, tough luck
                        {
                            packageText.TextColor = Color.Red;
                            packageText.ToolTip = TextManager.GetWithVariables("ServerListIncompatibleContentPackage",
                                new string[2] { "[contentpackage]", "[hash]" }, new string[2] { ContentPackageNames[i], ContentPackageHashes[i] });
                        }
                    }
                }
                if (availableWorkshopUrls.Count > 0)
                {
                    var workshopBtn = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), columnContainer.RectTransform), TextManager.Get("ServerListSubscribeMissingPackages"))
                    {
                        ToolTip = TextManager.Get(SteamManager.IsInitialized ? "ServerListSubscribeMissingPackagesTooltip" : "ServerListSubscribeMissingPackagesTooltipNoSteam"),
                        Enabled = SteamManager.IsInitialized,
                        OnClicked = (btn, userdata) =>
                        {
                            GameMain.SteamWorkshopScreen.SubscribeToPackages(availableWorkshopUrls);
                            GameMain.SteamWorkshopScreen.Select();
                            return true;
                        }
                    };
                    workshopBtn.TextBlock.AutoScale = true;
                }
            }

            // -----------------------------------------------------------------------------

            foreach (GUIComponent c in columnContainer.Children)
            {
                if (c is GUITextBlock textBlock) textBlock.Padding = Vector4.Zero;
            }
        }
    }
}
