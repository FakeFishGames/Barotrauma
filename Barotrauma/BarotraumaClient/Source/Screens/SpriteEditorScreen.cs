using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class SpriteEditorScreen : Screen
    {
        private GUIListBox textureList, spriteList;

        private GUIFrame topPanel;
        private GUIFrame leftPanel;
        private GUIFrame rightPanel;
        private GUIFrame bottomPanel;
        private GUIFrame backgroundColorPanel;

        private GUIFrame topPanelContents;
        private GUITextBlock texturePathText;
        private GUITextBlock xmlPathText;
        private GUIScrollBar zoomBar;
        private List<Sprite> selectedSprites = new List<Sprite>();
        private List<Sprite> dirtySprites = new List<Sprite>();
        private Texture2D selectedTexture;
        private Sprite lastSelected;
        private Rectangle textureRect;
        private float zoom = 1;
        private float minZoom = 0.25f;
        private float maxZoom;
        private int spriteCount;

        private bool editBackgroundColor;
        private Color backgroundColor = new Color(0.051f, 0.149f, 0.271f, 1.0f);

        private readonly Camera cam;
        public override Camera Cam
        {
            get { return cam; }
        }

        public GUIComponent TopPanel
        {
            get { return topPanel; }
        }

        public SpriteEditorScreen()
        {
            cam = new Camera();
            CreateGUIElements();
        }

        #region Initialization
        private void CreateGUIElements()
        {
            topPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), Frame.RectTransform) { MinSize = new Point(0, 60) }, "GUIFrameTop");
            topPanelContents = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.8f), topPanel.RectTransform, Anchor.Center), style: null);

            new GUIButton(new RectTransform(new Vector2(0.12f, 0.4f), topPanelContents.RectTransform, Anchor.TopLeft)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, "Reload Texture")
            {
                OnClicked = (button, userData) =>
                {
                    if (!(textureList.SelectedData is Texture2D selectedTexture)) { return false; }
                    var selected = selectedSprites;
                    Sprite firstSelected = selected.First();
                    selected.ForEach(s => s.ReloadTexture());
                    RefreshLists();
                    textureList.Select(firstSelected.Texture, autoScroll: false);
                    selected.ForEachMod(s => spriteList.Select(s, autoScroll: false));
                    texturePathText.Text = "Textures reloaded from " + firstSelected.FilePath;
                    texturePathText.TextColor = Color.LightGreen;
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.12f, 0.4f), topPanelContents.RectTransform, Anchor.BottomLeft)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, "Reset Changes")
            {
                OnClicked = (button, userData) =>
                {
                    if (selectedTexture == null) { return false; }
                    foreach (Sprite sprite in loadedSprites)
                    {
                        if (sprite.Texture != selectedTexture) { continue; }
                        var element = sprite.SourceElement;
                        if (element == null) { continue; }
                        // Not all sprites have a sourcerect defined, in which case we'll want to use the current source rect instead of an empty rect.
                        sprite.SourceRect = element.GetAttributeRect("sourcerect", sprite.SourceRect);
                        sprite.RelativeOrigin = element.GetAttributeVector2("origin", new Vector2(0.5f, 0.5f));
                    }
                    ResetWidgets();
                    xmlPathText.Text = "Changes successfully reset";
                    xmlPathText.TextColor = Color.LightGreen;
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.12f, 0.4f), topPanelContents.RectTransform, Anchor.TopLeft)
            {
                RelativeOffset = new Vector2(0.15f, 0.1f)
            }, "Save Selected Sprites")
            {
                OnClicked = (button, userData) =>
                {
                    return SaveSprites(selectedSprites);
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.12f, 0.4f), topPanelContents.RectTransform, Anchor.BottomLeft)
            {
                RelativeOffset = new Vector2(0.15f, 0.1f)
            }, "Save All Sprites")
            {
                OnClicked = (button, userData) =>
                {
                    return SaveSprites(loadedSprites);
                }
            };
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 0.2f), topPanelContents.RectTransform, Anchor.TopCenter, Pivot.CenterRight) { RelativeOffset = new Vector2(0, 0.3f) }, "Zoom: ");
            zoomBar = new GUIScrollBar(new RectTransform(new Vector2(0.2f, 0.35f), topPanelContents.RectTransform, Anchor.TopCenter, Pivot.CenterRight)
            {
                RelativeOffset = new Vector2(0.05f, 0.3f)
            }, barSize: 0.1f)
            {
                BarScroll = GetBarScrollValue(),
                Step = 0.01f,
                OnMoved = (scrollBar, value) =>
                {
                    zoom = MathHelper.Lerp(minZoom, maxZoom, value);
                    viewAreaOffset = Point.Zero;
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.05f, 0.35f), topPanelContents.RectTransform, Anchor.TopCenter, Pivot.CenterLeft) { RelativeOffset = new Vector2(0.055f, 0.3f) }, "Reset Zoom")
            {
                OnClicked = (box, data) =>
                {
                    ResetZoom();
                    return true;
                }
            };

            texturePathText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.4f), topPanelContents.RectTransform, Anchor.Center, Pivot.BottomCenter) { RelativeOffset = new Vector2(0.4f, 0) }, "", Color.LightGray);
            xmlPathText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.4f), topPanelContents.RectTransform, Anchor.Center, Pivot.TopCenter) { RelativeOffset = new Vector2(0.4f, 0) }, "", Color.LightGray);

            leftPanel = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f - topPanel.RectTransform.RelativeSize.Y), Frame.RectTransform, Anchor.BottomLeft)
            { MinSize = new Point(150, 0) }, style: "GUIFrameLeft");
            var paddedLeftPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), leftPanel.RectTransform, Anchor.CenterLeft)
            { RelativeOffset = new Vector2(0.02f, 0.0f) })
            { Stretch = true };
            textureList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), paddedLeftPanel.RectTransform))
            {
                OnSelected = (listBox, userData) =>
                {
                    var previousTexture = selectedTexture;
                    selectedTexture = userData as Texture2D;
                    if (previousTexture != selectedTexture)
                    {
                        ResetZoom();
                    }
                    foreach (GUIComponent child in spriteList.Content.Children)
                    {
                        var textBlock = (GUITextBlock)child;
                        var sprite = (Sprite)textBlock.UserData;
                        textBlock.TextColor = new Color(textBlock.TextColor, sprite.Texture == selectedTexture ? 1.0f : 0.4f);
                    }
                    if (selectedSprites.None(s => s.Texture == selectedTexture))
                    {
                        spriteList.Select(loadedSprites.First(s => s.Texture == selectedTexture), autoScroll: false);
                        UpdateScrollBar(spriteList);
                    }
                    texturePathText.TextColor = Color.LightGray;
                    topPanelContents.Visible = true;
                    return true;
                }
            };

            rightPanel = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f - topPanel.RectTransform.RelativeSize.Y), Frame.RectTransform, Anchor.BottomRight) { MinSize = new Point(150, 0) }, style: "GUIFrameRight");
            var paddedRightPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), rightPanel.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.02f, 0.0f) })
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            spriteList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), paddedRightPanel.RectTransform))
            {
                OnSelected = (listBox, userData) =>
                {
                    Sprite sprite = userData as Sprite;
                    if (sprite == null) return false;
                    SelectSprite(sprite);
                    return true;
                }
            };

            // Background color
            bottomPanel = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.05f), Frame.RectTransform, Anchor.BottomCenter), style: null, color: Color.Black * 0.5f);
            new GUITickBox(new RectTransform(new Vector2(0.2f, 0.5f), bottomPanel.RectTransform, Anchor.Center), "Edit Background Color")
            {
                Selected = editBackgroundColor,
                OnSelected = box =>
                {
                    editBackgroundColor = box.Selected;
                    return true;
                }
            };
            backgroundColorPanel = new GUIFrame(new RectTransform(new Point(400, 80), Frame.RectTransform, Anchor.BottomCenter) { RelativeOffset = new Vector2(0, 0.1f) }, style: null, color: Color.Black * 0.4f);
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), backgroundColorPanel.RectTransform) { MinSize = new Point(80, 26) }, "Background \nColor:", textColor: Color.WhiteSmoke);
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1), backgroundColorPanel.RectTransform, Anchor.TopRight)
            {
                AbsoluteOffset = new Point(20, 0)
            }, isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            var fields = new GUIComponent[4];
            string[] colorComponentLabels = { "R", "G", "B" };
            for (int i = 2; i >= 0; i--)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.2f, 1), inputArea.RectTransform)
                {
                    MinSize = new Point(40, 0),
                    MaxSize = new Point(100, 50)
                }, style: null, color: Color.Black * 0.6f);
                var colorLabel = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), colorComponentLabels[i],
                    font: GUI.SmallFont, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    GUINumberInput.NumberType.Int)
                {
                    Font = GUI.SmallFont
                };
                numberInput.MinValueInt = 0;
                numberInput.MaxValueInt = 255;
                numberInput.Font = GUI.SmallFont;
                switch (i)
                {
                    case 0:
                        colorLabel.TextColor = Color.Red;
                        numberInput.IntValue = backgroundColor.R;
                        numberInput.OnValueChanged += (numInput) => backgroundColor.R = (byte)(numInput.IntValue);
                        break;
                    case 1:
                        colorLabel.TextColor = Color.LightGreen;
                        numberInput.IntValue = backgroundColor.G;
                        numberInput.OnValueChanged += (numInput) => backgroundColor.G = (byte)(numInput.IntValue);
                        break;
                    case 2:
                        colorLabel.TextColor = Color.DeepSkyBlue;
                        numberInput.IntValue = backgroundColor.B;
                        numberInput.OnValueChanged += (numInput) => backgroundColor.B = (byte)(numInput.IntValue);
                        break;
                }
            }
        }

        private HashSet<Sprite> loadedSprites = new HashSet<Sprite>();
        private void LoadSprites()
        {
            loadedSprites.ForEach(s => s.Remove());
            loadedSprites.Clear();
            var contentPackages = GameMain.Config.SelectedContentPackages.ToList();

#if !DEBUG
            var vanilla = GameMain.VanillaContent;
            if (vanilla != null)
            {
                contentPackages.Remove(vanilla);
            }
#endif
            foreach (var contentPackage in contentPackages)
            {
                foreach (var file in contentPackage.Files)
                {
                    if (file.Path.EndsWith(".xml"))
                    {
                        XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                        if (doc != null && doc.Root != null)
                        {
                            LoadSprites(doc.Root);
                        }
                    }
                }
            }

            //foreach (string filePath in Directory.GetFiles("Content/", "*.xml", SearchOption.AllDirectories))
            //{
            //    XDocument doc = XMLExtensions.TryLoadXml(filePath);
            //    if (doc != null && doc.Root != null)
            //    {
            //        LoadSprites(doc.Root);
            //    }
            //}

            void LoadSprites(XElement element)
            {
                element.Elements("sprite").ForEach(s => CreateSprite(s));
                element.Elements("Sprite").ForEach(s => CreateSprite(s));
                element.Elements("backgroundsprite").ForEach(s => CreateSprite(s));
                element.Elements("BackgroundSprite").ForEach(s => CreateSprite(s));
                element.Elements("brokensprite").ForEach(s => CreateSprite(s));
                element.Elements("BrokenSprite").ForEach(s => CreateSprite(s));
                element.Elements("containedsprite").ForEach(s => CreateSprite(s));
                element.Elements("ContainedSprite").ForEach(s => CreateSprite(s));
                element.Elements("inventoryicon").ForEach(s => CreateSprite(s));
                element.Elements("InventoryIcon").ForEach(s => CreateSprite(s));
                //decorativesprites don't necessarily have textures (can be used to hide/disable other sprites)
                element.Elements("decorativesprite").ForEach(s => { if (s.Attribute("texture") != null) CreateSprite(s); });
                element.Elements("DecorativeSprite").ForEach(s => { if (s.Attribute("texture") != null) CreateSprite(s); });
                element.Elements().ForEach(e => LoadSprites(e));
            }

            void CreateSprite(XElement element)
            {
                string spriteFolder = "";
                string textureElement = element.GetAttributeString("texture", "");
                // TODO: parse and create
                if (textureElement.Contains("[GENDER]") || textureElement.Contains("[HEADID]") || textureElement.Contains("[RACE]")) { return; }
                if (!textureElement.Contains("/"))
                {
                    var parsedPath = element.ParseContentPathFromUri();
                    spriteFolder = Path.GetDirectoryName(parsedPath);
                }
                // Uncomment if we do multiple passes -> there can be duplicates
                string identifier = Sprite.GetID(element);
                if (loadedSprites.None(s => s.ID == identifier))
                {
                    loadedSprites.Add(new Sprite(element, spriteFolder));
                }
            }
        }

        private bool SaveSprites(IEnumerable<Sprite> sprites)
        {
            if (selectedTexture == null) { return false; }
            if (sprites.None()) { return false; }
            HashSet<XDocument> docsToSave = new HashSet<XDocument>();
            foreach (Sprite sprite in sprites)
            {
                if (sprite.Texture != selectedTexture) { continue; }
                var element = sprite.SourceElement;
                if (element == null) { continue; }
                element.SetAttributeValue("sourcerect", XMLExtensions.RectToString(sprite.SourceRect));
                element.SetAttributeValue("origin", XMLExtensions.Vector2ToString(sprite.RelativeOrigin));
                docsToSave.Add(element.Document);
            }
            xmlPathText.Text = "All changes saved to:";
            foreach (XDocument doc in docsToSave)
            {
                string xmlPath = doc.ParseContentPathFromUri();
                xmlPathText.Text += "\n" + xmlPath;
                doc.Save(xmlPath);
            }
            xmlPathText.TextColor = Color.LightGreen;
            return true;
        }
