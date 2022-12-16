#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ItemOrPackage = Barotrauma.Either<Steamworks.Ugc.Item, Barotrauma.ContentPackage>;

namespace Barotrauma.Steam
{
    sealed partial class MutableWorkshopMenu : WorkshopMenu
    {
        private string ExtractTitle(ItemOrPackage itemOrPackage)
            => itemOrPackage.TryGet(out ContentPackage package)
                ? package.Name
                : ((Steamworks.Ugc.Item)itemOrPackage).Title;

        private void CreateWorkshopItemDetailContainer(
            GUIFrame parent,
            out GUIListBox outerContainer,
            Action<ItemOrPackage, GUIFrame> onSelected,
            Action onDeselected,
            out Action<ItemOrPackage> select,
            out Action deselect)
        {
            ItemOrPackage? selectedItemOrPackage = null;

            GUIListBox outContainer = new GUIListBox(new RectTransform(Vector2.One, parent.RectTransform),
                isHorizontal: true,
                style: null)
            {
                ScrollBarEnabled = false,
                ScrollBarVisible = false,
                HoverCursor = CursorState.Default
            };
            outerContainer = outContainer;

            var selectedLayout =
                new GUILayoutGroup(new RectTransform(Vector2.One, outerContainer.Content.RectTransform));
            var selectedHeaderLayout =
                new GUILayoutGroup(new RectTransform((1.0f, 0.05f), selectedLayout.RectTransform),
                    isHorizontal: true,
                    childAnchor: Anchor.CenterLeft);

            void deselectMethod()
            {
                if (selectedItemOrPackage is null) { return; }
                selectedItemOrPackage = null;
                onDeselected();
            }

            deselect = deselectMethod;

            var backButton =
                new GUIButton(new RectTransform((0.04f, 1.0f), selectedHeaderLayout.RectTransform),
                    style: "GUIButtonToggleLeft")
                {
                    OnClicked = (button, o) =>
                    {
                        deselectMethod();
                        return false;
                    }
                };
            var padding = new GUIFrame(new RectTransform((1.0f, 0.005f), selectedLayout.RectTransform), style: null);
            var selectedFrame = new GUIFrame(new RectTransform((1.0f, 0.945f), selectedLayout.RectTransform),
                style: null);

            var selectionScroller = new GUICustomComponent(
                new RectTransform(Vector2.Zero, outerContainer.Parent.RectTransform),
                onUpdate: (deltaTime, component) =>
                {
                    float targetScroll = selectedItemOrPackage is null
                        ? 0.0f
                        : 1.0f;
                    outContainer.ScrollBar.BarScroll
                        = MathUtils.NearlyEqual(targetScroll, outContainer.ScrollBar.BarScroll)
                            ? targetScroll
                            : MathHelper.Lerp(outContainer.ScrollBar.BarScroll, targetScroll, 0.3f);
                });

            select = itemOrPackage =>
            {
                //showInSteamButton.Visible = itemOrPackage.TryGet(out Steamworks.Ugc.Item _);
                //selectedItem = itemOrPackage;
                //selectedTitle.Text = ExtractTitle(itemOrPackage);
                selectedFrame.ClearChildren();

                //Jank to fix mouserect not clamping properly
                //when shifting all elements to the left
                var dropdowns = outContainer.Content.GetAllChildren<GUIDropDown>().ToArray();
                var allChildren = outContainer.Content.GetAllChildren()
                    .Concat(selectedFrame.GetAllChildren());
                allChildren.ForEach(c =>
                    {
                        //c.CascadingMouseRectClamp = !dropdowns.Any(dd => dd.IsParentOf(c) || dd.ListBox.IsParentOf(c));
                        //c.CanBeFocused = c.CanBeFocused || !c.CascadingMouseRectClamp;
                        c.ClampMouseRectToParent = !(c.Parent?.Parent is GUIDropDown);
                    }
                );

                selectedItemOrPackage = itemOrPackage;
                onSelected(itemOrPackage, selectedFrame);
            };
        }

        private void CreateWorkshopItemList(
            GUIFrame parent,
            out GUIListBox outerContainer,
            out GUIListBox workshopItemList,
            Action<Steamworks.Ugc.Item, GUIFrame> onSelected)
            => CreateWorkshopItemOrPackageList(
                parent,
                out outerContainer,
                out workshopItemList,
                onSelected: (ItemOrPackage itemOrPackage, GUIFrame frame)
                    => onSelected((Steamworks.Ugc.Item)itemOrPackage, frame));
        
