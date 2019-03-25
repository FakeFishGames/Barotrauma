using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;
using System.Collections.Generic;
using Barotrauma.Media;
using System.IO;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    class VideoPlayer
    {
        private Video currentVideo;
        private Point resolution;
        private string filePath;

        private GUIFrame background, videoFrame, textFrame;
        private GUITextBlock title, textContent, objectiveTitle, objectiveText;
        private GUICustomComponent videoView;

        private Color backgroundColor = new Color(0f, 0f, 0f, 1f);
        private Action callbackOnStop;

        private bool isPlaying;

        public bool IsPlaying()
        {
            return isPlaying;
        }

        private readonly Point defaultResolution = new Point(520, 300);
        private readonly int borderSize = 20;
        private readonly Point buttonSize = new Point(160, 50);
        private readonly int titleHeight = 30;
        private readonly int objectiveFrameHeight = 60;
        private readonly int textHeight = 25;

        public struct TextSettings
        {
            public string Text;
            public int Width;

            public TextSettings(XElement element)
            {
                Text = TextManager.GetFormatted(element.GetAttributeString("text", string.Empty), true);
                Width = element.GetAttributeInt("width", 300);
            }
        }

        public struct VideoSettings
        {
            public string File;
            public int Width;
            public int Height;

            public VideoSettings(XElement element)
            {
                File = element.GetAttributeString("file", string.Empty);
                Width = element.GetAttributeInt("width", 0);
                Height = element.GetAttributeInt("height", 0);
            }
        }

        public VideoPlayer()
        {
            int width = defaultResolution.X;
            int height = defaultResolution.Y;

            background = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), GUI.Canvas, Anchor.Center), "InnerFrame", backgroundColor);
            videoFrame = new GUIFrame(new RectTransform(new Point(width + borderSize, height + borderSize), background.RectTransform, Anchor.Center, Pivot.CenterRight), "SonarFrame");
            videoFrame.RectTransform.AbsoluteOffset = new Point(-borderSize, 0);

            textFrame = new GUIFrame(new RectTransform(new Point(width + borderSize, height + borderSize * 2), background.RectTransform, Anchor.Center, Pivot.CenterLeft), "SonarFrame");
            textFrame.RectTransform.AbsoluteOffset = new Point(borderSize, 0);

            videoView = new GUICustomComponent(new RectTransform(new Point(width, height), videoFrame.RectTransform, Anchor.Center),
            (spriteBatch, guiCustomComponent) => { DrawVideo(spriteBatch, guiCustomComponent.Rect); });
            title = new GUITextBlock(new RectTransform(Point.Zero, textFrame.RectTransform, Anchor.TopLeft, Pivot.TopLeft) { AbsoluteOffset = new Point(0, 10) }, string.Empty, font: GUI.VideoTitleFont, textColor: new Color(253, 174, 0), textAlignment: Alignment.Left);

            textContent = new GUITextBlock(new RectTransform(new Vector2(1f, .8f), textFrame.RectTransform, Anchor.TopLeft, Pivot.TopLeft) { AbsoluteOffset = new Point(0, borderSize / 2 + titleHeight) }, string.Empty, font: GUI.Font, textAlignment: Alignment.TopLeft);

            objectiveTitle = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), textFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter), string.Empty, font: GUI.ObjectiveTitleFont, textAlignment: Alignment.CenterRight, textColor: Color.White);
            objectiveTitle.Text = TextManager.Get("NewObjective");
            objectiveText = new GUITextBlock(new RectTransform(new Point(textFrame.Rect.Width, textHeight), textFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter), string.Empty, font: GUI.ObjectiveNameFont, textColor: new Color(4, 180, 108), textAlignment: Alignment.CenterRight);

            objectiveTitle.Visible = objectiveText.Visible = false;
        }

        public void Play()
        {
            isPlaying = true;
        }

        public void Stop()
        {
            isPlaying = false;
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

            if (PlayerInput.KeyHit(Keys.Enter))
            {
                DisposeVideo(null, null);
                return;
            }

            if (currentVideo.IsPlaying) return;

            currentVideo.Dispose();
            currentVideo = null;
            currentVideo = CreateVideo();
        }

        public void AddToGUIUpdateList()
        {
            if (!IsPlaying()) return;
            background.AddToGUIUpdateList();
        }

        public void LoadContentWithObjective(string contentPath, VideoSettings videoSettings, TextSettings textSettings, string contentId, bool startPlayback, string objective, Action callback = null)
        {
            callbackOnStop = callback;
            filePath = contentPath + videoSettings.File;

            if (!File.Exists(filePath))
            {
                DebugConsole.ThrowError("No video found at: " + filePath);
                DisposeVideo(null, null);
                return;
            }

            ResetFrameSizes();

            if (currentVideo != null)
            {
                currentVideo.Dispose();
                currentVideo = null;
            }

            resolution = new Point(0, 0);

            if (currentVideo == null) // No preloaded video found
            {
                resolution = new Point(videoSettings.Width, videoSettings.Height);

                if (resolution.X == 0 || resolution.Y == 0)
                {
                    resolution = defaultResolution;
                }

                currentVideo = CreateVideo();
            }

            objectiveTitle.Visible = objectiveText.Visible = true;

            videoFrame.RectTransform.NonScaledSize += resolution + new Point(borderSize, borderSize);
            videoView.RectTransform.NonScaledSize += resolution;

            title.Text = TextManager.Get(contentId);
            title.RectTransform.NonScaledSize += new Point(textSettings.Width, titleHeight);

            if (!string.IsNullOrEmpty(textSettings.Text))
            {
                textSettings.Text = ToolBox.WrapText(textSettings.Text, textSettings.Width, GUI.Font);
                int wrappedHeight = textSettings.Text.Split('\n').Length * textHeight;
                textFrame.RectTransform.NonScaledSize += new Point(textSettings.Width + borderSize, wrappedHeight + borderSize + buttonSize.Y + titleHeight);
                textContent.RectTransform.NonScaledSize = new Point(textSettings.Width, wrappedHeight);
            }

            textContent.Text = textSettings.Text;

            objectiveTitle.RectTransform.AbsoluteOffset = new Point(-10, textContent.RectTransform.Rect.Height + (int)(textHeight * 1.75f));
            objectiveText.RectTransform.AbsoluteOffset = new Point(-10, textContent.RectTransform.Rect.Height + objectiveTitle.Rect.Height + (int)(textHeight * 2.25f));

            textFrame.RectTransform.NonScaledSize += new Point(0, objectiveFrameHeight);
            objectiveText.RectTransform.NonScaledSize += new Point(textFrame.Rect.Width, textHeight);
            objectiveText.Text = objective;

            var okButton = new GUIButton(new RectTransform(buttonSize, textFrame.RectTransform, Anchor.BottomRight, Pivot.BottomRight) { AbsoluteOffset = new Point(20, 20) },
                TextManager.Get("OK"))
            {
                OnClicked = DisposeVideo
            };

            if (startPlayback) Play();
        }

        public void LoadContent(string contentPath, VideoSettings videoSettings, TextSettings textSettings, string contentId, bool startPlayback, Action callback = null)
        {
            callbackOnStop = callback;
            filePath = contentPath + videoSettings.File;

            if (!File.Exists(filePath))
            {
                DebugConsole.ThrowError("No video found at: " + filePath);
                DisposeVideo(null, null);
                return;
            }

            ResetFrameSizes();

            if (currentVideo != null)
            {
                currentVideo.Dispose();
                currentVideo = null;
            }

            resolution = new Point(0, 0);

            if (currentVideo == null) // No preloaded video found
            {
                resolution = new Point(videoSettings.Width, videoSettings.Height);

                if (resolution.X == 0 || resolution.Y == 0)
                {
                    resolution = defaultResolution;
                }

                currentVideo = CreateVideo();
            }

            objectiveTitle.Visible = objectiveText.Visible = false;

            videoFrame.RectTransform.NonScaledSize += resolution + new Point(borderSize, borderSize);
            videoView.RectTransform.NonScaledSize += resolution;

            title.Text = TextManager.Get(contentId);
            title.RectTransform.NonScaledSize += new Point(textSettings.Width, titleHeight);

            if (textSettings.Text != string.Empty)
            {
                textSettings.Text = ToolBox.WrapText(textSettings.Text, textSettings.Width, GUI.Font);
                int wrappedHeight = textSettings.Text.Split('\n').Length * textHeight;
                textFrame.RectTransform.NonScaledSize += new Point(textSettings.Width + borderSize, wrappedHeight + borderSize + buttonSize.Y + titleHeight);
                textContent.RectTransform.NonScaledSize = new Point(textSettings.Width, wrappedHeight);
            }

            textContent.Text = textSettings.Text;

            var okButton = new GUIButton(new RectTransform(buttonSize, textFrame.RectTransform, Anchor.BottomRight, Pivot.BottomRight) { AbsoluteOffset = new Point(20, 20) },
                TextManager.Get("OK"))
            {
                OnClicked = DisposeVideo
            };

            if (startPlayback) Play();
        }

        private void ResetFrameSizes()
        {
            videoFrame.RectTransform.NonScaledSize = Point.Zero;
            videoView.RectTransform.NonScaledSize = Point.Zero;

            title.RectTransform.NonScaledSize = Point.Zero;
            textFrame.RectTransform.NonScaledSize = Point.Zero;
            textContent.RectTransform.NonScaledSize = Point.Zero;
            
            objectiveText.RectTransform.NonScaledSize = Point.Zero;
        }

        private Video CreateVideo()
        {
            Video video = null;

            try
            {
                //video = new Video(GameMain.Instance.GraphicsDevice, GameMain.SoundManager, "Content/splashscreen.mp4", 1280, 720);
                video = new Video(GameMain.Instance.GraphicsDevice, GameMain.SoundManager, filePath, (uint)resolution.X, (uint)resolution.Y);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error loading video content " + filePath + "!", e);
            }

            return video;
        }

        private void DrawVideo(SpriteBatch spriteBatch, Rectangle rect)
        {
            if (!isPlaying) return;
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