#endregion

#region Public methods
        public override void AddToGUIUpdateList()
        {
            leftPanel.AddToGUIUpdateList();
            rightPanel.AddToGUIUpdateList();
            topPanel.AddToGUIUpdateList();
            bottomPanel.AddToGUIUpdateList();
            if (editBackgroundColor)
            {
                backgroundColorPanel.AddToGUIUpdateList();
            }
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
            Widget.EnableMultiSelect = PlayerInput.KeyDown(Keys.LeftControl);
            spriteList.SelectMultiple = Widget.EnableMultiSelect;
            // Select rects with the mouse
            if (Widget.selectedWidgets.None() || Widget.EnableMultiSelect)
            {
                if (selectedTexture != null)
                {
                    foreach (Sprite sprite in loadedSprites)
                    {
                        if (sprite.Texture != selectedTexture) continue;
                        if (PlayerInput.LeftButtonClicked())
                        {
                            var scaledRect = new Rectangle(textureRect.Location + sprite.SourceRect.Location.Multiply(zoom), sprite.SourceRect.Size.Multiply(zoom));
                            if (scaledRect.Contains(PlayerInput.MousePosition))
                            {
                                spriteList.Select(sprite, autoScroll: false);
                                UpdateScrollBar(spriteList);
                                UpdateScrollBar(textureList);
                            }
                        }
                    }
                }
            }
            if (GUI.MouseOn == null)
            {
                if (PlayerInput.ScrollWheelSpeed != 0)
                {
                    zoom = MathHelper.Clamp(zoom + PlayerInput.ScrollWheelSpeed * (float)deltaTime * 0.05f * zoom, minZoom, maxZoom);
                    zoomBar.BarScroll = GetBarScrollValue();
                }
                widgets.Values.ForEach(w => w.Update((float)deltaTime));
                if (PlayerInput.MidButtonHeld())
                {
                    // "Camera" Pan
                    Vector2 moveSpeed = PlayerInput.MouseSpeed * (float)deltaTime * 100.0f;
                    viewAreaOffset += moveSpeed.ToPoint();
                }
            }
            if (PlayerInput.KeyHit(Keys.Left))
            {
                foreach (var sprite in selectedSprites)
                {
                    var newRect = sprite.SourceRect;
                    newRect.X--;
                    UpdateSourceRect(sprite, newRect);
                }
            }
            if (PlayerInput.KeyHit(Keys.Right))
            {
                foreach (var sprite in selectedSprites)
                {
                    var newRect = sprite.SourceRect;
                    newRect.X++;
                    UpdateSourceRect(sprite, newRect);
                }
            }
            if (PlayerInput.KeyHit(Keys.Down))
            {
                foreach (var sprite in selectedSprites)
                {
                    var newRect = sprite.SourceRect;
                    newRect.Y++;
                    UpdateSourceRect(sprite, newRect);
                }
            }
            if (PlayerInput.KeyHit(Keys.Up))
            {
                foreach (var sprite in selectedSprites)
                {
                    var newRect = sprite.SourceRect;
                    newRect.Y--;
                    UpdateSourceRect(sprite, newRect);
                }
            }
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(backgroundColor);
            spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable, samplerState: SamplerState.PointClamp);

            var viewArea = GetViewArea;

            if (selectedTexture != null)
            {
                textureRect = new Rectangle(
                    (int)(viewArea.Center.X - selectedTexture.Bounds.Width / 2f * zoom),
                    (int)(viewArea.Center.Y - selectedTexture.Bounds.Height / 2f * zoom),
                    (int)(selectedTexture.Bounds.Width * zoom),
                    (int)(selectedTexture.Bounds.Height * zoom));

                spriteBatch.Draw(selectedTexture,
                    viewArea.Center.ToVector2(),
                    sourceRectangle: null,
                    color: Color.White,
                    rotation: 0.0f,
                    origin: new Vector2(selectedTexture.Bounds.Width / 2.0f, selectedTexture.Bounds.Height / 2.0f),
                    scale: zoom,
                    effects: SpriteEffects.None,
                    layerDepth: 0);

                //GUI.DrawRectangle(spriteBatch, viewArea, Color.Green, isFilled: false);
                GUI.DrawRectangle(spriteBatch, textureRect, Color.Gray, isFilled: false);

                foreach (GUIComponent element in spriteList.Content.Children)
                {
                    Sprite sprite = element.UserData as Sprite;
                    if (sprite == null) { continue; }
                    if (sprite.Texture != selectedTexture) continue;
                    spriteCount++;

                    Rectangle sourceRect = new Rectangle(
                        textureRect.X + (int)(sprite.SourceRect.X * zoom),
                        textureRect.Y + (int)(sprite.SourceRect.Y * zoom),
                        (int)(sprite.SourceRect.Width * zoom),
                        (int)(sprite.SourceRect.Height * zoom));

                    bool isSelected = selectedSprites.Contains(sprite);
                    GUI.DrawRectangle(spriteBatch, sourceRect, isSelected ? Color.Yellow : Color.Red * 0.5f, thickness: isSelected ? 2 : 1);

                    string id = sprite.ID;
                    if (!string.IsNullOrEmpty(id))
                    {
                        int widgetSize = 10;
                        Vector2 GetTopLeft() => sprite.SourceRect.Location.ToVector2();
                        Vector2 GetTopRight() => new Vector2(GetTopLeft().X + sprite.SourceRect.Width, GetTopLeft().Y);
                        Vector2 GetBottomRight() => new Vector2(GetTopRight().X, GetTopRight().Y + sprite.SourceRect.Height);
                        var originWidget = GetWidget($"{id}_origin", sprite, widgetSize, Widget.Shape.Cross, initMethod: w =>
                        {
                            w.tooltip = $"Origin: {sprite.RelativeOrigin.FormatDoubleDecimal()}";
                            w.MouseHeld += dTime =>
                            {
                                w.DrawPos = PlayerInput.MousePosition.Clamp(textureRect.Location.ToVector2() + GetTopLeft() * zoom, textureRect.Location.ToVector2() + GetBottomRight() * zoom);
                                sprite.Origin = (w.DrawPos - textureRect.Location.ToVector2() - sprite.SourceRect.Location.ToVector2() * zoom) / zoom;
                                w.tooltip = $"Origin: {sprite.RelativeOrigin.FormatDoubleDecimal()}";
                            };
                            w.refresh = () =>
                                w.DrawPos = (textureRect.Location.ToVector2() + (sprite.Origin + sprite.SourceRect.Location.ToVector2()) * zoom)
                                    .Clamp(textureRect.Location.ToVector2() + GetTopLeft() * zoom, textureRect.Location.ToVector2() + GetBottomRight() * zoom);
                        });
                        var positionWidget = GetWidget($"{id}_position", sprite, widgetSize, Widget.Shape.Rectangle, initMethod: w =>
                        {
                            w.tooltip = $"Position: {sprite.SourceRect.Location}";
                            w.MouseHeld += dTime =>
                            {
                                w.DrawPos = PlayerInput.MousePosition;
                                sprite.SourceRect = new Rectangle(((w.DrawPos + new Vector2(w.size / 2) - textureRect.Location.ToVector2()) / zoom).ToPoint(), sprite.SourceRect.Size);
                                if (spriteList.SelectedComponent is GUITextBlock textBox)
                                {
                                    // TODO: cache the sprite name?
                                    textBox.Text = GetSpriteName(sprite) + " " + sprite.SourceRect;
                                }
                                w.tooltip = $"Position: {sprite.SourceRect.Location}";
                            };
                            w.refresh = () => w.DrawPos = textureRect.Location.ToVector2() + sprite.SourceRect.Location.ToVector2() * zoom - new Vector2(w.size / 2);
                        });
                        var sizeWidget = GetWidget($"{id}_size", sprite, widgetSize, Widget.Shape.Rectangle, initMethod: w =>
                        {
                            w.tooltip = $"Size: {sprite.SourceRect.Size}";
                            w.MouseHeld += dTime =>
                            {
                                w.DrawPos = PlayerInput.MousePosition;
                                sprite.SourceRect = new Rectangle(sprite.SourceRect.Location, ((w.DrawPos - new Vector2(w.size) - positionWidget.DrawPos) / zoom).ToPoint());
                                // TODO: allow to lock the origin
                                sprite.RelativeOrigin = sprite.RelativeOrigin;
                                if (spriteList.SelectedComponent is GUITextBlock textBox)
                                {
                                    // TODO: cache the sprite name?
                                    textBox.Text = GetSpriteName(sprite) + " " + sprite.SourceRect;
                                }
                                w.tooltip = $"Size: {sprite.SourceRect.Size}";
                            };
                            w.refresh = () => w.DrawPos = textureRect.Location.ToVector2() + new Vector2(sprite.SourceRect.Right, sprite.SourceRect.Bottom) * zoom + new Vector2(w.size / 2);
                        });
                        if (isSelected)
                        {
                            positionWidget.Draw(spriteBatch, (float)deltaTime);
                            sizeWidget.Draw(spriteBatch, (float)deltaTime);
                            originWidget.Draw(spriteBatch, (float)deltaTime);
                        }
                    }
                }
            }

            GUI.Draw(Cam, spriteBatch);

            spriteCount = 0;

            spriteBatch.End();
        }

        public override void Select()
        {
            base.Select();
            LoadSprites();
            RefreshLists();
            // Store the reference, because lastSelected is reassigned when the texture is selected.
            Sprite lastSprite = lastSelected;
            // Select the last selected texture if any.
            // TODO: Does not work if the texture has been disposed. This happens when it's not used by any sprite -> is there a better way to identify the textures? id or something?
            if (selectedTexture != null && textureList.Content.Children.Any(c => c.UserData as Texture2D == selectedTexture))
            {
                textureList.Select(selectedTexture, autoScroll: false);
                UpdateScrollBar(textureList);
                // Select the last selected sprite if any
                if (lastSprite != null && spriteList.Content.Children.FirstOrDefault(c => c.UserData is Sprite s && s.ID == lastSprite.ID)?.UserData is Sprite sprite)
                {
                    spriteList.Select(sprite, autoScroll: false);
                    UpdateScrollBar(spriteList);
                }
            }
            else
            {
                spriteList.Select(0, autoScroll: false);
            }
        }

        public override void Deselect()
        {
            base.Deselect();
            loadedSprites.ForEach(s => s.Remove());
            loadedSprites.Clear();
            ResetWidgets();
            // Automatically reload all sprites that have been selected at least once (and thus might have been edited)
            var reloadedSprites = new List<Sprite>();
            foreach (var sprite in dirtySprites)
            {
                foreach (var s in Sprite.LoadedSprites)
                {
                    if (s.Texture == sprite.Texture && !reloadedSprites.Contains(s))
                    {
                        s.ReloadXML();
                        reloadedSprites.Add(s);
                    }
                }
            }
            dirtySprites.Clear();
        }

        public void SelectSprite(Sprite sprite)
        {
            if (!loadedSprites.Contains(sprite))
            {
                loadedSprites.Add(sprite);
                RefreshLists();
            }

            if (selectedSprites.Any(s => s.Texture != selectedTexture))
            {
                ResetWidgets();
            }
            if (Widget.EnableMultiSelect)
            {
                if (selectedSprites.Contains(sprite))
                {
                    selectedSprites.Remove(sprite);
                }
                else
                {
                    selectedSprites.Add(sprite);
                    dirtySprites.Add(sprite);
                    lastSelected = sprite;
                }
            }
            else
            {
                selectedSprites.Clear();
                selectedSprites.Add(sprite);
                dirtySprites.Add(sprite);
                lastSelected = sprite;
            }
            if (selectedTexture != sprite.Texture)
            {
                textureList.Select(sprite.Texture, autoScroll: false);
                UpdateScrollBar(textureList);
            }
            xmlPathText.Text = string.Empty;
            foreach (var s in selectedSprites)
            {
                texturePathText.Text = s.FilePath;
                var element = s.SourceElement;
                if (element != null)
                {
                    string xmlPath = element.ParseContentPathFromUri();
                    if (!xmlPathText.Text.Contains(xmlPath))
                    {
                        xmlPathText.Text += "\n" + xmlPath;
                    }
                }
            }
            xmlPathText.TextColor = Color.LightGray;
        }

        public void RefreshLists()
        {
            //selectedTexture = null;
            selectedSprites.Clear();
            textureList.ClearChildren();
            spriteList.ClearChildren();
            ResetWidgets();
            HashSet<string> textures = new HashSet<string>();
            // Create texture list
            foreach (Sprite sprite in loadedSprites.OrderBy(s => Path.GetFileNameWithoutExtension(s.FilePath)))
            {
                //ignore sprites that don't have a file path (e.g. submarine pics)
                if (string.IsNullOrEmpty(sprite.FilePath)) continue;
                string normalizedFilePath = Path.GetFullPath(sprite.FilePath);
                if (!textures.Contains(normalizedFilePath))
                {
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), textureList.Content.RectTransform) { MinSize = new Point(0, 20) },
                        Path.GetFileName(sprite.FilePath))
                    {
                        Padding = Vector4.Zero,
                        ToolTip = sprite.FilePath,
                        UserData = sprite.Texture
                    };
                    textures.Add(normalizedFilePath);
                }
            }
            // Create sprite list
            // TODO: allow the user to choose whether to sort by file name or by texture sheet
            //foreach (Sprite sprite in loadedSprites.OrderBy(s => GetSpriteName(s)))
            foreach (Sprite sprite in loadedSprites.OrderBy(s => s.SourceElement.GetAttributeString("texture", string.Empty)))
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), spriteList.Content.RectTransform) { MinSize = new Point(0, 20) }, GetSpriteName(sprite) + " " + sprite.SourceRect)
                {
                    Padding = Vector4.Zero,
                    UserData = sprite
                };
            }
            topPanelContents.Visible = false;
        }

        public void ResetZoom()
        {
            if (selectedTexture == null) { return; }
            var viewArea = GetViewArea;
            float width = viewArea.Width / (float)selectedTexture.Width;
            float height = viewArea.Height / (float)selectedTexture.Height;
            maxZoom = 10; // TODO: user-definable?
            zoom = Math.Min(1, Math.Min(width, height));
            zoomBar.BarScroll = GetBarScrollValue();
            viewAreaOffset = Point.Zero;
        }
