using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    class SubmarineSelection
    {
        private const int submarinesPerPage = 4;
        private int currentPage = 1;
        private int pageCount;
        private readonly bool transferService, purchaseService;
        private bool initialized;
        private int deliveryFee;
        private string deliveryLocationName;

        public GUIFrame GuiFrame;
        private GUIFrame pageIndicatorHolder;
        private GUICustomComponent selectedSubmarineIndicator;
        private GUILayoutGroup submarineHorizontalGroup, submarineControlsGroup;
        private GUIButton browseLeftButton, browseRightButton, confirmButton, confirmButtonAlt;
        private GUIListBox specsFrame;
        private GUIImage[] pageIndicators;
        private GUITextBlock descriptionTextBlock;
        private int selectionIndicatorThickness;
        private GUIImage listBackground;

        private readonly List<SubmarineInfo> subsToShow;
        private readonly SubmarineDisplayContent[] submarineDisplays = new SubmarineDisplayContent[submarinesPerPage];
        private SubmarineInfo selectedSubmarine = null;
        private string purchaseAndSwitchText, purchaseOnlyText, deliveryText, currentSubText, deliveryFeeText, priceText, switchText, missingPreviewText, currencyShorthandText, currencyLongText;
        private readonly RectTransform parent;
        private readonly Action closeAction;
        private Sprite pageIndicator;

        public static readonly string[] DeliveryTextVariables = new string[] { "[submarinename1]", "[location1]", "[location2]", "[submarinename2]", "[amount]", "[currencyname]" };
        public static readonly string[] SwitchTextVariables = new string[] { "[submarinename1]", "[submarinename2]" };
        public static readonly string[] PurchaseAndSwitchTextVariables = new string[] { "[submarinename1]", "[amount]", "[currencyname]", "[submarinename2]" };
        public static readonly string[] PurchaseTextVariables = new string[] { "[submarinename]", "[amount]", "[currencyname]" };

        private static readonly string[] notEnoughCreditsDeliveryTextVariables = new string[] { "[currencyname]", "[submarinename]", "[location1]", "[location2]" };
        private static readonly string[] notEnoughCreditsPurchaseTextVariables = new string[] { "[currencyname]", "[submarinename]" };
        private readonly string[] messageBoxOptions;

        public const int DeliveryFeePerDistanceTravelled = 1000;
        public static bool ContentRefreshRequired = false;

        private static readonly Color indicatorColor = new Color(112, 149, 129);
        private Point createdForResolution;

        private struct SubmarineDisplayContent
        {
            public GUIFrame background;
            public GUIImage submarineImage;
            public SubmarineInfo displayedSubmarine;
            public GUITextBlock submarineName;
            public GUITextBlock submarineClass;
            public GUITextBlock submarineFee;
            public GUIButton selectSubmarineButton;
            public GUITextBlock middleTextBlock;
            public GUIButton previewButton;
        }

        public SubmarineSelection(bool transfer, Action closeAction, RectTransform parent)
        {
            if (GameMain.GameSession.Campaign == null) { return; }

            transferService = transfer;
            purchaseService = !transfer;
            this.parent = parent;
            this.closeAction = closeAction;

            subsToShow = new List<SubmarineInfo>();

            if (GameMain.Client == null)
            {
                messageBoxOptions = new string[2] { TextManager.Get("Yes"), TextManager.Get("Cancel") };
            }
            else
            {
                messageBoxOptions = new string[2] { TextManager.Get("Yes") + " " + TextManager.Get("initiatevoting"), TextManager.Get("Cancel") };
            }

            if (Submarine.MainSub?.Info == null) { return; }
            Initialize();
        }

        private void Initialize()
        {
            initialized = true;
            if (transferService)
            {
                deliveryFee = CalculateDeliveryFee();
                currentSubText = TextManager.Get("currentsub");
                deliveryFeeText = TextManager.Get("deliveryfee");
                deliveryText = TextManager.Get("requestdeliverybutton");
                switchText = TextManager.Get("switchtosubmarinebutton");
            }
            else
            {
                purchaseAndSwitchText = TextManager.Get("purchaseandswitch");
                purchaseOnlyText = TextManager.Get("purchase");
                priceText = TextManager.Get("price");
            }

            currencyShorthandText = TextManager.Get("currencyformat");
            currencyLongText = TextManager.Get("credit").ToLower();

            UpdateSubmarines();
            missingPreviewText = TextManager.Get("SubPreviewImageNotFound");
            CreateGUI();
        }

        private int CalculateDeliveryFee()
        {
            int distanceToOutpost = GameMain.GameSession.Map.DistanceToClosestLocationWithOutpost(GameMain.GameSession.Map.CurrentLocation, out Location endLocation);
            deliveryLocationName = endLocation.Name;
            return DeliveryFeePerDistanceTravelled * distanceToOutpost;
        }

        private void CreateGUI()
        {
            createdForResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            GUILayoutGroup content;
            GuiFrame = new GUIFrame(new RectTransform(new Vector2(0.75f, 0.7f), parent, Anchor.TopCenter, Pivot.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.02f) });
            selectionIndicatorThickness = HUDLayoutSettings.Padding / 2;

            GUIFrame background = new GUIFrame(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center), color: Color.Black * 0.9f)
            {
                CanBeFocused = false
            };

            content = new GUILayoutGroup(new RectTransform(new Point(background.Rect.Width - HUDLayoutSettings.Padding * 4, background.Rect.Height - HUDLayoutSettings.Padding * 4), background.RectTransform, Anchor.Center)) { AbsoluteSpacing = (int)(HUDLayoutSettings.Padding * 1.5f) };
            GUITextBlock header = new GUITextBlock(new RectTransform(new Vector2(1f, 0.0f), content.RectTransform), transferService ? TextManager.Get("switchsubmarineheader") : TextManager.GetWithVariable("outpostshipyard", "[location]", GameMain.GameSession.Map.CurrentLocation.Name), font: GUI.LargeFont);
            header.CalculateHeightFromText(0, true);
            GUITextBlock credits = new GUITextBlock(new RectTransform(Vector2.One, header.RectTransform), "", font: GUI.SubHeadingFont, textAlignment: Alignment.CenterRight)
            {
                TextGetter = CampaignUI.GetMoney
            };

            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), content.RectTransform), style: "HorizontalLine");

            GUILayoutGroup submarineContentGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.4f), content.RectTransform)) { AbsoluteSpacing = HUDLayoutSettings.Padding, Stretch = true };
            submarineHorizontalGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.9f), submarineContentGroup.RectTransform)) { IsHorizontal = true, AbsoluteSpacing = HUDLayoutSettings.Padding, Stretch = true };

            submarineControlsGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), submarineContentGroup.RectTransform), true, Anchor.TopCenter);

            GUILayoutGroup infoFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.4f), content.RectTransform)) { IsHorizontal = true, Stretch = true, AbsoluteSpacing = HUDLayoutSettings.Padding };
            new GUIFrame(new RectTransform(Vector2.One, infoFrame.RectTransform), style: null, new Color(8, 13, 19)) { IgnoreLayoutGroups = true };
            listBackground = new GUIImage(new RectTransform(new Vector2(0.59f, 1f), infoFrame.RectTransform, Anchor.CenterRight), style: null, true)
            {
                IgnoreLayoutGroups = true                
            };
            new GUIListBox(new RectTransform(Vector2.One, infoFrame.RectTransform)) { IgnoreLayoutGroups = true, CanBeFocused = false };
            specsFrame = new GUIListBox(new RectTransform(new Vector2(0.39f, 1f), infoFrame.RectTransform), style: null) { Spacing = GUI.IntScale(5), Padding = new Vector4(HUDLayoutSettings.Padding / 2f, HUDLayoutSettings.Padding, 0, 0) };
            new GUIFrame(new RectTransform(new Vector2(0.02f, 0.8f), infoFrame.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.1f) }, style: "VerticalLine");
            GUIListBox descriptionFrame = new GUIListBox(new RectTransform(new Vector2(0.59f, 1f), infoFrame.RectTransform), style: null) { Padding = new Vector4(HUDLayoutSettings.Padding / 2f, HUDLayoutSettings.Padding * 1.5f, HUDLayoutSettings.Padding * 1.5f, HUDLayoutSettings.Padding / 2f) };
            descriptionTextBlock = new GUITextBlock(new RectTransform(new Vector2(1, 0), descriptionFrame.Content.RectTransform), string.Empty, font: GUI.Font, wrap: true) { CanBeFocused = false };

            GUILayoutGroup buttonFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.075f), content.RectTransform), childAnchor: Anchor.CenterRight) { IsHorizontal = true, AbsoluteSpacing = HUDLayoutSettings.Padding };

            if (closeAction != null)
            {
                GUIButton closeButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1f), buttonFrame.RectTransform), TextManager.Get("Close"), style: "GUIButtonFreeScale")
                {
                    OnClicked = (button, userData) =>
                    {
                        closeAction();
                        return true;
                    }
                };
            }

            if (purchaseService) confirmButtonAlt = new GUIButton(new RectTransform(new Vector2(0.2f, 1f), buttonFrame.RectTransform), purchaseOnlyText, style: "GUIButtonFreeScale");
            confirmButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1f), buttonFrame.RectTransform), purchaseService ? purchaseAndSwitchText : deliveryFee > 0 ? deliveryText : switchText, style: "GUIButtonFreeScale");
            SetConfirmButtonState(false);

            pageIndicatorHolder = new GUIFrame(new RectTransform(new Vector2(1f, 1.5f), submarineControlsGroup.RectTransform), style: null);
            pageIndicator = GUI.Style.GetComponentStyle("GUIPageIndicator").GetDefaultSprite();
            UpdatePaging();

            for (int i = 0; i < submarineDisplays.Length; i++)
            {
                SubmarineDisplayContent submarineDisplayElement = new SubmarineDisplayContent
                {
                    background = new GUIFrame(new RectTransform(new Vector2(1f / submarinesPerPage, 1f), submarineHorizontalGroup.RectTransform), style: null, new Color(8, 13, 19))
                };
                submarineDisplayElement.submarineImage = new GUIImage(new RectTransform(new Vector2(0.8f, 1f), submarineDisplayElement.background.RectTransform, Anchor.Center), null, true);
                submarineDisplayElement.middleTextBlock = new GUITextBlock(new RectTransform(new Vector2(0.8f, 1f), submarineDisplayElement.background.RectTransform, Anchor.Center), string.Empty, textAlignment: Alignment.Center);
                submarineDisplayElement.submarineName = new GUITextBlock(new RectTransform(new Vector2(1f, 0.1f), submarineDisplayElement.background.RectTransform, Anchor.TopCenter, Pivot.TopCenter) { AbsoluteOffset = new Point(0, HUDLayoutSettings.Padding) }, string.Empty, textAlignment: Alignment.Center, font: GUI.SubHeadingFont);
                submarineDisplayElement.submarineClass = new GUITextBlock(new RectTransform(new Vector2(1f, 0.1f), submarineDisplayElement.background.RectTransform, Anchor.TopCenter, Pivot.TopCenter) { AbsoluteOffset = new Point(0, HUDLayoutSettings.Padding + (int)GUI.Font.MeasureString(submarineDisplayElement.submarineName.Text).Y) }, string.Empty, textAlignment: Alignment.Center);
                submarineDisplayElement.submarineFee = new GUITextBlock(new RectTransform(new Vector2(1f, 0.1f), submarineDisplayElement.background.RectTransform, Anchor.BottomCenter, Pivot.BottomCenter) { AbsoluteOffset = new Point(0, HUDLayoutSettings.Padding) }, string.Empty, textAlignment: Alignment.Center, font: GUI.SubHeadingFont);
                submarineDisplayElement.selectSubmarineButton = new GUIButton(new RectTransform(Vector2.One, submarineDisplayElement.background.RectTransform), style: null);
                submarineDisplayElement.previewButton = new GUIButton(new RectTransform(Vector2.One * 0.12f, submarineDisplayElement.background.RectTransform, anchor: Anchor.BottomRight, pivot: Pivot.BottomRight, scaleBasis: ScaleBasis.BothHeight) { AbsoluteOffset = new Point((int)(0.03f * background.Rect.Height)) }, style: "ExpandButton")
                {
                    Color = Color.White,
                    HoverColor = Color.White,
                    PressedColor = Color.White
                };
                submarineDisplays[i] = submarineDisplayElement;
            }

            selectedSubmarineIndicator = new GUICustomComponent(new RectTransform(Point.Zero, submarineHorizontalGroup.RectTransform), onDraw: (sb, component) => DrawSubmarineIndicator(sb, component.Rect)) { IgnoreLayoutGroups = true, CanBeFocused = false };
        }

        private void UpdatePaging()
        {
            if (pageIndicatorHolder == null) return;
            pageIndicatorHolder.ClearChildren();
            if (currentPage > pageCount) currentPage = pageCount;
            if (pageCount < 2) return;

            browseLeftButton = new GUIButton(new RectTransform(new Vector2(1.15f, 1.15f), pageIndicatorHolder.RectTransform, Anchor.CenterLeft, Pivot.CenterRight) { AbsoluteOffset = new Point(-HUDLayoutSettings.Padding * 3, 0) }, string.Empty, style: "GUIButtonToggleLeft")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (button, userData) =>
                {
                    ChangePage(-1);
                    return true;
                }
            };

            Point indicatorSize = new Point(GUI.IntScale(pageIndicator.SourceRect.Width * 1.5f), GUI.IntScale(pageIndicator.SourceRect.Height * 1.5f));
            pageIndicatorHolder.RectTransform.NonScaledSize = new Point(pageCount * indicatorSize.X + HUDLayoutSettings.Padding * (pageCount - 1), pageIndicatorHolder.RectTransform.NonScaledSize.Y);

            int xPos = 0;
            int yPos = pageIndicatorHolder.Rect.Height / 2 - indicatorSize.Y / 2;

            pageIndicators = new GUIImage[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                pageIndicators[i] = new GUIImage(new RectTransform(indicatorSize, pageIndicatorHolder.RectTransform) { AbsoluteOffset = new Point(xPos, yPos) }, pageIndicator, null, true);
                xPos += indicatorSize.X + HUDLayoutSettings.Padding;
            }

            for (int i = 0; i < pageIndicators.Length; i++)
            {
                pageIndicators[i].Color = i == currentPage - 1 ? Color.White : Color.Gray;
            }

            browseRightButton = new GUIButton(new RectTransform(new Vector2(1.15f, 1.15f), pageIndicatorHolder.RectTransform, Anchor.CenterRight, Pivot.CenterLeft) { AbsoluteOffset = new Point(-HUDLayoutSettings.Padding * 3, 0) }, string.Empty, style: "GUIButtonToggleRight")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (button, userData) =>
                {
                    ChangePage(1);
                    return true;
                }
            };

            browseLeftButton.Enabled = currentPage > 1;
            browseRightButton.Enabled = currentPage < pageCount;
        }

        private void DrawSubmarineIndicator(SpriteBatch spriteBatch, Rectangle area)
        {
            if (area == Rectangle.Empty) return;
            GUI.DrawRectangle(spriteBatch, area, indicatorColor, thickness: selectionIndicatorThickness);
        }

        public void Update()
        {
            if (ContentRefreshRequired)
            {
                RefreshSubmarineDisplay(true);
            }

            // Input
            if (PlayerInput.KeyHit(Keys.Left))
            {
                SelectSubmarine(subsToShow.IndexOf(selectedSubmarine), -1);
            }
            else if (PlayerInput.KeyHit(Keys.Right))
            {
                SelectSubmarine(subsToShow.IndexOf(selectedSubmarine), 1);
            }
        }

        public void RefreshSubmarineDisplay(bool updateSubs)
        {
            if (!initialized) Initialize();
            if (GameMain.GraphicsWidth != createdForResolution.X || GameMain.GraphicsHeight != createdForResolution.Y) CreateGUI();
            if (updateSubs) UpdateSubmarines();

            if (pageIndicators != null)
            {
                for (int i = 0; i < pageIndicators.Length; i++)
                {
                    pageIndicators[i].Color = i == currentPage - 1 ? Color.White : Color.Gray;
                }
            }

            int submarineIndex = (currentPage - 1) * submarinesPerPage;

            for (int i = 0; i < submarineDisplays.Length; i++)
            {
                SubmarineInfo subToDisplay = GetSubToDisplay(submarineIndex);
                if (subToDisplay == null)
                {
                    submarineDisplays[i].submarineImage.Sprite = null;
                    submarineDisplays[i].submarineName.Text = string.Empty;
                    submarineDisplays[i].submarineFee.Text = string.Empty;
                    submarineDisplays[i].submarineClass.Text = string.Empty;
                    submarineDisplays[i].selectSubmarineButton.Enabled = false;
                    submarineDisplays[i].selectSubmarineButton.OnClicked = null;
                    submarineDisplays[i].displayedSubmarine = null;
                    submarineDisplays[i].middleTextBlock.AutoDraw = false;
                    submarineDisplays[i].previewButton.Visible = false;
                }
                else
                {
                    submarineDisplays[i].displayedSubmarine = subToDisplay;
                    Sprite previewImage = GetPreviewImage(subToDisplay);

                    if (previewImage != null)
                    {
                        submarineDisplays[i].submarineImage.Sprite = previewImage;
                        submarineDisplays[i].middleTextBlock.AutoDraw = false;
                    }
                    else
                    {
                        submarineDisplays[i].submarineImage.Sprite = null;
                        submarineDisplays[i].middleTextBlock.Text = missingPreviewText;
                        submarineDisplays[i].middleTextBlock.AutoDraw = true;
                    }

                    submarineDisplays[i].selectSubmarineButton.Enabled = true;

                    int index = i;
                    submarineDisplays[i].selectSubmarineButton.OnClicked = (button, userData) =>
                    {
                        SelectSubmarine(subToDisplay, submarineDisplays[index].background.Rect);
                        return true;
                    };

                    submarineDisplays[i].submarineName.Text = subToDisplay.DisplayName;
                    submarineDisplays[i].submarineClass.Text = $"{TextManager.GetWithVariable("submarineclass.classsuffixformat", "[type]", TextManager.Get($"submarineclass.{subToDisplay.SubmarineClass}"))}";

                    if (!GameMain.GameSession.IsSubmarineOwned(subToDisplay))
                    {
                        string amountString = currencyShorthandText.Replace("[credits]", subToDisplay.Price.ToString());
                        submarineDisplays[i].submarineFee.Text = priceText.Replace("[amount]", amountString).Replace("[currencyname]", string.Empty).TrimEnd();
                    }
                    else
                    {
                        if (subToDisplay.Name != CurrentOrPendingSubmarine().Name)
                        {
                            if (deliveryFee > 0)
                            {
                                string amountString = currencyShorthandText.Replace("[credits]", deliveryFee.ToString());
                                submarineDisplays[i].submarineFee.Text = deliveryFeeText.Replace("[amount]", amountString).Replace("[currencyname]", string.Empty).TrimEnd();
                            }
                            else
                            {
                                submarineDisplays[i].submarineFee.Text = string.Empty;
                            }
                        }
                        else
                        {
                            submarineDisplays[i].submarineFee.Text = currentSubText;
                        }
                    }

                    if (transferService && subToDisplay.Name == CurrentOrPendingSubmarine().Name && updateSubs)
                    {                        
                        if (selectedSubmarine == null)
                        {
                            CoroutineManager.StartCoroutine(SelectOwnSubmarineWithDelay(subToDisplay, submarineDisplays[i]));
                        }
                        else
                        {
                            SelectSubmarine(subToDisplay, submarineDisplays[i].background.Rect);
                        }
                    }
                    else if (!transferService && selectedSubmarine == null || !transferService && GameMain.GameSession.IsSubmarineOwned(selectedSubmarine) || subToDisplay == selectedSubmarine)
                    {
                        SelectSubmarine(subToDisplay, submarineDisplays[i].background.Rect);
                    }

                    submarineDisplays[i].previewButton.Visible = true;
                    submarineDisplays[i].previewButton.OnClicked = (btn, obj) =>
                    {
                        SubmarinePreview.Create(subToDisplay);
                        return false;
                    };
                }

                submarineIndex++;
            }

            if (subsToShow.Count == 0)
            {
                SelectSubmarine(null, Rectangle.Empty);
            }
        }

        private void UpdateSubmarines()
        {
            subsToShow.Clear();
            if (transferService)
            {
                subsToShow.AddRange(GameMain.GameSession.OwnedSubmarines);
                subsToShow.Sort((x, y) => x.SubmarineClass.CompareTo(y.SubmarineClass));
                string currentSubName = CurrentOrPendingSubmarine().Name;
                int currentIndex = subsToShow.FindIndex(s => s.Name == currentSubName);
                if (currentIndex != -1)
                {
                    currentPage = (int)Math.Ceiling((currentIndex + 1) / (float)submarinesPerPage);
                }
            }
            else
            {
                if (GameMain.Client == null)
                {
                    subsToShow.AddRange(SubmarineInfo.SavedSubmarines.Where(s => s.IsCampaignCompatible && !GameMain.GameSession.OwnedSubmarines.Any(os => os.Name == s.Name)));
                }
                else
                {
                    subsToShow.AddRange(GameMain.NetLobbyScreen.CampaignSubmarines.Where(s => !GameMain.GameSession.OwnedSubmarines.Any(os => os.Name == s.Name)));
                }

                subsToShow.Sort((x, y) => x.SubmarineClass.CompareTo(y.SubmarineClass));
            }

            if (transferService) SetConfirmButtonState(selectedSubmarine != null && selectedSubmarine.Name != CurrentOrPendingSubmarine().Name);

            subsToShow.Sort((x, y) => x.SubmarineClass.CompareTo(y.SubmarineClass));
            pageCount = Math.Max(1, (int)Math.Ceiling(subsToShow.Count / (float)submarinesPerPage));
            UpdatePaging();
            ContentRefreshRequired = false;
        }

        private SubmarineInfo GetSubToDisplay(int index)
        {
            if (subsToShow.Count <= index || index < 0) { return null; }
            return subsToShow[index];
        }

        private Sprite GetPreviewImage(SubmarineInfo info)
        {
            Sprite preview = info.PreviewImage;

            if (preview == null)
            {
                SubmarineInfo potentialMatch;

                if (GameMain.Client == null)
                {
                    potentialMatch = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.EqualityCheckVal == info.EqualityCheckVal);
                }
                else
                {
                    potentialMatch = GameMain.NetLobbyScreen.CampaignSubmarines.FirstOrDefault(s => s.EqualityCheckVal == info.EqualityCheckVal);
                }

                preview = potentialMatch?.PreviewImage;

                // Try from savedsubmarines with name comparison as a backup
                if (preview == null)
                {
                    potentialMatch = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == info.Name);
                    preview = potentialMatch?.PreviewImage;
                }
            }

            return preview;
        }

        // Initial submarine selection needs a slight wait to allow the layoutgroups to place content properly
        private IEnumerable<CoroutineStatus> SelectOwnSubmarineWithDelay(SubmarineInfo info, SubmarineDisplayContent display)
        {
            yield return new WaitForSeconds(0.05f);
            SelectSubmarine(info, display.background.Rect);
        }

        // Selection based on key input
        private void SelectSubmarine(int index, int direction)
        {
            SubmarineInfo nextSub = GetSubToDisplay(index + direction);
            if (nextSub == null) return;

            for (int i = 0; i < submarineDisplays.Length; i++)
            {
                if (submarineDisplays[i].displayedSubmarine == nextSub)
                {
                    SelectSubmarine(nextSub, submarineDisplays[i].background.Rect);
                    return;
                }
            }

            ChangePage(direction);

            for (int i = 0; i < submarineDisplays.Length; i++)
            {
                if (submarineDisplays[i].displayedSubmarine == nextSub)
                {
                    SelectSubmarine(nextSub, submarineDisplays[i].background.Rect);
                    return;
                }
            }
        }

        private void SelectSubmarine(SubmarineInfo info, Rectangle backgroundRect)
        {
#if !DEBUG
            if (selectedSubmarine == info) return;
#endif
            specsFrame.Content.ClearChildren();
            selectedSubmarine = info;

            if (info != null)
            {
                bool owned = GameMain.GameSession.IsSubmarineOwned(info);

                if (owned)
                {
                    confirmButton.Text = deliveryFee > 0 ? deliveryText : switchText;
                    confirmButton.OnClicked = (button, userData) =>
                    {
                        ShowTransferPrompt();
                        return true;
                    };
                }
                else
                {
                    confirmButton.Text = purchaseAndSwitchText;
                    confirmButton.OnClicked = (button, userData) =>
                    {
                        ShowBuyPrompt(false);
                        return true;
                    };

                    confirmButtonAlt.Text = purchaseOnlyText;
                    confirmButtonAlt.OnClicked = (button, userData) =>
                    {
                        ShowBuyPrompt(true);
                        return true;
                    };
                }

                SetConfirmButtonState(selectedSubmarine.Name != CurrentOrPendingSubmarine().Name);

                selectedSubmarineIndicator.RectTransform.NonScaledSize = backgroundRect.Size;
                selectedSubmarineIndicator.RectTransform.AbsoluteOffset = new Point(backgroundRect.Left - submarineHorizontalGroup.Rect.Left, 0);

                Sprite previewImage = GetPreviewImage(info);
                listBackground.Sprite = previewImage;
                listBackground.SetCrop(true);

                ScalableFont font = GUI.Font;
                info.CreateSpecsWindow(specsFrame, font);
                descriptionTextBlock.Text = info.Description;
                descriptionTextBlock.CalculateHeightFromText();
            }
            else
            {
                listBackground.Sprite = null;
                listBackground.SetCrop(false);
                descriptionTextBlock.Text = string.Empty;
                selectedSubmarineIndicator.RectTransform.NonScaledSize = Point.Zero;
                SetConfirmButtonState(false);
            }
        }

        private void SetConfirmButtonState(bool state)
        {
            if (confirmButtonAlt != null)
            {
                confirmButtonAlt.Enabled = state;
            }

            if (confirmButton != null)
            {
                confirmButton.Enabled = state;
            }
        }

        public static SubmarineInfo CurrentOrPendingSubmarine()
        {
            if (GameMain.GameSession?.Campaign?.PendingSubmarineSwitch == null)
            {
                return Submarine.MainSub.Info;
            }
            else
            {
                return GameMain.GameSession.Campaign.PendingSubmarineSwitch;
            }
        }

        private void ChangePage(int pageChangeDirection)
        {
            SelectSubmarine(null, Rectangle.Empty);
            if (pageChangeDirection < 0 && currentPage > 1) currentPage--;
            if (pageChangeDirection > 0 && currentPage < pageCount) currentPage++;
            browseLeftButton.Enabled = currentPage > 1;
            browseRightButton.Enabled = currentPage < pageCount;

            RefreshSubmarineDisplay(false);
        }

        private void ShowTransferPrompt()
        {
            if (GameMain.GameSession.Campaign.Money < deliveryFee && deliveryFee > 0)
            {
                new GUIMessageBox(TextManager.Get("deliveryrequestheader"), TextManager.GetWithVariables("notenoughmoneyfordeliverytext", notEnoughCreditsDeliveryTextVariables,
                    new string[] { currencyLongText, selectedSubmarine.DisplayName, deliveryLocationName, GameMain.GameSession.Map.CurrentLocation.Name }));
                return;
            }

            GUIMessageBox msgBox;

            if (deliveryFee > 0)
            {
                msgBox = new GUIMessageBox(TextManager.Get("deliveryrequestheader"), TextManager.GetWithVariables("deliveryrequesttext", DeliveryTextVariables,
                    new string[6] { selectedSubmarine.DisplayName, deliveryLocationName, GameMain.GameSession.Map.CurrentLocation.Name, CurrentOrPendingSubmarine().DisplayName, deliveryFee.ToString(), currencyLongText }), messageBoxOptions);
            }
            else
            {
                msgBox = new GUIMessageBox(TextManager.Get("switchsubmarineheader"), TextManager.GetWithVariables("switchsubmarinetext", SwitchTextVariables,
                    new string[2] { CurrentOrPendingSubmarine().DisplayName, selectedSubmarine.DisplayName }), messageBoxOptions);
            }

            msgBox.Buttons[0].OnClicked = (applyButton, obj) =>
            {
                if (GameMain.Client == null)
                {
                    SubmarineInfo newSub = GameMain.GameSession.SwitchSubmarine(selectedSubmarine, deliveryFee);
                    RefreshSubmarineDisplay(true);
                }
                else
                {
                    GameMain.Client.InitiateSubmarineChange(selectedSubmarine, Networking.VoteType.SwitchSub);
                }
                return true;
            };
            msgBox.Buttons[0].OnClicked += msgBox.Close;
            msgBox.Buttons[1].OnClicked = msgBox.Close;
        }

        private void ShowBuyPrompt(bool purchaseOnly)
        {
            if (GameMain.GameSession.Campaign.Money < selectedSubmarine.Price)
            {
                new GUIMessageBox(TextManager.Get("purchasesubmarineheader"), TextManager.GetWithVariables("notenoughmoneyforpurchasetext", notEnoughCreditsPurchaseTextVariables, 
                    new string[2] { currencyLongText, selectedSubmarine.DisplayName }));
                return;
            }

            GUIMessageBox msgBox;

            if (!purchaseOnly)
            {
                msgBox = new GUIMessageBox(TextManager.Get("purchaseandswitchsubmarineheader"), TextManager.GetWithVariables("purchaseandswitchsubmarinetext", PurchaseAndSwitchTextVariables,
                    new string[4] { selectedSubmarine.DisplayName, selectedSubmarine.Price.ToString(), currencyLongText, CurrentOrPendingSubmarine().DisplayName }), messageBoxOptions);

                msgBox.Buttons[0].OnClicked = (applyButton, obj) =>
                {
                    if (GameMain.Client == null)
                    {
                        GameMain.GameSession.PurchaseSubmarine(selectedSubmarine);
                        SubmarineInfo newSub = GameMain.GameSession.SwitchSubmarine(selectedSubmarine, 0);
                        RefreshSubmarineDisplay(true);
                    }
                    else
                    {
                        GameMain.Client.InitiateSubmarineChange(selectedSubmarine, Networking.VoteType.PurchaseAndSwitchSub);
                    }
                    return true;
                };
            }
            else
            {
                msgBox = new GUIMessageBox(TextManager.Get("purchasesubmarineheader"), TextManager.GetWithVariables("purchasesubmarinetext", PurchaseTextVariables,
                    new string[3] { selectedSubmarine.DisplayName, selectedSubmarine.Price.ToString(), currencyLongText }), messageBoxOptions);

                msgBox.Buttons[0].OnClicked = (applyButton, obj) =>
                {
                    if (GameMain.Client == null)
                    {
                        GameMain.GameSession.PurchaseSubmarine(selectedSubmarine);
                        RefreshSubmarineDisplay(true);
                    }
                    else
                    {
                        GameMain.Client.InitiateSubmarineChange(selectedSubmarine, Networking.VoteType.PurchaseSub);
                    }
                    return true;
                };
            }

            msgBox.Buttons[0].OnClicked += msgBox.Close;
            msgBox.Buttons[1].OnClicked = msgBox.Close;
        }        
    }
}
