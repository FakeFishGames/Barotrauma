using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;
using Barotrauma.Media;
using Barotrauma.IO;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    class VideoPlayer
    {
        public bool IsPlaying;

        private Video currentVideo;
        private string filePath;

        private GUIFrame background, videoFrame, textFrame;
        private GUITextBlock title, textContent, objectiveTitle, objectiveText;
        private GUICustomComponent videoView;
        private GUIButton okButton;

        private Color backgroundColor = new Color(0f, 0f, 0f, 0.8f);
        private Action callbackOnStop;

        private Point scaledVideoResolution;
        private readonly int borderSize = 20;
        private readonly Point buttonSize = new Point(120, 30);
        private readonly int titleHeight = 30;
        private readonly int objectiveFrameHeight = 60;
        private readonly int textHeight = 25;

        private bool useTextOnRightSide = false;

        public class TextSettings
        {
            public LocalizedString Text;
            public int Width;

            public TextSettings(Identifier textTag, int width)
            {
                Text = TextManager.GetFormatted(textTag);
                Width = width;
            }

            public TextSettings(XElement element)
            {
                Text = TextManager.GetFormatted(element.GetAttributeIdentifier("text", Identifier.Empty));
                Width = element.GetAttributeInt("width", 450);
            }
        }

        public class VideoSettings
        {
            public readonly string File;

            public VideoSettings(string file)
            {
                File = file;
            }
        }

        public VideoPlayer() // GUI elements with size set to Point.Zero are resized based on content
        {
            int screenWidth = (int)(GameMain.GraphicsWidth * 0.65f);
            scaledVideoResolution = new Point(screenWidth, (int)(screenWidth / 16f * 9f));

            int width = scaledVideoResolution.X;
            int height = scaledVideoResolution.Y;

            background = new GUIFrame(new RectTransform(Point.Zero, GUI.Canvas, Anchor.Center), style: null, color: backgroundColor);
            videoFrame = new GUIFrame(new RectTransform(Point.Zero, background.RectTransform, Anchor.Center, Pivot.Center), style: "InnerFrame");

            if (useTextOnRightSide)
            {
                textFrame = new GUIFrame(new RectTransform(Point.Zero, videoFrame.RectTransform, Anchor.CenterLeft, Pivot.CenterLeft), "TextFrame");
            }
            else
            {
                textFrame = new GUIFrame(new RectTransform(Point.Zero, videoFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter), "TextFrame");
            }

            videoView = new GUICustomComponent(new RectTransform(Point.Zero, videoFrame.RectTransform, Anchor.Center), (spriteBatch, guiCustomComponent) => { DrawVideo(spriteBatch, guiCustomComponent.Rect); });
            title = new GUITextBlock(new RectTransform(Point.Zero, textFrame.RectTransform, Anchor.TopLeft, Pivot.TopLeft), string.Empty, font: GUIStyle.LargeFont, textColor: new Color(253, 174, 0), textAlignment: Alignment.Left);

            textContent = new GUITextBlock(new RectTransform(Point.Zero, textFrame.RectTransform, Anchor.TopLeft, Pivot.TopLeft), string.Empty, font: GUIStyle.Font, textAlignment: Alignment.TopLeft);

            objectiveTitle = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), textFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter), string.Empty, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterRight, textColor: Color.White);
            objectiveTitle.Text = TextManager.Get("Tutorial.NewObjective");
            objectiveText = new GUITextBlock(new RectTransform(Point.Zero, textFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter), string.Empty, font: GUIStyle.SubHeadingFont, textColor: new Color(4, 180, 108), textAlignment: Alignment.CenterRight);

            objectiveTitle.Visible = objectiveText.Visible = false;
        }

        public void Play()
        {
            IsPlaying = true;
        }

        public void Stop()
        {
            IsPlaying = false;
            if (currentVideo == null) return;
            currentVideo.Dispose();
            currentVideo = null;
        }

        private bool DisposeVideo(GUIButton button, object userData)
        {
            Stop();
            callbackOnStop?.Invoke();
            return true;
        }

        public void Update()
        {
            if (currentVideo == null) return;
            if (currentVideo.IsPlaying) return;

            currentVideo.Play();
        }

        public void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            if (!IsPlaying) return;
            background.AddToGUIUpdateList(ignoreChildren, order);
        }

        public void LoadContent(string contentPath, VideoSettings videoSettings, TextSettings textSettings, Identifier contentId, bool startPlayback)
        {
            LoadContent(contentPath, videoSettings, textSettings, contentId, startPlayback, new RawLString(""), null);
        }

        public void LoadContent(string contentPath, VideoSettings videoSettings, TextSettings textSettings, Identifier contentId, bool startPlayback, LocalizedString objective, Action onStop = null)
        {
            callbackOnStop = onStop;
            filePath = contentPath + videoSettings.File;

            if (!File.Exists(filePath))
            {
                DebugConsole.ThrowError("No video found at: " + filePath);
                DisposeVideo(null, null);
                return;
            }

            if (currentVideo != null)
            {
                currentVideo.Dispose();
                currentVideo = null;
            }

            currentVideo = CreateVideo();
            title.Text = textSettings != null ? TextManager.Get(contentId) : string.Empty;
            textContent.Text = textSettings != null ? textSettings.Text : string.Empty;
            objectiveText.Text = objective;

            AdjustFrames(videoSettings, textSettings);

            if (startPlayback) Play();
        }

        private void AdjustFrames(VideoSettings videoSettings, TextSettings textSettings)
        {
            int screenWidth = (int)(GameMain.GraphicsWidth * 0.55f);
            scaledVideoResolution = new Point(screenWidth, (int)(screenWidth / 16f * 9f));

            background.RectTransform.NonScaledSize = Point.Zero;
            videoFrame.RectTransform.NonScaledSize = Point.Zero;
            videoView.RectTransform.NonScaledSize = Point.Zero;

            title.RectTransform.NonScaledSize = Point.Zero;
            textFrame.RectTransform.NonScaledSize = Point.Zero;
            textContent.RectTransform.NonScaledSize = Point.Zero;

            objectiveText.RectTransform.NonScaledSize = Point.Zero;

            title.TextScale = textContent.TextScale = objectiveText.TextScale = objectiveTitle.TextScale = GUI.Scale;

            int scaledBorderSize = (int)(borderSize * GUI.Scale);
            int scaledTextWidth = 0;
            if (textSettings != null) scaledTextWidth = useTextOnRightSide ? (int)(textSettings.Width * GUI.Scale) : scaledVideoResolution.X / 2;
            int scaledTitleHeight = (int)(titleHeight * GUI.Scale);
            int scaledTextHeight = (int)(textHeight * GUI.Scale);
            int scaledObjectiveFrameHeight = (int)(objectiveFrameHeight * GUI.Scale);

            Point scaledButtonSize = new Point((int)(buttonSize.X * GUI.Scale), (int)(buttonSize.Y * GUI.Scale));

            background.RectTransform.NonScaledSize = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            videoFrame.RectTransform.NonScaledSize = scaledVideoResolution + new Point(scaledBorderSize, scaledBorderSize);
            videoView.RectTransform.NonScaledSize = scaledVideoResolution;
            videoFrame.RectTransform.AbsoluteOffset = new Point(0, videoFrame.RectTransform.NonScaledSize.Y);

            title.RectTransform.NonScaledSize = new Point(scaledTextWidth, scaledTitleHeight);
            title.RectTransform.AbsoluteOffset = new Point((int)(5 * GUI.Scale), (int)(10 * GUI.Scale));

            if (textSettings != null && !textSettings.Text.IsNullOrEmpty())
            {
                textSettings.Text = ToolBox.WrapText(textSettings.Text, scaledTextWidth, GUIStyle.Font);
                int wrappedHeight = textSettings.Text.Value.Split('\n').Length * scaledTextHeight;

                textFrame.RectTransform.NonScaledSize = new Point(scaledTextWidth + scaledBorderSize, wrappedHeight + scaledBorderSize + scaledButtonSize.Y + scaledTitleHeight);

                if (useTextOnRightSide)
                {
                    textFrame.RectTransform.AbsoluteOffset = new Point(scaledVideoResolution.X + scaledBorderSize * 2, 0);
                }
                else
                {
                    textFrame.RectTransform.AbsoluteOffset = new Point(0, scaledVideoResolution.Y + scaledBorderSize * 2);
                }

                textContent.RectTransform.NonScaledSize = new Point(scaledTextWidth, wrappedHeight);
                textContent.RectTransform.AbsoluteOffset = new Point(0, scaledBorderSize + scaledTitleHeight);
            }

            if (!objectiveText.Text.IsNullOrEmpty())
            {
                int scaledXOffset = (int)(-10 * GUI.Scale);

                objectiveTitle.RectTransform.AbsoluteOffset = new Point(scaledXOffset, textContent.RectTransform.Rect.Height + (int)(scaledTextHeight * 1.95f));
                objectiveText.RectTransform.AbsoluteOffset = new Point(scaledXOffset, textContent.RectTransform.Rect.Height + objectiveTitle.Rect.Height + (int)(scaledTextHeight * 2.25f));

                textFrame.RectTransform.NonScaledSize += new Point(0, scaledObjectiveFrameHeight);
                objectiveText.RectTransform.NonScaledSize = new Point(textFrame.Rect.Width, scaledTextHeight);
                objectiveTitle.Visible = objectiveText.Visible = true;
            }
            else
            {
                textFrame.RectTransform.NonScaledSize += new Point(0, scaledBorderSize);
                objectiveTitle.Visible = objectiveText.Visible = false;
            }

            if (okButton != null)
            {
                textFrame.RemoveChild(okButton);
                okButton = null;
            }

            if (textSettings != null)
            {
                if (useTextOnRightSide)
                {
                    int totalFrameWidth = videoFrame.Rect.Width + textFrame.Rect.Width + scaledBorderSize * 2;
                    int xOffset = videoFrame.Rect.Width / 2 + scaledBorderSize - (videoFrame.Rect.Width / 2 - textFrame.Rect.Width / 2);
                    videoFrame.RectTransform.AbsoluteOffset = new Point(-xOffset, (int)(50 * GUI.Scale));
                }
                else
                {
                    int totalFrameHeight = videoFrame.Rect.Height + textFrame.Rect.Height + scaledBorderSize * 2;
                    int yOffset = videoFrame.Rect.Height / 2 + scaledBorderSize - (videoFrame.Rect.Height / 2 - textFrame.Rect.Height / 2);
                    videoFrame.RectTransform.AbsoluteOffset = new Point(0, -yOffset);
                }
                
                okButton = new GUIButton(new RectTransform(scaledButtonSize, textFrame.RectTransform, Anchor.BottomRight, Pivot.BottomRight) { AbsoluteOffset = new Point(scaledBorderSize, scaledBorderSize) }, TextManager.Get("OK"))
                {
                    OnClicked = DisposeVideo
                };
            }
            else
            {
                videoFrame.RectTransform.AbsoluteOffset = new Point(0, 0);

                okButton = new GUIButton(new RectTransform(scaledButtonSize, videoFrame.RectTransform, Anchor.TopLeft, Pivot.TopLeft) { AbsoluteOffset = new Point(scaledBorderSize, scaledBorderSize) }, TextManager.Get("Back"))
                {
                    OnClicked = DisposeVideo
                };
            }
        }

        private Video CreateVideo()
        {
            Video video = null;

            try
            {
                video = Video.Load(GameMain.Instance.GraphicsDevice, GameMain.SoundManager, filePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error loading video content " + filePath + "!", e);
            }

            return video;
        }

        private void DrawVideo(SpriteBatch spriteBatch, Rectangle rect)
        {
            if (!IsPlaying) return;
            spriteBatch.Draw(currentVideo.GetTexture(), rect, Color.White);
        }

        public void Remove()
        {
            if (currentVideo != null)
            {
                currentVideo.Dispose();
                currentVideo = null;
            }
        }
    }
}
