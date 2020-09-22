using Barotrauma.IO;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Barotrauma
{
    class SteamWorkshopScreen : Screen
    {
        private GUIFrame menu;
        private GUIListBox subscribedItemList, topItemList;
        private GUITextBox subscribedItemFilter, topItemFilter;

        private GUIListBox publishedItemList, myItemList;

        //shows information of a selected workshop item
        private GUIFrame modsPreviewFrame, browsePreviewFrame;

        //menu for creating new items
        private GUIFrame createItemFrame;
        //listbox that shows the files included in the item being created
        private GUIListBox createItemFileList;

        private System.IO.FileSystemWatcher createItemWatcher;

        private readonly List<GUIButton> tabButtons = new List<GUIButton>();

        private class PendingPreviewImageDownload
        {
            /// <summary>
            /// Was the image downloaded
            /// </summary>
            public bool Downloaded = false;

            /// <summary>
            /// How many tasks are looking to create a preview image based on this download
            /// </summary>
            public int PendingLoads = 1;
        }
        private readonly Dictionary<ulong, PendingPreviewImageDownload> pendingPreviewImageDownloads = new Dictionary<ulong, PendingPreviewImageDownload>();
        private readonly Dictionary<string, Sprite> itemPreviewSprites = new Dictionary<string, Sprite>();

        private enum Tab
        {
            Mods,
            Browse,
            Publish
        }

        private GUIComponent[] tabs;

        private ContentPackage itemContentPackage;
        private Steamworks.Ugc.Editor? itemEditor;

        private enum VisibilityType
        {
            Public,
            FriendsOnly,
            Private
        }

        public SteamWorkshopScreen()
        {
            GameMain.Instance.ResolutionChanged += CreateUI;
            CreateUI();

            Steamworks.SteamUGC.GlobalOnItemInstalled += OnItemInstalled;
        }
        
        private void CreateUI()
        {
            tabs = new GUIComponent[Enum.GetValues(typeof(Tab)).Length];
            menu = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), GUI.Canvas, Anchor.Center) { MinSize = new Point(GameMain.GraphicsHeight, 0) });

            var container = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), menu.RectTransform, Anchor.Center)) { Stretch = true };
            var topButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.03f), container.RectTransform), isHorizontal: true);
            
            foreach (Tab tab in Enum.GetValues(typeof(Tab)))
            {
                GUIButton tabButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), topButtonContainer.RectTransform),
                    TextManager.Get(tab.ToString() + "Tab"), style: "GUITabButton")
                {
                    UserData = tab,
                    OnClicked = (btn, userData) =>
                    {
                        SelectTab((Tab)userData); return true;
                    }
                };
                tabButtons.Add(tabButton);
            }
            topButtonContainer.RectTransform.MinSize = new Point(0, topButtonContainer.RectTransform.Children.Max(c => c.MinSize.Y));
            topButtonContainer.RectTransform.MaxSize = new Point(int.MaxValue, topButtonContainer.RectTransform.MinSize.Y);

            var tabContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.7f), container.RectTransform), style: "InnerFrame");

            var bottomButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), container.RectTransform), isHorizontal: true);
            GUIButton backButton = new GUIButton(new RectTransform(new Vector2(0.1f, 0.9f), bottomButtonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("Back"))
            {
                OnClicked = GameMain.MainMenuScreen.ReturnToMainMenu
            };
            backButton.SelectedColor = backButton.Color;
            topButtonContainer.RectTransform.MinSize = new Point(0, backButton.RectTransform.MinSize.Y);
            topButtonContainer.RectTransform.MaxSize = new Point(int.MaxValue, backButton.RectTransform.MinSize.Y);

            //-------------------------------------------------------------------------------
            //Subscribed Mods tab
            //-------------------------------------------------------------------------------

            tabs[(int)Tab.Mods] = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.95f), tabContainer.RectTransform, Anchor.Center), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var modsContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), tabs[(int)Tab.Mods].RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            subscribedItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), modsContainer.RectTransform))
            {
                ScrollBarVisible = true,
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    if (GUI.MouseOn is GUIButton || GUI.MouseOn?.Parent is GUIButton) { return false; }
                    ShowItemPreview(userdata as Steamworks.Ugc.Item?, modsPreviewFrame);
                    return true;
                }
            };

            subscribedItemFilter = CreateFilterBox(modsContainer, subscribedItemList);

            modsPreviewFrame = new GUIFrame(new RectTransform(new Vector2(0.6f, 1.0f), tabs[(int)Tab.Mods].RectTransform, Anchor.TopRight), style: null);

            //-------------------------------------------------------------------------------
            //Popular Mods tab
            //-------------------------------------------------------------------------------

            tabs[(int)Tab.Browse] = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.95f), tabContainer.RectTransform, Anchor.Center), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var listContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), tabs[(int)Tab.Browse].RectTransform), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            topItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.9f), listContainer.RectTransform))
            {
                ScrollBarVisible = true,
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    ShowItemPreview(userdata as Steamworks.Ugc.Item?, browsePreviewFrame);
                    return true;
                }
            };

            topItemFilter = CreateFilterBox(listContainer, topItemList);

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.02f), listContainer.RectTransform), TextManager.Get("FindModsButton"), style: "GUIButtonSmall")
            {
                OnClicked = (btn, userdata) =>
                {
                    SteamManager.OverlayCustomURL("steam://url/SteamWorkshopPage/" + SteamManager.AppID);
                    return true;
                }
            };

            browsePreviewFrame = new GUIFrame(new RectTransform(new Vector2(0.6f, 1.0f), tabs[(int)Tab.Browse].RectTransform, Anchor.TopRight), style: null);

            //-------------------------------------------------------------------------------
            //Publish tab
            //-------------------------------------------------------------------------------

            tabs[(int)Tab.Publish] = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.95f), tabContainer.RectTransform, Anchor.Center), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), tabs[(int)Tab.Publish].RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("PublishedWorkshopItems"), font: GUI.SubHeadingFont);
            publishedItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), leftColumn.RectTransform))
            {
                OnSelected = (component, userdata) =>
                {
                    if (GUI.MouseOn is GUIButton || GUI.MouseOn?.Parent is GUIButton) { return false; }
                    if (GUI.MouseOn is GUITickBox || GUI.MouseOn?.Parent is GUITickBox) { return false; }
                    myItemList.Deselect();
                    if (userdata is Steamworks.Ugc.Item?)
                    {
                        var item = userdata as Steamworks.Ugc.Item?;
                        if (!(item?.IsInstalled ?? false)) { return false; }
                        if (CreateWorkshopItem(item)) { ShowCreateItemFrame(); }
                    }
                    return true;
                }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("YourWorkshopItems"), font: GUI.SubHeadingFont);
            myItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), leftColumn.RectTransform))
            {
                OnSelected = (component, userdata) =>
                {
                    if (GUI.MouseOn is GUIButton || GUI.MouseOn?.Parent is GUIButton) { return false; }
                    publishedItemList.Deselect();
                    if (userdata is SubmarineInfo sub)
                    {
                        CreateWorkshopItem(sub);
                    }
                    else if (userdata is ContentPackage contentPackage)
                    {
                        CreateWorkshopItem(contentPackage);
                    }
                    ShowCreateItemFrame();
                    return true;
                }
            };

            createItemFrame = new GUIFrame(new RectTransform(new Vector2(0.58f, 1.0f), tabs[(int)Tab.Publish].RectTransform, Anchor.TopRight), style: null);

            SelectTab(Tab.Mods);

            CoroutineManager.StartCoroutine(PollSubscribedItems());
        }

        private GUITextBox CreateFilterBox(GUIComponent parent, GUIListBox listbox)
        {
            var filterContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            filterContainer.RectTransform.SetAsFirstChild();
            var searchTitle = new GUITextBlock(new RectTransform(new Vector2(0.001f, 1.0f), filterContainer.RectTransform), TextManager.Get("serverlog.filter"), textAlignment: Alignment.CenterLeft, font: GUI.Font);
            var searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 1.0f), filterContainer.RectTransform, Anchor.CenterRight), font: GUI.Font, createClearButton: true);
            filterContainer.RectTransform.MinSize = searchBox.RectTransform.MinSize;
            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = true; };
            searchBox.OnTextChanged += (textBox, text) =>
            {
                foreach (GUIComponent child in listbox.Content.Children)
                {
                    if (!(child.UserData is Steamworks.Ugc.Item item)) { continue; }
                    child.Visible = string.IsNullOrEmpty(text) ? true : (item.Title?.ToLower().Contains(text.ToLower()) ?? false);
                }
                return true;
            };

            return searchBox;
        }

        public override void Select()
        {
            base.Select();

            modsPreviewFrame.ClearChildren();
            browsePreviewFrame.ClearChildren();
            createItemFrame.ClearChildren();
            itemContentPackage = null;
            itemEditor = null;

            SelectTab(Tab.Mods);
        }

        private void OnItemInstalled(ulong itemId)
        {
            RefreshSubscribedItems();
        }

        float subscribePollAdditionalWait = 0.0f;

        private IEnumerable<object> PollSubscribedItems()
        {
            if (!SteamManager.IsInitialized) { yield return CoroutineStatus.Success; }

            uint numSubscribed = 0;
            while (true)
            {
                while (CoroutineManager.IsCoroutineRunning("Load")) { yield return new WaitForSeconds(1.0f); }
                while (subscribePollAdditionalWait > 0.01f)
                {
                    subscribePollAdditionalWait = Math.Min(subscribePollAdditionalWait, 3.0f);
                    float wait = subscribePollAdditionalWait;
                    yield return new WaitForSeconds(wait);
                    subscribePollAdditionalWait -= wait;
                }
                uint newNumSubscribed = Steamworks.SteamUGC.NumSubscribedItems;
                if (newNumSubscribed != numSubscribed)
                {
                    RefreshSubscribedItems();
                    numSubscribed = newNumSubscribed;
                }

                yield return new WaitForSeconds(1.0f);
            }
        }

        private void SelectTab(Tab tab)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                tabButtons[i].Selected = tabs[i].Visible = i == (int)tab;
            }

            if (createItemFrame.CountChildren == 0)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.9f), createItemFrame.RectTransform, Anchor.Center), 
                    TextManager.Get("WorkshopItemCreateHelpText"), wrap: true)
                {
                    CanBeFocused = false
                };
            }

            createItemWatcher?.Dispose(); createItemWatcher = null;
            if (Screen.Selected == this)
            {
                switch (tab)
                {
                    case Tab.Mods:
                        RefreshSubscribedItems();
                        break;
                    case Tab.Browse:
                        RefreshPopularItems();
                        break;
                    case Tab.Publish:
                        RefreshPublishedItems();
                        break;
                }
            }
        }

        public IEnumerable<object> RefreshDownloadState()
        {
            bool isDownloading = true;
            while (true)
            {
                SteamManager.GetSubscribedWorkshopItems((items) =>
                {
                    isDownloading = items.Any(it => it.IsDownloading || it.IsDownloadPending);

                    GameMain.MainMenuScreen.SetDownloadingModsNotification(isDownloading);
                });

                if (!isDownloading) { break; }

                yield return new WaitForSeconds(0.5f);
            }
            yield return CoroutineStatus.Success;
        }

        private void RefreshSubscribedItems()
        {
            SteamManager.GetSubscribedWorkshopItems((items) =>
            {
                //filter out the items published by the player (they're shown in the publish tab)
                var mySteamID = SteamManager.GetSteamID();
                OnItemsReceived(GetVisibleItems(items.Where(it => it.Owner.Id != mySteamID)), subscribedItemList);

                GameMain.MainMenuScreen.SetDownloadingModsNotification(items.Any(it => it.IsDownloading || it.IsDownloadPending));
            });
        }

        private void RefreshPopularItems()
        {
            SteamManager.GetPopularWorkshopItems((items) => { OnItemsReceived(GetVisibleItems(items), topItemList); }, 20);
        }

        private void RefreshPublishedItems()
        {
            SteamManager.GetPublishedWorkshopItems((items) => { OnItemsReceived(items, publishedItemList); });
            RefreshMyItemList();
        }

        private IEnumerable<Steamworks.Ugc.Item> GetVisibleItems(IEnumerable<Steamworks.Ugc.Item> items)
        {
#if UNSTABLE
            //show everything in Unstable
            return items;
#else
            //hide Unstable items in normal version
            return items.Where(it => !it.HasTag("unstable"));
#endif
        }

        private void RefreshMyItemList()
        {
            myItemList.ClearChildren();
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), myItemList.Content.RectTransform), TextManager.Get("WorkshopLabelSubmarines"), 
                textAlignment: Alignment.CenterLeft, font: GUI.SubHeadingFont)
            {
                CanBeFocused = false
            };
            foreach (SubmarineInfo sub in SubmarineInfo.SavedSubmarines)
            {
                if (sub.HasTag(SubmarineTag.HideInMenus)) { continue; }
                string subPath = Path.GetFullPath(sub.FilePath);

                //ignore subs that are part of the vanilla content package
                if (GameMain.VanillaContent != null &&
                    GameMain.VanillaContent.GetFilesOfType(ContentType.Submarine).Any(s => Path.GetFullPath(s) == subPath))
                {
                    continue;
                }
                //ignore subs that are part of a workshop content package
                if (ContentPackage.AllPackages.Any(cp => cp.SteamWorkshopId != 0 &&
                    cp.Files.Any(f => f.Type == ContentType.Submarine && Path.GetFullPath(f.Path) == subPath)))
                {
                    continue;
                }
                //ignore subs that are defined in a content package with more files than just the sub 
                //(these will be listed in the "content packages" section)
                if (ContentPackage.AllPackages.Any(cp => cp.Files.Count > 1 &&
                    cp.Files.Any(f => f.Type == ContentType.Submarine && Path.GetFullPath(f.Path) == subPath)))
                {
                    continue;
                }

                CreateMyItemFrame(sub, myItemList);
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), myItemList.Content.RectTransform), TextManager.Get("WorkshopLabelContentPackages"), 
                textAlignment: Alignment.CenterLeft, font: GUI.SubHeadingFont)
            {
                CanBeFocused = false
            };
            foreach (ContentPackage contentPackage in ContentPackage.AllPackages)
            {
                if (contentPackage.SteamWorkshopId != 0 || contentPackage.HideInWorkshopMenu) { continue; }
                if (contentPackage == GameMain.VanillaContent) { continue; }
                //don't list content packages that only define one sub (they're visible in the "Submarines" section)
                if (contentPackage.Files.Count == 1 && contentPackage.Files[0].Type == ContentType.Submarine) { continue; }
                CreateMyItemFrame(contentPackage, myItemList);
            }
        }

        private void OnItemsReceived(IEnumerable<Steamworks.Ugc.Item> itemDetails, GUIListBox listBox)
        {
            CrossThread.RequestExecutionOnMainThread(() =>
            {
                listBox.ClearChildren();
                foreach (var item in itemDetails)
                {
                    CreateWorkshopItemFrame(item, listBox);
                }

                if (itemDetails.Count() == 0 && listBox == subscribedItemList)
                {
                    new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.9f), listBox.Content.RectTransform, Anchor.Center), TextManager.Get("NoSubscribedMods"), wrap: true)
                    {
                        CanBeFocused = false
                    };
                }
            });
        }

        private void CreateWorkshopItemFrame(Steamworks.Ugc.Item? item, GUIListBox listBox)
        {
            if (string.IsNullOrEmpty(item?.Title))
            {
                return;
            }

            string text = string.Empty;
            if (listBox == subscribedItemList)
            {
                text = subscribedItemFilter.Text;
            }
            else if (listBox == topItemList)
            {
                text = topItemFilter.Text;
            }

            bool visible = string.IsNullOrEmpty(text) || (item?.Title?.ToLower().Contains(text.ToLower()) ?? false);

            int prevIndex = -1;
            var existingFrame = listBox.Content.FindChild((component) => { return (component.UserData is Steamworks.Ugc.Item?) && (component.UserData as Steamworks.Ugc.Item?)?.Id == item?.Id; });
            if (existingFrame != null)
            {
                prevIndex = listBox.Content.GetChildIndex(existingFrame);
                listBox.Content.RemoveChild(existingFrame);
            }

            var itemFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), listBox.Content.RectTransform, minSize: new Point(0, 80)),
                    style: "ListBoxElement")
            {
                UserData = item,
                Visible = visible
            };
            if (prevIndex > -1)
            {
                itemFrame.RectTransform.RepositionChildInHierarchy(prevIndex);
            }

            var innerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), itemFrame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                CanBeFocused = false,
                Stretch = true
            };

            int iconSize = innerFrame.Rect.Height;
            if (itemPreviewSprites.ContainsKey(item?.PreviewImageUrl))
            {
                new GUIImage(new RectTransform(new Point(iconSize), innerFrame.RectTransform), itemPreviewSprites[item?.PreviewImageUrl], scaleToFit: true)
                {
                    UserData = "previewimage",
                    CanBeFocused = false
                };
            }
            else if (Screen.Selected == this)
            {
                new GUIImage(new RectTransform(new Point(iconSize), innerFrame.RectTransform), SteamManager.DefaultPreviewImage, scaleToFit: true)
                {
                    UserData = "previewimage",
                    CanBeFocused = false
                };
                try
                {
                    if (!string.IsNullOrEmpty(item?.PreviewImageUrl))
                    {
                        string imagePreviewPath = Path.Combine(SteamManager.WorkshopItemPreviewImageFolder, item?.Id + ".png");
                        
                        bool isNewImage;
                        lock (pendingPreviewImageDownloads)
                        {
                            isNewImage = !pendingPreviewImageDownloads.ContainsKey(item.Value.Id);
                            if (isNewImage)
                            {
                                if (File.Exists(imagePreviewPath))
                                {
                                    File.Delete(imagePreviewPath);
                                }

                                pendingPreviewImageDownloads.Add(item.Value.Id, new PendingPreviewImageDownload());
                            }
                        }

                        if (isNewImage)
                        {
                            Directory.CreateDirectory(SteamManager.WorkshopItemPreviewImageFolder);

                            Uri baseAddress = new Uri(item?.PreviewImageUrl);
                            Uri directory = new Uri(baseAddress, "."); // "." == current dir, like MS-DOS
                            string fileName = Path.GetFileName(baseAddress.LocalPath);

                            IRestClient client = new RestClient(directory);
                            var request = new RestRequest(fileName, Method.GET);
                            client.ExecuteAsync(request, response =>
                            {
                                OnPreviewImageDownloaded(response, imagePreviewPath,
                                    () =>
                                    {
                                        lock (pendingPreviewImageDownloads)
                                        {
                                            pendingPreviewImageDownloads[item.Value.Id].Downloaded = true;
                                        }
                                        CoroutineManager.StartCoroutine(WaitForItemPreviewDownloaded(item, listBox, imagePreviewPath));
                                    });
                            });                            
                        }
                        else
                        {
                            lock (pendingPreviewImageDownloads)
                            {
                                pendingPreviewImageDownloads[item.Value.Id].PendingLoads++;
                            }
                            CoroutineManager.StartCoroutine(WaitForItemPreviewDownloaded(item, listBox, imagePreviewPath));
                        }
                    }
                }
                catch (Exception e)
                {
                    lock (pendingPreviewImageDownloads)
                    {
                        pendingPreviewImageDownloads.Remove(item.Value.Id);
                    }
                    DebugConsole.ThrowError("Downloading the preview image of the Workshop item \"" + item?.Title + "\" failed.", e);
                }
            }

            var rightColumn = new GUILayoutGroup(new RectTransform(new Point(innerFrame.Rect.Width - iconSize, innerFrame.Rect.Height), innerFrame.RectTransform), childAnchor: Anchor.CenterLeft)
            {
                IsHorizontal = true,
                Stretch = true,
                RelativeSpacing = 0.05f,
                CanBeFocused = false
            };

            var titleText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), rightColumn.RectTransform), item?.Title, textAlignment: Alignment.CenterLeft, wrap: true)
            {
                UserData = "titletext",
                CanBeFocused = false
            };

            if ((item?.IsSubscribed ?? false) && (item?.IsInstalled ?? false) && Directory.Exists(item?.Directory))
            {
                bool installed = SteamManager.CheckWorkshopItemInstalled(item);

                if (!installed)
                {
                    bool? compatible = SteamManager.CheckWorkshopItemCompatibility(item);
                    if (compatible.HasValue && !compatible.Value)
                    {
                        new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.3f), rightColumn.RectTransform),
                            TextManager.Get("WorkshopItemIncompatible"), textColor: GUI.Style.Red)
                        {
                            ToolTip = TextManager.Get("WorkshopItemIncompatibleTooltip")
                        };
                    }
                    else
                    {
                        installed = SteamManager.InstallWorkshopItem(item, out string errorMsg, Selected == this);
                        if (!installed)
                        {
                            DebugConsole.NewMessage(errorMsg, Color.Red);
                            titleText.TextColor = Color.Red;
                            titleText.ToolTip = itemFrame.ToolTip = TextManager.GetWithVariables("WorkshopItemUpdateFailed", new string[2] { "[itemname]", "[errormessage]" }, new string[2] { item?.Title, errorMsg });
                        }
                    }
                }

                if (installed)
                {
                    bool upToDate = SteamManager.CheckWorkshopItemUpToDate(item);
                    
                    if (!upToDate)
                    {
                        if (!SteamManager.UpdateWorkshopItem(item, out string errorMsg))
                        {
                            DebugConsole.NewMessage(errorMsg, Color.Red);
                            titleText.TextColor = Color.Red;
                            titleText.ToolTip = itemFrame.ToolTip = TextManager.GetWithVariables("WorkshopItemUpdateFailed", new string[2] { "[itemname]", "[errormessage]" }, new string[2] { item?.Title, errorMsg });
                        }
                    }
                }

            }
            else if (item?.IsDownloading ?? false)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), rightColumn.RectTransform), TextManager.Get("WorkshopItemDownloading"));
            }
            else if (item?.IsDownloadPending ?? false)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), rightColumn.RectTransform), TextManager.Get("WorkshopItemDownloadPending"));
            }
            else if (!(item?.IsSubscribed ?? false) && (listBox != subscribedItemList))
            {
                var downloadBtn = new GUIButton(new RectTransform(new Point((int)(32 * GUI.Scale)), rightColumn.RectTransform), "", style: "GUIPlusButton")
                {
                    ToolTip = TextManager.Get("DownloadButton"),
                    ForceUpperCase = true,
                    UserData = item
                };
                downloadBtn.OnClicked = (btn, userdata) => { DownloadItem(itemFrame, downloadBtn, item); return true; };
            }

            if ((item?.IsSubscribed ?? false) && listBox == subscribedItemList)
            {
                var reinstallBtn = new GUIButton(new RectTransform(new Point((int)(32 * GUI.Scale)), rightColumn.RectTransform), "", style: "GUIReloadButton")
                {
                    ToolTip = TextManager.Get("WorkshopItemReinstall"),
                    ForceUpperCase = true,
                    UserData = "reinstall"
                };
                reinstallBtn.OnClicked = (btn, userdata) =>
                {
                    var elem = subscribedItemList.Content.GetChildByUserData(item);
                    try
                    {
                        bool reselect = GameMain.Config.AllEnabledPackages.Any(cp => cp.SteamWorkshopId != 0 && cp.SteamWorkshopId == item?.Id);
                        if (!SteamManager.UninstallWorkshopItem(item, false, out string errorMsg) ||
                            !SteamManager.InstallWorkshopItem(item, out errorMsg, reselect, true))
                        {
                            DebugConsole.ThrowError($"Failed to reinstall \"{item?.Title}\": {errorMsg}", null, true);
                            elem.Flash(GUI.Style.Red);
                        }
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError($"Failed to reinstall \"{item?.Title}\"", e, true);
                        elem.Flash(GUI.Style.Red);
                    }
                    return true;
                };
                var unsubBtn = new GUIButton(new RectTransform(new Point((int)(32 * GUI.Scale)), rightColumn.RectTransform), "", style: "GUIMinusButton")
                {
                    ToolTip = TextManager.Get("WorkshopItemUnsubscribe"),
                    ForceUpperCase = true,
                    UserData = "unsubscribe"
                };
                unsubBtn.OnClicked = (btn, userdata) =>
                {
                    subscribePollAdditionalWait += 1.0f;
                    item?.Unsubscribe();
                    SteamManager.UninstallWorkshopItem(item, true, out _);
                    subscribedItemList.RemoveChild(subscribedItemList.Content.GetChildByUserData(item));
                    return true;
                };
            }

            innerFrame.Recalculate();
            listBox.RecalculateChildren();
        }

        public void SetReinstallButtonStatus(Steamworks.Ugc.Item? item, bool enabled, Color? flashColor)
        {
            var child = subscribedItemList.Content.FindChild((component) => { return (component.UserData is Steamworks.Ugc.Item?) && (component.UserData as Steamworks.Ugc.Item?)?.Id == item?.Id; });
            if (child != null)
            {
                var reinstallBtn = child.FindChild("reinstall", true);
                if (reinstallBtn != null) { reinstallBtn.Enabled = enabled; }
                var unsubBtn = child.FindChild("unsubscribe", true);
                if (unsubBtn != null) { unsubBtn.Enabled = enabled; }
                if (flashColor.HasValue) { child.Flash(flashColor); }
            }
        }

        private void RemoveItemFromLists(ulong itemID)
        {
            RemoveItemFromList(publishedItemList);
            RemoveItemFromList(subscribedItemList);
            RemoveItemFromList(topItemList);

            void RemoveItemFromList(GUIListBox listBox)
            {
                listBox.Content.RemoveChild(
                    listBox.Content.Children.FirstOrDefault(c => c.UserData is Steamworks.Ugc.Item? && (c.UserData as Steamworks.Ugc.Item?)?.Id == itemID));
            }
        }

        private void CreateMyItemFrame(SubmarineInfo submarine, GUIListBox listBox)
        {
            var itemFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), listBox.Content.RectTransform, minSize: new Point(0, 80)),
                    style: "ListBoxElement")
            {
                UserData = submarine
            };
            var innerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), itemFrame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                RelativeSpacing = 0.1f,
                Stretch = true
            };
            if (submarine.PreviewImage != null)
            {
                new GUIImage(new RectTransform(new Point(innerFrame.Rect.Height), innerFrame.RectTransform), submarine.PreviewImage, scaleToFit: true);
            }
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), innerFrame.RectTransform), submarine.Name, textAlignment: Alignment.CenterLeft);
        }
        private void CreateMyItemFrame(ContentPackage contentPackage, GUIListBox listBox)
        {
            var itemFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), listBox.Content.RectTransform, minSize: new Point(0, 80)),
                    style: "ListBoxElement")
            {
                UserData = contentPackage
            };
            var innerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), itemFrame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                RelativeSpacing = 0.1f,
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), innerFrame.RectTransform), contentPackage.Name, textAlignment: Alignment.CenterLeft);
        }

        private void OnPreviewImageDownloaded(IRestResponse response, string previewImagePath, Action action)
        {
            if (response.ResponseStatus == ResponseStatus.Completed)
            {
                TaskPool.Add("WritePreviewImageAsync", WritePreviewImageAsync(response, previewImagePath), (task) => { action?.Invoke(); });
            }
        }

        private async Task WritePreviewImageAsync(IRestResponse response, string previewImagePath)
        {
            await Task.Yield();
            try
            {
                File.WriteAllBytes(previewImagePath, response.RawBytes);
            }
            catch (Exception e)
            {
                string errorMsg = "Failed to save workshop item preview image to \"" + previewImagePath + "\".";
                GameAnalyticsManager.AddErrorEventOnce("SteamWorkshopScreen.OnItemPreviewDownloaded:WriteAllBytesFailed" + previewImagePath,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg + "\n" + e.Message);
                return;
            }
        }

        private IEnumerable<object> WaitForItemPreviewDownloaded(Steamworks.Ugc.Item? item, GUIListBox listBox, string previewImagePath)
        {
            while (true)
            {
                lock (pendingPreviewImageDownloads)
                {
                    if (pendingPreviewImageDownloads[item.Value.Id].Downloaded){ break; }
                }

                yield return new WaitForSeconds(0.2f);
            }

            if (File.Exists(previewImagePath))
            {
                TaskPool.Add("LoadPreviewImageAsync", LoadPreviewImageAsync(item?.PreviewImageUrl, previewImagePath),
                new Tuple<Steamworks.Ugc.Item?, GUIListBox>(item, listBox),
                (task, tuple) =>
                {
                    //must be done in the main thread because creating/removing GUI elements is not thread-safe
                    CrossThread.RequestExecutionOnMainThread(() =>
                    {
                        (var it, var lb) = tuple;
                        if (lb.Content.FindChild(item)?.GetChildByUserData("previewimage") is GUIImage previewImage)
                        {
                            previewImage.Sprite = ((Task<Sprite>)task).Result;
                        }
                        else
                        {
                            CreateWorkshopItemFrame(it, lb);
                        }

                        if (modsPreviewFrame.FindChild(it) != null)
                        {
                            ShowItemPreview(it, modsPreviewFrame);
                        }
                        if (browsePreviewFrame.FindChild(item) != null)
                        {
                            ShowItemPreview(it, browsePreviewFrame);
                        }

                        lock (pendingPreviewImageDownloads)
                        {
                            pendingPreviewImageDownloads[it.Value.Id].PendingLoads--;
                            if (pendingPreviewImageDownloads[it.Value.Id].PendingLoads <= 0) { pendingPreviewImageDownloads.Remove(it.Value.Id); }
                        }
                    });
                });
            }

            yield return CoroutineStatus.Success;
        }

        private async Task<Sprite> LoadPreviewImageAsync(string previewImageUrl, string previewImagePath)
        {
            await Task.Yield();
            lock (itemPreviewSprites)
            {
                if (itemPreviewSprites.ContainsKey(previewImageUrl))
                {
                    return itemPreviewSprites[previewImageUrl];
                }
                else
                {
                    Sprite newSprite = new Sprite(previewImagePath, sourceRectangle: null);
                    itemPreviewSprites.Add(previewImageUrl, newSprite);
                    return newSprite;
                }
            }
        }

        private bool DownloadItem(GUIComponent frame, GUIButton downloadButton, Steamworks.Ugc.Item? item)
        {
            if (item == null) { return false; }

            var parentElement = downloadButton.Parent;
            parentElement.RemoveChild(downloadButton);
            var textBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), parentElement.RectTransform), TextManager.Get("WorkshopItemDownloading"));

            SteamManager.SubscribeToWorkshopItem(item.Value.Id, () =>
            {
                if (SteamManager.InstallWorkshopItem(item, out _))
                {
                    textBlock.Text = TextManager.Get("workshopiteminstalled");
                    frame.Flash(GUI.Style.Green);
                }
                else
                {
                    frame.Flash(GUI.Style.Red);
                }
                RefreshSubscribedItems();
            });

            return true;
        }

        private void ShowItemPreview(Steamworks.Ugc.Item? item, GUIFrame itemPreviewFrame)
        {
            itemPreviewFrame.ClearChildren();

            if (item == null) { return; }

            var content = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), itemPreviewFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                UserData = item
            };

            var headerArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), content.RectTransform))
            {
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerArea.RectTransform), item?.Title, textAlignment: Alignment.CenterLeft, font: GUI.LargeFont, wrap: true);

            new GUITextBlock(new RectTransform(new Vector2(0.3f, 0.0f), headerArea.RectTransform), item?.Owner.Name, textAlignment: Alignment.CenterLeft, font: GUI.SubHeadingFont);

            var btn = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), headerArea.RectTransform, Anchor.CenterRight), TextManager.Get("WorkshopShowItemInSteam"), style: "GUIButtonSmall")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (btn, userdata) =>
                {
                    SteamManager.OverlayCustomURL("steam://url/CommunityFilePage/" + item?.Id);
                    return true;
                }
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform), style: null);

            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.005f), content.RectTransform), style: "HorizontalLine");

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform), style: null);

            //---------------

            var centerArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            
            if (itemPreviewSprites.ContainsKey(item?.PreviewImageUrl))
            {
                new GUIImage(new RectTransform(new Vector2(0.5f, 1.0f), centerArea.RectTransform), itemPreviewSprites[item?.PreviewImageUrl], scaleToFit: true);
            }
            else
            {
                new GUIImage(new RectTransform(new Vector2(0.5f, 0.0f), centerArea.RectTransform), SteamManager.DefaultPreviewImage, scaleToFit: true);
            }

            var statsFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 1.0f), centerArea.RectTransform), style: "GUIFrameListBox");
            var statsContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f), statsFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            //score -------------------------------------
            var scoreContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), statsContent.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 0.0f), scoreContainer.RectTransform), TextManager.Get("WorkshopItemScore"), font: GUI.SubHeadingFont);
            int starCount = (int)Math.Round((item?.Score ?? 0.0f) * 5);
            for (int i = 0; i < 5; i++)
            {
                new GUIImage(new RectTransform(new Point(scoreContainer.Rect.Height), scoreContainer.RectTransform),
                    i < starCount ? "GUIStarIconBright" : "GUIStarIconDark");
            }
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 0.0f), scoreContainer.RectTransform), 
                TextManager.GetWithVariable("WorkshopItemVotes", "[votecount]", (item.Value.VotesUp + item.Value.VotesDown).ToString()),
                textAlignment: Alignment.CenterRight);

            //tags ------------------------------------   
            
            List<string> tags = new List<string>();
            for (int i = 0; i < item?.Tags.Length && i < 5; i++)
            {
                if (string.IsNullOrEmpty(item?.Tags[i])) { continue; }
                string tag = TextManager.Get("Workshop.ContentTag." + item?.Tags[i].Replace(" ", ""), true);
                if (string.IsNullOrEmpty(tag)) { tag = item?.Tags[i].CapitaliseFirstInvariant(); }
                tags.Add(tag);
            }
            if (tags.Count > 0)
            {
                var tagContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), statsContent.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f,
                    CanBeFocused = true
                };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), tagContainer.RectTransform), TextManager.Get("WorkshopItemTags"), font: GUI.SubHeadingFont);

                var t = new GUITextBlock(new RectTransform(new Vector2(0.8f, 1.0f), tagContainer.RectTransform, Anchor.TopRight), string.Join(", ", tags), textAlignment: Alignment.CenterRight);
                t.RectTransform.SizeChanged += () =>
                {
                    t.TextScale = 1.0f;
                    t.AutoScaleHorizontal = true;
                };
            }

            var fileSize = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), statsContent.RectTransform), TextManager.Get("WorkshopItemFileSize"), font: GUI.SubHeadingFont);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), fileSize.RectTransform, Anchor.TopRight), MathUtils.GetBytesReadable(item?.IsInstalled ?? false ? (long)item.Value.SizeBytes : item.Value.DownloadBytesDownloaded), textAlignment: Alignment.CenterRight);

            //var dateContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), isHorizontal: true);

            var creationDate = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), statsContent.RectTransform), TextManager.Get("WorkshopItemCreationDate"), font: GUI.SubHeadingFont);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), creationDate.RectTransform, Anchor.CenterRight), item?.Created.ToString("dd.MM.yyyy"), textAlignment: Alignment.CenterRight);

            var modificationDate = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), statsContent.RectTransform), TextManager.Get("WorkshopItemModificationDate"), font: GUI.SubHeadingFont);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), modificationDate.RectTransform, Anchor.CenterRight), item?.Updated.ToString("dd.MM.yyyy"), textAlignment: Alignment.CenterRight);

            if (item?.IsSubscribed ?? false)
            {
                var buttonContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), statsContent.RectTransform), style: null);
                var unsubscribeButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.95f), buttonContainer.RectTransform, Anchor.Center), TextManager.Get("WorkshopItemUnsubscribe"), style: "GUIButtonSmall")
                {
                    UserData = item,
                    OnClicked = (btn, userdata) =>
                    {
                    subscribePollAdditionalWait += 1.0f;
                        item?.Unsubscribe();
                        SteamManager.UninstallWorkshopItem(item, true, out _);
                        subscribedItemList.RemoveChild(subscribedItemList.Content.GetChildByUserData(item));
                        itemPreviewFrame.ClearChildren();
                        return true;
                    }
                };
                buttonContainer.RectTransform.MinSize = unsubscribeButton.RectTransform.MinSize;
                statsContent.Recalculate();
            }

            //------------------

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform), style: null);

            var descriptionContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform)) { ScrollBarVisible = true };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), descriptionContainer.Content.RectTransform) { MinSize = new Point(0, 5) }, style: null);

            string description = item?.Description;
            description = ToolBox.RemoveBBCodeTags(description);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), descriptionContainer.Content.RectTransform), description, wrap: true)
            {
                CanBeFocused = false
            };
        }
        
        private void CreateWorkshopItem(SubmarineInfo sub)
        {
            string destinationFolder = Path.Combine("Mods", sub.Name.Trim());
            itemContentPackage = ContentPackage.CreatePackage(sub.Name, Path.Combine(destinationFolder, SteamManager.MetadataFileName), corePackage: false);
            SteamManager.CreateWorkshopItemStaging(itemContentPackage, out itemEditor);

            string submarineDir = Path.GetDirectoryName(sub.FilePath);
            if (submarineDir != Path.GetDirectoryName(destinationFolder))
            {
                string destinationPath = Path.Combine(destinationFolder, Path.GetFileName(sub.FilePath));
                if (!File.Exists(destinationPath))
                {
                    File.Move(sub.FilePath, destinationPath);
                }
                sub.FilePath = destinationPath;
            }
            
            itemContentPackage.AddFile(sub.FilePath, ContentType.Submarine);
            itemContentPackage.Name = sub.Name;
            itemContentPackage.Save(itemContentPackage.Path);
            //ContentPackage.List.Add(itemContentPackage);
            //GameMain.Config.SelectContentPackage(itemContentPackage);

            itemEditor = itemEditor?.WithTitle(sub.Name).WithTag("Submarine").WithDescription(sub.Description);

            if (sub.PreviewImage != null)
            {
                string previewImagePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(itemContentPackage.Path), SteamManager.PreviewImageName));
                try
                {
                    using (System.IO.Stream s = File.Create(previewImagePath))
                    {
                        sub.PreviewImage.Texture.SaveAsPng(s, (int)sub.PreviewImage.size.X, (int)sub.PreviewImage.size.Y);
                        itemEditor = itemEditor?.WithPreviewFile(previewImagePath);
                    }
                    if (new FileInfo(previewImagePath).Length > 1024 * 1024)
                    {
                        new GUIMessageBox(TextManager.Get("Error"), TextManager.Get("WorkshopItemPreviewImageTooLarge"));
                        itemEditor = itemEditor?.WithPreviewFile(SteamManager.DefaultPreviewImagePath);
                    }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Saving submarine preview image failed.", e);
                    itemEditor = itemEditor?.WithPreviewFile(null);
                }
            }
        }
        private void CreateWorkshopItem(ContentPackage contentPackage)
        {
            //SteamManager.CreateWorkshopItemStaging(new List<ContentFile>(), out itemEditor, out itemContentPackage);

            itemContentPackage = contentPackage;
            SteamManager.CreateWorkshopItemStaging(itemContentPackage, out itemEditor);
            itemEditor = itemEditor?.WithTitle(contentPackage.Name);

            /*string modDirectory = "";
            foreach (ContentFile file in contentPackage.Files)
            {
                itemContentPackage.AddFile(file.Path, file.Type);
                //if some of the content files are in a subdirectory of the Mods folder, 
                //assume that directory contains mod files for this package and copy them to the staging folder
                if (modDirectory == "" && ContentPackage.IsModFilePathAllowed(file.Path))
                {
                    string directoryName = Path.GetDirectoryName(file.Path);
                    string[] splitPath = directoryName.Split(Path.DirectorySeparatorChar);
                    if (splitPath.Length >= 2 && splitPath[0] == "Mods")
                    {
                        modDirectory = splitPath[1];
                    }
                }
            }

            if (!string.IsNullOrEmpty(modDirectory))
            {
                SaveUtil.CopyFolder(Path.Combine("Mods", modDirectory), Path.Combine(SteamManager.WorkshopItemStagingFolder, "Mods", modDirectory), copySubDirs: true);
            }*/

        }

        private bool CreateWorkshopItem(Steamworks.Ugc.Item? item)
        {
            if (!(item?.IsInstalled ?? false))
            {
                new GUIMessageBox(TextManager.Get("Error"), 
                    TextManager.GetWithVariable("WorkshopErrorInstallRequiredToEdit", "[itemname]", (item?.Title ?? "[NULL]")));
                return false;
            }
            if (!SteamManager.CreateWorkshopItemStaging(item, out itemEditor, out itemContentPackage))
            {
                return false;
            }
            var tickBox = publishedItemList.Content.GetChildByUserData(item)?.GetAnyChild<GUITickBox>();
            if (tickBox != null) { tickBox.Selected = true; }
            return true;
        }

        private void ShowCreateItemFrame()
        {
            createItemFrame.ClearChildren();
            
            if (itemEditor == null) { return; }

            if (itemContentPackage == null)
            {
                string errorMsg = "Failed to edit workshop item (content package null)\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("SteamWorkshopScreen.ShowCreateItemFrame:ContentPackageNull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }

            var createItemContent = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.98f), createItemFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            
            var topPanel = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.4f), createItemContent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            var topLeftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1.0f), topPanel.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            var topRightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1.0f), topPanel.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            // top right column --------------------------------------------------------------------------------------

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topRightColumn.RectTransform), TextManager.Get("WorkshopItemTitle"), font: GUI.SubHeadingFont);
            var titleBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.15f), topRightColumn.RectTransform), itemEditor?.Title);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topRightColumn.RectTransform), TextManager.Get("WorkshopItemDescription"), font: GUI.SubHeadingFont);

            var descriptionContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), topRightColumn.RectTransform));
            var descriptionBox = new GUITextBox(new RectTransform(Vector2.One, descriptionContainer.Content.RectTransform), itemEditor?.Description,
                textAlignment: Alignment.TopLeft, style: "GUITextBoxNoBorder", font: GUI.SmallFont, wrap: true);
            descriptionBox.OnTextChanged += (textBox, text) => 
            {
                Vector2 textSize = textBox.Font.MeasureString(descriptionBox.WrappedText);
                textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(descriptionContainer.Content.Rect.Height, (int)textSize.Y + 10));
                descriptionContainer.UpdateScrollBarSize();
                descriptionContainer.BarScroll = 1.0f;
                itemEditor = itemEditor?.WithDescription(text);
                return true;
            };
            descriptionContainer.RectTransform.SizeChanged += () => { descriptionBox.Text = descriptionBox.Text; };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topRightColumn.RectTransform), TextManager.Get("WorkshopItemTags"), font: GUI.SubHeadingFont);
            var tagHolder = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.17f), topRightColumn.RectTransform) { MinSize = new Point(0, 50) }, isHorizontal: true)
            {
                Spacing = 5
            };

            HashSet<string> availableTags = new HashSet<string>();
            foreach (string tag in itemEditor?.Tags ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrEmpty(tag)) { availableTags.Add(tag.ToLowerInvariant()); }
            }
            foreach (string tag in SteamManager.PopularTags)
            {
                if (!string.IsNullOrEmpty(tag)) { availableTags.Add(tag.ToLowerInvariant()); }
                if (availableTags.Count > 10) { break; }
            }

            foreach (string tag in availableTags)
            {
                var tagBtn = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), tagHolder.Content.RectTransform, anchor: Anchor.CenterLeft), 
                    tag.CapitaliseFirstInvariant(), style: "GUIButtonRound");
                tagBtn.TextBlock.AutoScaleHorizontal = true;
                tagBtn.Selected = itemEditor?.Tags?.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)) ?? false;

                tagBtn.OnClicked = (btn, userdata) =>
                {
                    if (!tagBtn.Selected)
                    {
                        if (!(itemEditor?.Tags?.Any(t => t.ToLowerInvariant() == tag) ?? false)) { itemEditor = itemEditor?.WithTag(tagBtn.Text); }
                        tagBtn.Selected = true;
                    }
                    else
                    {
                        itemEditor?.Tags?.RemoveAll(t => t.Equals(tagBtn.Text, StringComparison.OrdinalIgnoreCase));
                        tagBtn.Selected = false;
                    }
                    return true;
                };
            }
            tagHolder.UpdateScrollBarSize();

            // top left column --------------------------------------------------------------------------------------

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topLeftColumn.RectTransform), TextManager.Get("WorkshopItemPreviewImage"), font: GUI.SubHeadingFont);

            var previewIcon = new GUIImage(new RectTransform(new Vector2(1.0f, 0.7f), topLeftColumn.RectTransform), SteamManager.DefaultPreviewImage, scaleToFit: true);
            new GUIButton(new RectTransform(new Vector2(1.0f, 0.2f), topLeftColumn.RectTransform), TextManager.Get("WorkshopItemBrowse"), style: "GUIButtonSmall")
            {
                OnClicked = (btn, userdata) =>
                {
                    FileSelection.OnFileSelected = (file) =>
                    {
                        OnPreviewImageSelected(previewIcon, file);
                    };
                    FileSelection.ClearFileTypeFilters();
                    FileSelection.AddFileTypeFilter("PNG", "*.png");
                    FileSelection.AddFileTypeFilter("JPEG", "*.jpg, *.jpeg");
                    FileSelection.AddFileTypeFilter("All files", "*.*");
                    FileSelection.SelectFileTypeFilter("*.png");
                    FileSelection.Open = true;
                    return true;
                }
            };

            //if preview image has not been set, but there's a PreviewImage file inside the mod folder, use that by default
            if (string.IsNullOrEmpty(itemEditor?.PreviewFile))
            {
                string previewImagePath = Path.Combine(Path.GetDirectoryName(itemContentPackage.Path), SteamManager.PreviewImageName);
                if (File.Exists(previewImagePath))
                {
                    itemEditor = itemEditor?.WithPreviewFile(Path.GetFullPath(previewImagePath));
                }
            }
            if (!string.IsNullOrEmpty(itemEditor?.PreviewFile))
            {
                itemEditor = itemEditor?.WithPreviewFile(Path.GetFullPath(itemEditor?.PreviewFile));
                if (itemPreviewSprites.ContainsKey(itemEditor?.PreviewFile))
                {
                    itemPreviewSprites[itemEditor?.PreviewFile].Remove();
                }
                var newPreviewImage = new Sprite(itemEditor?.PreviewFile, sourceRectangle: null);
                previewIcon.Sprite = newPreviewImage;
                itemPreviewSprites[itemEditor?.PreviewFile] = newPreviewImage;
            }

            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), topLeftColumn.RectTransform), TextManager.Get("WorkshopItemCorePackage"))
            {
                ToolTip = TextManager.Get("WorkshopItemCorePackageTooltip"),
                Selected = itemContentPackage.IsCorePackage,
                OnSelected = (tickbox) => 
                {
                    if (tickbox.Selected)
                    {
                        if (!itemContentPackage.ContainsRequiredCorePackageFiles(out List<ContentType> missingContentTypes))
                        {
                            new GUIMessageBox(
                                TextManager.Get("Error"),
                                TextManager.GetWithVariables("ContentPackageCantMakeCorePackage", new string[2] { "[packagename]", "[missingfiletypes]" }, 
                                new string[2] { itemContentPackage.Name, string.Join(", ", missingContentTypes) }, new bool[2] { false, true }));
                            tickbox.Selected = false;
                        }
                        else
                        {
                            itemContentPackage.IsCorePackage = tickbox.Selected;
                        }
                    }
                    else
                    {
                        itemContentPackage.IsCorePackage = false;
                    }
                    return true;
                }
            };

            // file list --------------------------------------------------------------------------------------

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), createItemContent.RectTransform), style: null);

            var fileListTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), createItemContent.RectTransform), TextManager.Get("WorkshopItemFiles"), font: GUI.SubHeadingFont);
            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), fileListTitle.RectTransform, Anchor.CenterRight), TextManager.Get("WorkshopItemShowFolder"), style: "GUIButtonSmall")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (btn, userdata) => { ToolBox.OpenFileWithShell(Path.GetFullPath(Path.GetDirectoryName(itemContentPackage.Path))); return true; }
            };
            createItemFileList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.35f), createItemContent.RectTransform));
            createItemWatcher?.Dispose();
            createItemWatcher = new System.IO.FileSystemWatcher(Path.GetDirectoryName(itemContentPackage.Path))
            {
                Filter = "*",
                NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.DirectoryName
            };
            createItemWatcher.Created += OnFileSystemChanges;
            createItemWatcher.Deleted += OnFileSystemChanges;
            createItemWatcher.Renamed += OnFileSystemChanges;
            createItemWatcher.EnableRaisingEvents = true;
            RefreshCreateItemFileList();

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), createItemContent.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.02f
            };

            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonContainer.RectTransform, Anchor.TopRight), TextManager.Get("WorkshopItemRefreshFileList"), style: "GUIButtonSmall")
            {
                ToolTip = TextManager.Get("WorkshopItemRefreshFileListTooltip"),
                OnClicked = (btn, userdata) =>
                {
                    itemContentPackage = new ContentPackage(itemContentPackage.Path);
                    RefreshCreateItemFileList();
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonContainer.RectTransform, Anchor.TopRight), TextManager.Get("WorkshopItemAddFiles"), style: "GUIButtonSmall")
            {
                OnClicked = (btn, userdata) =>
                {
                    FileSelection.OnFileSelected = (file) =>
                    {
                        OnAddFilesSelected(new string[] { file });
                    };
                    FileSelection.ClearFileTypeFilters();
                    FileSelection.AddFileTypeFilter("PNG", "*.png");
                    FileSelection.AddFileTypeFilter("JPEG", "*.jpg, *.jpeg");
                    FileSelection.AddFileTypeFilter("OGG", "*.ogg");
                    FileSelection.AddFileTypeFilter("XML", "*.xml");
                    FileSelection.AddFileTypeFilter("TXT", "*.txt");
                    FileSelection.AddFileTypeFilter("All files", "*.*");
                    FileSelection.SelectFileTypeFilter("*.*");
                    FileSelection.Open = true;

                    return true;
                }
            };

            //the item has been already published if it has a non-zero ID -> allow adding a changenote
            if ((itemEditor?.FileId ?? 0) > 0)
            {
                var bottomRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), createItemContent.RectTransform), isHorizontal: true);
                var changeNoteLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1.0f), bottomRow.RectTransform));

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.25f), changeNoteLayout.RectTransform), TextManager.Get("WorkshopItemChangenote"), font: GUI.SubHeadingFont)
                {
                    ToolTip = TextManager.Get("WorkshopItemChangenoteTooltip")
                };

                var changenoteContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.75f), changeNoteLayout.RectTransform));
                var changenoteBox = new GUITextBox(new RectTransform(Vector2.One, changenoteContainer.Content.RectTransform), "", 
                    textAlignment: Alignment.TopLeft, style: "GUITextBoxNoBorder", wrap: true)
                {
                    ToolTip = TextManager.Get("WorkshopItemChangenoteTooltip")
                };
                changenoteBox.OnTextChanged += (textBox, text) =>
                {
                    Vector2 textSize = textBox.Font.MeasureString(changenoteBox.WrappedText);
                    textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(changenoteContainer.Content.Rect.Height, (int)textSize.Y + 10));
                    changenoteContainer.UpdateScrollBarSize();
                    changenoteContainer.BarScroll = 1.0f;
                    itemEditor = itemEditor?.WithChangeLog(text);
                    return true;
                };
            }

            var bottomButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), createItemContent.RectTransform), 
                isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                RelativeSpacing = 0.03f
            };

            var visibilityLabel = new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), bottomButtonContainer.RectTransform), TextManager.Get("WorkshopItemVisibility"), 
               textAlignment: Alignment.CenterLeft, font: GUI.SubHeadingFont)
            {
                ToolTip = TextManager.Get("WorkshopItemVisibilityTooltip")
            };
            visibilityLabel.RectTransform.MaxSize = new Point((int)(visibilityLabel.TextSize.X * 1.1f), 0);

            var visibilityDropDown = new GUIDropDown(new RectTransform(new Vector2(0.2f, 1.0f), bottomButtonContainer.RectTransform));
            foreach (VisibilityType visibilityType in Enum.GetValues(typeof(VisibilityType)))
            {
                visibilityDropDown.AddItem(TextManager.Get("WorkshopItemVisibility." + visibilityType), visibilityType);
            }
            visibilityDropDown.SelectItem(itemEditor.Value.IsPublic ? VisibilityType.Public : 
                itemEditor.Value.IsFriendsOnly ? VisibilityType.FriendsOnly : 
                VisibilityType.Private);
            visibilityDropDown.OnSelected = (c, ud) =>
            {
                if (!(ud is VisibilityType visibilityType)) { return false; }
                switch (visibilityType)
                {
                    case VisibilityType.Public:
                        itemEditor = itemEditor?.WithPublicVisibility();
                        break;
                    case VisibilityType.FriendsOnly:
                        itemEditor = itemEditor?.WithFriendsOnlyVisibility();
                        break;
                    case VisibilityType.Private:
                        itemEditor = itemEditor?.WithPrivateVisibility();
                        break;
                }

                return true;
            };

            if ((itemEditor?.FileId ?? 0) > 0)
            {
                new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), bottomButtonContainer.RectTransform),
                    TextManager.Get("WorkshopItemDelete"), style: "GUIButtonSmall")
                {
                    ToolTip = TextManager.Get("WorkshopItemDeleteTooltip"),
                    TextColor = GUI.Style.Red,
                    OnClicked = (btn, userData) =>
                    {
                        if (itemEditor == null) { return false; }
                        var deleteVerification = new GUIMessageBox("", TextManager.GetWithVariable("WorkshopItemDeleteVerification", "[itemname]", itemEditor?.Title),
                            new string[] {  TextManager.Get("Yes"), TextManager.Get("No") });
                        deleteVerification.Buttons[0].OnClicked = (yesBtn, userdata) =>
                        {
                            if (itemEditor == null) { return false; }
                            RemoveItemFromLists(itemEditor.Value.FileId);
                            TaskPool.Add("DeleteFileAsync", Steamworks.SteamUGC.DeleteFileAsync(itemEditor.Value.FileId),
                                (t) =>
                                {
                                    if (t.Status == TaskStatus.Faulted)
                                    {
                                        TaskPool.PrintTaskExceptions(t, "Failed to delete Workshop item " + (itemEditor?.Title ?? "[NULL]"));
                                        return;
                                    }
                                });
                            itemEditor = null;
                            SelectTab(Tab.Browse);
                            deleteVerification.Close();
                            createItemFrame.ClearChildren();
                            itemContentPackage.SteamWorkshopId = 0;
                            itemContentPackage.Save(itemContentPackage.Path);
                            return true;
                        };
                        deleteVerification.Buttons[1].OnClicked = deleteVerification.Close;
                        return true;
                    }
                };
            }
            var publishBtn = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), bottomButtonContainer.RectTransform, Anchor.CenterRight),
                TextManager.Get((itemEditor?.FileId ?? 0) > 0 ? "WorkshopItemUpdate" : "WorkshopItemPublish"))
            {
                IgnoreLayoutGroups = true,
                ToolTip = TextManager.Get("WorkshopItemPublishTooltip"),
                OnClicked = (btn, userData) => 
                {
                    itemEditor = itemEditor?.WithTitle(titleBox.Text);
                    itemEditor = itemEditor?.WithDescription(descriptionBox.Text);
                    if (string.IsNullOrWhiteSpace(itemEditor?.Title))
                    {
                        titleBox.Flash(GUI.Style.Red);
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(itemEditor?.Description))
                    {
                        descriptionBox.Flash(GUI.Style.Red);
                        return false;
                    }
                    if (createItemFileList.Content.CountChildren == 0)
                    {
                        createItemFileList.Flash(GUI.Style.Red);
                    }

                    if (!itemContentPackage.CheckErrors(out List<string> errorMessages))
                    {
                        new GUIMessageBox(
                            TextManager.GetWithVariable("workshopitempublishfailed", "[itemname]", itemEditor?.Title),
                            string.Join("\n", errorMessages));
                        return false;
                    }

                    PublishWorkshopItem();
                    return true;
                }
            };
            publishBtn.TextBlock.AutoScaleHorizontal = true;
        }

        private void OnPreviewImageSelected(GUIImage previewImageElement, string filePath)
        {
            string previewImagePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(itemContentPackage.Path), SteamManager.PreviewImageName));
            if (new FileInfo(filePath).Length > 1024 * 1024)
            {
                new GUIMessageBox(TextManager.Get("Error"), TextManager.Get("WorkshopItemPreviewImageTooLarge"));
                return;
            }

            if (Path.GetFullPath(filePath) != previewImagePath)
            {
                try
                {
                    File.Copy(filePath, previewImagePath, overwrite: true);
                }
                catch (System.IO.IOException e)
                {
                    DebugConsole.ThrowError("Failed to copy the preview image \"{previewImagePath}\" to the mod folder.", e);
                    return;
                }
            }

            if (itemPreviewSprites.ContainsKey(previewImagePath))
            {
                itemPreviewSprites[previewImagePath].Remove();
            }
            var newPreviewImage = new Sprite(previewImagePath, sourceRectangle: null);
            previewImageElement.Sprite = newPreviewImage;
            itemPreviewSprites[previewImagePath] = newPreviewImage;
            itemEditor?.WithPreviewFile(previewImagePath);
        }

        private void OnAddFilesSelected(string[] fileNames)
        {
            if (fileNames == null) { return; }
            for (int i = 0; i < fileNames.Length; i++)
            {
                string file = fileNames[i]?.Trim();
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) { continue; }

                string modFolder = Path.GetDirectoryName(itemContentPackage.Path);                
                string filePathRelativeToModFolder = UpdaterUtil.GetRelativePath(file, Path.Combine(Environment.CurrentDirectory, modFolder));

                //file is not inside the mod folder, we need to move it
                if (filePathRelativeToModFolder.StartsWith("..") || 
                    Path.GetPathRoot(Environment.CurrentDirectory) != Path.GetPathRoot(file))
                {
                    string destinationPath = Path.Combine(modFolder, Path.GetFileName(file));
                    //add a number to the filename if a file with the same name already exists
                    i = 2;
                    while (File.Exists(destinationPath))
                    {
                        destinationPath = Path.Combine(modFolder, $"{Path.GetFileNameWithoutExtension(file)} ({i}){Path.GetExtension(file)}");
                        i++;
                    }
                    try
                    {
                        File.Copy(file, destinationPath);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Copying the file \"" + file + "\" to the mod folder failed.", e);
                        return;
                    }                
                }
            }
            RefreshCreateItemFileList();
        }

        volatile bool refreshFileList = false;

        private void OnFileSystemChanges(object sender, System.IO.FileSystemEventArgs e)
        {
            refreshFileList = true;
        }

        private void RefreshCreateItemFileList()
        {
            createItemFileList.ClearChildren();
            if (itemContentPackage == null) return;
            var contentTypes = Enum.GetValues(typeof(ContentType));

            List<ContentFile> files = itemContentPackage.FilesUnsaved.ToList();

            for (int i = files.Count - 1; i >= 0; i--)
            {
                ContentFile contentFile = files[i];

                bool fileExists = File.Exists(contentFile.Path);

                if (contentFile.Type == ContentType.ServerExecutable)
                {
                    fileExists |= File.Exists(Path.GetFileNameWithoutExtension(contentFile.Path) + ".dll");
                }

                if (!fileExists)
                {
                    itemContentPackage.RemoveFile(contentFile);
                    files.RemoveAt(i);
                }
            }

            List<ContentFile> allFiles = Directory.GetFiles(Path.GetDirectoryName(itemContentPackage.Path), "*", System.IO.SearchOption.AllDirectories)
                .Select(f => new ContentFile(f, ContentType.None))
                .Where(file => Path.GetFileName(file.Path) != SteamManager.MetadataFileName &&
                               Path.GetFileName(file.Path) != SteamManager.PreviewImageName)
                .ToList();
            for (int i=0;i<allFiles.Count;i++)
            {
                ContentFile file = allFiles[i];
                ContentFile otherFile = files.Find(f => string.Equals(Path.GetFullPath(f.Path).CleanUpPath(),
                                                                      Path.GetFullPath(file.Path).CleanUpPath(),
                                                                      StringComparison.InvariantCultureIgnoreCase));
                if (otherFile != null)
                {
                    //replace the generated ContentFile object with the one that's present in the
                    //content package to determine which tickboxes should already be checked
                    allFiles[i] = otherFile;
                    files.Remove(otherFile);
                }
            }

            allFiles.AddRange(files);

            foreach (ContentFile contentFile in allFiles)
            {
                bool illegalPath = !ContentPackage.IsModFilePathAllowed(contentFile);
                bool fileExists = File.Exists(contentFile.Path);

                if (contentFile.Type == ContentType.ServerExecutable)
                {
                    fileExists |= File.Exists(Path.GetFileNameWithoutExtension(contentFile.Path) + ".dll");
                }

                var fileFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.12f), createItemFileList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    style: "ListBoxElement")
                {
                    CanBeFocused = false,
                    UserData = contentFile
                };

                var content = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 1.0f), fileFrame.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                var tickBox = new GUITickBox(new RectTransform(Vector2.One, content.RectTransform, scaleBasis: ScaleBasis.BothHeight), "")
                {
                    Selected = itemContentPackage.FilesUnsaved.Contains(contentFile),
                    UserData = contentFile
                };

                tickBox.OnSelected = (tb) =>
                {
                    ContentFile f = tb.UserData as ContentFile;
                    if (tb.Selected)
                    {
                        if (!itemContentPackage.FilesUnsaved.Contains(f)) { itemContentPackage.AddFile(f); }
                    }
                    else
                    {
                        if (itemContentPackage.FilesUnsaved.Contains(f)) { itemContentPackage.RemoveFile(f); }
                    }

                    return true;
                };

                var nameText = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), content.RectTransform, Anchor.CenterLeft), contentFile.Path, font: GUI.SmallFont)
                {
                    ToolTip = contentFile.Path
                };
                if (!fileExists)
                {
                    nameText.TextColor = GUI.Style.Red;
                    tickBox.ToolTip = TextManager.Get("WorkshopItemFileNotFound");
                }
                else if (illegalPath && !ContentPackage.AllPackages.Any(cp => cp.FilesUnsaved.Any(f => Path.GetFullPath(f.Path) == Path.GetFullPath(contentFile.Path))))
                {
                    nameText.TextColor = GUI.Style.Red;
                    tickBox.ToolTip = TextManager.Get("WorkshopItemIllegalPath");
                }

                var contentTypeSelection = new GUIDropDown(new RectTransform(new Vector2(0.4f, 1.0f), content.RectTransform, Anchor.CenterRight),
                    elementCount: contentTypes.Length)
                {
                    UserData = contentFile,
                };
                foreach (ContentType contentType in contentTypes)
                {
                    contentTypeSelection.AddItem(contentType.ToString(), contentType);
                }
                contentTypeSelection.SelectItem(contentFile.Type);

                contentTypeSelection.OnSelected = (GUIComponent selected, object userdata) =>
                {
                    ((ContentFile)contentTypeSelection.UserData).Type = (ContentType)userdata;
                    itemContentPackage.Save(itemContentPackage.Path);
                    return true;
                };

                if (!files.Contains(contentFile)) //this prevents deletion of files not contained in the mod's path (i.e. vanilla content)
                {
                    new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), content.RectTransform), TextManager.Get("Delete"), style: "GUIButtonSmall")
                    {
                        OnClicked = (btn, userdata) =>
                        {
                            var msgBox = new GUIMessageBox(TextManager.Get("ConfirmFileDeletionHeader"),
                                    TextManager.GetWithVariable("ConfirmFileDeletion", "[file]", contentFile.Path),
                                    new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") })
                            {
                                UserData = "verificationprompt"
                            };
                            msgBox.Buttons[0].OnClicked = (applyButton, obj) =>
                            {
                                try
                                {
                                    File.Delete(contentFile.Path);
                                    if (contentFile.Type == ContentType.Submarine) { SubmarineInfo.RefreshSavedSub(contentFile.Path); }
                                }
                                catch (Exception e)
                                {
                                    DebugConsole.ThrowError($"Failed to delete \"${contentFile.Path}\".", e);
                                }
                                //RefreshCreateItemFileList();
                                RefreshMyItemList();
                                return true;
                            };
                            msgBox.Buttons[0].OnClicked += msgBox.Close;
                            msgBox.Buttons[1].OnClicked = msgBox.Close;
                            return true;
                        }
                    };
                }

                content.Recalculate();
                fileFrame.RectTransform.MinSize = 
                    new Point(0, (int)(content.RectTransform.Children.Max(c => c.MinSize.Y) / content.RectTransform.RelativeSize.Y));
                nameText.Text = ToolBox.LimitString(nameText.Text, nameText.Font, maxWidth: nameText.Rect.Width);
            }

            itemContentPackage.Save(itemContentPackage.Path);
        }

        private void PublishWorkshopItem()
        {
            if (itemContentPackage == null || itemEditor == null) { return; }

#if UNSTABLE
            var msgBox = new GUIMessageBox(TextManager.Get("warning"), TextManager.Get("unstableworkshopitempublishwarning"),
                new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                var workshopPublishStatus = SteamManager.StartPublishItem(itemContentPackage, itemEditor);
                if (workshopPublishStatus != null)
                {
                    if (!(itemEditor?.HasTag("unstable") ?? false)) { itemEditor = itemEditor?.WithTag("unstable"); }
                    CoroutineManager.StartCoroutine(WaitForPublish(workshopPublishStatus), "WaitForPublish");
                }
                msgBox.Close();
                return true;
            };
            msgBox.Buttons[1].OnClicked += msgBox.Close;
#else
            itemEditor = itemEditor?.WithoutTag("unstable");
            var workshopPublishStatus = SteamManager.StartPublishItem(itemContentPackage, itemEditor);
            if (workshopPublishStatus == null) { return; }
            CoroutineManager.StartCoroutine(WaitForPublish(workshopPublishStatus), "WaitForPublish");
#endif

        }

        private IEnumerable<object> WaitForPublish(SteamManager.WorkshopPublishStatus workshopPublishStatus)
        {
            var item = workshopPublishStatus.Item;
            var coroutine = workshopPublishStatus.Coroutine;

            string pleaseWaitText = TextManager.Get("WorkshopPublishPleaseWait");
            var msgBox = new GUIMessageBox(
                pleaseWaitText,
                TextManager.GetWithVariable("WorkshopPublishInProgress", "[itemname]", item?.Title), 
                new string[] { TextManager.Get("Cancel") });

            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                CoroutineManager.StopCoroutines("WaitForPublish");
                createItemFrame.ClearChildren();
                SelectTab(Tab.Browse);
                msgBox.Close();
                return true;
            };

            yield return CoroutineStatus.Running;
            while (CoroutineManager.IsCoroutineRunning(coroutine))
            {
                msgBox.Header.Text = pleaseWaitText + new string('.', ((int)Timing.TotalTime % 3 + 1));
                yield return CoroutineStatus.Running;
            }
            msgBox.Close();

            if (workshopPublishStatus.Success ?? false)
            {
                new GUIMessageBox("", TextManager.GetWithVariable("WorkshopItemPublished", "[itemname]", item?.Title));
            }
            else
            {
                string errorMsg = workshopPublishStatus.Result.HasValue ?
                    TextManager.GetWithVariable("WorkshopPublishError." + workshopPublishStatus.Result?.Result.ToString(), "[savepath]", SaveUtil.SaveFolder, returnNull: true) :
                    null;

                if (errorMsg == null)
                {
                    new GUIMessageBox(
                        TextManager.Get("Error"),
                        TextManager.GetWithVariable("WorkshopItemPublishFailed", "[itemname]", item?.Title) +
                            (workshopPublishStatus?.TaskStatus != null ?
                                " Task ended with status " +workshopPublishStatus?.TaskStatus?.ToString() :
                                " Publish failed with result "+ workshopPublishStatus.Result?.Result.ToString()));
                }
                else
                {
                    new GUIMessageBox(TextManager.Get("Error"), errorMsg);
                }
            }

            createItemFrame.ClearChildren();
            SelectTab(Tab.Browse);
        }

#region UI management

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            GameMain.MainMenuScreen.DrawBackground(graphics, spriteBatch);

            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);
            GUI.Draw(Cam, spriteBatch);
            spriteBatch.End();
        }

        public override void AddToGUIUpdateList()
        {
            menu.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
            if (refreshFileList)
            {
                RefreshCreateItemFileList();
                refreshFileList = false;
            }
        }
        
#endregion
    }
}