#endregion

#region Helpers
        private Point viewAreaOffset;
        private Rectangle GetViewArea
        {
            get
            {
                int margin = 20;
                var viewArea = new Rectangle(leftPanel.Rect.Right + margin + viewAreaOffset.X, topPanel.Rect.Bottom + margin + viewAreaOffset.Y, rightPanel.Rect.Left - leftPanel.Rect.Right - margin * 2, Frame.Rect.Height - topPanel.Rect.Height - margin * 2);
                return viewArea;
            }
        }

        private float GetBarScrollValue() => MathHelper.Lerp(0, 1, MathUtils.InverseLerp(minZoom, maxZoom, zoom));

        private string GetSpriteName(Sprite sprite)
        {
            var sourceElement = sprite.SourceElement;
            if (sourceElement == null) { return string.Empty; }
            string name = sprite.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = sourceElement.Parent.GetAttributeString("identifier", string.Empty);
            }
            if (string.IsNullOrEmpty(name))
            {
                name = sourceElement.Parent.GetAttributeString("name", string.Empty);
            }
            return string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(sprite.FilePath) : name;
        }

        private void UpdateScrollBar(GUIListBox listBox)
        {
            var sb = listBox.ScrollBar;
            sb.BarScroll = MathHelper.Clamp(MathHelper.Lerp(0, 1, MathUtils.InverseLerp(0, listBox.Content.CountChildren - 1, listBox.SelectedIndex)), sb.MinValue, sb.MaxValue);
        }

        private void UpdateSourceRect(Sprite sprite, Rectangle newRect)
        {
            sprite.SourceRect = newRect;
            // Keeps the relative origin unchanged. The absolute origin will be recalculated.
            sprite.RelativeOrigin = sprite.RelativeOrigin;
        }
