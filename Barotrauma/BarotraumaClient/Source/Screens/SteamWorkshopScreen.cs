using Barotrauma.Extensions;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;

namespace Barotrauma
{
    class SteamWorkshopScreen : Screen
    {
        private GUIFrame menu;
        private GUIListBox subscribedItemList, topItemList;

        private GUIListBox publishedItemList, myItemList;

        //shows information of a selected workshop item
        private GUIFrame itemPreviewFrame;

        //menu for creating new items
        private GUIFrame createItemFrame;
        //listbox that shows the files included in the item being created
        private GUIListBox createItemFileList;

        private HashSet<string> pendingPreviewImageDownloads = new HashSet<string>();
        private Dictionary<string, Sprite> itemPreviewSprites = new Dictionary<string, Sprite>();

        private enum Tab
        {
            Browse,
            Publish
        }

        private GUIFrame[] tabs;

        private ContentPackage itemContentPackage;
        private Facepunch.Steamworks.Workshop.Editor itemEditor;
        
        public SteamWorkshopScreen()
        {
            int width = Math.Min(GameMain.GraphicsWidth - 160, 1000);
            int height = Math.Min(GameMain.GraphicsHeight - 160, 700);

            Rectangle panelRect = new Rectangle(0, 0, width, height);

            tabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length];

            menu = new GUIFrame(new RectTransform(new Vector2(0.8f, 0.9f), GUI.Canvas, Anchor.Center));