        private GUIButton CreateShowInSteamButton(Steamworks.Ugc.Item workshopItem, RectTransform rectT)
            => new GUIButton(
                rectT,
                TextManager.Get("WorkshopShowItemInSteam"), style: "GUIButtonSmall")
            {
                OnClicked = (button, o) =>
                {
                    SteamManager.OverlayCustomUrl(workshopItem.Url);
                    return false;
                }
            };

        private GUIButton? CreateShowInSteamButton(ItemOrPackage itemOrPackage)
            => itemOrPackage.TryGet(out Steamworks.Ugc.Item workshopItem)
                ? CreateShowInSteamButton(workshopItem)
                : null;
        
        private void CreateWorkshopItemOrPackageList(
            GUIFrame parent,
            out GUIListBox outerContainer,
            out GUIListBox workshopItemList,
            Action<ItemOrPackage, GUIFrame> onSelected)
        {
            GUIListBox? itemList = null;

            CreateWorkshopItemDetailContainer(
                parent,
                out outerContainer,
                onSelected: onSelected,
                onDeselected: () => itemList?.Deselect(),
                out var select, out var deselect);

            itemList = new GUIListBox(new RectTransform(Vector2.One, outerContainer.Content.RectTransform))
            {
                PlaySoundOnSelect = true,
            };
            itemList.RectTransform.SetAsFirstChild();
            workshopItemList = itemList;

            var deselectCarrier
                = CreateActionCarrier(outerContainer.Content, nameof(deselect).ToIdentifier(), deselect);

            itemList.OnSelected = (component, userData) =>
            {
                //Don't select if hitting the subscribe button
                if (GUI.MouseOn.Parent != itemList.Content) { return false; }

                if (!(userData is ItemOrPackage itemOrPackage)) { return false; }

                select(itemOrPackage);

                return true;
            };
        }
        
