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
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listContainer.RectTransform), "Mods");
            subscribedItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.7f), listContainer.RectTransform))
            {
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    ShowItemPreview(userdata as Facepunch.Steamworks.Workshop.Item);
                    return true;
                }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listContainer.RectTransform), "Popular Mods");
            topItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), listContainer.RectTransform))
            {
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    ShowItemPreview(userdata as Facepunch.Steamworks.Workshop.Item);
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), listContainer.RectTransform), "Find more mods...")
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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), "Published items");
            publishedItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), leftColumn.RectTransform))
            {
                OnSelected = (component, userdata) =>
                {
                    if (userdata is Facepunch.Steamworks.Workshop.Item item)
                    {
                        CreateWorkshopItem(item);
                        ShowCreateItemFrame();
                    }
                    return true;
                }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), "Your items");
            myItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), leftColumn.RectTransform))
            {
                OnSelected = (component, userdata) =>
                {
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

            new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), leftColumn.RectTransform),
                "Create item")
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
            RefreshItemLists();
            SelectTab(Tab.Browse);
        }

        private void SelectTab(Tab tab)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].Visible = i == (int)tab;
            }
        }

        private void RefreshItemLists()
        {
            SteamManager.GetSubscribedWorkshopItems((items) => { OnItemsReceived(items, subscribedItemList); });
            SteamManager.GetPopularWorkshopItems((items) => { OnItemsReceived(items, topItemList); }, 5);
            SteamManager.GetPublishedWorkshopItems((items) => { OnItemsReceived(items, publishedItemList); });

            myItemList.ClearChildren();
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), myItemList.Content.RectTransform), "Submarines", textAlignment: Alignment.Center, font: GUI.LargeFont)
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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), myItemList.Content.RectTransform), "Content Packages", textAlignment: Alignment.Center, font: GUI.LargeFont)
            {
                CanBeFocused = false
            };
            foreach (ContentPackage contentPackage in ContentPackage.List)
            {
                if (!string.IsNullOrEmpty(contentPackage.SteamWorkshopUrl)) { continue; }
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
        }

        private void CreateWorkshopItemFrame(Facepunch.Steamworks.Workshop.Item item, GUIListBox listBox)
        {
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
                Stretch = true
            };

            int iconSize = innerFrame.Rect.Height;
            if (itemPreviewSprites.ContainsKey(item.PreviewImageUrl))
            {
                new GUIImage(new RectTransform(new Point(iconSize), innerFrame.RectTransform), itemPreviewSprites[item.PreviewImageUrl], scaleToFit: true);
            }
            else
            {
                new GUIImage(new RectTransform(new Point(iconSize), innerFrame.RectTransform), SteamManager.Instance.DefaultPreviewImage, scaleToFit: true);
                try
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
                catch (Exception e)
                {
                    pendingPreviewImageDownloads.Remove(item.PreviewImageUrl);
                    DebugConsole.ThrowError("Downloading the preview image of the Workshop item \"" + item.Title + "\" failed.", e);
                }
            }

            var rightColumn = new GUILayoutGroup(new RectTransform(new Point(innerFrame.Rect.Width - iconSize, innerFrame.Rect.Height), innerFrame.RectTransform), childAnchor: Anchor.TopRight)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), rightColumn.RectTransform), item.Title, textAlignment: Alignment.CenterLeft);
            /*new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.75f), itemFrame.RectTransform, Anchor.BottomLeft), item.Description,
                wrap: true, font: GUI.SmallFont);*/
                
            if (item.Installed)
            {
                var enabledTickBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 0.5f), rightColumn.RectTransform), "Enabled")
                {
                    UserData = item,
                };
                try
                {
                    enabledTickBox.Selected = SteamManager.CheckWorkshopItemEnabled(item);
                }
                catch (Exception e)
                {
                    new GUIMessageBox("Error", e.Message);
                    enabledTickBox.Enabled = false;
                    itemFrame.Color = Color.Red;
                    itemFrame.HoverColor = Color.Red;
                    itemFrame.SelectedColor = Color.Red;
                    itemFrame.GetChild<GUITextBlock>().TextColor = Color.Red;
                }
                enabledTickBox.OnSelected = ToggleItemEnabled;
            }
            else if (item.Downloading)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), rightColumn.RectTransform), "Downloading...");
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
            item.Download();
            return true;
        }

        private bool ToggleItemEnabled(GUITickBox tickBox)
        {
            Facepunch.Steamworks.Workshop.Item item = tickBox.UserData as Facepunch.Steamworks.Workshop.Item;
            if (tickBox.Selected)
            {
                tickBox.Selected = SteamManager.EnableWorkShopItem(item, false);
            }
            else
            {
                tickBox.Selected = !SteamManager.DisableWorkShopItem(item);
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

            var headerArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), content.RectTransform, maxSize: new Point(int.MaxValue, 150)), isHorizontal: true)
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
            new GUITextBlock(new RectTransform(new Vector2(0.75f, 1.0f), headerArea.RectTransform), item.Title, textAlignment: Alignment.CenterLeft, font: GUI.LargeFont);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), item.Description, wrap: true);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Score: " + item.Score);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Upvotes: " + item.VotesUp);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Downvotes: "+item.VotesDown);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Creator: " + item.OwnerName);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Created: " + item.Created);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Modified: " + item.Modified);

            new GUIButton(new RectTransform(new Vector2(0.2f, 0.05f), content.RectTransform), "Show in Steam")
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
            itemContentPackage.AddFile(sub.FilePath, ContentType.Submarine);
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
            foreach (ContentFile file in contentPackage.Files)
            {
                itemContentPackage.AddFile(file.Path, file.Type);
            }
            itemContentPackage.CorePackage = contentPackage.CorePackage;
            itemContentPackage.Name = contentPackage.Name;
            itemEditor.Title = contentPackage.Name;
        }
        private void CreateWorkshopItem(Facepunch.Steamworks.Workshop.Item item)
        {
            if (!item.Installed)
            {
                new GUIMessageBox("Error", "Cannot edit the workshop item \"" + item.Title + "\" because it has not been installed.");
                return;
            }
            SteamManager.CreateWorkshopItemStaging(item, out itemEditor, out itemContentPackage);
        }

        private void ShowCreateItemFrame()
        {
            createItemFrame.ClearChildren();

            if (itemEditor == null) return;

            var createItemContent = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), createItemFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            
            var topPanel = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), createItemContent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.1f
            };

            var topRightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.25f, 0.8f), topPanel.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            var topLeftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1.0f), topPanel.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topLeftColumn.RectTransform), "Title");
            var titleBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.2f), topLeftColumn.RectTransform), itemEditor.Title);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topLeftColumn.RectTransform), "Description");
            var descriptionBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), topLeftColumn.RectTransform), itemEditor.Description, textAlignment: Alignment.TopLeft, wrap: true);
            descriptionBox.OnTextChanged += (textBox, text) => { itemEditor.Description = text; return true; };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), topRightColumn.RectTransform), "Preview Icon");

            var previewIcon = new GUIImage(new RectTransform(new Vector2(1.0f, 0.7f), topRightColumn.RectTransform), SteamManager.Instance.DefaultPreviewImage, scaleToFit: true);
            new GUIButton(new RectTransform(new Vector2(1.0f, 0.2f), topRightColumn.RectTransform), "Browse...")
            {
                OnClicked = (btn, userdata) =>
                {
                    OpenFileDialog ofd = new OpenFileDialog()
                    {
                        InitialDirectory = Path.GetFullPath(SteamManager.WorkshopItemStagingFolder),
                        Filter = "Preview Image|*.png",
                        Title = "Select the preview image for the Steam Workshop item"
                    };
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        string previewImagePath = Path.GetFullPath(Path.Combine(SteamManager.WorkshopItemStagingFolder, SteamManager.PreviewImageName));
                        File.Copy(ofd.FileName, previewImagePath, overwrite: true);

                        if (itemPreviewSprites.ContainsKey(previewImagePath))
                        {
                            itemPreviewSprites[previewImagePath].Remove();
                        }
                        var newPreviewImage = new Sprite(previewImagePath, sourceRectangle: null);
                        previewIcon.Sprite = newPreviewImage;
                        itemPreviewSprites[previewImagePath] = newPreviewImage;
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

            var fileListTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), createItemContent.RectTransform), "Files included in the item");
            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), fileListTitle.RectTransform, Anchor.CenterRight), "Show folder")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (btn, userdata) => { System.Diagnostics.Process.Start(Path.GetFullPath(SteamManager.WorkshopItemStagingFolder)); return true; }
            };
            createItemFileList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), createItemContent.RectTransform));
            RefreshCreateItemFileList();

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), createItemContent.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.05f
            };
            
            new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonContainer.RectTransform, Anchor.TopRight), "Refresh")
            {
                OnClicked = (btn, userdata) => 
                {
                    itemContentPackage = new ContentPackage(itemContentPackage.Path);
                    RefreshCreateItemFileList();
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonContainer.RectTransform, Anchor.TopRight), "Add files...")
            {
                OnClicked = (btn, userdata) =>
                {
                    OpenFileDialog ofd = new OpenFileDialog()
                    {
                        InitialDirectory = Path.GetFullPath(SteamManager.WorkshopItemStagingFolder),
                        Title = "Select the files you want to add to the Steam Workshop item"
                    };
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        foreach (string file in ofd.FileNames)
                        {
                            string destinationPath = Path.Combine(SteamManager.WorkshopItemStagingFolder, Path.GetFileName(file));
                            File.Copy(file, destinationPath, overwrite: true);
                            itemContentPackage.AddFile(destinationPath, ContentType.None);
                        }
                        RefreshCreateItemFileList();
                    }
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.25f, 0.05f), createItemContent.RectTransform, Anchor.BottomRight),
                "Publish item")
            {
                IgnoreLayoutGroups = true,
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
                string pathInStagingFolder = contentFile.Type == ContentType.Submarine ?
                    contentFile.Path :
                    Path.Combine(SteamManager.WorkshopItemStagingFolder, contentFile.Path);
                bool fileExists = File.Exists(pathInStagingFolder);

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
                    ToolTip = fileExists ? 
                        "File will be included in the Workshop item." : 
                        "File is not present in the Workshop item folder and will not be included in the Workshop item. This may be desirable if you want the mod to use Vanilla files that the players already have - otherwise you should copy the file to the Workshop item folder."
                };

                var nameText = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), content.RectTransform, Anchor.CenterLeft), contentFile.Path, font: GUI.SmallFont)
                {
                    ToolTip = contentFile.Path
                };
                if (!fileExists) { nameText.TextColor *= 0.8f; }
                if (illegalPath && !ContentPackage.List.Any(cp => cp.Files.Any(f => Path.GetFullPath(f.Path) == Path.GetFullPath(contentFile.Path))))
                {
                    nameText.TextColor = Color.Red;
                    tickBox.ToolTip = "Workshop items are only allowed to modify files in the Mod folder or add submarine files to the Submarines folder. Please create a separate subfolder for the files.";
                }

                var contentTypeSelection = new GUIDropDown(new RectTransform(new Vector2(0.4f, 1.0f), content.RectTransform, Anchor.CenterRight),
                    elementCount: contentTypes.Length)
                {
                    UserData = contentFile,
                };
                contentTypeSelection.OnSelected = (GUIComponent selected, object userdata) =>
                {
                    ((ContentFile)contentTypeSelection.UserData).Type = (ContentType)userdata;
                    itemContentPackage.Save(itemContentPackage.Path);
                    return true;
                };
                foreach (ContentType contentType in contentTypes)
                {
                    contentTypeSelection.AddItem(contentType.ToString(), contentType);
                }
                contentTypeSelection.SelectItem(contentFile.Type);

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
            var msgBox = new GUIMessageBox("Please wait...", "Publishing \"" + item.Title + "\" in the Steam Workshop.", new string[] { TextManager.Get("Cancel") });
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
                yield return CoroutineStatus.Running;
            }
            msgBox.Close();

            if (string.IsNullOrEmpty(item.Error))
            {
                new GUIMessageBox("", "\"" + item.Title + "\" is now available in the Steam Workshop!");
            }
            else
            {
                new GUIMessageBox("Error", "Publishing \"" + item.Title + "\" in the Steam Workshop failed. " + item.Error);
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
