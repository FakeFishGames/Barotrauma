using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Barotrauma
{
    class SteamWorkshopScreen : Screen
    {
        private GUIFrame menu;
        private GUIListBox subscribedItemList, topItemList;

        private GUIListBox publishedItemList, myItemList;

        //shows information of a selected workshop item
        private GUIFrame modsPreviewFrame, browsePreviewFrame;

        //menu for creating new items
        private GUIFrame createItemFrame;
        //listbox that shows the files included in the item being created
        private GUIListBox createItemFileList;

        private GUIComponent buttonContainer;

        private List<GUIButton> tabButtons = new List<GUIButton>();

        private readonly HashSet<string> pendingPreviewImageDownloads = new HashSet<string>();
        private Dictionary<string, Sprite> itemPreviewSprites = new Dictionary<string, Sprite>();

        private enum Tab
        {
            Mods,
            Browse,
            Publish
        }

        private GUIFrame[] tabs;

        private ContentPackage itemContentPackage;
        private Facepunch.Steamworks.Workshop.Editor itemEditor;

        public SteamWorkshopScreen()
        {
            GameMain.Instance.OnResolutionChanged += OnResolutionChanged;

            tabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length];

            menu = new GUIFrame(new RectTransform(new Vector2(0.85f, 0.85f), GUI.Canvas, Anchor.Center) { MinSize = new Point(GameMain.GraphicsHeight, 0) });

            var container = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.85f), menu.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.0f, 0.05f) }) { Stretch = true };

            var tabContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.7f), container.RectTransform), style: "InnerFrame");

            var tabButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), tabContainer.RectTransform, Anchor.TopRight, Pivot.BottomRight),
                isHorizontal: true)
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };

            foreach (Tab tab in Enum.GetValues(typeof(Tab)))
            {
                GUIButton tabButton = new GUIButton(new RectTransform(new Vector2(0.05f, 1.0f), tabButtonHolder.RectTransform),
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

            //-------------------------------------------------------------------------------
            //Subscribed Mods tab
            //-------------------------------------------------------------------------------

            tabs[(int)Tab.Mods] = new GUIFrame(new RectTransform(Vector2.One, tabContainer.RectTransform, Anchor.Center), style: null);

            var modsContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), tabs[(int)Tab.Mods].RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            subscribedItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.7f), modsContainer.RectTransform))
            {
                ScrollBarVisible = true,
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    if (GUI.MouseOn is GUIButton || GUI.MouseOn?.Parent is GUIButton) { return false; }
                    ShowItemPreview(userdata as Facepunch.Steamworks.Workshop.Item, modsPreviewFrame);
                    return true;
                }
            };

            modsPreviewFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 1.0f), tabs[(int)Tab.Mods].RectTransform, Anchor.TopRight), style: "InnerFrame");

            //-------------------------------------------------------------------------------
            //Popular Mods tab
            //-------------------------------------------------------------------------------

            tabs[(int)Tab.Browse] = new GUIFrame(new RectTransform(Vector2.One, tabContainer.RectTransform, Anchor.Center), style: null);

            var listContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), tabs[(int)Tab.Browse].RectTransform))
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };

            topItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), listContainer.RectTransform))
            {
                ScrollBarVisible = true,
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    ShowItemPreview(userdata as Facepunch.Steamworks.Workshop.Item, browsePreviewFrame);
                    return true;
                }
            };

            var findModsButtonContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), listContainer.RectTransform), style: null);
            new GUIButton(new RectTransform(new Vector2(1.0f, 0.9f), findModsButtonContainer.RectTransform, Anchor.Center), TextManager.Get("FindModsButton"), style: null)
            {
                Color = new Color(38, 86, 38, 75),
                HoverColor = new Color(85, 203, 99, 50),
                TextColor = Color.White,
                OutlineColor = new Color(72, 124, 77, 255),
                OnClicked = (btn, userdata) =>
                {
                    SteamManager.OverlayCustomURL("steam://url/SteamWorkshopPage/" + SteamManager.AppID);
                    return true;
                }
            };

            browsePreviewFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 1.0f), tabs[(int)Tab.Browse].RectTransform, Anchor.TopRight), style: "InnerFrame");

            //-------------------------------------------------------------------------------
            //Publish tab
            //-------------------------------------------------------------------------------

            tabs[(int)Tab.Publish] = new GUIFrame(new RectTransform(Vector2.One, tabContainer.RectTransform, Anchor.Center), style: null);

            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), tabs[(int)Tab.Publish].RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("PublishedWorkshopItems"));
            publishedItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), leftColumn.RectTransform))
            {
                OnSelected = (component, userdata) =>
                {
                    if (GUI.MouseOn is GUIButton || GUI.MouseOn?.Parent is GUIButton) { return false; }
                    if (GUI.MouseOn is GUITickBox || GUI.MouseOn?.Parent is GUITickBox) { return false; }
                    myItemList.Deselect();
                    if (userdata is Facepunch.Steamworks.Workshop.Item item)
                    {
                        if (!item.Installed) { return false; }
                        if (CreateWorkshopItem(item)) { ShowCreateItemFrame(); }
                    }
                    return true;
                }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("YourWorkshopItems"));
            myItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), leftColumn.RectTransform))
            {
                OnSelected = (component, userdata) =>
                {
                    if (GUI.MouseOn is GUIButton || GUI.MouseOn?.Parent is GUIButton) { return false; }
                    publishedItemList.Deselect();
                    if (userdata is Submarine sub)
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

            createItemFrame = new GUIFrame(new RectTransform(new Vector2(0.58f, 1.0f), tabs[(int)Tab.Publish].RectTransform, Anchor.TopRight), style: "InnerFrame");

            buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), container.RectTransform), childAnchor: Anchor.CenterLeft);

            GUIButton backButton = new GUIButton(new RectTransform(new Vector2(0.15f, 0.9f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("Back"), style: "GUIButtonLarge")
            {
                OnClicked = GameMain.MainMenuScreen.ReturnToMainMenu
            };
            backButton.SelectedColor = backButton.Color;

            SelectTab(Tab.Mods);
        }

        private void OnResolutionChanged()
        {
            menu.RectTransform.MinSize = new Point(GameMain.GraphicsHeight, 0);
        }

        public override void Select()
        {
            base.Select();

            modsPreviewFrame.ClearChildren();
            browsePreviewFrame.ClearChildren();
            createItemFrame.ClearChildren();
            itemContentPackage = null;
            itemEditor = null;

            RefreshItemLists();
            SelectTab(Tab.Mods);
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
        }

        public void SubscribeToPackages(List<string> packageUrls)
        {
            foreach (string url in packageUrls)
            {
                SteamManager.SubscribeToWorkshopItem(url);
            }
            GameMain.SteamWorkshopScreen.Select();
        }

        private void RefreshItemLists()
        {
            SteamManager.GetSubscribedWorkshopItems((items) => 
            {
                //filter out the items published by the player (they're shown in the publish tab)
                var mySteamID = SteamManager.GetSteamID();
                OnItemsReceived(items.Where(it => it.OwnerId != mySteamID).ToList(), subscribedItemList);
            });
            SteamManager.GetPopularWorkshopItems((items) => { OnItemsReceived(items, topItemList); }, 20);
            SteamManager.GetPublishedWorkshopItems((items) => { OnItemsReceived(items, publishedItemList); });
            RefreshMyItemList();
        }

        private void RefreshMyItemList()
        {
            myItemList.ClearChildren();
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), myItemList.Content.RectTransform), TextManager.Get("WorkshopLabelSubmarines"), textAlignment: Alignment.Center, font: GUI.LargeFont)
            {
                CanBeFocused = false
            };
            foreach (Submarine sub in Submarine.SavedSubmarines)
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
                if (ContentPackage.List.Any(cp => !string.IsNullOrEmpty(cp.SteamWorkshopUrl) &&
                    cp.Files.Any(f => f.Type == ContentType.Submarine && Path.GetFullPath(f.Path) == subPath)))
                {
                    continue;
                }
                //ignore subs that are defined in a content package with more files than just the sub 
                //(these will be listed in the "content packages" section)
                if (ContentPackage.List.Any(cp => cp.Files.Count > 1 &&
                    cp.Files.Any(f => f.Type == ContentType.Submarine && Path.GetFullPath(f.Path) == subPath)))
                {
                    continue;
                }

                CreateMyItemFrame(sub, myItemList);
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), myItemList.Content.RectTransform), TextManager.Get("WorkshopLabelContentPackages"), textAlignment: Alignment.Center, font: GUI.LargeFont)
            {
                CanBeFocused = false
            };
            foreach (ContentPackage contentPackage in ContentPackage.List)
            {
                if (!string.IsNullOrEmpty(contentPackage.SteamWorkshopUrl) || contentPackage.HideInWorkshopMenu) { continue; }
                //don't list content packages that only define one sub (they're visible in the "Submarines" section)
                if (contentPackage.Files.Count == 1 && contentPackage.Files[0].Type == ContentType.Submarine) { continue; }
                CreateMyItemFrame(contentPackage, myItemList);
            }
        }

        private void OnItemsReceived(IList<Facepunch.Steamworks.Workshop.Item> itemDetails, GUIListBox listBox)
        {
            listBox.ClearChildren();
            foreach (var item in itemDetails)
            {
                CreateWorkshopItemFrame(item, listBox);
            }

            if (itemDetails.Count == 0 && listBox == subscribedItemList)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.9f), listBox.Content.RectTransform, Anchor.Center), TextManager.Get("NoSubscribedMods"), wrap: true)
                {
                    CanBeFocused = false
                };
            }
        }

        private void CreateWorkshopItemFrame(Facepunch.Steamworks.Workshop.Item item, GUIListBox listBox)
        {
            if (string.IsNullOrEmpty(item.Title))
            {
                return;
            }

            int prevIndex = -1;
            var existingFrame = listBox.Content.FindChild(item);
            if (existingFrame != null)
            {
                prevIndex = listBox.Content.GetChildIndex(existingFrame);
                listBox.Content.RemoveChild(existingFrame);
            }

            var itemFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), listBox.Content.RectTransform, minSize: new Point(0, 80)),
                    style: "ListBoxElement")
            {
                UserData = item
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
            if (itemPreviewSprites.ContainsKey(item.PreviewImageUrl))
            {
                new GUIImage(new RectTransform(new Point(iconSize), innerFrame.RectTransform), itemPreviewSprites[item.PreviewImageUrl], scaleToFit: true)
                {
                    UserData = "previewimage",
                    CanBeFocused = false
                };
            }
            else
            {
                new GUIImage(new RectTransform(new Point(iconSize), innerFrame.RectTransform), SteamManager.Instance.DefaultPreviewImage, scaleToFit: true)
                {
                    UserData = "previewimage",
                    CanBeFocused = false
                };
                try
                {
                    if (!string.IsNullOrEmpty(item.PreviewImageUrl))
                    {
                        string imagePreviewPath = Path.Combine(SteamManager.WorkshopItemPreviewImageFolder, item.Id + ".png");

                        bool isNewImage;
                        lock (pendingPreviewImageDownloads)
                        {
                            isNewImage = !pendingPreviewImageDownloads.Contains(item.PreviewImageUrl);
                            if (isNewImage) { pendingPreviewImageDownloads.Add(item.PreviewImageUrl); }
                        }

                        if (isNewImage)
                        {
                            if (File.Exists(imagePreviewPath))
                            {
                                File.Delete(imagePreviewPath);
                            }
                            Directory.CreateDirectory(SteamManager.WorkshopItemPreviewImageFolder);

                            Uri baseAddress = new Uri(item.PreviewImageUrl);
                            Uri directory = new Uri(baseAddress, "."); // "." == current dir, like MS-DOS
                            string fileName = Path.GetFileName(baseAddress.LocalPath);

                            IRestClient client = new RestClient(directory);
                            var request = new RestRequest(fileName, Method.GET);
                            client.ExecuteAsync(request, response =>
                            {
                                lock (pendingPreviewImageDownloads)
                                {
                                    pendingPreviewImageDownloads.Remove(item.PreviewImageUrl);
                                }
                                OnPreviewImageDownloaded(response, imagePreviewPath);
                                CoroutineManager.StartCoroutine(WaitForItemPreviewDownloaded(item, listBox, imagePreviewPath));
                            });
                        }
                        else
                        {
                            CoroutineManager.StartCoroutine(WaitForItemPreviewDownloaded(item, listBox, imagePreviewPath));
                        }
                    }
                }
                catch (Exception e)
                {
                    lock (pendingPreviewImageDownloads)
                    {
                        pendingPreviewImageDownloads.Remove(item.PreviewImageUrl);
                    }
                    DebugConsole.ThrowError("Downloading the preview image of the Workshop item \"" + TextManager.EnsureUTF8(item.Title) + "\" failed.", e);
                }
            }

            var rightColumn = new GUILayoutGroup(new RectTransform(new Point(innerFrame.Rect.Width - iconSize, innerFrame.Rect.Height), innerFrame.RectTransform), childAnchor: Anchor.CenterLeft)
            {
                IsHorizontal = true,
                Stretch = true,
                RelativeSpacing = 0.05f,
                CanBeFocused = false
            };

            var titleText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), rightColumn.RectTransform), TextManager.EnsureUTF8(item.Title), textAlignment: Alignment.CenterLeft, wrap: true)
            {
                UserData = "titletext",
                CanBeFocused = false
            };

            if (item.Installed)
            {
                GUITickBox enabledTickBox = null;
                try
                {
                    bool? compatible = SteamManager.CheckWorkshopItemCompatibility(item);
                    if (compatible.HasValue && !compatible.Value)
                    {
                        new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.3f), rightColumn.RectTransform),
                            TextManager.Get("WorkshopItemIncompatible"), textColor: Color.Red)
                        {
                            ToolTip = TextManager.Get("WorkshopItemIncompatibleTooltip")
                        };
                    }
                    else
                    {
                        enabledTickBox = new GUITickBox(new RectTransform(new Point(32, 32), rightColumn.RectTransform), null)
                        {
                            ToolTip = TextManager.Get("WorkshopItemEnabled"),
                            UserData = item,
                        };
                        enabledTickBox.Selected = SteamManager.CheckWorkshopItemEnabled(enabledTickBox.UserData as Facepunch.Steamworks.Workshop.Item);
                        enabledTickBox.OnSelected = ToggleItemEnabled;
                    }
                }
                catch (Exception e)
                {
                    if (enabledTickBox != null) { enabledTickBox.Enabled = false; }
                    itemFrame.ToolTip = e.Message;
                    itemFrame.Color = Color.Red;
                    itemFrame.HoverColor = Color.Red;
                    itemFrame.SelectedColor = Color.Red;
                    titleText.TextColor = Color.Red;

                    if (item.Subscribed)
                    {
                        new GUIButton(new RectTransform(new Vector2(0.5f, 0.5f), rightColumn.RectTransform), TextManager.Get("WorkshopItemUnsubscribe"))
                        {
                            UserData = item,
                            OnClicked = (btn, userdata) =>
                            {
                                item.UnSubscribe();
                                subscribedItemList.RemoveChild(subscribedItemList.Content.GetChildByUserData(item));
                                return true;
                            }
                        };
                    }
                }

                if (listBox != publishedItemList && SteamManager.CheckWorkshopItemEnabled(item) && !SteamManager.CheckWorkshopItemUpToDate(item))
                {
                    new GUIButton(new RectTransform(new Vector2(0.4f, 0.5f), rightColumn.RectTransform, Anchor.BottomLeft), text: TextManager.Get("WorkshopItemUpdate"))
                    {
                        UserData = "updatebutton",
                        Font = GUI.SmallFont,
                        OnClicked = (btn, userdata) =>
                        {
                            if (SteamManager.UpdateWorkshopItem(item, out string errorMsg))
                            {
                                new GUIMessageBox("", TextManager.GetWithVariable("WorkshopItemUpdated", "[itemname]", TextManager.EnsureUTF8(item.Title)));
                            }
                            else
                            {
                                DebugConsole.ThrowError(errorMsg);
                                new GUIMessageBox(
                                    TextManager.Get("Error"),
                                    TextManager.GetWithVariables("WorkshopItemUpdateFailed", new string[2] { "[itemname]", "[errormessage]" }, new string[2] { TextManager.EnsureUTF8(item.Title), errorMsg }));
                            }
                            btn.Enabled = false;
                            btn.Visible = false;
                            return true;
                        }
                    };
                }

            }
            else if (item.Downloading)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), rightColumn.RectTransform), TextManager.Get("WorkshopItemDownloading"));
            }
            else
            {
                var downloadBtn = new GUIButton(new RectTransform(new Point((int)(32 * GUI.Scale)), rightColumn.RectTransform), "+", style: null)
                {
                    Font = GUI.LargeFont,
                    Color = new Color(38, 65, 86, 255),
                    HoverColor = new Color(85, 160, 203, 255),
                    TextColor = Color.White,
                    OutlineColor = new Color(72, 103, 124, 255),
                    ToolTip = TextManager.Get("DownloadButton"),
                    ForceUpperCase = true,
                    UserData = item,
                    OnClicked = DownloadItem
                };
            }

            innerFrame.Recalculate();
            listBox.RecalculateChildren();
        }

        private void RemoveItemFromLists(ulong itemID)
        {
            RemoveItemFromList(publishedItemList);
            RemoveItemFromList(subscribedItemList);
            RemoveItemFromList(topItemList);

            void RemoveItemFromList(GUIListBox listBox)
            {
                listBox.Content.RemoveChild(
                    listBox.Content.Children.FirstOrDefault(c => c.UserData is Facepunch.Steamworks.Workshop.Item item && item.Id == itemID));
            }
        }

        private void CreateMyItemFrame(Submarine submarine, GUIListBox listBox)
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

        private void OnPreviewImageDownloaded(IRestResponse response, string previewImagePath)
        {
            if (response.ResponseStatus == ResponseStatus.Completed)
            {
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
        }

        private IEnumerable<object> WaitForItemPreviewDownloaded(Facepunch.Steamworks.Workshop.Item item, GUIListBox listBox, string previewImagePath)
        {
            while (true)
            {
                lock (pendingPreviewImageDownloads)
                {
                    if (!pendingPreviewImageDownloads.Contains(item.PreviewImageUrl)) { break; }
                }

                yield return CoroutineStatus.Running;
            }

            if (File.Exists(previewImagePath))
            {
                Sprite newSprite;
                if (itemPreviewSprites.ContainsKey(item.PreviewImageUrl))
                {
                    newSprite = itemPreviewSprites[item.PreviewImageUrl];
                }
                else
                {
                    newSprite = new Sprite(previewImagePath, sourceRectangle: null);
                    itemPreviewSprites.Add(item.PreviewImageUrl, newSprite);
                }


                var previewImage = listBox.Content.FindChild(item)?.GetChildByUserData("previewimage") as GUIImage;
                if (previewImage != null)
                {
                    previewImage.Sprite = newSprite;
                }
                else
                {
                    CreateWorkshopItemFrame(item, listBox);
                }

                if (modsPreviewFrame.FindChild(item) != null)
                {
                    ShowItemPreview(item, modsPreviewFrame);
                }
                if (browsePreviewFrame.FindChild(item) != null)
                {
                    ShowItemPreview(item, browsePreviewFrame);
                }
            }

            yield return CoroutineStatus.Success;
        }

        private bool DownloadItem(GUIButton btn, object userdata)
        {
            var item = (Facepunch.Steamworks.Workshop.Item)userdata;
            if (!item.Subscribed) { item.Subscribe(); }
            item.Download(onInstalled: RefreshItemLists);

            var parentElement = btn.Parent;
            parentElement.RemoveChild(btn);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), parentElement.RectTransform), TextManager.Get("WorkshopItemDownloading"));
            return true;
        }

        private bool ToggleItemEnabled(GUITickBox tickBox)
        {
            if (!(tickBox.UserData is Facepunch.Steamworks.Workshop.Item item)) { return false; }

            //currently editing the item, don't allow enabling/disabling it
            if (itemEditor?.Id == item.Id) { tickBox.Selected = true; return false; }

            var updateButton = tickBox.Parent.FindChild("updatebutton");

            string errorMsg = "";
            if (tickBox.Selected)
            {
                if (!SteamManager.EnableWorkShopItem(item, false, out errorMsg))
                {
                    tickBox.Visible = false;
                    tickBox.Selected = false;
                    if (tickBox.Parent.GetChildByUserData("titletext") is GUITextBlock titleText) { titleText.TextColor = Color.Red; }
                }
            }
            else
            {
                if (!SteamManager.DisableWorkShopItem(item, out errorMsg))
                {
                    tickBox.Enabled = false;
                }
                GameMain.Config.EnsureCoreContentPackageSelected();
            }
            if (updateButton != null)
            {
                //cannot update if enabling/disabling the item failed or if the item is not enabled
                updateButton.Enabled = tickBox.Enabled && tickBox.Selected;                
            }
            if (!string.IsNullOrEmpty(errorMsg))
            {
                new GUIMessageBox(TextManager.Get("Error"), errorMsg);
            }

            return true;
        }

        private void ShowItemPreview(Facepunch.Steamworks.Workshop.Item item, GUIFrame itemPreviewFrame)
        {
            itemPreviewFrame.ClearChildren();

            if (item == null) return;

            var content = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), itemPreviewFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                UserData = item,
                RelativeSpacing = 0.015f
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.005f), content.RectTransform), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), TextManager.EnsureUTF8(item.Title), textAlignment: Alignment.TopLeft, font: GUI.LargeFont, wrap: true);

            var creatorHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform)) { IsHorizontal = true, Stretch = true };

            new GUITextBlock(new RectTransform(new Vector2(0.3f, 0.0f), creatorHolder.RectTransform), 
                TextManager.Get("WorkshopItemCreator"), textAlignment: Alignment.TopLeft, wrap: true);

            new GUITextBlock(new RectTransform(new Vector2(0.3f, 0.0f), creatorHolder.RectTransform), 
                TextManager.EnsureUTF8(item.OwnerName), textAlignment: Alignment.TopRight, wrap: true);


            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), creatorHolder.RectTransform, Anchor.BottomRight), TextManager.Get("WorkshopShowItemInSteam"), style: null)
            {
                Color = new Color(38, 86, 38, 75),
                HoverColor = new Color(85, 203, 99, 50),
                TextColor = Color.White,
                OutlineColor = new Color(72, 124, 77, 255),
                OnClicked = (btn, userdata) =>
                {
                    SteamManager.OverlayCustomURL("steam://url/CommunityFilePage/" + item.Id);
                    return true;
                }
            };

            var centerArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f,
                Color = Color.Black * 0.9f
            };
            
            if (itemPreviewSprites.ContainsKey(item.PreviewImageUrl))
            {
                new GUIImage(new RectTransform(new Vector2(0.5f, 1.0f), centerArea.RectTransform), itemPreviewSprites[item.PreviewImageUrl], scaleToFit: true);
            }
            else
            {
                new GUIImage(new RectTransform(new Vector2(0.5f, 0.0f), centerArea.RectTransform), SteamManager.Instance.DefaultPreviewImage, scaleToFit: true);
            }
            
            var descriptionContainer = new GUIListBox(new RectTransform(new Vector2(0.5f, 1.0f), centerArea.RectTransform)) { ScrollBarVisible = true };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), descriptionContainer.Content.RectTransform) { MinSize = new Point(0, 5) }, style: null);

            string description = TextManager.EnsureUTF8(item.Description);
            description = ToolBox.RemoveBBCodeTags(description);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), descriptionContainer.Content.RectTransform), description, wrap: true)
            {
                CanBeFocused = false
            };
            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), descriptionContainer.Content.RectTransform) { MinSize = new Point(0, 5) }, style: null);


            //score -------------------------------------
            var scoreContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                RelativeSpacing = 0.02f
            };
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 0.0f), scoreContainer.RectTransform), TextManager.Get("WorkshopItemScore"));
            int starCount = (int)Math.Round(item.Score * 5);
            for (int i = 0; i < 5; i++)
            {
                new GUIImage(new RectTransform(new Point(scoreContainer.Rect.Height), scoreContainer.RectTransform),
                    i < starCount ? "GUIStarIconBright" : "GUIStarIconDark");
            }
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 0.0f), scoreContainer.RectTransform), TextManager.GetWithVariable("WorkshopItemVotes", "[votecount]", (item.VotesUp + item.VotesDown).ToString()));

            //tags ------------------------------------
            var tagContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), tagContainer.RectTransform), TextManager.Get("WorkshopItemTags"));
            List<string> tags = new List<string>();
            for (int i = 0; i < item.Tags.Length && i < 5; i++)
            {
                if (string.IsNullOrEmpty(item.Tags[i])) { continue; }
                string tag = TextManager.Get("Workshop.ContentTag." + item.Tags[i].Replace(" ", ""), true);
                if (string.IsNullOrEmpty(tag)) { tag = item.Tags[i].CapitaliseFirstInvariant(); }
                tags.Add(tag);
            }
            if (tags.Count > 0)
            {
                if (tags.Count == 1)
                {
                    tagContainer.RectTransform.RelativeSize = new Vector2(0.7f, tagContainer.RectTransform.RelativeSize.Y);
                }
                new GUITextBlock(new RectTransform(new Vector2(tags.Count == 1 ? 0.5f : 0.8f, 1.0f), tagContainer.RectTransform, Anchor.TopRight), string.Join(", ", tags))
                {
                    AutoScale = true
                };
            }

            var fileSize = new GUITextBlock(new RectTransform(new Vector2(0.7f, 0.0f), content.RectTransform), TextManager.Get("WorkshopItemFileSize"));
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), fileSize.RectTransform, Anchor.TopRight), MathUtils.GetBytesReadable(item.Installed ? (long)item.Size : item.DownloadSize), textAlignment: Alignment.TopRight);

            //var dateContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), isHorizontal: true);

            var creationDate = new GUITextBlock(new RectTransform(new Vector2(0.7f, 0.0f), content.RectTransform), TextManager.Get("WorkshopItemCreationDate"));
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), creationDate.RectTransform, Anchor.CenterRight), item.Created.ToString("dd.MM.yyyy"), textAlignment: Alignment.TopRight);

            var modificationDate = new GUITextBlock(new RectTransform(new Vector2(0.7f, 0.0f), content.RectTransform), TextManager.Get("WorkshopItemModificationDate"));
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), modificationDate.RectTransform, Anchor.CenterRight), item.Modified.ToString("dd.MM.yyyy"), textAlignment: Alignment.TopRight);

            if (item.Subscribed)
            {
                var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform) { MinSize = new Point(0, 25) }, isHorizontal: true);
                new GUIButton(new RectTransform(new Vector2(0.5f, 0.95f), buttonContainer.RectTransform), TextManager.Get("WorkshopItemUnsubscribe"))
                {
                    UserData = item,
                    OnClicked = (btn, userdata) =>
                    {
                        item.UnSubscribe();
                        subscribedItemList.RemoveChild(subscribedItemList.Content.GetChildByUserData(item));
                        itemPreviewFrame.ClearChildren();
                        return true;
                    }
                };
            }
        }
        
        private void CreateWorkshopItem(Submarine sub)
        {
            string destinationFolder = Path.Combine("Mods", sub.Name);
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
            ContentPackage.List.Add(itemContentPackage);
            GameMain.Config.SelectContentPackage(itemContentPackage);

            itemEditor.Title = sub.Name;
            itemEditor.Tags.Add("Submarine");
            itemEditor.Description = sub.Description;

            if (sub.PreviewImage != null)
            {
                string previewImagePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(itemContentPackage.Path), SteamManager.PreviewImageName));
                try
                {
                    using (Stream s = File.Create(previewImagePath))
                    {
                        sub.PreviewImage.Texture.SaveAsPng(s, (int)sub.PreviewImage.size.X, (int)sub.PreviewImage.size.Y);
                        itemEditor.PreviewImage = previewImagePath;
                    }
                    if (new FileInfo(previewImagePath).Length > 1024 * 1024)
                    {
                        new GUIMessageBox(TextManager.Get("Error"), TextManager.Get("WorkshopItemPreviewImageTooLarge"));
                        itemEditor.PreviewImage = SteamManager.DefaultPreviewImagePath;
                    }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Saving submarine preview image failed.", e);
                    itemEditor.PreviewImage = null;
                }
            }
        }
        private void CreateWorkshopItem(ContentPackage contentPackage)
        {
            //SteamManager.CreateWorkshopItemStaging(new List<ContentFile>(), out itemEditor, out itemContentPackage);

            itemContentPackage = contentPackage;
            SteamManager.CreateWorkshopItemStaging(itemContentPackage, out itemEditor);
            itemEditor.Title = contentPackage.Name;

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
        private bool CreateWorkshopItem(Facepunch.Steamworks.Workshop.Item item)
        {
            if (!item.Installed)
            {
                new GUIMessageBox(TextManager.Get("Error"),
                    TextManager.GetWithVariable("WorkshopErrorInstallRequiredToEdit", "[itemname]", TextManager.EnsureUTF8(item.Title)));
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

            var createItemContent = new GUILayoutGroup(new RectTransform(new Vector2(0.92f, 0.92f), createItemFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            
            var topPanel = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.4f), createItemContent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            var topLeftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.25f, 1.0f), topPanel.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            var topRightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1.0f), topPanel.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            // top right column --------------------------------------------------------------------------------------

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topRightColumn.RectTransform), TextManager.Get("WorkshopItemTitle"));
            var titleBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.15f), topRightColumn.RectTransform), itemEditor.Title);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topRightColumn.RectTransform), TextManager.Get("WorkshopItemDescription"));

            var descriptionContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), topRightColumn.RectTransform));
            var descriptionBox = new GUITextBox(new RectTransform(Vector2.One, descriptionContainer.Content.RectTransform), itemEditor.Description, textAlignment: Alignment.TopLeft, font: GUI.SmallFont, wrap: true);
            descriptionBox.OnTextChanged += (textBox, text) => 
            {
                Vector2 textSize = textBox.Font.MeasureString(descriptionBox.WrappedText);
                textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(descriptionContainer.Rect.Height, (int)textSize.Y + 10));
                descriptionContainer.UpdateScrollBarSize();
                descriptionContainer.BarScroll = 1.0f;
                itemEditor.Description = text;
                return true;
            };
            descriptionContainer.RectTransform.SizeChanged += () => { descriptionBox.Text = descriptionBox.Text; };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topRightColumn.RectTransform), TextManager.Get("WorkshopItemTags"));
            var tagHolder = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.17f), topRightColumn.RectTransform) { MinSize = new Point(0, 50) }, isHorizontal: true)
            {
                Spacing = 5
            };

            HashSet<string> availableTags = new HashSet<string>();
            foreach (string tag in itemEditor.Tags)
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
                var tagBtn = new GUIButton(new RectTransform(new Vector2(0.25f, 0.8f), tagHolder.Content.RectTransform, anchor: Anchor.CenterLeft), tag.CapitaliseFirstInvariant());
                tagBtn.TextBlock.AutoScale = true;
                tagBtn.Color *= 0.5f;
                tagBtn.SelectedColor = Color.LightGreen;
                tagBtn.HoverColor = Color.Lerp(tagBtn.HoverColor, Color.LightGreen, 0.5f);
                tagBtn.Selected = itemEditor.Tags.Any(t => t.ToLowerInvariant() == tag);

                Color defaultTextColor = tagBtn.TextColor;
                tagBtn.TextColor = tagBtn.Selected ? Color.LightGreen : defaultTextColor;

                tagBtn.OnClicked = (btn, userdata) =>
                {
                    if (!tagBtn.Selected)
                    {
                        if (!itemEditor.Tags.Any(t => t.ToLowerInvariant() == tag)) { itemEditor.Tags.Add(tagBtn.Text); }
                        tagBtn.Selected = true;
                        tagBtn.TextColor = Color.LightGreen;
                    }
                    else
                    {
                        itemEditor.Tags.RemoveAll(t => t.ToLowerInvariant() == tagBtn.Text.ToLowerInvariant());
                        tagBtn.Selected = false;
                        tagBtn.TextColor = defaultTextColor;
                    }
                    return true;
                };
            }
            tagHolder.UpdateScrollBarSize();

            // top left column --------------------------------------------------------------------------------------

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topLeftColumn.RectTransform), TextManager.Get("WorkshopItemPreviewImage"));

            var previewIcon = new GUIImage(new RectTransform(new Vector2(1.0f, 0.7f), topLeftColumn.RectTransform), SteamManager.Instance.DefaultPreviewImage, scaleToFit: true);
            new GUIButton(new RectTransform(new Vector2(1.0f, 0.2f), topLeftColumn.RectTransform), TextManager.Get("WorkshopItemBrowse"))
            {
                OnClicked = (btn, userdata) =>
                {
                    try
                    {
                        Barotrauma.OpenFileDialog ofd = new Barotrauma.OpenFileDialog()
                        {
                            Multiselect = true,
                            InitialDirectory = Path.GetFullPath(Path.GetDirectoryName(itemContentPackage.Path)),
                            Filter = TextManager.Get("WorkshopItemPreviewImage") + "|*.png",
                            Title = TextManager.Get("WorkshopItemPreviewImageDialogTitle")
                        };
                        if (ofd.ShowDialog() == DialogResult.OK)
                        {
                            OnPreviewImageSelected(previewIcon, ofd.FileName);
                        }
                    }
                    catch
                    {
                        //use a custom prompt if OpenFileDialog fails (Linux/Mac)
                        var msgBox = new GUIMessageBox(TextManager.Get("WorkshopItemPreviewImageDialogTitle"), "", relativeSize: new Vector2(0.4f, 0.2f),
                            buttons: new string[] { TextManager.Get("Cancel"), TextManager.Get("OK") });

                        var pathBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), msgBox.Content.RectTransform, Anchor.Center) { MinSize = new Point(0,25) });

                        msgBox.Buttons[0].OnClicked += msgBox.Close;
                        msgBox.Buttons[1].OnClicked += msgBox.Close;
                        msgBox.Buttons[1].OnClicked += (btn2, userdata2) =>
                        {
                            if (File.Exists(pathBox.Text))
                            {
                                OnPreviewImageSelected(previewIcon, pathBox.Text);
                            };
                            return true;
                        };
                    }
                    return true;
                }
            };

            //if preview image has not been set, but there's a PreviewImage file inside the mod folder, use that by default
            if (string.IsNullOrEmpty(itemEditor.PreviewImage))
            {
                string previewImagePath = Path.Combine(Path.GetDirectoryName(itemContentPackage.Path), SteamManager.PreviewImageName);
                if (File.Exists(previewImagePath))
                {
                    itemEditor.PreviewImage = Path.GetFullPath(previewImagePath);
                }
            }
            if (!string.IsNullOrEmpty(itemEditor.PreviewImage))
            {
                itemEditor.PreviewImage = Path.GetFullPath(itemEditor.PreviewImage);
                if (itemPreviewSprites.ContainsKey(itemEditor.PreviewImage))
                {
                    itemPreviewSprites[itemEditor.PreviewImage].Remove();
                }
                var newPreviewImage = new Sprite(itemEditor.PreviewImage, sourceRectangle: null);
                previewIcon.Sprite = newPreviewImage;
                itemPreviewSprites[itemEditor.PreviewImage] = newPreviewImage;
            }

            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), topLeftColumn.RectTransform), TextManager.Get("WorkshopItemCorePackage"))
            {
                ToolTip = TextManager.Get("WorkshopItemCorePackageTooltip"),
                Selected = itemContentPackage.CorePackage,
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
                            itemContentPackage.CorePackage = tickbox.Selected;
                        }
                    }
                    else
                    {
                        itemContentPackage.CorePackage = false;
                    }
                    return true;
                }
            };

            // file list --------------------------------------------------------------------------------------

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), createItemContent.RectTransform), style: null);

            var fileListTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), createItemContent.RectTransform), TextManager.Get("WorkshopItemFiles"));
            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), fileListTitle.RectTransform, Anchor.CenterRight), TextManager.Get("WorkshopItemShowFolder"))
            {
                IgnoreLayoutGroups = true,
                OnClicked = (btn, userdata) => { System.Diagnostics.Process.Start(Path.GetFullPath(Path.GetDirectoryName(itemContentPackage.Path))); return true; }
            };
            createItemFileList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.35f), createItemContent.RectTransform));
            RefreshCreateItemFileList();

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), createItemContent.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.05f
            };

            new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonContainer.RectTransform, Anchor.TopRight), TextManager.Get("WorkshopItemRefreshFileList"))
            {
                ToolTip = TextManager.Get("WorkshopItemRefreshFileListTooltip"),
                OnClicked = (btn, userdata) =>
                {
                    itemContentPackage = new ContentPackage(itemContentPackage.Path);
                    RefreshCreateItemFileList();
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonContainer.RectTransform, Anchor.TopRight), TextManager.Get("WorkshopItemAddFiles"))
            {
                OnClicked = (btn, userdata) =>
                {
                    try
                    {
                        Barotrauma.OpenFileDialog ofd = new Barotrauma.OpenFileDialog()
                        {
                            InitialDirectory = Path.GetFullPath(Path.GetDirectoryName(itemContentPackage.Path)),
                            Title = TextManager.Get("workshopitemaddfiles"),
                            Multiselect = true
                        };
                        if (ofd.ShowDialog() == DialogResult.OK)
                        {
                            OnAddFilesSelected(ofd.FileNames);
                            RefreshMyItemList();
                        }
                    }
                    catch
                    {
                        //use a custom prompt if OpenFileDialog fails (Linux/Mac)
                        var msgBox = new GUIMessageBox(TextManager.Get("workshopitemaddfiles"), "", relativeSize: new Vector2(0.4f, 0.2f),
                        buttons: new string[] { TextManager.Get("Cancel"), TextManager.Get("OK") });

                        var pathBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), msgBox.Content.RectTransform, Anchor.Center) { MinSize = new Point(0, 25) });

                        msgBox.Buttons[0].OnClicked += msgBox.Close;
                        msgBox.Buttons[1].OnClicked += msgBox.Close;
                        msgBox.Buttons[1].OnClicked += (btn2, userdata2) =>
                        {
                            if (string.IsNullOrEmpty(pathBox?.Text)) { return true; }
                            string[] filePaths = pathBox.Text.Split(',');
                            if (File.Exists(pathBox.Text))
                            {
                                OnAddFilesSelected(filePaths);
                            };
                            return true;
                        };
                    }

                    return true;
                }
            };


            //the item has been already published if it has a non-zero ID -> allow adding a changenote
            if (itemEditor.Id > 0)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), createItemContent.RectTransform), TextManager.Get("WorkshopItemChangenote"))
                {
                    ToolTip = TextManager.Get("WorkshopItemChangenoteTooltip")
                };


                var changenoteContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.2f), createItemContent.RectTransform));
                var changenoteBox = new GUITextBox(new RectTransform(Vector2.One, changenoteContainer.Content.RectTransform), "", textAlignment: Alignment.TopLeft, wrap: true)
                {
                    ToolTip = TextManager.Get("WorkshopItemChangenoteTooltip")
                };
                changenoteBox.OnTextChanged += (textBox, text) =>
                {
                    Vector2 textSize = textBox.Font.MeasureString(changenoteBox.WrappedText);
                    textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(changenoteContainer.Rect.Height, (int)textSize.Y + 10));
                    changenoteContainer.UpdateScrollBarSize();
                    changenoteContainer.BarScroll = 1.0f;
                    itemEditor.ChangeNote = text;
                    return true;
                };
            }

            var bottomButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), createItemContent.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.05f
            };

            if (itemEditor.Id > 0)
            {
                new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), bottomButtonContainer.RectTransform),
                    TextManager.Get("WorkshopItemDelete"), style: "GUIButtonLarge")
                {
                    ToolTip = TextManager.Get("WorkshopItemDeleteTooltip"),
                    TextColor = Color.Red,
                    OnClicked = (btn, userData) =>
                    {
                        if (itemEditor == null) { return false; }
                        var deleteVerification = new GUIMessageBox("", TextManager.GetWithVariable("WorkshopItemDeleteVerification", "[itemname]", itemEditor.Title),
                            new string[] {  TextManager.Get("Yes"), TextManager.Get("No") });
                        deleteVerification.Buttons[0].OnClicked = (yesBtn, userdata) =>
                        {
                            if (itemEditor == null) { return false; }
                            RemoveItemFromLists(itemEditor.Id);
                            itemEditor.Delete();
                            itemEditor = null;
                            SelectTab(Tab.Browse);
                            deleteVerification.Close();
                            createItemFrame.ClearChildren();
                            itemContentPackage.SteamWorkshopUrl = "";
                            itemContentPackage.Save(itemContentPackage.Path);
                            return true;
                        };
                        deleteVerification.Buttons[1].OnClicked = deleteVerification.Close;
                        return true;
                    }
                };
            }
            var publishBtn = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), bottomButtonContainer.RectTransform, Anchor.CenterRight),
                TextManager.Get(itemEditor.Id > 0 ? "WorkshopItemUpdate" : "WorkshopItemPublish"), style: "GUIButtonLarge")
            {
                IgnoreLayoutGroups = true,
                ToolTip = TextManager.Get("WorkshopItemPublishTooltip"),
                OnClicked = (btn, userData) => 
                {
                    itemEditor.Title = titleBox.Text;
                    itemEditor.Description = descriptionBox.Text;
                    if (string.IsNullOrWhiteSpace(itemEditor.Title))
                    {
                        titleBox.Flash(Color.Red);
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(itemEditor.Description))
                    {
                        descriptionBox.Flash(Color.Red);
                        return false;
                    }
                    if (createItemFileList.Content.CountChildren == 0)
                    {
                        createItemFileList.Flash(Color.Red);
                    }

                    if (!itemContentPackage.CheckErrors(out List<string> errorMessages))
                    {
                        new GUIMessageBox(
                            TextManager.GetWithVariable("workshopitempublishfailed", "[itemname]", itemEditor.Title),
                            string.Join("\n", errorMessages));
                        return false;
                    }

                    PublishWorkshopItem();
                    return true;
                }
            };
            publishBtn.TextBlock.AutoScale = true;
        }

        private void OnPreviewImageSelected(GUIImage previewImageElement, string filePath)
        {
            string previewImagePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(itemContentPackage.Path), SteamManager.PreviewImageName));
            if (new FileInfo(filePath).Length > 1024 * 1024)
            {
                new GUIMessageBox(TextManager.Get("Error"), TextManager.Get("WorkshopItemPreviewImageTooLarge"));
                return;
            }

            if (filePath != previewImagePath)
            {
                File.Copy(filePath, previewImagePath, overwrite: true);
            }

            if (itemPreviewSprites.ContainsKey(previewImagePath))
            {
                itemPreviewSprites[previewImagePath].Remove();
            }
            var newPreviewImage = new Sprite(previewImagePath, sourceRectangle: null);
            previewImageElement.Sprite = newPreviewImage;
            itemPreviewSprites[previewImagePath] = newPreviewImage;
            itemEditor.PreviewImage = previewImagePath;
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
                string destinationPath;

                //file is not inside the mod folder, we need to move it
                if (filePathRelativeToModFolder.StartsWith("..") || 
                    Path.GetPathRoot(Environment.CurrentDirectory) != Path.GetPathRoot(file))
                {
                    destinationPath = Path.Combine(modFolder, Path.GetFileName(file));
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
                else
                {
                    destinationPath = Path.Combine(modFolder, filePathRelativeToModFolder);
                }
                itemContentPackage.AddFile(destinationPath, ContentType.None);
            }
            itemContentPackage.Save(itemContentPackage.Path);
            RefreshCreateItemFileList();
        }
        
        private void RefreshCreateItemFileList()
        {
            createItemFileList.ClearChildren();
            if (itemContentPackage == null) return;
            var contentTypes = Enum.GetValues(typeof(ContentType));
            
            foreach (ContentFile contentFile in itemContentPackage.Files)
            {
                bool illegalPath = !ContentPackage.IsModFilePathAllowed(contentFile);
                //string pathInStagingFolder = Path.Combine(SteamManager.WorkshopItemStagingFolder, contentFile.Path);
                //bool fileInStagingFolder = File.Exists(pathInStagingFolder);
                bool fileExists = File.Exists(contentFile.Path);

                var fileFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.12f), createItemFileList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    style: "ListBoxElement")
                {
                    UserData = contentFile
                };

                var content = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 1.0f), fileFrame.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                var tickBox = new GUITickBox(new RectTransform(new Vector2(0.1f, 0.8f), content.RectTransform), "")
                {
                    Selected = fileExists && !illegalPath,
                    Enabled = false,
                    ToolTip = TextManager.Get(illegalPath ? "WorkshopItemFileNotIncluded" : "WorkshopItemFileIncluded")
                };

                var nameText = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), content.RectTransform, Anchor.CenterLeft), contentFile.Path, font: GUI.SmallFont)
                {
                    ToolTip = contentFile.Path
                };
                if (!fileExists)
                {
                    nameText.TextColor = Color.Red;
                    tickBox.ToolTip = TextManager.Get("WorkshopItemFileNotFound");
                }
                else if (illegalPath && !ContentPackage.List.Any(cp => cp.Files.Any(f => Path.GetFullPath(f.Path) == Path.GetFullPath(contentFile.Path))))
                {
                    nameText.TextColor = Color.Red;
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

                new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), content.RectTransform), TextManager.Get("Delete"))
                {
                    OnClicked = (btn, userdata) =>
                    {
                        itemContentPackage.RemoveFile(contentFile);
                        itemContentPackage.Save(itemContentPackage.Path);
                        RefreshCreateItemFileList();
                        RefreshMyItemList();
                        return true;
                    }
                };

                content.Recalculate();
                tickBox.RectTransform.NonScaledSize = new Point(content.Rect.Height);
                nameText.Text = ToolBox.LimitString(nameText.Text, nameText.Font, maxWidth: nameText.Rect.Width);
            }
        }

        private void PublishWorkshopItem()
        {
            if (itemContentPackage == null || itemEditor == null) return;
            
            SteamManager.StartPublishItem(itemContentPackage, itemEditor);            
            CoroutineManager.StartCoroutine(WaitForPublish(itemEditor), "WaitForPublish");
        }

        private IEnumerable<object> WaitForPublish(Facepunch.Steamworks.Workshop.Editor item)
        {
            string pleaseWaitText = TextManager.Get("WorkshopPublishPleaseWait");
            var msgBox = new GUIMessageBox(
                pleaseWaitText,
                TextManager.GetWithVariable("WorkshopPublishInProgress", "[itemname]", TextManager.EnsureUTF8(item.Title)), 
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
            while (item.Publishing)
            {
                msgBox.Header.Text = pleaseWaitText + new string('.', ((int)Timing.TotalTime % 3 + 1));
                yield return CoroutineStatus.Running;
            }
            msgBox.Close();

            if (string.IsNullOrEmpty(item.Error))
            {
                new GUIMessageBox("", TextManager.GetWithVariable("WorkshopItemPublished", "[itemname]", TextManager.EnsureUTF8(item.Title)));
            }
            else
            {
                string errorMsg = item.ErrorCode.HasValue ?
                    TextManager.GetWithVariable("WorkshopPublishError." + item.ErrorCode.Value.ToString(), "[savepath]", SaveUtil.SaveFolder, returnNull: true) :
                    null;

                if (errorMsg == null)
                {
                    new GUIMessageBox(
                        TextManager.Get("Error"),
                        TextManager.GetWithVariable("WorkshopItemPublishFailed", "[itemname]", TextManager.EnsureUTF8(item.Title)) + " " + item.Error);
                }
                else
                {
                    new GUIMessageBox(TextManager.Get("Error"), errorMsg);
                }
            }

            createItemFrame.ClearChildren();
            RefreshItemLists();
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
        }
        
        #endregion
    }
}