        private void AddUnpublishedMods(ISet<Steamworks.Ugc.Item> workshopItems)
        {
            //Users that don't have a proper license cannot publish Workshop items
            //(see https://partner.steamgames.com/doc/features/workshop#15)
            void clearWithMessage(LocalizedString message)
            {
                selfModsList.ClearChildren();
                var messageFrame = new GUIFrame(new RectTransform(Vector2.One, selfModsList.Content.RectTransform),
                    style: null)
                {
                    CanBeFocused = false
                };
                new GUITextBlock(new RectTransform((0.5f, 1.0f), messageFrame.RectTransform, Anchor.Center),
                    text: message,
                    textAlignment: Alignment.Center,
                    wrap: true,
                    font: GUIStyle.Font);
            }

            if (SteamManager.IsFreeWeekend())
            {
                clearWithMessage(TextManager.Get("FreeWeekendCantPublish"));
                return;
            }
            if (SteamManager.IsFamilyShared())
            {
                clearWithMessage(TextManager.Get("FamilySharedCantPublish"));
                return;
            }

            DateTime getEditTime(ContentPackage p)
            {
                DateTime writeTime = File.GetLastWriteTime(p.Dir);
                
                //File.GetLastWriteTime on the directory is not good enough;
                //it's possible to update a file in a directory without
                //updating its parent directories' write time, so let's
                //look at all of those files
                var files = Directory.GetFiles(p.Dir, "*", System.IO.SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    DateTime newTime = File.GetLastWriteTime(file);
                    if (newTime > writeTime) { writeTime = newTime; }
                }

                return writeTime;
            }

            //Find local packages associated with the Workshop items if available
            (Steamworks.Ugc.Item WorkshopItem, ContentPackage? LocalPackage)[] publishedItems = workshopItems
                .Select(item => (item,
                    (ContentPackage?)ContentPackageManager.LocalPackages.FirstOrDefault(p
                        => p.TryExtractSteamWorkshopId(out var workshopId) && workshopId.Value == item.Id)))
                //Sort the pairs by last local edit time if available
                .OrderBy(t => t.Item2 == null)
                .ThenByDescending(t => t.Item2 is { } p ? getEditTime(p) : t.Item1.LatestUpdateTime)
                .ToArray();

            int indexOfUserDataInPublishedItemsArray(object userData)
                => publishedItems.IndexOf(t
                    => t.WorkshopItem.Id == ((Steamworks.Ugc.Item)(userData as ItemOrPackage)!).Id);

            //Take the existing GUI items that are in the list and sort to match the order of publishedItems
            var publishedGuiComponents = selfModsList.Content.Children.OrderBy(c => indexOfUserDataInPublishedItemsArray(c.UserData)).ToArray();

            //Get mods that haven't been published and add them to the list
            var unpublishedMods = ContentPackageManager.LocalPackages
                .Where(p =>
                    !p.TryExtractSteamWorkshopId(out var workshopId)
                    || !publishedItems.Any(item => item.WorkshopItem.Id == workshopId.Value))
                .OrderByDescending(getEditTime).ToArray();

            if (unpublishedMods.Any())
            {
                var unpublishedHeader
                    = new GUITextBlock(new RectTransform((1.0f, 1.0f / 11.0f), selfModsList.Content.RectTransform),
                        TextManager.Get("UnpublishedModsHeader"), font: GUIStyle.SubHeadingFont) { CanBeFocused = false };
            }

            foreach (var unpublishedMod in unpublishedMods)
            {
                var unpublishedFrame = new GUIFrame(
                    new RectTransform((1.0f, 1.0f / 5.5f), selfModsList.Content.RectTransform),
                    style: "ListBoxElement")
                {
                    UserData = (ItemOrPackage)unpublishedMod
                };
                var unpublishedLayout
                    = new GUILayoutGroup(new RectTransform(Vector2.One, unpublishedFrame.RectTransform),
                        isHorizontal: true)
                    {
                        Stretch = true,
                        RelativeSpacing = 0.02f
                    };
                var unpublishedPadding
                    = new GUIFrame(
                        new RectTransform(Vector2.One, unpublishedLayout.RectTransform,
                            scaleBasis: ScaleBasis.BothHeight), style: null)
                    {
                        CanBeFocused = false
                    };
                var unpublishedTextBlock
                    = new GUITextBlock(new RectTransform(Vector2.One, unpublishedLayout.RectTransform),
                        $"{unpublishedMod.Name}\n\n" +
                        TextManager.GetWithVariable("LastLocalEditTime",
                            "[datetime]",
                            getEditTime(unpublishedMod).ToString()),
                        font: GUIStyle.Font)
                    {
                        CanBeFocused = false
                    };
                unpublishedLayout.Recalculate();
            }

            if (publishedGuiComponents.Any())
            {
                var publishedHeader
                    = new GUITextBlock(new RectTransform((1.0f, 1.0f / 11.0f), selfModsList.Content.RectTransform),
                        TextManager.Get("PublishedModsHeader"), font: GUIStyle.SubHeadingFont) { CanBeFocused = false };
            }

            foreach (var c in publishedGuiComponents)
            {
                c.SetAsLastChild();
                var textBlock = (c.FindChild(b => b is GUITextBlock, recursive: true) as GUITextBlock)!;
                textBlock.Text += $"\n";
                
                int index = indexOfUserDataInPublishedItemsArray(c.UserData);
                (Steamworks.Ugc.Item workshopItem, ContentPackage? localMod) = publishedItems[index];
                if (localMod != null)
                {
                    textBlock.Text += $"\n" + TextManager.GetWithVariable("LastLocalEditTime", "[datetime]", getEditTime(localMod).ToString());
                }
                textBlock.Text += $"\n" + TextManager.GetWithVariable("LatestPublishTime", "[datetime]", workshopItem.LatestUpdateTime.ToLocalTime().ToString());
            }
        }
        
        private static (GUIButton Button, GUIFrame Sprite) CreatePaddedButton(RectTransform rectT, string style, float spriteScale)
        {
            var button = new GUIButton(
                rectT,
                style: null);

            var sprite = new GUIFrame(
                new RectTransform(Vector2.One * spriteScale, button.RectTransform, Anchor.Center),
                style: style)
            {
                CanBeFocused = false
            };

            return (button, sprite);
        }
        