            var buttonContainer = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.05f), menu.RectTransform, Anchor.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.05f) }, style: null);
            var tabContainer = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.85f), menu.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.0f, 0.05f) }, style: null);
            
            GUIButton backButton = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), buttonContainer.RectTransform),
                TextManager.Get("Back"))
            {
                OnClicked = GameMain.MainMenuScreen.SelectTab
            };
            backButton.SelectedColor = backButton.Color;

            int i = 0;
            foreach (Tab tab in Enum.GetValues(typeof(Tab)))
            {
                GUIButton tabButton = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), buttonContainer.RectTransform) { RelativeOffset = new Vector2(0.4f + 0.15f * i, 0.0f) },
                    tab.ToString())
                {
                    UserData = tab,
                    OnClicked = (btn, userData) => { SelectTab((Tab)userData); return true; }
                };
                i++;
            }


            //-------------------------------------------------------------------------------
            //Browse tab
            //-------------------------------------------------------------------------------

            tabs[(int)Tab.Browse] = new GUIFrame(new RectTransform(Vector2.One, tabContainer.RectTransform, Anchor.Center), style: null);

            var listContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), tabs[(int)Tab.Browse].RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listContainer.RectTransform), TextManager.Get("SubscribedMods"));
            subscribedItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.7f), listContainer.RectTransform))
            {
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    if (GUI.MouseOn is GUIButton || GUI.MouseOn?.Parent is GUIButton) { return false; }
                    ShowItemPreview(userdata as Facepunch.Steamworks.Workshop.Item);
                    return true;
                }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listContainer.RectTransform), TextManager.Get("PopularMods"));
            topItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), listContainer.RectTransform))
            {
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    ShowItemPreview(userdata as Facepunch.Steamworks.Workshop.Item);
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), listContainer.RectTransform), TextManager.Get("FindModsButton"))
            {
                OnClicked = (btn, userdata) =>
                {
                    System.Diagnostics.Process.Start("steam://url/SteamWorkshopPage/" + SteamManager.AppID);
                    return true;
                }
            };

            itemPreviewFrame = new GUIFrame(new RectTransform(new Vector2(0.58f, 1.0f), tabs[(int)Tab.Browse].RectTransform, Anchor.TopRight), style: "InnerFrame");

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
                    myItemList.Deselect();
                    if (userdata is Facepunch.Steamworks.Workshop.Item item)
                    {
                        if (!item.Installed) { return false; }
                        CreateWorkshopItem(item);
                        ShowCreateItemFrame();
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

            new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), leftColumn.RectTransform), TextManager.Get("CreateWorkshopItem"))
            {
                OnClicked = (btn, userData) => 
                {
                    CreateWorkshopItem();
                    ShowCreateItemFrame();
                    return true;
                }
            };

            createItemFrame = new GUIFrame(new RectTransform(new Vector2(0.58f, 1.0f), tabs[(int)Tab.Publish].RectTransform, Anchor.TopRight), style: "InnerFrame");

            SelectTab(Tab.Browse);
        }

        public override void Select()
        {
            base.Select();

            itemPreviewFrame.ClearChildren();
            createItemFrame.ClearChildren();
            itemContentPackage = null;
            itemEditor = null;

            RefreshItemLists();
            SelectTab(Tab.Browse);
        }

        private void SelectTab(Tab tab)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].Visible = i == (int)tab;
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
            SteamManager.GetSubscribedWorkshopItems((items) => { OnItemsReceived(items, subscribedItemList); });
            SteamManager.GetPopularWorkshopItems((items) => { OnItemsReceived(items, topItemList); }, 5);
            SteamManager.GetPublishedWorkshopItems((items) => { OnItemsReceived(items, publishedItemList); });

            myItemList.ClearChildren();
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), myItemList.Content.RectTransform), TextManager.Get("WorkshopLabelSubmarines"), textAlignment: Alignment.Center, font: GUI.LargeFont)
            {
                CanBeFocused = false
            };
            foreach (Submarine sub in Submarine.SavedSubmarines)
            {
                if (sub.HasTag(SubmarineTag.HideInMenus)) { continue; }
                //ignore subs that are part of some content package
                string subPath = Path.GetFullPath(sub.FilePath);
                if (ContentPackage.List.Any(cp => cp.Files.Any(f => f.Type == ContentType.Submarine && Path.GetFullPath(f.Path) == subPath)))
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

            var existingFrame = listBox.Content.FindChild(item);
            if (existingFrame != null) { listBox.Content.RemoveChild(existingFrame); }

            var itemFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), listBox.Content.RectTransform, minSize: new Point(0, 80)),
                    style: "ListBoxElement")
            {
                UserData = item
            };

            var innerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), itemFrame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                RelativeSpacing = 0.1f,
                CanBeFocused = false,
                Stretch = true
            };

            int iconSize = innerFrame.Rect.Height;
            if (itemPreviewSprites.ContainsKey(item.PreviewImageUrl))
            {
                new GUIImage(new RectTransform(new Point(iconSize), innerFrame.RectTransform), itemPreviewSprites[item.PreviewImageUrl], scaleToFit: true)
                {
                    CanBeFocused = false
                };
            }
            else
            {
                new GUIImage(new RectTransform(new Point(iconSize), innerFrame.RectTransform), SteamManager.Instance.DefaultPreviewImage, scaleToFit: true)
                {
                    CanBeFocused = false
                };
                try
                {
                    if (!string.IsNullOrEmpty(item.PreviewImageUrl))
                    {
                        string imagePreviewPath = Path.Combine(SteamManager.WorkshopItemPreviewImageFolder, item.Id + ".png");
                        if (!pendingPreviewImageDownloads.Contains(item.PreviewImageUrl))
                        {
                            pendingPreviewImageDownloads.Add(item.PreviewImageUrl);
                            using (WebClient client = new WebClient())
                            {
                                if (File.Exists(imagePreviewPath))
                                {
                                    File.Delete(imagePreviewPath);
                                }
                                Directory.CreateDirectory(SteamManager.WorkshopItemPreviewImageFolder);
                                client.DownloadFileAsync(new Uri(item.PreviewImageUrl), imagePreviewPath);
                                CoroutineManager.StartCoroutine(WaitForItemPreviewDownloaded(item, listBox, imagePreviewPath));
                                client.DownloadFileCompleted += (sender, args) =>
                                {
                                    pendingPreviewImageDownloads.Remove(item.PreviewImageUrl);
                                };
                            }
                        }
                        else
                        {
                            CoroutineManager.StartCoroutine(WaitForItemPreviewDownloaded(item, listBox, imagePreviewPath));
                        }
                    }
                }
                catch (Exception e)
                {
                    pendingPreviewImageDownloads.Remove(item.PreviewImageUrl);
                    DebugConsole.ThrowError("Downloading the preview image of the Workshop item \"" + item.Title + "\" failed.", e);
                }
            }

            var rightColumn = new GUILayoutGroup(new RectTransform(new Point(innerFrame.Rect.Width - iconSize, innerFrame.Rect.Height), innerFrame.RectTransform), childAnchor: Anchor.TopRight)
            {
                Stretch = true,
                CanBeFocused = false,
                RelativeSpacing = 0.05f
            };

            var titleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), rightColumn.RectTransform), item.Title, textAlignment: Alignment.CenterLeft)
            {
                CanBeFocused = false
            };

            if (item.Installed)
            {
                if (listBox != publishedItemList && SteamManager.CheckWorkshopItemEnabled(item) && !SteamManager.CheckWorkshopItemUpToDate(item))
                {
                    new GUIButton(new RectTransform(new Vector2(0.4f, 0.5f), rightColumn.RectTransform, Anchor.BottomLeft), text: "Update")
                    {
                        UserData = "updatebutton",
                        IgnoreLayoutGroups = true,
                        OnClicked = (btn, userdata) =>
                        {
                            SteamManager.UpdateWorkshopItem(item, out string errorMsg);
                            btn.Enabled = false;
                            btn.Visible = false;
                            return true;
                        }
                    };
                }

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
                        enabledTickBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 0.5f), rightColumn.RectTransform), TextManager.Get("WorkshopItemEnabled"))
                        {
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
                                return true;
                            }
                        };
                    }
                }
            }
            else if (item.Downloading)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), rightColumn.RectTransform), TextManager.Get("WorkshopItemDownloading"));
            }
            else
            {
                var downloadBtn = new GUIButton(new RectTransform(new Vector2(0.5f, 0.5f), rightColumn.RectTransform),
                    TextManager.Get("DownloadButton"))
                {
                    UserData = item,
                    OnClicked = DownloadItem
                };
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

        private IEnumerable<object> WaitForItemPreviewDownloaded(Facepunch.Steamworks.Workshop.Item item, GUIListBox listBox, string previewImagePath)
        {
            while (pendingPreviewImageDownloads.Contains(item.PreviewImageUrl))
            {
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

                CreateWorkshopItemFrame(item, listBox);
                if (itemPreviewFrame.FindChild(item) != null)
                {
                    ShowItemPreview(item);
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
            Facepunch.Steamworks.Workshop.Item item = tickBox.UserData as Facepunch.Steamworks.Workshop.Item;
            if (item == null) { return false; }

            var updateButton = tickBox.Parent.FindChild("updatebutton");

            string errorMsg = "";
            if (tickBox.Selected)
            {
                if (!SteamManager.EnableWorkShopItem(item, false, out errorMsg))
                {
                    tickBox.Enabled = false;
                }
            }
            else
            {
                if (!SteamManager.DisableWorkShopItem(item, out errorMsg))
                {
                    tickBox.Enabled = false;
                }
            }
            if (!tickBox.Enabled && updateButton == null)
            {
                updateButton.Enabled = false;
            }
            if (!string.IsNullOrEmpty(errorMsg))
            {
                new GUIMessageBox(TextManager.Get("Error"), errorMsg);
            }

            return true;
        }

        private void ShowItemPreview(Facepunch.Steamworks.Workshop.Item item)
        {
            itemPreviewFrame.ClearChildren();

            if (item == null) return;

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), itemPreviewFrame.RectTransform, Anchor.Center))
            {
                UserData = item,
                RelativeSpacing = 0.02f
            };

            var headerArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), content.RectTransform, maxSize: new Point(int.MaxValue, 150)), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            
            if (itemPreviewSprites.ContainsKey(item.PreviewImageUrl))
            {
                new GUIImage(new RectTransform(new Point(headerArea.Rect.Height), headerArea.RectTransform), itemPreviewSprites[item.PreviewImageUrl], scaleToFit: true);
            }
            else
            {
                new GUIImage(new RectTransform(new Point(headerArea.Rect.Height), headerArea.RectTransform), SteamManager.Instance.DefaultPreviewImage, scaleToFit: true);
            }

            var titleArea = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 0.75f), headerArea.RectTransform))
            {
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), titleArea.RectTransform), item.Title, textAlignment: Alignment.TopLeft, font: GUI.LargeFont);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), titleArea.RectTransform), TextManager.Get("WorkshopItemCreator") + ": " + item.OwnerName, textAlignment: Alignment.TopLeft);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), TextManager.Get("WorkshopItemDescription"));
            var descriptionContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.2f), content.RectTransform));
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), descriptionContainer.Content.RectTransform), item.Description, wrap: true)
            {
                CanBeFocused = false
            };


            //score -------------------------------------
            var scoreContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                RelativeSpacing = 0.02f
            };
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 0.0f), scoreContainer.RectTransform), TextManager.Get("WorkshopItemScore") + ": ");
            int starCount = (int)Math.Round(item.Score * 5);
            for (int i = 0; i < 5; i++)
            {
                new GUIImage(new RectTransform(new Point(scoreContainer.Rect.Height), scoreContainer.RectTransform),
                    i < starCount ? "GUIStarIconBright" : "GUIStarIconDark");
            }
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 0.0f), scoreContainer.RectTransform), TextManager.Get("WorkshopItemVotes").Replace("[votecount]", (item.VotesUp + item.VotesDown).ToString()));
            
            //tags ------------------------------------
            var tagContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                RelativeSpacing = 0.02f
            };
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), tagContainer.RectTransform), TextManager.Get("WorkshopItemTags")+": ");
            if (!item.Tags.Any(t => !string.IsNullOrEmpty(t)))
            {
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), tagContainer.RectTransform), TextManager.Get("None"));
            }
            for (int i = 0; i < item.Tags.Length && i < 5; i++)
            {
                if (string.IsNullOrEmpty(item.Tags[i])) { continue; }
                new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), tagContainer.RectTransform), item.Tags[i].CapitaliseFirstInvariant(), style: "ListBoxElement");
            }
            
            var creationDate = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), content.RectTransform), TextManager.Get("WorkshopItemCreationDate") +": ");
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), creationDate.RectTransform, Anchor.TopRight), item.Created.ToString(), textAlignment: Alignment.TopRight);

            var modificationDate = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), content.RectTransform), TextManager.Get("WorkshopItemModificationDate") + ": ");
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), modificationDate.RectTransform, Anchor.TopRight), item.Modified.ToString(), textAlignment: Alignment.TopRight);
            
            var fileSize = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), content.RectTransform), TextManager.Get("WorkshopItemFileSize") + ": ");
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), fileSize.RectTransform, Anchor.TopRight), MathUtils.GetBytesReadable(item.Installed ? (long)item.Size : item.DownloadSize), textAlignment: Alignment.TopRight);

            new GUIButton(new RectTransform(new Vector2(0.2f, 0.05f), content.RectTransform), TextManager.Get("WorkshopShowItemInSteam"))
            {
                OnClicked = (btn, userdata) =>
                {
                    System.Diagnostics.Process.Start("steam://url/CommunityFilePage/" + item.Id);
                    return true;
                }
            };
        }

        private void CreateWorkshopItem()
        {
            SteamManager.CreateWorkshopItemStaging(new List<ContentFile>(), out itemEditor, out itemContentPackage);
        }
        private void CreateWorkshopItem(Submarine sub)
        {
            SteamManager.CreateWorkshopItemStaging(new List<ContentFile>(), out itemEditor, out itemContentPackage);

            string destinationPath = Path.Combine(SteamManager.WorkshopItemStagingFolder, "Submarines", Path.GetFileName(sub.FilePath));
            try
            {
                File.Copy(sub.FilePath, destinationPath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to copy submarine file \""+sub.FilePath+"\" to the Workshop item staging folder.", e);
                return;
            }

            itemContentPackage.AddFile(Path.Combine("Submarines", Path.GetFileName(sub.FilePath)), ContentType.Submarine);
            itemContentPackage.Name = sub.Name;
            itemEditor.Title = sub.Name;
            itemEditor.Tags.Add("Submarine");
            itemEditor.Description = sub.Description;

            string previewImagePath = Path.GetFullPath(Path.Combine(SteamManager.WorkshopItemStagingFolder, SteamManager.PreviewImageName));
            try
            {
                using (Stream s = File.Create(previewImagePath))
                {
                    sub.PreviewImage.Texture.SaveAsPng(s, (int)sub.PreviewImage.size.X, (int)sub.PreviewImage.size.Y);
                    itemEditor.PreviewImage = previewImagePath;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving submarine preview image failed.", e);
                itemEditor.PreviewImage = null;
            }
        }
        private void CreateWorkshopItem(ContentPackage contentPackage)
        {
            SteamManager.CreateWorkshopItemStaging(new List<ContentFile>(), out itemEditor, out itemContentPackage);
            string modDirectory = "";
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
            }

            itemContentPackage.CorePackage = contentPackage.CorePackage;
            itemContentPackage.Name = contentPackage.Name;
            itemContentPackage.Save(itemContentPackage.Path);
            itemEditor.Title = contentPackage.Name;
        }
        private void CreateWorkshopItem(Facepunch.Steamworks.Workshop.Item item)
        {
            if (!item.Installed)
            {
                new GUIMessageBox(TextManager.Get("Error"), 
                    TextManager.Get("WorkshopErrorInstallRequiredToEdit").Replace("[itemname]", item.Title));
                return;
            }
            SteamManager.CreateWorkshopItemStaging(item, out itemEditor, out itemContentPackage);
        }

        private void ShowCreateItemFrame()
        {
            createItemFrame.ClearChildren();

            if (itemEditor == null) return;

            var createItemContent = new GUILayoutGroup(new RectTransform(new Vector2(0.92f, 0.92f), createItemFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            
            var topPanel = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.4f), createItemContent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.1f
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
            var descriptionBox = new GUITextBox(new RectTransform(Vector2.One, descriptionContainer.Content.RectTransform), itemEditor.Description, textAlignment: Alignment.TopLeft, wrap: true);
            descriptionBox.OnTextChanged += (textBox, text) => 
            {
                Vector2 textSize = textBox.Font.MeasureString(descriptionBox.WrappedText);
                textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(descriptionContainer.Rect.Height, (int)textSize.Y + 10));
                descriptionContainer.UpdateScrollBarSize();
                descriptionContainer.BarScroll = 1.0f;
                itemEditor.Description = text;
                return true;
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topRightColumn.RectTransform), TextManager.Get("WorkshopItemTags"));
            var tagHolder = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.17f), topRightColumn.RectTransform) { MinSize=new Point(0,50) }, isHorizontal: true)
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
                tagBtn.Selected = itemEditor.Tags.Any(t => t.ToLowerInvariant() == tag);

                tagBtn.OnClicked = (btn, userdata) =>
                {
                    if (!tagBtn.Selected)
                    {
                        if (!itemEditor.Tags.Any(t => t.ToLowerInvariant() == tag)) { itemEditor.Tags.Add(tagBtn.Text); }
                        tagBtn.Selected = true;
                        tagBtn.TextBlock.TextColor = Color.LightGreen;
                    }
                    else
                    {
                        itemEditor.Tags.RemoveAll(t => t.ToLowerInvariant() == tagBtn.Text.ToLowerInvariant());
                        tagBtn.Selected = false;
                        tagBtn.TextBlock.TextColor = tagBtn.TextColor;
                    }
                    return true;
                };
            }

            // top left column --------------------------------------------------------------------------------------

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topLeftColumn.RectTransform), TextManager.Get("WorkshopItemPreviewImage"));

            var previewIcon = new GUIImage(new RectTransform(new Vector2(1.0f, 0.7f), topLeftColumn.RectTransform), SteamManager.Instance.DefaultPreviewImage, scaleToFit: true);
            new GUIButton(new RectTransform(new Vector2(1.0f, 0.2f), topLeftColumn.RectTransform), TextManager.Get("WorkshopItemBrowse"))
            {
                OnClicked = (btn, userdata) =>
                {
                    OpenFileDialog ofd = new OpenFileDialog()
                    {
                        InitialDirectory = Path.GetFullPath(SteamManager.WorkshopItemStagingFolder),
                        Filter = TextManager.Get("WorkshopItemPreviewImage")+"|*.png",
                        Title = TextManager.Get("WorkshopItemPreviewImageDialogTitle")
                    };
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        string previewImagePath = Path.GetFullPath(Path.Combine(SteamManager.WorkshopItemStagingFolder, SteamManager.PreviewImageName));
                        if (ofd.FileName != previewImagePath)
                        {
                            File.Copy(ofd.FileName, previewImagePath, overwrite: true);
                        }

                        if (itemPreviewSprites.ContainsKey(previewImagePath))
                        {
                            itemPreviewSprites[previewImagePath].Remove();
                        }
                        var newPreviewImage = new Sprite(previewImagePath, sourceRectangle: null);
                        previewIcon.Sprite = newPreviewImage;
                        itemPreviewSprites[previewImagePath] = newPreviewImage;
                        itemEditor.PreviewImage = previewImagePath;
                    }
                    return true;
                }
            };
            
            if (!string.IsNullOrEmpty(itemEditor.PreviewImage))
            {
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
                                TextManager.Get("ContentPackageCantMakeCorePackage")
                                    .Replace("[packagename]", itemContentPackage.Name)
                                    .Replace("[missingfiletypes]", string.Join(", ", missingContentTypes)));
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
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), createItemContent.RectTransform), style: null);

            var fileListTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), createItemContent.RectTransform), TextManager.Get("WorkshopItemFiles"));
            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), fileListTitle.RectTransform, Anchor.CenterRight), TextManager.Get("WorkshopItemShowFolder"))
            {
                IgnoreLayoutGroups = true,
                OnClicked = (btn, userdata) => { System.Diagnostics.Process.Start(Path.GetFullPath(SteamManager.WorkshopItemStagingFolder)); return true; }
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
                    OpenFileDialog ofd = new OpenFileDialog()
                    {
                        InitialDirectory = Path.GetFullPath(SteamManager.WorkshopItemStagingFolder),
                        Title = "Select the files you want to add to the Steam Workshop item",
                    };
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        foreach (string file in ofd.FileNames)
                        {
                            string filePathRelativeToStagingFolder = UpdaterUtil.GetRelativePath(file, Path.Combine(Environment.CurrentDirectory, SteamManager.WorkshopItemStagingFolder));
                            //file is not inside the staging folder -> copy it
                            if (filePathRelativeToStagingFolder.StartsWith(".."))
                            {
                                string filePathRelativeToBaseFolder = UpdaterUtil.GetRelativePath(file, Environment.CurrentDirectory);
                                itemContentPackage.AddFile(filePathRelativeToBaseFolder, ContentType.None);
                            }
                            else
                            {
                                itemContentPackage.AddFile(filePathRelativeToStagingFolder, ContentType.None);
                            }
                        }
                        RefreshCreateItemFileList();
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
                    TextManager.Get("WorkshopItemDelete"))
                {
                    ToolTip = TextManager.Get("WorkshopItemDeleteTooltip"),
                    TextColor = Color.Red,
                    OnClicked = (btn, userData) =>
                    {
                        if (itemEditor == null) { return false; }
                        var deleteVerification = new GUIMessageBox("", TextManager.Get("WorkshopItemDeleteVerification").Replace("[itemname]", itemEditor.Title),
                            new string[] {  TextManager.Get("Yes"), TextManager.Get("No") });
                        deleteVerification.Buttons[0].OnClicked = (yesBtn, userdata) =>
                        {
                            if (itemEditor == null) { return false; }
                            RemoveItemFromLists(itemEditor.Id);
                            itemEditor.Delete();
                            itemEditor = null;
                            SelectTab(Tab.Browse);
                            deleteVerification.Close();
                            return true;
                        };
                        deleteVerification.Buttons[1].OnClicked = deleteVerification.Close;
                        return true;
                    }
                };
            }
            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), bottomButtonContainer.RectTransform),
                TextManager.Get(itemEditor.Id > 0 ? "WorkshopItemUpdate" : "WorkshopItemPublish"))
            {
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

                    PublishWorkshopItem();
                    return true;
                }
            };

        }
        
        private void RefreshCreateItemFileList()
        {
            createItemFileList.ClearChildren();
            if (itemContentPackage == null) return;
            var contentTypes = Enum.GetValues(typeof(ContentType));
            
            foreach (ContentFile contentFile in itemContentPackage.Files)
            {
                bool illegalPath = !ContentPackage.IsModFilePathAllowed(contentFile);
                string pathInStagingFolder = Path.Combine(SteamManager.WorkshopItemStagingFolder, contentFile.Path);
                bool fileInStagingFolder = File.Exists(pathInStagingFolder);
                bool fileExists = illegalPath ? File.Exists(contentFile.Path) : fileInStagingFolder;

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
                    ToolTip = TextManager.Get(fileInStagingFolder ? "WorkshopItemFileIncluded" : "WorkshopItemFileNotIncluded")
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
                TextManager.Get("WorkshopPublishInProgress").Replace("[itemname]", item.Title), 
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
                new GUIMessageBox("", TextManager.Get("WorkshopItemPublished").Replace("[itemname]", item.Title));
            }
            else
            {
                new GUIMessageBox(
                    TextManager.Get("Error"), 
                    TextManager.Get("WorkshopItemPublishFailed").Replace("[itemname]", item.Title) + item.Error);
            }

            createItemFrame.ClearChildren();
            SelectTab(Tab.Browse);
        }

        #region UI management

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            GameMain.TitleScreen.DrawLoadingText = false;
            GameMain.TitleScreen.Draw(spriteBatch, graphics, (float)deltaTime);

            spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, GameMain.ScissorTestEnable);
            
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
