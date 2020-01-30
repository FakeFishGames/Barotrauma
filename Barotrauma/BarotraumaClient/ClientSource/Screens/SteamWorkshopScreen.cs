using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        private readonly List<GUIButton> tabButtons = new List<GUIButton>();

        private readonly HashSet<string> pendingPreviewImageDownloads = new HashSet<string>();
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
            GameMain.Instance.OnResolutionChanged += CreateUI;
            CreateUI();
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

            createItemFrame = new GUIFrame(new RectTransform(new Vector2(0.58f, 1.0f), tabs[(int)Tab.Publish].RectTransform, Anchor.TopRight), style: null);

            SelectTab(Tab.Mods);
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

        public void SubscribeToPackages(List<string> packageUrls)
        {
            foreach (string url in packageUrls)
            {
                SteamManager.SubscribeToWorkshopItem(url);
            }
            GameMain.SteamWorkshopScreen.Select();
        }

        private void RefreshSubscribedItems()
        {
            SteamManager.GetSubscribedWorkshopItems((items) =>
            {
                //filter out the items published by the player (they're shown in the publish tab)
                var mySteamID = SteamManager.GetSteamID();
                OnItemsReceived(GetVisibleItems(items.Where(it => it.Owner.Id != mySteamID)), subscribedItemList);
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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), myItemList.Content.RectTransform), TextManager.Get("WorkshopLabelContentPackages"), 
                textAlignment: Alignment.CenterLeft, font: GUI.SubHeadingFont)
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
            if (itemPreviewSprites.ContainsKey(item?.PreviewImageUrl))
            {
                new GUIImage(new RectTransform(new Point(iconSize), innerFrame.RectTransform), itemPreviewSprites[item?.PreviewImageUrl], scaleToFit: true)
                {
                    UserData = "previewimage",
                    CanBeFocused = false
                };
            }
            else
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
                            isNewImage = !pendingPreviewImageDownloads.Contains(item?.PreviewImageUrl);
                            if (isNewImage) { pendingPreviewImageDownloads.Add(item?.PreviewImageUrl); }
                        }

                        if (isNewImage)
                        {
                            if (File.Exists(imagePreviewPath))
                            {
                                File.Delete(imagePreviewPath);
                            }
                            Directory.CreateDirectory(SteamManager.WorkshopItemPreviewImageFolder);

                            Uri baseAddress = new Uri(item?.PreviewImageUrl);
                            Uri directory = new Uri(baseAddress, "."); // "." == current dir, like MS-DOS
                            string fileName = Path.GetFileName(baseAddress.LocalPath);

                            IRestClient client = new RestClient(directory);
                            var request = new RestRequest(fileName, Method.GET);
                            client.ExecuteAsync(request, response =>
                            {
                                lock (pendingPreviewImageDownloads)
                                {
                                    pendingPreviewImageDownloads.Remove(item?.PreviewImageUrl);
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
                        pendingPreviewImageDownloads.Remove(item?.PreviewImageUrl);
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

            if ((item?.IsSubscribed ?? false) && (item?.IsInstalled ?? false))
            {
                GUITickBox enabledTickBox = null;
                try
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
                        enabledTickBox = new GUITickBox(new RectTransform(new Point(32, 32), rightColumn.RectTransform), null)
                        {
                            ToolTip = TextManager.Get("WorkshopItemEnabled"),
                            UserData = item,
                        };
                        enabledTickBox.Selected = SteamManager.CheckWorkshopItemEnabled(item);
                        enabledTickBox.OnSelected = ToggleItemEnabled;
                    }
                }
                catch (Exception e)
                {
                    if (enabledTickBox != null) { enabledTickBox.Enabled = false; }
                    itemFrame.ToolTip = e.Message;
                    itemFrame.Color = GUI.Style.Red;
                    itemFrame.HoverColor = GUI.Style.Red;
                    itemFrame.SelectedColor = GUI.Style.Red;
                    titleText.TextColor = GUI.Style.Red;

                    if (item?.IsSubscribed ?? false)
                    {
                        new GUIButton(new RectTransform(new Vector2(0.5f, 0.5f), rightColumn.RectTransform), TextManager.Get("WorkshopItemUnsubscribe"))
                        {
                            UserData = item,
                            OnClicked = (btn, userdata) =>
                            {
                                item?.Unsubscribe();
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
                                new GUIMessageBox("", TextManager.GetWithVariable("WorkshopItemUpdated", "[itemname]", item?.Title));
                            }
                            else
                            {
                                DebugConsole.ThrowError(errorMsg);
                                new GUIMessageBox(
                                    TextManager.Get("Error"),
                                    TextManager.GetWithVariables("WorkshopItemUpdateFailed", new string[2] { "[itemname]", "[errormessage]" }, new string[2] { item?.Title, errorMsg }));
                            }
                            btn.Enabled = false;
                            btn.Visible = false;
                            return true;
                        }
                    };
                }

            }
            else if (item?.IsDownloading ?? false)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), rightColumn.RectTransform), TextManager.Get("WorkshopItemDownloading"));
            }
            else
            {
                var downloadBtn = new GUIButton(new RectTransform(new Point((int)(32 * GUI.Scale)), rightColumn.RectTransform), "", style: "GUIPlusButton")
                {
                    ToolTip = TextManager.Get("DownloadButton"),
                    ForceUpperCase = true,
                    UserData = item
                };
                downloadBtn.OnClicked = (btn, userdata) => { DownloadItem(itemFrame, downloadBtn, item); return true; };
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
                    listBox.Content.Children.FirstOrDefault(c => c.UserData is Steamworks.Ugc.Item? && (c.UserData as Steamworks.Ugc.Item?)?.Id == itemID));
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

        private IEnumerable<object> WaitForItemPreviewDownloaded(Steamworks.Ugc.Item? item, GUIListBox listBox, string previewImagePath)
        {
            while (true)
            {
                lock (pendingPreviewImageDownloads)
                {
                    if (!pendingPreviewImageDownloads.Contains(item?.PreviewImageUrl)) { break; }
                }

                yield return CoroutineStatus.Running;
            }

            if (File.Exists(previewImagePath))
            {
                Sprite newSprite;
                if (itemPreviewSprites.ContainsKey(item?.PreviewImageUrl))
                {
                    newSprite = itemPreviewSprites[item?.PreviewImageUrl];
                }
                else
                {
                    newSprite = new Sprite(previewImagePath, sourceRectangle: null);
                    itemPreviewSprites.Add(item?.PreviewImageUrl, newSprite);
                }

                if (listBox.Content.FindChild(item)?.GetChildByUserData("previewimage") is GUIImage previewImage)
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

        private bool DownloadItem(GUIComponent frame, GUIButton downloadButton, Steamworks.Ugc.Item? item)
        {
            if (item == null) { return false; }

            if (!(item?.IsSubscribed ?? false)) { item?.Subscribe(); }

            var parentElement = downloadButton.Parent;
            parentElement.RemoveChild(downloadButton);
            var textBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), parentElement.RectTransform), TextManager.Get("WorkshopItemDownloading"));

            item?.Download(onInstalled: () =>
            {
                if (SteamManager.EnableWorkShopItem(item, false, out _))
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

        private bool ToggleItemEnabled(GUITickBox tickBox)
        {
            if (!(tickBox.UserData is Steamworks.Ugc.Item?)) { return false; }

            var item = tickBox.UserData as Steamworks.Ugc.Item?;
            if (item == null) { return false; }

            //currently editing the item, don't allow enabling/disabling it
            if (itemEditor?.FileId == item?.Id) { tickBox.Selected = true; return false; }

            var updateButton = tickBox.Parent.FindChild("updatebutton");

            string errorMsg;
            if (tickBox.Selected)
            {
                if (!SteamManager.EnableWorkShopItem(item, false, out errorMsg))
                {
                    tickBox.Visible = false;
                    tickBox.Selected = false;
                    if (tickBox.Parent.GetChildByUserData("titletext") is GUITextBlock titleText) { titleText.TextColor = GUI.Style.Red; }
                }
            }
            else
            {
                if (!SteamManager.DisableWorkShopItem(item, false, out errorMsg))
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
                    t.AutoScale = true;
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
                        SteamManager.DisableWorkShopItem(item, true, out _);
                        item?.Unsubscribe();
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

            itemEditor = itemEditor?.WithTitle(sub.Name).WithTag("Submarine").WithDescription(sub.Description);

            if (sub.PreviewImage != null)
            {
                string previewImagePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(itemContentPackage.Path), SteamManager.PreviewImageName));
                try
                {
                    using (Stream s = File.Create(previewImagePath))
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
                tagBtn.TextBlock.AutoScale = true;
                tagBtn.Selected = itemEditor?.Tags?.Any(t => t.ToLowerInvariant() == tag) ?? false;

                Color defaultTextColor = tagBtn.TextColor;
                tagBtn.TextColor = tagBtn.Selected ? GUI.Style.Green : defaultTextColor;

                tagBtn.OnClicked = (btn, userdata) =>
                {
                    if (!tagBtn.Selected)
                    {
                        if (!(itemEditor?.Tags?.Any(t => t.ToLowerInvariant() == tag) ?? false)) { itemEditor = itemEditor?.WithTag(tagBtn.Text); }
                        tagBtn.Selected = true;
                        tagBtn.TextColor = GUI.Style.Green;
                    }
                    else
                    {
                        itemEditor?.Tags?.RemoveAll(t => t.ToLowerInvariant() == tagBtn.Text.ToLowerInvariant());
                        tagBtn.Selected = false;
                        tagBtn.TextColor = defaultTextColor;
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

            var fileListTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), createItemContent.RectTransform), TextManager.Get("WorkshopItemFiles"), font: GUI.SubHeadingFont);
            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), fileListTitle.RectTransform, Anchor.CenterRight), TextManager.Get("WorkshopItemShowFolder"), style: "GUIButtonSmall")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (btn, userdata) => { ToolBox.OpenFileWithShell(Path.GetFullPath(Path.GetDirectoryName(itemContentPackage.Path))); return true; }
            };
            createItemFileList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.35f), createItemContent.RectTransform));
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
                            TaskPool.Add(Steamworks.SteamUGC.DeleteFileAsync(itemEditor.Value.FileId),
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
                    CanBeFocused = false,
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
                    nameText.TextColor = GUI.Style.Red;
                    tickBox.ToolTip = TextManager.Get("WorkshopItemFileNotFound");
                }
                else if (illegalPath && !ContentPackage.List.Any(cp => cp.Files.Any(f => Path.GetFullPath(f.Path) == Path.GetFullPath(contentFile.Path))))
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

                new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), content.RectTransform), TextManager.Get("Delete"), style: "GUIButtonSmall")
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
                fileFrame.RectTransform.MinSize = 
                    new Point(0, (int)(content.RectTransform.Children.Max(c => c.MinSize.Y) / content.RectTransform.RelativeSize.Y));
                nameText.Text = ToolBox.LimitString(nameText.Text, nameText.Font, maxWidth: nameText.Rect.Width);
            }
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
                    if (!itemEditor.Value.Tags.Contains("unstable")) { itemEditor.Value.Tags.Add("unstable"); }
                    CoroutineManager.StartCoroutine(WaitForPublish(workshopPublishStatus), "WaitForPublish");
                }
                msgBox.Close();
                return true;
            };
            msgBox.Buttons[1].OnClicked += msgBox.Close;
#else
            var workshopPublishStatus = SteamManager.StartPublishItem(itemContentPackage, itemEditor);
            if (workshopPublishStatus == null) { return; }
            if (itemEditor.Value.Tags.Contains("unstable")) { itemEditor.Value.Tags.Remove("unstable"); }
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
                        TextManager.GetWithVariable("WorkshopItemPublishFailed", "[itemname]", item?.Title) + " Task ended with status "+workshopPublishStatus?.TaskStatus?.ToString());
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
        }
        
#endregion
    }
}