        private static void CreateSubscribeButton(Steamworks.Ugc.Item workshopItem, RectTransform rectT, float spriteScale)
        {
            const string plusButton = "GUIPlusButton";
            const string minusButton = "GUIMinusButton";
            
            LocalizedString subscribeTooltip = TextManager.Get("DownloadButton");
            LocalizedString unsubscribeTooptip = TextManager.Get("WorkshopItemUnsubscribe");

            var (subscribeButton, subscribeButtonSprite) = CreatePaddedButton(rectT, plusButton, spriteScale);
            subscribeButton.ToolTip = subscribeTooltip;
            
            subscribeButton.OnClicked = (button, o) =>
            {
                if (!workshopItem.IsSubscribed)
                {
                    workshopItem.Subscribe();
                    TaskPool.Add($"DownloadSubscribedItem{workshopItem.Id}",
                        SteamManager.Workshop.ForceRedownload(workshopItem),
                        t => { });
                }
                else
                {
                    workshopItem.Unsubscribe();
                    SteamManager.Workshop.Uninstall(workshopItem);
                }

                return false;
            };

            var buttonStyleUpdater = new GUICustomComponent(
                new RectTransform(Vector2.Zero, subscribeButton.RectTransform),
                onUpdate: (deltaTime, component) =>
                {
                    if (subscribeButtonSprite.Style is { Identifier: { } styleId })
                    {
                        if (workshopItem.IsSubscribed && styleId != minusButton)
                        {
                            subscribeButtonSprite.ApplyStyle(GUIStyle.GetComponentStyle(minusButton));
                            subscribeButton.ToolTip = unsubscribeTooptip;
                        }
                        if (!workshopItem.IsSubscribed && styleId != plusButton)
                        {
                            subscribeButtonSprite.ApplyStyle(GUIStyle.GetComponentStyle(plusButton));
                            subscribeButton.ToolTip = subscribeTooltip;
                        }
                    }
                });

            float displayedDownloadAmount = workshopItem.DownloadAmount;
            var downloadProgressBar = new GUICustomComponent(
                new RectTransform((1.22f, 1.22f), subscribeButtonSprite.RectTransform, Anchor.Center),
                onDraw: (spriteBatch, component) =>
                {
                    bool visible = workshopItem.IsSubscribed
                                   && (workshopItem.IsDownloading
                                       || workshopItem.IsDownloadPending
                                       || !MathUtils.NearlyEqual(workshopItem.DownloadAmount, displayedDownloadAmount));
                    if (!visible) { return; }
                    
                    void drawSection(float amount, Color color, float thickness)
                        => GUI.DrawDonutSection(
                            spriteBatch,
                            component.Rect.Center.ToVector2() + (0, 1),
                            new Range<float>(component.Rect.Width * 0.55f - thickness * 0.5f, component.Rect.Width * 0.55f + thickness * 0.5f),
                            amount * MathF.PI * 2.0f,
                            color);

                    void drawSectionFuzzy(float amount, Color color, float thickness)
                    {
                        drawSection(amount, color, thickness);
                        drawSection(amount, color * 0.6f, thickness + 0.5f);
                        drawSection(amount, color * 0.3f, thickness + 1.0f);
                    }
                    
                    drawSectionFuzzy(1.0f, Color.Lerp(Color.Black, GUIStyle.Blue, 0.2f), component.Rect.Width * 0.25f);
                    drawSectionFuzzy(1.0f, Color.Black, component.Rect.Width * 0.15f);
                    drawSectionFuzzy(displayedDownloadAmount, GUIStyle.Green, component.Rect.Width * 0.08f);
                },
                onUpdate: (deltaTime, component) =>
                {
                    displayedDownloadAmount = Math.Min(
                        workshopItem.DownloadAmount,
                        MathHelper.Lerp(displayedDownloadAmount, workshopItem.DownloadAmount, 0.05f));
                })
            {
                CanBeFocused = false
            };
        }

