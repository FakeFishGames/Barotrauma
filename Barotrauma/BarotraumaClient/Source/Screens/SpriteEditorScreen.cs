using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma
{
    class SpriteEditorScreen : Screen
    {
        private GUIListBox textureList, spriteList;

        private GUIComponent topPanelContents;
        private GUITextBlock pathText;

        private Camera cam;
        public override Camera Cam
        {
            get { return cam; }
        }

        public SpriteEditorScreen()
        {
            cam = new Camera();

            var topPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.04f), Frame.RectTransform) { MinSize = new Point(0, 35) }, "GUIFrameTop");
            topPanelContents = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.55f), topPanel.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.0f, -0.1f) },
                style: null);

            new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), topPanelContents.RectTransform), "Reload")
            {
                OnClicked = (button, userData) =>
                {
                    var selectedTexture = textureList.SelectedData as Texture2D;
                    if (selectedTexture == null) return false;

                    object selectedSprite = spriteList.SelectedData;
                    Sprite matchingSprite = Sprite.LoadedSprites.First(s => s.Texture == selectedTexture);
                    matchingSprite?.ReloadTexture();

                    RefreshLists();
                    textureList.Select(matchingSprite.Texture);
                    spriteList.Select(selectedSprite);
                    pathText.Text = "Reloaded from " + matchingSprite.FilePath;
                    return true;
                }
            };
            pathText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.8f), topPanelContents.RectTransform, Anchor.Center), "");

            var leftPanel = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f - topPanel.RectTransform.RelativeSize.Y), Frame.RectTransform, Anchor.BottomLeft)
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
                    pathText.Text = listBox.ToolTip;
                    topPanelContents.Visible = true;
                    return true;
                }
            };
            
            var rightPanel = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f - topPanel.RectTransform.RelativeSize.Y), Frame.RectTransform, Anchor.BottomRight)
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

        private void RefreshLists()
        {
            textureList.ClearChildren();
            spriteList.ClearChildren();

            HashSet<string> textures = new HashSet<string>();
            foreach (Sprite sprite in Sprite.LoadedSprites.OrderBy(s => Path.GetFileNameWithoutExtension(s.FilePath)))
            {
                //ignore sprites that don't have a file path (e.g. submarine pics)
                if (string.IsNullOrEmpty(sprite.FilePath)) continue;

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), spriteList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    Path.GetFileName(sprite.FilePath) + " " + sprite.SourceRect)
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
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, GameMain.ScissorTestEnable);

            graphics.Clear(new Color(0.051f, 0.149f, 0.271f, 1.0f));

            GUI.Draw(Cam, spriteBatch);

            Rectangle viewArea = new Rectangle(textureList.Rect.Right + 50, 50, spriteList.Rect.X - textureList.Rect.Right - 80, Frame.Rect.Height - 100);
            Rectangle textureRect = Rectangle.Empty;

            if (textureList.SelectedData is Texture2D texture)
            {
                // TODO: allow to adjust, toggle to snap to pixel perfect
                float scale = Math.Min(viewArea.Width / (float)texture.Width, viewArea.Height / (float)texture.Height);

                textureRect = new Rectangle(
                    (int)(viewArea.Center.X - texture.Bounds.Width / 2 * scale),
                    (int)(viewArea.Center.Y - texture.Bounds.Height / 2 * scale),
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

                GUI.DrawRectangle(spriteBatch,textureRect, Color.White, isFilled: false);

                foreach (Sprite sprite in Sprite.LoadedSprites)
                {
                    if (sprite.Texture != texture) continue;

                    Rectangle sourceRect = new Rectangle(
                        textureRect.X + (int)(sprite.SourceRect.X * scale),
                        textureRect.Y + (int)(sprite.SourceRect.Y * scale),
                        (int)(sprite.SourceRect.Width * scale),
                        (int)(sprite.SourceRect.Height * scale));

                    GUI.DrawRectangle(spriteBatch, sourceRect,
                        spriteList.SelectedData == sprite ? Color.Red : Color.White * 0.5f);
                }
            }

            spriteBatch.End();
        }
    }
}
