﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
#if DEBUG
using System.IO;
#else
using Barotrauma.IO;
#endif

namespace Barotrauma
{
    class SpriteEditorScreen : EditorScreen
    {
        private GUIListBox textureList, spriteList;

        private GUIFrame topPanel;
        private GUIFrame leftPanel;
        private GUIFrame rightPanel;
        private GUIFrame bottomPanel;
        private GUIFrame backgroundColorPanel;

        private bool drawGrid, snapToGrid;

        private GUIFrame topPanelContents;
        private GUITextBlock texturePathText;
        private GUITextBlock xmlPathText;
        private GUIScrollBar zoomBar;
        private readonly List<Sprite> selectedSprites = new List<Sprite>();
        private readonly List<Sprite> dirtySprites = new List<Sprite>();
        private Texture2D SelectedTexture => lastSprite?.Texture;
        private Sprite lastSprite;
        private string selectedTexturePath;

        private Rectangle textureRect;
        private float zoom = 1;
        private const float MinZoom = 0.25f, MaxZoom = 10.0f;

        private GUITextBox filterSpritesBox;
        private GUITextBlock filterSpritesLabel;
        private GUITextBox filterTexturesBox;
        private GUITextBlock filterTexturesLabel;

        private LocalizedString originLabel, positionLabel, sizeLabel;

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
            GameMain.Instance.ResolutionChanged += CreateUI;
            CreateUI();
        }