        private void PopulateItemList(GUIListBox itemListBox, Task<ISet<Steamworks.Ugc.Item>> items, bool includeSubscribeButton, Action<ISet<Steamworks.Ugc.Item>>? onFill = null)
        {
            itemListBox.ClearChildren();
            itemListBox.Deselect();
            itemListBox.ScrollBar.BarScroll = 0.0f;
            TaskPool.AddIfNotFound("PopulateTabWithItemList", items,
                (t) =>
                {
                    taskCancelSrc = taskCancelSrc.IsCancellationRequested ? new CancellationTokenSource() : taskCancelSrc;
                    itemListBox.ClearChildren();
                    var workshopItems = ((Task<ISet<Steamworks.Ugc.Item>>)t).Result;
                    foreach (var workshopItem in workshopItems)
                    {
                        var itemFrame = new GUIFrame(
                            new RectTransform((1.0f, 1.0f / 5.5f), itemListBox.Content.RectTransform),
                            style: "ListBoxElement")
                        {
                            UserData = (ItemOrPackage)workshopItem
                        };
                        var itemLayout = new GUILayoutGroup(
                            new RectTransform(Vector2.One, itemFrame.RectTransform),
                            isHorizontal: true, childAnchor: Anchor.CenterLeft)
                        {
                            Stretch = true
                        };

                        var thumbnailContainer
                            = CreateThumbnailContainer(itemLayout, Vector2.One, ScaleBasis.BothHeight);
                        CreateItemThumbnail(workshopItem, taskCancelSrc.Token, thumbnailContainer);
                        thumbnailContainer.CanBeFocused = false;
                        thumbnailContainer.GetAllChildren().ForEach(c => c.CanBeFocused = false);

                        var title = new GUITextBlock(
                            new RectTransform(Vector2.One, itemLayout.RectTransform),
                            workshopItem.Title, font: GUIStyle.Font)
                        {
                            CanBeFocused = false
                        };

                        if (includeSubscribeButton)
                        {
                            CreateSubscribeButton(workshopItem, new RectTransform(Vector2.One, itemLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), spriteScale: 0.4f);
                        }
                        itemLayout.Recalculate();
                    }
                    onFill?.Invoke(workshopItems);
                });
        }
        
        private GUIFrame CreateThumbnailContainer(
            GUIComponent parent,
            Vector2 relativeSize,
            ScaleBasis scaleBasis)
        => new GUIFrame(new RectTransform(relativeSize, parent.RectTransform, scaleBasis: scaleBasis),
                    style: "GUIFrameListBox");
        
        private SteamManager.Workshop.ItemThumbnail CreateItemThumbnail(
            in Steamworks.Ugc.Item workshopItem,
            CancellationToken cancellationToken,
            GUIFrame thumbnailContainer)
        {
            var thumbnail = new SteamManager.Workshop.ItemThumbnail(workshopItem, cancellationToken);
            itemThumbnails.Add(thumbnail);
            CreateAsyncThumbnailComponent(thumbnailContainer, () => thumbnail.Texture, () => thumbnail.Loading);
            return thumbnail;
        }

        private GUICustomComponent CreateAsyncThumbnailComponent(GUIFrame thumbnailContainer, Func<Texture2D?> textureGetter, Func<bool> throbberEnabled)
        {
            int randomThrobberOffset = Rand.Range(0, 10, Rand.RandSync.Unsynced);
            return new GUICustomComponent(
                new RectTransform(Vector2.One, thumbnailContainer.RectTransform, Anchor.Center),
                onDraw: (spriteBatch, component) =>
                {
                    Rectangle rect = component.Rect;
                    Texture2D? texture = textureGetter();
                    if (texture != null)
                    {
                        rect.Location += (4, 4);
                        rect.Size -= (8, 8);
                        Point destinationSizeMaxWidth = (rect.Width, rect.Width * texture.Height / texture.Width);
                        Point destinationSizeMaxHeight = (rect.Height * texture.Width / texture.Height, rect.Height);
                        Point destinationSize = destinationSizeMaxHeight.X > rect.Width
                            ? destinationSizeMaxWidth
                            : destinationSizeMaxHeight;
                        Rectangle destinationRectangle = new Rectangle(
                            rect.Center.X - destinationSize.X / 2,
                            rect.Center.Y - destinationSize.Y / 2,
                            destinationSize.X,
                            destinationSize.Y);
                        spriteBatch.Draw(texture, destinationRectangle, Color.White);
                    }
                    else if (throbberEnabled())
                    {
                        var sheet = GUIStyle.GenericThrobber;
                        Vector2 pos = rect.Center.ToVector2() - Vector2.One * rect.Height * 0.4f;
                        sheet.Draw(spriteBatch, ((int)Math.Floor(Timing.TotalTime * 24.0f) + randomThrobberOffset) % sheet.FrameCount, pos, Color.White,
                            origin: Vector2.Zero, rotate: 0.0f,
                            scale: Vector2.One * component.Rect.Height / sheet.FrameSize.ToVector2() * 0.8f);
                    }
                });
        }
        
