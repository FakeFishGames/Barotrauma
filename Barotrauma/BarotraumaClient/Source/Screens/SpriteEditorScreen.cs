using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        private GUIFrame topPanelContents;
        private GUITextBlock texturePathText;
        private GUITextBlock xmlPathText;
        private XElement element;
        private Sprite selectedSprite;
        private Rectangle viewArea;
        private Rectangle textureRect;
        private float scale;
        private int spriteCount;

        private readonly Camera cam;
        public override Camera Cam
        {
            get { return cam; }
        }

        public SpriteEditorScreen()
        {
            cam = new Camera();

            topPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.08f), Frame.RectTransform) { MinSize = new Point(0, 35) }, "GUIFrameTop");
            topPanelContents = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.8f), topPanel.RectTransform, Anchor.Center), style: null);

            new GUIButton(new RectTransform(new Vector2(0.15f, 0.4f), topPanelContents.RectTransform), "Reload Texture")
            {
                OnClicked = (button, userData) =>
                {
                    var selectedTexture = textureList.SelectedData as Texture2D;
                    if (selectedTexture == null) return false;

                    object selectedSprite = spriteList.SelectedData;
                    Sprite matchingSprite = Sprite.LoadedSprites.First(s => s.Texture == selectedTexture);
                    matchingSprite.ReloadTexture();

                    RefreshLists();
                    textureList.Select(matchingSprite.Texture);
                    spriteList.Select(selectedSprite);
                    texturePathText.Text = "Textures reloaded from " + matchingSprite.FilePath;
                    texturePathText.TextColor = Color.LightGreen;
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.15f, 0.4f), topPanelContents.RectTransform) { RelativeOffset = new Vector2(0, 0.5f) }, "Save Selected Source Rect")
            {
                OnClicked = (button, userData) =>
                {
                    if (element == null) { return false; }
                    if (selectedSprite == null) { return false; }
                    element.SetAttributeValue("sourcerect", XMLExtensions.RectToString(selectedSprite.SourceRect));
                    element.Document.Save(xmlPathText.Text);
                    xmlPathText.Text = "Changes saved to " + xmlPathText.Text;
                    xmlPathText.TextColor = Color.LightGreen;
                    return true;
                }
            };

            texturePathText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.4f), topPanelContents.RectTransform, Anchor.Center, Pivot.BottomCenter), "", Color.LightGray);
            xmlPathText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.4f), topPanelContents.RectTransform, Anchor.Center, Pivot.TopCenter), "", Color.LightGray);

            leftPanel = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f - topPanel.RectTransform.RelativeSize.Y), Frame.RectTransform, Anchor.BottomLeft)
                { MinSize = new Point(150, 0) },
                style: "GUIFrameLeft");
            var paddedLeftPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), leftPanel.RectTransform, Anchor.CenterLeft) { RelativeOffset = new Vector2(0.02f, 0.0f) })
            {
                Stretch = true
            };
            textureList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), paddedLeftPanel.RectTransform))
            {
                OnSelected = (listBox, userData) =>
                {
                    foreach (GUIComponent child in spriteList.Content.Children)
                    {
                        var textBlock = (GUITextBlock)child;
                        textBlock.TextColor = new Color(textBlock.TextColor, ((Sprite)textBlock.UserData).Texture == userData ? 1.0f : 0.4f);
                    }
                    if (selectedSprite == null || selectedSprite.Texture != userData)
                    {
                        selectedSprite = Sprite.LoadedSprites.First(s => s.Texture == userData);
                        widgets.Clear();
                    }
                    texturePathText.Text = selectedSprite.FilePath;
                    texturePathText.TextColor = Color.LightGray;
                    element = selectedSprite.SourceElement;
                    if (element == null)
                    {
                        xmlPathText.Text = string.Empty;
                    }
                    else
                    {
                        string[] splitted = element.BaseUri.Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                        IEnumerable<string> filtered = splitted.SkipWhile(part => part != "Content");
                        string parsed = string.Join("/", filtered);
                        xmlPathText.Text = parsed;
                        xmlPathText.TextColor = Color.LightGray;
                    }
                    topPanelContents.Visible = true;
                    return true;
                }
            };
            
            rightPanel = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f - topPanel.RectTransform.RelativeSize.Y), Frame.RectTransform, Anchor.BottomRight)
                { MinSize = new Point(150, 0) },
                style: "GUIFrameRight");
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
                    if (selectedSprite.Texture != sprite.Texture)
                    {
                        widgets.Clear();
                    }
                    selectedSprite = sprite;
                    textureList.Select(sprite.Texture);
                    return true;
                }
            };
       
            RefreshLists();
        }

        public override void Select()
        {
            base.Select();
            RefreshLists();
        }

        private string GetIdentifier(Sprite sprite)
        {
            // TODO: cache?
            return sprite.SourceElement?.Parent.GetAttributeString("identifier", string.Empty);
        }

        private void RefreshLists()
        {
            textureList.ClearChildren();
            spriteList.ClearChildren();
            widgets.Clear();
            HashSet<string> textures = new HashSet<string>();
            foreach (Sprite sprite in Sprite.LoadedSprites.OrderBy(s => Path.GetFileNameWithoutExtension(s.FilePath)))
            {
                //ignore sprites that don't have a file path (e.g. submarine pics)
                if (string.IsNullOrEmpty(sprite.FilePath)) continue;
                string identifier = GetIdentifier(sprite);
                string name = string.IsNullOrEmpty(identifier) ? Path.GetFileNameWithoutExtension(sprite.FilePath) : identifier;
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), spriteList.Content.RectTransform) { MinSize = new Point(0, 20) }, name + " " + sprite.SourceRect)
                {
                    Padding = Vector4.Zero,
                    UserData = sprite
                };

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

            topPanelContents.Visible = false;
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
            for (int i = 0; i < widgets.Count; i++)
            {
                widgets.ElementAt(i).Value.Update((float)deltaTime);
            }
            // Select rects with the mouse
            if (textureList.SelectedData is Texture2D texture)
            {
                foreach (Sprite sprite in Sprite.LoadedSprites)
                {
                    if (sprite.Texture != texture) continue;
                    if (PlayerInput.LeftButtonClicked())
                    {
                        var scaledRect = new Rectangle(textureRect.Location + sprite.SourceRect.Location.Multiply(scale), sprite.SourceRect.Size.Multiply(scale));
                        if (scaledRect.Contains(PlayerInput.MousePosition))
                        {
                            spriteList.Select(sprite);
                        }
                    }
                }
            }
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(new Color(0.051f, 0.149f, 0.271f, 1.0f));
            spriteBatch.Begin(SpriteSortMode.Immediate, rasterizerState: GameMain.ScissorTestEnable);

            int margin = 20;
            viewArea = new Rectangle(leftPanel.Rect.Right + margin, topPanel.Rect.Bottom + margin, rightPanel.Rect.Left - leftPanel.Rect.Right - margin * 2, Frame.Rect.Height - topPanel.Rect.Height - margin * 2);

            if (textureList.SelectedData is Texture2D texture)
            {
                // TODO: allow to adjust, toggle to snap to pixel perfect
                scale = Math.Min(viewArea.Width / (float)texture.Width, viewArea.Height / (float)texture.Height);

                textureRect = new Rectangle(
                    (int)(viewArea.Center.X - texture.Bounds.Width / 2f * scale),
                    (int)(viewArea.Center.Y - texture.Bounds.Height / 2f * scale),
                    (int)(texture.Bounds.Width * scale),
                    (int)(texture.Bounds.Height * scale));

                spriteBatch.Draw(texture,
                    viewArea.Center.ToVector2(), 
                    sourceRectangle: null, 
                    color: Color.White, 
                    rotation: 0.0f,
                    origin: new Vector2(texture.Bounds.Width / 2.0f, texture.Bounds.Height / 2.0f), 
                    scale: scale, 
                    effects: SpriteEffects.None, 
                    layerDepth: 0);

                //GUI.DrawRectangle(spriteBatch, viewArea, Color.Green, isFilled: false);
                GUI.DrawRectangle(spriteBatch, textureRect, Color.Yellow, isFilled: false);

                foreach (Sprite sprite in Sprite.LoadedSprites)
                {
                    if (sprite.Texture != texture) continue;
                    spriteCount++;

                    Rectangle sourceRect = new Rectangle(
                        textureRect.X + (int)(sprite.SourceRect.X * scale),
                        textureRect.Y + (int)(sprite.SourceRect.Y * scale),
                        (int)(sprite.SourceRect.Width * scale),
                        (int)(sprite.SourceRect.Height * scale));

                    GUI.DrawRectangle(spriteBatch, sourceRect,
                        selectedSprite == sprite ? Color.Red : Color.White * 0.5f);

                    string identifier = GetIdentifier(sprite);
                    if (!string.IsNullOrEmpty(identifier))
                    {
                        int widgetSize = 10;
                        Vector2 halfSize = new Vector2(widgetSize) / 2;
                        //Vector2 stringOffset = new Vector2(5, 14);
                        var rect = sprite.SourceRect;
                        var topLeft = rect.Location.ToVector2();
                        var topRight = new Vector2(topLeft.X + rect.Width, topLeft.Y);
                        var bottomRight = new Vector2(topRight.X, topRight.Y + rect.Height);
                        //var bottomLeft = new Vector2(topLeft.X, bottomRight.Y);
                        var positionWidget = GetWidget($"{identifier}_position", widgetSize, Widget.Shape.Rectangle, initMethod: w =>
                        {
                            w.DrawPos = textureRect.Location.ToVector2() + topLeft * scale - halfSize;
                            w.inputAreaMargin = new Point(widgetSize / 2);
                            w.MouseDown += () => spriteList.Select(sprite);
                            w.MouseHeld += () =>
                            {
                                w.DrawPos = PlayerInput.MousePosition;
                                sprite.SourceRect = new Rectangle(((w.DrawPos + halfSize - textureRect.Location.ToVector2()) / scale).ToPoint(), sprite.SourceRect.Size);
                                if (widgets.TryGetValue($"{identifier}_size", out Widget sizeW))
                                {
                                    sizeW.DrawPos = w.DrawPos + halfSize + sprite.SourceRect.Size.ToVector2() * scale;
                                }
                            };
                            w.PostUpdate += dTime =>
                            {
                                w.color = selectedSprite == sprite ? Color.Red : Color.White;
                            };
                        });
                        var sizeWidget = GetWidget($"{identifier}_size", widgetSize, Widget.Shape.Rectangle, initMethod: w =>
                        {
                            w.DrawPos = textureRect.Location.ToVector2() + bottomRight * scale + halfSize;
                            w.inputAreaMargin = new Point(widgetSize / 2);
                            w.MouseDown += () => spriteList.Select(sprite);
                            w.MouseHeld += () =>
                            {
                                w.DrawPos = PlayerInput.MousePosition;
                                sprite.SourceRect = new Rectangle(sprite.SourceRect.Location, ((w.DrawPos - halfSize - positionWidget.DrawPos) / scale).ToPoint());
                            };
                            w.PostUpdate += dTime =>
                            {
                                w.color = selectedSprite == sprite ? Color.Red : Color.White;
                            };
                        });
                        positionWidget.Draw(spriteBatch, (float)deltaTime);
                        sizeWidget.Draw(spriteBatch, (float)deltaTime);
                    }
                }
            }

            GUI.Draw(Cam, spriteBatch);

            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 200, 0), "widgets: " + widgets.Count, Color.White);
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 200, 20), "sprites: " + spriteCount, Color.White);
            spriteCount = 0;

            spriteBatch.End();
        }

        #region Widgets
        private Dictionary<string, Widget> widgets = new Dictionary<string, Widget>();

        private Widget GetWidget(string id, int size = 5, Widget.Shape shape = Widget.Shape.Rectangle, Action<Widget> initMethod = null)
        {
            if (!widgets.TryGetValue(id, out Widget widget))
            {
                widget = new Widget(id, size, shape);
                initMethod?.Invoke(widget);
                widgets.Add(id, widget);
            }
            return widget;
        }
        #endregion
    }
}