#endregion

#region Widgets
        private Dictionary<string, Widget> widgets = new Dictionary<string, Widget>();

        private Widget GetWidget(string id, Sprite sprite, int size = 5, Widget.Shape shape = Widget.Shape.Rectangle, Action<Widget> initMethod = null)
        {
            if (!widgets.TryGetValue(id, out Widget widget))
            {
                int selectedSize = (int)Math.Round(size * 1.5f);
                widget = new Widget(id, size, shape)
                {
                    data = sprite,
                    color = Color.Yellow,
                    secondaryColor = Color.Gray,
                    tooltipOffset = new Vector2(selectedSize / 2 + 5, -10)
                };
                widget.PreDraw += (sp, dTime) =>
                {
                    if (!widget.IsControlled)
                    {
                        widget.refresh();
                    }
                };
                widget.PreUpdate += dTime => widget.Enabled = selectedSprites.Contains(sprite);
                widget.PostUpdate += dTime =>
                {
                    widget.inputAreaMargin = widget.IsControlled ? 1000 : 0;
                    widget.size = widget.IsSelected ? selectedSize : size;
                    widget.isFilled = widget.IsControlled;
                };
                widgets.Add(id, widget);
                initMethod?.Invoke(widget);
            }
            return widget;
        }

        private void ResetWidgets()
        {
            widgets.Clear();
            Widget.selectedWidgets.Clear();
        }
#endregion
    }
}