        private GUIListBox CreateTagsList(IEnumerable<Identifier> tags, RectTransform rectT, bool canBeFocused)
        {
            var tagsList
                = new GUIListBox(rectT, style: null, isHorizontal: false)
                {
                    UseGridLayout = true,
                    ScrollBarEnabled = false,
                    ScrollBarVisible = false,
                    HideChildrenOutsideFrame = false,
                    Spacing = GUI.IntScale(4)
                };
            tagsList.Content.ClampMouseRectToParent = false;
            foreach (Identifier tag in tags)
            {
                var tagBtn = new GUIButton(
                    new RectTransform(new Vector2(0.25f, 1.0f / 8.0f), tagsList.Content.RectTransform,
                        anchor: Anchor.TopLeft),
                    TextManager.Get($"workshop.contenttag.{tag.Value.RemoveWhitespace()}")
                        .Fallback(tag.Value.CapitaliseFirstInvariant()), style: "GUIButtonRound")
                {
                    CanBeFocused = canBeFocused,
                    Selected = !canBeFocused,
                    UserData = tag
                };
                tagBtn.RectTransform.NonScaledSize
                    = tagBtn.Font.MeasureString(tagBtn.Text).ToPoint() + new Point(GUI.IntScale(15), GUI.IntScale(5));
                tagBtn.RectTransform.IsFixedSize = true;
                tagBtn.ClampMouseRectToParent = false;
            }

            return tagsList;
        }
        
