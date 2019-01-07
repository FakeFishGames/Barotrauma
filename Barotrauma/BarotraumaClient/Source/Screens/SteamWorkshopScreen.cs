using Barotrauma.Extensions;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace Barotrauma
{
    class SteamWorkshopScreen : Screen
    {
        private GUIFrame menu;
        private GUIListBox installedItemList;
        private GUIListBox availableItemList;

        //shows information of a selected workshop item
        private GUIFrame itemPreviewFrame;

        //menu for creating new items
        private GUIFrame createItemFrame;
        //listbox that shows the files included in the item being created
        private GUIListBox createItemFileList;

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


            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listContainer.RectTransform), "Installed items");
            installedItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), listContainer.RectTransform))
            {
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    ShowItemPreview(userdata as Facepunch.Steamworks.Workshop.Item);
                    return true;
                }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listContainer.RectTransform), "Available items");
            availableItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), listContainer.RectTransform))
            {
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    ShowItemPreview(userdata as Facepunch.Steamworks.Workshop.Item);
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
            new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), leftColumn.RectTransform));

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), "Your items");
            new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), leftColumn.RectTransform));

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
            RefreshItemList();
        }

        private void SelectTab(Tab tab)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].Visible = i == (int)tab;
            }
        }

        private void RefreshItemList()
        {
            SteamManager.GetWorkshopItems(OnItemsReceived);
        }
        
        private void OnItemsReceived(IList<Facepunch.Steamworks.Workshop.Item> itemDetails)
        {
            installedItemList.ClearChildren();
            availableItemList.ClearChildren();
            foreach (var item in itemDetails)
            {
                GUIListBox listBox = item.Installed ? installedItemList : availableItemList;
                var itemFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), listBox.Content.RectTransform, minSize: new Point(0, 80)),
                    style: "ListBoxElement")
                {
                    UserData = item
                };
                new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.25f), itemFrame.RectTransform), item.Title);
                new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.75f), itemFrame.RectTransform, Anchor.BottomLeft), item.Description,
                    wrap: true, font: GUI.SmallFont);
                
                if (item.Installed)
                {
                    var enabledTickBox = new GUITickBox(new RectTransform(new Vector2(0.25f, 0.5f), itemFrame.RectTransform, Anchor.CenterRight), "Enabled")
                    {
                        UserData = item,
                        OnSelected = ToggleItemEnabled
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
                }
                else if (item.Downloading)
                {
                    new GUITextBlock(new RectTransform(new Vector2(0.25f, 0.5f), itemFrame.RectTransform, Anchor.CenterRight), "Downloading");
                }
                else
                {
                    var downloadBtn = new GUIButton(new RectTransform(new Vector2(0.2f, 0.5f), itemFrame.RectTransform, Anchor.CenterRight),
                        TextManager.Get("DownloadButton"))
                    {
                        UserData = item,
                        OnClicked = DownloadItem
                    };
                }
            }
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
                RelativeSpacing = 0.02f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), item.Title, font: GUI.LargeFont);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), item.Description, wrap: true);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Score: " + item.Score);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Upvotes: " + item.VotesUp);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Downvotes: "+item.VotesDown);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Creator: " + item.OwnerName);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Created: " + item.Created);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Modified: " + item.Modified);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Url: " + item.Url);
        }

        private void CreateWorkshopItem()
        {
            SteamManager.CreateWorkshopItemStaging(new List<ContentFile>(), out itemEditor, out itemContentPackage);
        }

        private void ShowCreateItemFrame()
        {
            createItemFrame.ClearChildren();

            if (itemEditor == null) return;

            var createItemContent = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), createItemFrame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.02f };

            new GUITextBlock(new RectTransform(new Vector2(0.2f,0.05f), createItemContent.RectTransform), "Title");
            var titleBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 0.05f), createItemContent.RectTransform), itemEditor.Title);
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 0.05f), createItemContent.RectTransform), "Description");
            var descriptionBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 0.1f), createItemContent.RectTransform), itemEditor.Description);
            descriptionBox.OnTextChanged += (textBox, text) => { itemEditor.Description = text; return true; };

            new GUIButton(new RectTransform(new Vector2(0.25f, 0.05f), createItemContent.RectTransform, Anchor.TopRight), "Show folder")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (btn, userdata) => { System.Diagnostics.Process.Start(Path.GetFullPath(SteamManager.WorkshopItemStagingFolder)); return true; }
            };

            new GUITextBlock(new RectTransform(new Vector2(0.2f, 0.05f), createItemContent.RectTransform), "Files included in the item");
            createItemFileList = new GUIListBox(new RectTransform(new Vector2(0.8f, 0.4f), createItemContent.RectTransform));
            RefreshCreateItemFileList();

            new GUIButton(new RectTransform(new Vector2(0.25f, 0.05f), createItemContent.RectTransform, Anchor.TopRight), "Refresh")
            {
                OnClicked = (btn, userdata) => 
                {
                    itemContentPackage = new ContentPackage(itemContentPackage.Path);
                    RefreshCreateItemFileList();
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
                var fileFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), createItemFileList.Content.RectTransform) { MinSize = new Point(0, 15) },
                    style: "ListBoxElement")
                {
                    UserData = contentFile
                };

                new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), fileFrame.RectTransform, Anchor.CenterLeft), contentFile.Path);

                var contentTypeSelection = new GUIDropDown(new RectTransform(new Vector2(0.4f, 1.0f), fileFrame.RectTransform, Anchor.CenterRight),
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
            }
        }

        private void PublishWorkshopItem()
        {
            if (itemContentPackage == null || itemEditor == null) return;
            
            SteamManager.StartPublishItem(itemContentPackage, itemEditor);
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
