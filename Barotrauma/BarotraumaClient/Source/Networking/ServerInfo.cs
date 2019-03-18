using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class ServerInfo
    {
        public void CreatePreviewWindow(GUIMessageBox messageBox)
        {
            var title = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), messageBox.Content.RectTransform), ServerName, textAlignment: Alignment.Center, font: GUI.LargeFont, wrap: true);

            var serverMsg = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.2f), messageBox.Content.RectTransform));
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), serverMsg.Content.RectTransform), ServerMessage, wrap: true);

            var columnContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), messageBox.Content.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            var columnLeft = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), columnContainer.RectTransform))
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };
            var columnRight = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), columnContainer.RectTransform))
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            float elementHeight = 0.1f;

            // left column -----------------------------------------------------------------------------

            //new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnLeft.RectTransform), IP + ":" + Port);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnLeft.RectTransform),
                TextManager.Get("ServerListVersion") + ": " + (string.IsNullOrEmpty(GameVersion) ? TextManager.Get("Unknown") : GameVersion));
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), columnLeft.RectTransform),
                TextManager.Get("ServerListContentPackages"));

            var contentPackageList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.7f), columnLeft.RectTransform));
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
                            packageText.ToolTip = TextManager.Get("ServerListContentPackageNotEnabled")
                                .Replace("[contentpackage]", ContentPackageNames[i]);
                        }
                        //workshop download link found
                        else if (i < ContentPackageWorkshopUrls.Count && !string.IsNullOrEmpty(ContentPackageWorkshopUrls[i]))
                        {
                            availableWorkshopUrls.Add(ContentPackageWorkshopUrls[i]);
                            packageText.TextColor = Color.Yellow;
                            packageText.ToolTip = TextManager.Get("ServerListIncompatibleContentPackageWorkshopAvailable")
                                .Replace("[contentpackage]", ContentPackageNames[i]);
                        }
                        else //no package or workshop download link found, tough luck
                        {
                            packageText.TextColor = Color.Red;
                            packageText.ToolTip = TextManager.Get("ServerListIncompatibleContentPackage")
                                .Replace("[contentpackage]", ContentPackageNames[i])
                                .Replace("[hash]", ContentPackageHashes[i]);
                        }
                    }
                }
                if (availableWorkshopUrls.Count > 0 )
                {
                    var workshopBtn = new GUIButton(new RectTransform(new Vector2(1.0f, 0.15f), columnLeft.RectTransform), TextManager.Get("ServerListSubscribeMissingPackages"))
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
            
            // right column -----------------------------------------------------------------------------

            var playerCount = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListPlayers"));
            new GUITextBlock(new RectTransform(Vector2.One, playerCount.RectTransform), PlayerCount + "/" + MaxPlayers, textAlignment: Alignment.Right);


            new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnRight.RectTransform), "Round running")
            {
                Selected = GameStarted,
                CanBeFocused = false
            };

            var gameMode = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("GameMode"));
            new GUITextBlock(new RectTransform(Vector2.One, gameMode.RectTransform), string.IsNullOrEmpty(GameMode) ? "Unknown" : GameMode, textAlignment: Alignment.Right);

           var traitors = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("Traitors"));
            new GUITextBlock(new RectTransform(Vector2.One, traitors.RectTransform), !TraitorsEnabled.HasValue ? "Unknown" : TraitorsEnabled.Value.ToString(), textAlignment: Alignment.Right);


            var subSelection = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListSubSelection"));
            new GUITextBlock(new RectTransform(Vector2.One, subSelection.RectTransform), !SubSelectionMode.HasValue ? "Unknown" : SubSelectionMode.Value.ToString(), textAlignment: Alignment.Right);

            var modeSelection = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListModeSelection"));
            new GUITextBlock(new RectTransform(Vector2.One, modeSelection.RectTransform), (!ModeSelectionMode.HasValue ? "Unknown" : ModeSelectionMode.Value.ToString()), textAlignment: Alignment.Right);

            var allowSpectating = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListAllowSpectating"))
            {
                CanBeFocused = false
            };
            if (!AllowSpectating.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), allowSpectating.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                allowSpectating.Selected = AllowSpectating.Value;

            var allowRespawn = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnRight.RectTransform), "Allow respawn")
            {
                CanBeFocused = false
            };
            if (!AllowRespawn.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), allowRespawn.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                allowRespawn.Selected = AllowRespawn.Value;

            new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListHasPassword"))
            {
                Selected = HasPassword,
                CanBeFocused = false
            };

            var usingWhiteList = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListUsingWhitelist"))
            {
                CanBeFocused = false
            };
            if (!UsingWhiteList.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), usingWhiteList.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                usingWhiteList.Selected = UsingWhiteList.Value;

            // -----------------------------------------------------------------------------

            foreach (GUIComponent c in columnLeft.Children)
            {
                if (c is GUITextBlock textBlock) textBlock.Padding = Vector4.Zero;
            }
            foreach (GUIComponent c in columnRight.Children)
            {
                if (c is GUITextBlock textBlock) textBlock.Padding = Vector4.Zero;
            }
        }
    }
}