        private void PopulateFrameWithItemInfo(Steamworks.Ugc.Item workshopItem, GUIFrame parentFrame)
        {
            ViewingItemDetails = true;
            taskCancelSrc = taskCancelSrc.IsCancellationRequested ? new CancellationTokenSource() : taskCancelSrc;

            var contentPackage
                = ContentPackageManager.WorkshopPackages.FirstOrDefault(p =>
                    p.TryExtractSteamWorkshopId(out var workshopId)
                    && workshopId.Value == workshopItem.Id);
            
            var verticalLayout = new GUILayoutGroup(new RectTransform(Vector2.One, parentFrame.RectTransform));

            var headerLayout = new GUILayoutGroup(new RectTransform((1.0f, 0.1f), verticalLayout.RectTransform),
                isHorizontal: true) { Stretch = true };

            var titleAndAuthorLayout = new GUILayoutGroup(new RectTransform(Vector2.One, headerLayout.RectTransform));
            
            var selectedTitle =
                new GUITextBlock(new RectTransform((1.0f, 0.5f), titleAndAuthorLayout.RectTransform), workshopItem.Title,
                    font: GUIStyle.LargeFont);
            
            var author = workshopItem.Owner;
            var authorButton = new GUIButton(new RectTransform((1.0f, 0.5f),
                    titleAndAuthorLayout.RectTransform),
                style: null,
                textAlignment: Alignment.CenterLeft)
            {
                ForceUpperCase = ForceUpperCase.No,
                Font = GUIStyle.SubHeadingFont,
                TextColor = GUIStyle.TextColorNormal,
                HoverTextColor = Color.White,
                SelectedTextColor = GUIStyle.TextColorNormal,
                OnClicked = (button, o) =>
                {
                    SteamManager.OverlayCustomUrl(
                        $"https://steamcommunity.com/profiles/{author.Id}/myworkshopfiles/?appid={SteamManager.AppID}");
                    return false;
                }
            };
            var authorPadding = authorButton.GetChild<GUITextBlock>().Padding;

            RectTransform rightSideButtonRectT()
                => new RectTransform(Vector2.One, headerLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight);

            bool reinstallAction(GUIButton button, object o)
            {
                SettingsMenu.Instance?.ApplyInstalledModChanges();
                int prevIndex = ContentPackageManager.EnabledPackages.Regular.IndexOf(contentPackage);
                TaskPool.AddIfNotFound($"Reinstall{workshopItem.Id}", 
                    SteamManager.Workshop.Reinstall(workshopItem), t =>
                {
                    ContentPackageManager.WorkshopPackages.Refresh();
                    ContentPackageManager.EnabledPackages.RefreshUpdatedMods();
                    if (SettingsMenu.Instance?.WorkshopMenu is MutableWorkshopMenu mutableWorkshopMenu && !mutableWorkshopMenu.ViewingItemDetails)
                    {
                        mutableWorkshopMenu.PopulateInstalledModLists(forceRefreshEnabled: true);
                    }
                });
                return false;
            }

            var (updateButton, updateSprite) = CreatePaddedButton(
                rightSideButtonRectT(),
                "GUIUpdateButton",
                spriteScale: 0.8f);
            updateButton.ToolTip = TextManager.Get("WorkshopItemUpdate");
            updateButton.Visible = false;
            updateButton.OnClicked = reinstallAction;

            if (contentPackage != null)
            {
                TaskPool.AddIfNotFound(
                    $"DetermineUpdateRequired{contentPackage.UgcId}",
                    contentPackage.IsUpToDate(),
                    t =>
                    {
                        if (!t.TryGetResult(out bool isUpToDate)) { return; }

                        updateButton.Visible = !isUpToDate;
                    });
            }
            
            var (reinstallButton, reinstallSprite) = CreatePaddedButton(
                rightSideButtonRectT(),
                "GUIReloadButton",
                spriteScale: 0.8f);
            reinstallButton.ToolTip = TextManager.Get("WorkshopItemReinstall");
            reinstallButton.OnClicked = reinstallAction;
            var reinstallButtonUpdater = new GUICustomComponent(
                new RectTransform(Vector2.Zero, reinstallButton.RectTransform),
                onUpdate: (f, component) =>
                {
                    reinstallButton.Visible = workshopItem.IsSubscribed
                                              || workshopItem.Owner.Id == SteamManager.GetSteamId().Select(steamId => steamId.Value).Fallback(0);
                    reinstallButton.Enabled = !workshopItem.IsDownloading && !workshopItem.IsDownloadPending
                                              && !SteamManager.Workshop.IsInstalling(workshopItem);

                    reinstallSprite.Color = reinstallButton.Enabled
                        ? reinstallSprite.Style.Color
                        : Color.DimGray;
                    updateButton.Enabled = reinstallButton.Enabled && contentPackage != null && ContentPackageManager.WorkshopPackages.Contains(contentPackage);
                    updateSprite.Color = reinstallSprite.Color;

                    if (contentPackage != null
                        && !ContentPackageManager.WorkshopPackages.Contains(contentPackage)
                        && ContentPackageManager.WorkshopPackages.Any(p =>
                            p.TryExtractSteamWorkshopId(out var workshopId)
                            && workshopId.Value == workshopItem.Id))
                    {
                        updateButton.Visible = false;
                    }
                });
            CreateSubscribeButton(workshopItem,
                rightSideButtonRectT(),
                spriteScale: 0.8f);

            var padding = new GUIFrame(
                new RectTransform((0.15f, 1.0f), headerLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                style: null);
            
            padding = new GUIFrame(new RectTransform((1.0f, 0.015f), verticalLayout.RectTransform), style: null);
            
            var horizontalLayout = new GUILayoutGroup(new RectTransform((1.0f, 0.45f), verticalLayout.RectTransform),
                isHorizontal: true)
            {
                Stretch = true
            };
            
            TaskPool.Add($"Request username for {author.Id}", author.RequestInfoAsync(), (t) =>
            {
                authorButton.Text = author.Name;
                authorButton.RectTransform.NonScaledSize =
                    ((int)(authorButton.Font.MeasureString(author.Name).X + authorPadding.X + authorPadding.Z),
                        authorButton.RectTransform.NonScaledSize.Y);
            });

            var thumbnailSuperContainer = new GUIFrame(
                new RectTransform(Vector2.One, horizontalLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                style: null);
            GUIFrame thumbnailContainer = CreateThumbnailContainer(thumbnailSuperContainer, Vector2.One,
                scaleBasis: ScaleBasis.BothHeight);
            CreateItemThumbnail(workshopItem, taskCancelSrc.Token, thumbnailContainer);
            thumbnailContainer.RectTransform.Anchor = Anchor.Center;
            thumbnailContainer.RectTransform.Pivot = Pivot.Center;

            var statsBox = new GUIFrame(new RectTransform((0.6f, 1.0f), horizontalLayout.RectTransform),
                style: "GUIFrameListBox");
            
            #region Stats box
            var statsHorizontalLayout = new GUILayoutGroup(new RectTransform(Vector2.One, statsBox.RectTransform), isHorizontal: true);
            var statsVertical0
                = new GUILayoutGroup(new RectTransform((1.0f, 1.0f), statsHorizontalLayout.RectTransform), childAnchor: Anchor.TopCenter);

            statFrame("", ""); //padding
            
            var scoreFrame = new GUIFrame(new RectTransform((1.0f, 0.12f), statsVertical0.RectTransform), style: null);
            var scoreLabel = new GUITextBlock(new RectTransform((0.4f, 1.0f), scoreFrame.RectTransform),
                TextManager.Get("WorkshopItemScore"), font: GUIStyle.SubHeadingFont);
            var scoreStarContainer
                = new GUILayoutGroup(
                    new RectTransform((0.6f, 1.0f), scoreFrame.RectTransform, Anchor.CenterRight),
                    isHorizontal: true,
                    childAnchor: Anchor.CenterLeft) { Stretch = true };
            var starColor = Color.Lerp(
                Color.Lerp(Color.White, Color.Yellow, Math.Min(workshopItem.Score * 2.0f, 1.0f)),
                Color.Lime, Math.Max(0.0f, (workshopItem.Score - 0.5f) * 2.0f));
            for (int i = 0; i < 5; i++)
            {
                bool isStarLit = i <= Round(workshopItem.Score * 5.0f);
                var star = new GUIFrame(new RectTransform(Vector2.One, scoreStarContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    style: isStarLit ? "GUIStarIconBright" : "GUIStarIconDark");
                if (isStarLit)
                {
                    star.Color = starColor;
                    star.HoverColor = starColor;
                    star.SelectedColor = starColor;
                }
            }
            var scoreTextPadding = new GUIFrame(new RectTransform((0.5f, 1.0f), scoreStarContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                style: null);

            var scoreTextContainer = new GUIFrame(new RectTransform(Vector2.One, scoreStarContainer.RectTransform),
                style: null);
            
            var scoreVoteCount = new GUITextBlock(
                new RectTransform((1.0f, 1.5f), scoreTextContainer.RectTransform, Anchor.Center),
                TextManager.GetWithVariable("WorkshopItemVotes", "[VoteCount]",
                    (workshopItem.VotesUp + workshopItem.VotesDown).ToString()), textAlignment: Alignment.BottomLeft)
            {
                Padding = Vector4.Zero
            };
            var subscriptionCount = new GUITextBlock(
                new RectTransform((1.0f, 1.5f), scoreTextContainer.RectTransform, Anchor.Center),
                TextManager.GetWithVariable("WorkshopItemSubscriptions", "[SubscriptionCount]",
                    workshopItem.NumUniqueSubscriptions.ToString()), textAlignment: Alignment.TopLeft)
            {
                Padding = Vector4.Zero
            };

            void statFrame(LocalizedString labelText, LocalizedString dataText)
            {
                var frame = new GUIFrame(new RectTransform((1.0f, 0.12f), statsVertical0!.RectTransform), style: null);
                var label = new GUITextBlock(new RectTransform((0.4f, 1.0f), frame.RectTransform),
                    labelText, font: GUIStyle.SubHeadingFont);
                var data = new GUITextBlock(new RectTransform((0.6f, 1.0f), frame.RectTransform, Anchor.CenterRight),
                    dataText, font: GUIStyle.Font)
                {
                    Padding = Vector4.Zero
                };
            }

            statFrame(TextManager.Get("WorkshopItemFileSize"), MathUtils.GetBytesReadable(workshopItem.SizeOfFileInBytes));
            statFrame(TextManager.Get("WorkshopItemCreationDate"), workshopItem.Created.ToShortDateString());
            statFrame(TextManager.Get("WorkshopItemModificationDate"), workshopItem.Updated.ToShortDateString());

            var tagsLabel = new GUITextBlock(new RectTransform((1.0f, 0.12f), statsVertical0.RectTransform),
                TextManager.Get("WorkshopItemTags"), font: GUIStyle.SubHeadingFont);
            CreateTagsList(workshopItem.Tags.ToIdentifiers(), new RectTransform((0.97f, 0.3f), statsVertical0.RectTransform), canBeFocused: false);
            #endregion

            var descriptionListBox = new GUIListBox(new RectTransform((1.0f, 0.38f), verticalLayout.RectTransform));
            CreateBBCodeElement(workshopItem.Description, descriptionListBox);

            var showInSteamContainer
                = new GUIFrame(new RectTransform((1.0f, 0.05f), verticalLayout.RectTransform), style: null);
            CreateShowInSteamButton(workshopItem, new RectTransform((0.2f, 1.0f), showInSteamContainer.RectTransform, Anchor.CenterRight));
        }
    }
}