        #region Initialization
        private void CreateUI()
        {
            originLabel = TextManager.Get("charactereditor.origin");
            positionLabel = TextManager.GetWithVariable("charactereditor.position", "[coordinates]", string.Empty);
            sizeLabel = TextManager.Get("charactereditor.size");

            topPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.15f), Frame.RectTransform) { MinSize = new Point(0, 60) }, "GUIFrameTop");
            topPanelContents = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.8f), topPanel.RectTransform, Anchor.Center), style: null);

            new GUIButton(new RectTransform(new Vector2(0.14f, 0.4f), topPanelContents.RectTransform, Anchor.TopLeft)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, TextManager.Get("spriteeditor.reloadtexture"))
            {
                OnClicked = (button, userData) =>
                {
                    var selected = selectedSprites.ToList();
                    Sprite firstSelected = selected.First();
                    selected.ForEach(s => s.ReloadTexture());
                    RefreshLists();
                    textureList.Select(firstSelected.FullPath, autoScroll: GUIListBox.AutoScroll.Disabled);
                    selected.ForEachMod(s => spriteList.Select(s, autoScroll: GUIListBox.AutoScroll.Disabled));
                    texturePathText.Text = TextManager.GetWithVariable("spriteeditor.texturesreloaded", "[filepath]", firstSelected.FilePath.Value);
                    texturePathText.TextColor = GUIStyle.Green;
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.14f, 0.4f), topPanelContents.RectTransform, Anchor.BottomLeft)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, TextManager.Get("spriteeditor.resetchanges"))
            {
                OnClicked = (button, userData) =>
                {
                    if (SelectedTexture == null) { return false; }
                    foreach (Sprite sprite in loadedSprites)
                    {
                        if (sprite.FullPath != selectedTexturePath) { continue; }
                        var element = sprite.SourceElement;
                        if (element == null) { continue; }
                        // Not all sprites have a sourcerect defined, in which case we'll want to use the current source rect instead of an empty rect.
                        sprite.SourceRect = element.GetAttributeRect("sourcerect", sprite.SourceRect);
                        sprite.RelativeOrigin = element.GetAttributeVector2("origin", new Vector2(0.5f, 0.5f));
                    }
                    ResetWidgets();
                    xmlPathText.Text = TextManager.Get("spriteeditor.resetsuccessful");
                    xmlPathText.TextColor = GUIStyle.Green;
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.14f, 0.4f), topPanelContents.RectTransform, Anchor.TopLeft)
            {
                RelativeOffset = new Vector2(0.15f, 0.1f)
            }, TextManager.Get("spriteeditor.saveselectedsprites"))
            {
                OnClicked = (button, userData) =>
                {
                    return SaveSprites(selectedSprites);
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.14f, 0.4f), topPanelContents.RectTransform, Anchor.BottomLeft)
            {
                RelativeOffset = new Vector2(0.15f, 0.1f)
            }, TextManager.Get("spriteeditor.saveallsprites"))
            {
                OnClicked = (button, userData) =>
                {
                    return SaveSprites(loadedSprites);
                }
            };

            GUITextBlock.AutoScaleAndNormalize(topPanelContents.Children.Where(c => c is GUIButton).Select(c => ((GUIButton)c).TextBlock));

            new GUITextBlock(new RectTransform(new Vector2(0.2f, 0.2f), topPanelContents.RectTransform, Anchor.TopCenter, Pivot.CenterRight) { RelativeOffset = new Vector2(0, 0.3f) }, TextManager.Get("spriteeditor.zoom"));
            zoomBar = new GUIScrollBar(new RectTransform(new Vector2(0.2f, 0.35f), topPanelContents.RectTransform, Anchor.TopCenter, Pivot.CenterRight)
            {
                RelativeOffset = new Vector2(0.05f, 0.3f)
            }, style: "GUISlider", barSize: 0.1f)
            {
                BarScroll = GetBarScrollValue(),
                Step = 0.01f,
                OnMoved = (scrollBar, value) =>
                {
                    zoom = MathHelper.Lerp(MinZoom, MaxZoom, value);
                    viewAreaOffset = Point.Zero;
                    return true;
                }
            };
            var resetBtn = new GUIButton(new RectTransform(new Vector2(0.05f, 0.35f), topPanelContents.RectTransform, Anchor.TopCenter, Pivot.CenterLeft) { RelativeOffset = new Vector2(0.055f, 0.3f) }, TextManager.Get("spriteeditor.resetzoom"))
            {
                OnClicked = (box, data) =>
                {
                    ResetZoom();
                    return true;
                }
            };
            resetBtn.TextBlock.AutoScaleHorizontal = true;

            new GUITickBox(new RectTransform(new Vector2(0.2f, 0.2f), topPanelContents.RectTransform, Anchor.BottomCenter, Pivot.CenterRight) { RelativeOffset = new Vector2(0, 0.3f) }, TextManager.Get("spriteeditor.showgrid"))
            {
                Selected = drawGrid,
                OnSelected = (tickBox) =>
                {
                    drawGrid = tickBox.Selected;
                    return true;
                }
            };
            new GUITickBox(new RectTransform(new Vector2(0.2f, 0.2f), topPanelContents.RectTransform, Anchor.BottomCenter, Pivot.CenterRight) { RelativeOffset = new Vector2(0.17f, 0.3f) }, TextManager.Get("spriteeditor.snaptogrid"))
            {
                Selected = snapToGrid,
                OnSelected = (tickBox) =>
                {
                    snapToGrid = tickBox.Selected;
                    return true;
                }
            };

            texturePathText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.4f), topPanelContents.RectTransform, Anchor.Center, Pivot.BottomCenter) { RelativeOffset = new Vector2(0.4f, 0) }, "", Color.LightGray);
            xmlPathText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.4f), topPanelContents.RectTransform, Anchor.Center, Pivot.TopCenter) { RelativeOffset = new Vector2(0.4f, 0) }, "", Color.LightGray);

            leftPanel = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f - topPanel.RectTransform.RelativeSize.Y), Frame.RectTransform, Anchor.BottomLeft)
            { MinSize = new Point(150, 0) }, style: "GUIFrameLeft");
            var paddedLeftPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), leftPanel.RectTransform, Anchor.Center))
            { RelativeSpacing = 0.01f, Stretch = true };

            var filterArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.03f), paddedLeftPanel.RectTransform) { MinSize = new Point(0, 20) }, isHorizontal: true)
            {
                Stretch = true,
                UserData = "filterarea"
            };
            filterTexturesLabel = new GUITextBlock(new RectTransform(Vector2.One, filterArea.RectTransform), TextManager.Get("serverlog.filter"), font: GUIStyle.Font, textAlignment: Alignment.CenterLeft) { IgnoreLayoutGroups = true }; ;
            filterTexturesBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1.0f), filterArea.RectTransform), font: GUIStyle.Font, createClearButton: true);
            filterArea.RectTransform.MinSize = filterTexturesBox.RectTransform.MinSize;
            filterTexturesBox.OnTextChanged += (textBox, text) => { FilterTextures(text); return true; };

            textureList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), paddedLeftPanel.RectTransform))
            {
                PlaySoundOnSelect = true,
                OnSelected = (listBox, userData) =>
                {
                    var newTexturePath = userData as string;
                    if (selectedTexturePath == null || selectedTexturePath != newTexturePath)
                    {
                        selectedTexturePath = newTexturePath;
                        ResetZoom();
                        spriteList.Select(loadedSprites.First(s => s.FilePath == selectedTexturePath), autoScroll: GUIListBox.AutoScroll.Disabled);
                        UpdateScrollBar(spriteList);
                    }
                    foreach (GUIComponent child in spriteList.Content.Children)
                    {
                        var textBlock = (GUITextBlock)child;
                        var sprite = (Sprite)textBlock.UserData;
                        textBlock.TextColor = new Color(textBlock.TextColor, sprite.FilePath == selectedTexturePath ? 1.0f : 0.4f);
                        if (sprite.FilePath == selectedTexturePath) { textBlock.Visible = true; }
                    }
                    texturePathText.TextColor = Color.LightGray;
                    topPanelContents.Visible = true;
                    return true;
                }
            };

            rightPanel = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f - topPanel.RectTransform.RelativeSize.Y), Frame.RectTransform, Anchor.BottomRight) { MinSize = new Point(150, 0) }, style: "GUIFrameRight");
            var paddedRightPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), rightPanel.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            filterArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.03f), paddedRightPanel.RectTransform) { MinSize = new Point(0, 20) }, isHorizontal: true)
            {
                Stretch = true,
                UserData = "filterarea"
            };
            filterSpritesLabel = new GUITextBlock(new RectTransform(Vector2.One, filterArea.RectTransform), TextManager.Get("serverlog.filter"), font: GUIStyle.Font, textAlignment: Alignment.CenterLeft) { IgnoreLayoutGroups = true };
            filterSpritesBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1.0f), filterArea.RectTransform), font: GUIStyle.Font, createClearButton: true);
            filterArea.RectTransform.MinSize = filterSpritesBox.RectTransform.MinSize;
            filterSpritesBox.OnTextChanged += (textBox, text) => { FilterSprites(text); return true; };

            spriteList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), paddedRightPanel.RectTransform))
            {
                PlaySoundOnSelect = true,
                OnSelected = (listBox, userData) =>
                {
                    if (userData is Sprite sprite)
                    {
                        SelectSprite(sprite);
                        return true;
                    }
                    return false;
                }
            };

            // Background color
            bottomPanel = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.05f), Frame.RectTransform, Anchor.BottomCenter), style: null, color: Color.Black * 0.5f);
            new GUITickBox(new RectTransform(new Vector2(0.2f, 0.5f), bottomPanel.RectTransform, Anchor.Center), TextManager.Get("charactereditor.editbackgroundcolor"))
            {
                Selected = editBackgroundColor,
                OnSelected = box =>
                {
                    editBackgroundColor = box.Selected;
                    return true;
                }
            };
            backgroundColorPanel = new GUIFrame(new RectTransform(new Point(400, 80), Frame.RectTransform, Anchor.BottomCenter) { RelativeOffset = new Vector2(0, 0.1f) }, style: null, color: Color.Black * 0.4f);
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), backgroundColorPanel.RectTransform) { MinSize = new Point(80, 26) }, TextManager.Get("spriteeditor.backgroundcolor"), textColor: Color.WhiteSmoke);
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1), backgroundColorPanel.RectTransform, Anchor.TopRight)
            {
                AbsoluteOffset = new Point(20, 0)
            }, isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            var fields = new GUIComponent[4];
            LocalizedString[] colorComponentLabels = { TextManager.Get("spriteeditor.colorcomponentr"), TextManager.Get("spriteeditor.colorcomponentg"), TextManager.Get("spriteeditor.colorcomponentb") };
            for (int i = 2; i >= 0; i--)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.2f, 1), inputArea.RectTransform)
                {
                    MinSize = new Point(40, 0),
                    MaxSize = new Point(100, 50)
                }, style: null, color: Color.Black * 0.6f);
                var colorLabel = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), colorComponentLabels[i],
                    font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft);
                var numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight), NumberType.Int)
                {
                    Font = GUIStyle.SmallFont
                };
                numberInput.MinValueInt = 0;
                numberInput.MaxValueInt = 255;
                numberInput.Font = GUIStyle.SmallFont;
                switch (i)
                {
                    case 0:
                        colorLabel.TextColor = GUIStyle.Red;
                        numberInput.IntValue = backgroundColor.R;
                        numberInput.OnValueChanged += (numInput) => backgroundColor.R = (byte)(numInput.IntValue);
                        break;
                    case 1:
                        colorLabel.TextColor = GUIStyle.Green;
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

        private readonly HashSet<Sprite> loadedSprites = new HashSet<Sprite>();
        private void LoadSprites()
        {
            loadedSprites.ForEach(s => s.Remove());
            loadedSprites.Clear();
            var contentPackages = ContentPackageManager.EnabledPackages.All.ToList();

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
                        if (doc != null)
                        {
                            LoadSprites(doc.Root.FromPackage(file.Path.ContentPackage));
                        }
                    }
                }
            }

            void LoadSprites(ContentXElement element)
            {
                string[] spriteElementNames = {
                    "Sprite",
                    "DeformableSprite",
                    "BackgroundSprite",
                    "BrokenSprite",
                    "ContainedSprite",
                    "InventoryIcon",
                    "Icon",
                    "VineSprite",
                    "LeafSprite",
                    "FlowerSprite",
                    "DecorativeSprite",
                    "BarrelSprite",
                    "RailSprite",
                    "SchematicSprite"
                };

                foreach (string spriteElementName in spriteElementNames)
                {
                    element.GetChildElements(spriteElementName).ForEach(s => CreateSprite(s));
                }

                element.Elements().ForEach(e => LoadSprites(e));
            }

            void CreateSprite(ContentXElement element)
            {
                //empty element, probably an item variant?
                if (element.Attributes().None()) { return; }

                string spriteFolder = "";
                ContentPath texturePath = null;
                
                if (element.GetAttribute("texture") != null)
                {
                    texturePath = element.GetAttributeContentPath("texture");
                }
                else
                {
                    if (element.Name.ToString().ToLower() == "vinesprite")
                    {
                        texturePath = element.Parent.GetAttributeContentPath("vineatlas");
                    }
                }
                if (texturePath.IsNullOrEmpty()) { return; }

                // TODO: parse and create?
                if (texturePath.Value.Contains("[GENDER]") || texturePath.Value.Contains("[HEADID]") || texturePath.Value.Contains("[RACE]") || texturePath.Value.Contains("[VARIANT]")) { return; }
                if (!texturePath.Value.Contains("/"))
                {
                    var parsedPath = element.ParseContentPathFromUri();
                    spriteFolder = Path.GetDirectoryName(parsedPath);
                }
                // Uncomment if we do multiple passes -> there can be duplicates
                //string identifier = Sprite.GetID(element);
                //if (loadedSprites.None(s => s.ID == identifier))
                //{
                //    loadedSprites.Add(new Sprite(element, spriteFolder));
                //}
                loadedSprites.Add(new Sprite(element, spriteFolder, texturePath.Value, lazyLoad: true));
            }
        }

        private bool SaveSprites(IEnumerable<Sprite> sprites)
        {
            if (SelectedTexture == null) { return false; }
            if (sprites.None()) { return false; }
            HashSet<XDocument> docsToSave = new HashSet<XDocument>();
            foreach (Sprite sprite in sprites)
            {
                if (sprite.FullPath != selectedTexturePath) { continue; }
                var element = sprite.SourceElement;
                if (element == null) { continue; }
                element.SetAttributeValue("sourcerect", XMLExtensions.RectToString(sprite.SourceRect));
                element.SetAttributeValue("origin", XMLExtensions.Vector2ToString(sprite.RelativeOrigin));

                /*if (element.Attribute("slice") != null)
                {
                    Rectangle slice = new Rectangle(
                        sprite.SourceRect.X + 5, 
                        sprite.SourceRect.Y + 5,
                        sprite.SourceRect.Right - 5,
                        sprite.SourceRect.Bottom - 5);
                    element.SetAttributeValue("slice", XMLExtensions.RectToString(slice));
                }*/
                docsToSave.Add(element.Document);
            }
            xmlPathText.Text = TextManager.Get("spriteeditor.allchangessavedto");
            foreach (XDocument doc in docsToSave)
            {
                string xmlPath = doc.ParseContentPathFromUri();
                xmlPathText.Text += "\n" + xmlPath;
#if DEBUG
                doc.Save(xmlPath);
#else
                doc.SaveSafe(xmlPath);
#endif
            }
            xmlPathText.TextColor = GUIStyle.Green;
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
                if (SelectedTexture != null && GUI.MouseOn == null)
                {
                    foreach (Sprite sprite in loadedSprites)
                    {
                        if (sprite.FullPath != selectedTexturePath) { continue; }
                        if (PlayerInput.PrimaryMouseButtonClicked())
                        {
                            var scaledRect = new Rectangle(textureRect.Location + sprite.SourceRect.Location.Multiply(zoom), sprite.SourceRect.Size.Multiply(zoom));
                            if (scaledRect.Contains(PlayerInput.MousePosition))
                            {
                                spriteList.Select(sprite, autoScroll: GUIListBox.AutoScroll.Disabled);
                                UpdateScrollBar(spriteList);
                                UpdateScrollBar(textureList);
                                // Release the keyboard so that we can nudge the source rects
                                GUI.KeyboardDispatcher.Subscriber = null;
                            }
                        }
                    }
                }
            }
            if (GUI.MouseOn == null)
            {
                if (PlayerInput.ScrollWheelSpeed != 0)
                {
                    zoom = MathHelper.Clamp(zoom + PlayerInput.ScrollWheelSpeed * (float)deltaTime * 0.05f * zoom, MinZoom, MaxZoom);
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
            if (GUI.KeyboardDispatcher.Subscriber == null)
            {
                if (PlayerInput.KeyHit(Keys.Left))
                {
                    Nudge(Keys.Left);
                }
                if (PlayerInput.KeyHit(Keys.Right))
                {
                    Nudge(Keys.Right);
                }
                if (PlayerInput.KeyHit(Keys.Down))
                {
                    Nudge(Keys.Down);
                }
                if (PlayerInput.KeyHit(Keys.Up))
                {
                    Nudge(Keys.Up);
                }
                if (PlayerInput.KeyDown(Keys.Left))
                {
                    holdTimer += deltaTime;
                    if (holdTimer > holdTime)
                    {
                        Nudge(Keys.Left);
                    }
                }
                else if (PlayerInput.KeyDown(Keys.Right))
                {
                    holdTimer += deltaTime;
                    if (holdTimer > holdTime)
                    {
                        Nudge(Keys.Right);
                    }
                }
                else if (PlayerInput.KeyDown(Keys.Down))
                {
                    holdTimer += deltaTime;
                    if (holdTimer > holdTime)
                    {
                        Nudge(Keys.Down);
                    }
                }
                else if (PlayerInput.KeyDown(Keys.Up))
                {
                    holdTimer += deltaTime;
                    if (holdTimer > holdTime)
                    {
                        Nudge(Keys.Up);
                    }
                }
                else
                {
                    holdTimer = 0;
                }
            }            
        }

        private double holdTimer;
        private readonly float holdTime = 0.2f;
        private void Nudge(Keys key)
        {
            switch (key)
            {
                case Keys.Left:
                    foreach (var sprite in selectedSprites)
                    {
                        var newRect = sprite.SourceRect;
                        if (PlayerInput.KeyDown(Keys.LeftControl))
                        {
                            newRect.Width--;
                        }
                        else
                        {
                            newRect.X--;
                        }
                        UpdateSourceRect(sprite, newRect);
                    }
                    break;
                case Keys.Right:
                    foreach (var sprite in selectedSprites)
                    {
                        var newRect = sprite.SourceRect;
                        if (PlayerInput.KeyDown(Keys.LeftControl))
                        {
                            newRect.Width++;
                        }
                        else
                        {
                            newRect.X++;
                        }
                        UpdateSourceRect(sprite, newRect);
                    }
                    break;
                case Keys.Down:
                    foreach (var sprite in selectedSprites)
                    {
                        var newRect = sprite.SourceRect;
                        if (PlayerInput.KeyDown(Keys.LeftControl))
                        {
                            newRect.Height++;
                        }
                        else
                        {
                            newRect.Y++;
                        }
                        UpdateSourceRect(sprite, newRect);
                    }
                    break;
                case Keys.Up:
                    foreach (var sprite in selectedSprites)
                    {
                        var newRect = sprite.SourceRect;
                        if (PlayerInput.KeyDown(Keys.LeftControl))
                        {
                            newRect.Height--;
                        }
                        else
                        {
                            newRect.Y--;
                        }
                        UpdateSourceRect(sprite, newRect);
                    }
                    break;
            }
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(backgroundColor);
            spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable, samplerState: SamplerState.PointClamp);

            var viewArea = GetViewArea;

            if (SelectedTexture != null)
            {
                textureRect = new Rectangle(
                    (int)(viewArea.Center.X - SelectedTexture.Bounds.Width / 2f * zoom),
                    (int)(viewArea.Center.Y - SelectedTexture.Bounds.Height / 2f * zoom),
                    (int)(SelectedTexture.Bounds.Width * zoom),
                    (int)(SelectedTexture.Bounds.Height * zoom));

                spriteBatch.Draw(SelectedTexture,
                    viewArea.Center.ToVector2(),
                    sourceRectangle: null,
                    color: Color.White,
                    rotation: 0.0f,
                    origin: new Vector2(SelectedTexture.Bounds.Width / 2.0f, SelectedTexture.Bounds.Height / 2.0f),
                    scale: zoom,
                    effects: SpriteEffects.None,
                    layerDepth: 0);

                //GUI.DrawRectangle(spriteBatch, viewArea, Color.Green, isFilled: false);
                GUI.DrawRectangle(spriteBatch, textureRect, Color.Gray, isFilled: false);

                if (drawGrid)
                {
                    DrawGrid(spriteBatch, textureRect, zoom, Submarine.GridSize);
                }

                foreach (GUIComponent element in spriteList.Content.Children)
                {
                    if (!(element.UserData is Sprite sprite)) { continue; }
                    if (sprite.FullPath != selectedTexturePath) { continue; }

                    Rectangle sourceRect = new Rectangle(
                        textureRect.X + (int)(sprite.SourceRect.X * zoom),
                        textureRect.Y + (int)(sprite.SourceRect.Y * zoom),
                        (int)(sprite.SourceRect.Width * zoom),
                        (int)(sprite.SourceRect.Height * zoom));

                    bool isSelected = selectedSprites.Contains(sprite);
                    GUI.DrawRectangle(spriteBatch, sourceRect, isSelected ? GUIStyle.Orange : GUIStyle.Red * 0.5f, thickness: isSelected ? 2 : 1);

                    Identifier id = sprite.Identifier;
                    if (!id.IsEmpty)
                    {
                        int widgetSize = 10;
                        Vector2 GetTopLeft() => sprite.SourceRect.Location.ToVector2();
                        Vector2 GetTopRight() => new Vector2(GetTopLeft().X + sprite.SourceRect.Width, GetTopLeft().Y);
                        Vector2 GetBottomRight() => new Vector2(GetTopRight().X, GetTopRight().Y + sprite.SourceRect.Height);
                        var originWidget = GetWidget($"{id}_origin", sprite, widgetSize, Widget.Shape.Cross, initMethod: w =>
                        {
                            w.tooltip = TextManager.AddPunctuation(':', originLabel, sprite.RelativeOrigin.FormatDoubleDecimal());
                            w.MouseHeld += dTime =>
                            {
                                w.DrawPos = PlayerInput.MousePosition.Clamp(textureRect.Location.ToVector2() + GetTopLeft() * zoom, textureRect.Location.ToVector2() + GetBottomRight() * zoom);
                                sprite.Origin = (w.DrawPos - textureRect.Location.ToVector2() - sprite.SourceRect.Location.ToVector2() * zoom) / zoom;
                                w.tooltip = TextManager.AddPunctuation(':', originLabel, sprite.RelativeOrigin.FormatDoubleDecimal());
                            };
                            w.refresh = () =>
                                w.DrawPos = (textureRect.Location.ToVector2() + (sprite.Origin + sprite.SourceRect.Location.ToVector2()) * zoom)
                                    .Clamp(textureRect.Location.ToVector2() + GetTopLeft() * zoom, textureRect.Location.ToVector2() + GetBottomRight() * zoom);
                        });
                        var positionWidget = GetWidget($"{id}_position", sprite, widgetSize, Widget.Shape.Rectangle, initMethod: w =>
                        {
                            w.tooltip = positionLabel + sprite.SourceRect.Location;
                            w.MouseHeld += dTime =>
                            {
                                w.DrawPos = (drawGrid && snapToGrid) ?
                                    SnapToGrid(PlayerInput.MousePosition, textureRect, zoom, Submarine.GridSize, Submarine.GridSize.X / 4.0f * zoom) :
                                    PlayerInput.MousePosition;
                                w.DrawPos = new Vector2((float)Math.Ceiling(w.DrawPos.X), (float)Math.Ceiling(w.DrawPos.Y));
                                sprite.SourceRect = new Rectangle(((w.DrawPos - textureRect.Location.ToVector2()) / zoom).ToPoint(), sprite.SourceRect.Size);
                                if (spriteList.SelectedComponent is GUITextBlock textBox)
                                {
                                    // TODO: cache the sprite name?
                                    textBox.Text = GetSpriteName(sprite) + " " + sprite.SourceRect;
                                }
                                w.tooltip = positionLabel + sprite.SourceRect.Location;
                            };
                            w.refresh = () => w.DrawPos = textureRect.Location.ToVector2() + sprite.SourceRect.Location.ToVector2() * zoom;
                        });
                        var sizeWidget = GetWidget($"{id}_size", sprite, widgetSize, Widget.Shape.Rectangle, initMethod: w =>
                        {
                            w.tooltip = TextManager.AddPunctuation(':', sizeLabel, sprite.SourceRect.Size.ToString());
                            w.MouseHeld += dTime =>
                            {
                                w.DrawPos = (drawGrid && snapToGrid) ?
                                    SnapToGrid(PlayerInput.MousePosition, textureRect, zoom, Submarine.GridSize, Submarine.GridSize.X / 4.0f * zoom) :
                                    PlayerInput.MousePosition;
                                w.DrawPos = new Vector2((float)Math.Ceiling(w.DrawPos.X), (float)Math.Ceiling(w.DrawPos.Y));
                                sprite.SourceRect = new Rectangle(sprite.SourceRect.Location, ((w.DrawPos - positionWidget.DrawPos) / zoom).ToPoint());
                                // TODO: allow to lock the origin
                                sprite.RelativeOrigin = sprite.RelativeOrigin;
                                if (spriteList.SelectedComponent is GUITextBlock textBox)
                                {
                                    // TODO: cache the sprite name?
                                    textBox.Text = GetSpriteName(sprite) + " " + sprite.SourceRect;
                                }
                                w.tooltip = TextManager.AddPunctuation(':', sizeLabel, sprite.SourceRect.Size.ToString());
                            };
                            w.refresh = () => w.DrawPos = textureRect.Location.ToVector2() + new Vector2(sprite.SourceRect.Right, sprite.SourceRect.Bottom) * zoom;
                        });
                        originWidget.MouseDown += () => GUI.KeyboardDispatcher.Subscriber = null;
                        positionWidget.MouseDown += () => GUI.KeyboardDispatcher.Subscriber = null;
                        sizeWidget.MouseDown += () => GUI.KeyboardDispatcher.Subscriber = null;
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

            spriteBatch.End();
        }

        private void DrawGrid(SpriteBatch spriteBatch, Rectangle gridArea, float zoom, Vector2 gridSize)
        {
            gridSize *= zoom;
            if (gridSize.X < 1.0f) { return; }
            if (gridSize.Y < 1.0f) { return; }
            int xLines = (int)(gridArea.Width / gridSize.X);
            int yLines = (int)(gridArea.Height / gridSize.Y);

            for (int x = 0; x <= xLines; x++)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(gridArea.X + x * gridSize.X, gridArea.Y),
                    new Vector2(gridArea.X + x * gridSize.X, gridArea.Bottom),
                    Color.White * 0.25f);
            }
            for (int y = 0; y <= yLines; y++)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(gridArea.X, gridArea.Y + y * gridSize.Y),
                    new Vector2(gridArea.Right, gridArea.Y + y * gridSize.Y),
                    Color.White * 0.25f);
            }
        }

        private Vector2 SnapToGrid(Vector2 position, Rectangle gridArea, float zoom, Vector2 gridSize, float tolerance)
        {
            gridSize *= zoom;
            if (gridSize.X < 1.0f) { return position; }
            if (gridSize.Y < 1.0f) { return position; }

            Vector2 snappedPos = position;
            snappedPos.X -= gridArea.X;
            snappedPos.Y -= gridArea.Y;

            Vector2 gridPos = new Vector2(
                MathUtils.RoundTowardsClosest(snappedPos.X, gridSize.X),
                MathUtils.RoundTowardsClosest(snappedPos.Y, gridSize.Y));

            if (Math.Abs(gridPos.X - snappedPos.X) < tolerance)
            {
                snappedPos.X = gridPos.X;
            }
            if (Math.Abs(gridPos.Y - snappedPos.Y) < tolerance)
            {
                snappedPos.Y = gridPos.Y;
            }

            snappedPos.X += gridArea.X;
            snappedPos.Y += gridArea.Y;
            return snappedPos;
        }

        private void FilterTextures(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                filterTexturesLabel.Visible = true;
                textureList.Content.Children.ForEach(c => c.Visible = true);
                return;
            }
            text = text.ToLower();
            filterTexturesLabel.Visible = false;
            foreach (GUIComponent child in textureList.Content.Children)
            {
                if (!(child is GUITextBlock textBlock)) { continue; }
                textBlock.Visible = textBlock.Text.Contains(text, StringComparison.OrdinalIgnoreCase);
            }
        }
        private void FilterSprites(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                filterSpritesLabel.Visible = true;
                spriteList.Content.Children.ForEach(c => c.Visible = true);
                return;
            }
            text = text.ToLower();
            filterSpritesLabel.Visible = false;
            foreach (GUIComponent child in spriteList.Content.Children)
            {
                if (!(child is GUITextBlock textBlock)) { continue; }
                textBlock.Visible = textBlock.Text.Contains(text, StringComparison.OrdinalIgnoreCase);
            }
        }

        public override void Select()
        {
            base.Select();
            LoadSprites();
            RefreshLists();
            spriteList.Select(0, autoScroll: GUIListBox.AutoScroll.Disabled);            
        }

        protected override void DeselectEditorSpecific()
        {
            loadedSprites.ForEach(s => s.Remove());
            loadedSprites.Clear();
            ResetWidgets();
            // Automatically reload all sprites that have been selected at least once (and thus might have been edited)
            var reloadedSprites = new List<Sprite>();
            foreach (var sprite in dirtySprites)
            {
                foreach (var s in Sprite.LoadedSprites)
                {
                    if (s.FullPath == sprite.FullPath && !reloadedSprites.Contains(s))
                    {
                        s.ReloadXML();
                        reloadedSprites.Add(s);
                    }
                }
            }
            dirtySprites.Clear();
            filterSpritesBox.Text = "";
            filterTexturesBox.Text = "";
        }

        public void SelectSprite(Sprite sprite)
        {
            lastSprite = sprite;
            if (!loadedSprites.Contains(sprite))
            {
                loadedSprites.Add(sprite);
                RefreshLists();
            }
            if (selectedSprites.Any(s => s.FullPath != selectedTexturePath))
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
                }
            }
            else
            {
                selectedSprites.Clear();
                selectedSprites.Add(sprite);
                dirtySprites.Add(sprite);
            }
            if (sprite.FullPath != selectedTexturePath)
            {
                textureList.Select(sprite.FullPath, autoScroll: GUIListBox.AutoScroll.Disabled);
                UpdateScrollBar(textureList);
            }
            xmlPathText.Text = string.Empty;
            foreach (var s in selectedSprites)
            {
                texturePathText.Text = s.FilePath.Value;
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
            selectedSprites.Clear();
            textureList.ClearChildren();
            spriteList.ClearChildren();
            ResetWidgets();
            HashSet<string> textures = new HashSet<string>();
            // Create texture list
            foreach (Sprite sprite in loadedSprites.OrderBy(s => Path.GetFileNameWithoutExtension(s.FilePath.Value)))
            {
                //ignore sprites that don't have a file path (e.g. submarine pics)
                if (sprite.FilePath.IsNullOrEmpty()) { continue; }
                string normalizedFilePath = sprite.FilePath.FullPath;
                if (!textures.Contains(normalizedFilePath))
                {
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), textureList.Content.RectTransform) { MinSize = new Point(0, 20) },
                        Path.GetFileName(sprite.FilePath.Value))
                    {
                        ToolTip = sprite.FilePath.Value,
                        UserData = sprite.FullPath
                    };
                    textures.Add(normalizedFilePath);
                }
            }
            // Create sprite list
            // TODO: allow the user to choose whether to sort by file name or by texture sheet
            //foreach (Sprite sprite in loadedSprites.OrderBy(s => GetSpriteName(s)))
            foreach (Sprite sprite in loadedSprites.OrderBy(s => s.SourceElement.GetAttributeContentPath("texture")?.Value ?? string.Empty))
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), spriteList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    GetSpriteName(sprite) + " (" + sprite.SourceRect.X + ", " + sprite.SourceRect.Y + ", " + sprite.SourceRect.Width + ", " + sprite.SourceRect.Height + ")")
                {
                    UserData = sprite
                };
            }
            topPanelContents.Visible = false;
        }

        public void ResetZoom()
        {
            if (SelectedTexture == null) { return; }
            var viewArea = GetViewArea;
            float width = viewArea.Width / (float)SelectedTexture.Width;
            float height = viewArea.Height / (float)SelectedTexture.Height;
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

        private float GetBarScrollValue() => MathHelper.Lerp(0, 1, MathUtils.InverseLerp(MinZoom, MaxZoom, zoom));

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
            return string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(sprite.FilePath.Value) : name;
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
