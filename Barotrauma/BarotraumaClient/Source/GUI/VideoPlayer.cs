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

        private Point scaledResolution;
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
                Width = element.GetAttributeInt("width", 450);
            }
        }

        public struct VideoSettings
        {
            public string File;

            public VideoSettings(XElement element)
            {
                File = element.GetAttributeString("file", string.Empty);
            }
        }

        public VideoPlayer()
        {
            int screenWidth = (int)(GameMain.GraphicsWidth * 0.55f);
            scaledResolution = new Point(screenWidth, (int)(screenWidth / 16f * 9f));

            int width = scaledResolution.X;
            int height = scaledResolution.Y;

            background = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), GUI.Canvas, Anchor.Center), "InnerFrame", backgroundColor);
            videoFrame = new GUIFrame(new RectTransform(new Point(width + borderSize, height + borderSize), background.RectTransform, Anchor.Center, Pivot.Center) { AbsoluteOffset = new Point((int)(-100 / (GUI.Scale * 0.6f)), 0) }, "SonarFrame");
            //videoFrame.RectTransform.AbsoluteOffset = new Point(-borderSize, 0);

            textFrame = new GUIFrame(new RectTransform(new Point(width + borderSize, height + borderSize * 2), videoFrame.RectTransform, Anchor.CenterLeft, Pivot.CenterLeft), "TextFrame");
            textFrame.RectTransform.AbsoluteOffset = new Point(borderSize + videoFrame.Rect.Width, 0);

            videoView = new GUICustomComponent(new RectTransform(new Point(width, height), videoFrame.RectTransform, Anchor.Center),
            (spriteBatch, guiCustomComponent) => { DrawVideo(spriteBatch, guiCustomComponent.Rect); });
            title = new GUITextBlock(new RectTransform(Point.Zero, textFrame.RectTransform, Anchor.TopLeft, Pivot.TopLeft) { AbsoluteOffset = new Point(5, 10) }, string.Empty, font: GUI.VideoTitleFont, textColor: new Color(253, 174, 0), textAlignment: Alignment.Left);

            textContent = new GUITextBlock(new RectTransform(new Vector2(1f, .8f), textFrame.RectTransform, Anchor.TopLeft, Pivot.TopLeft) { AbsoluteOffset = new Point(0, borderSize + titleHeight) }, string.Empty, font: GUI.Font, textAlignment: Alignment.TopLeft);

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

            if (PlayerInput.KeyHit(Keys.Enter) || PlayerInput.KeyHit(Keys.Escape))
            {
                DisposeVideo(null, null);
                return;
            }

            if (currentVideo.IsPlaying) return;

            currentVideo.Dispose();
            currentVideo = null;
            currentVideo = CreateVideo(scaledResolution);
        }

        public void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            if (!IsPlaying()) return;
            background.AddToGUIUpdateList(ignoreChildren, order);
        }

        public void LoadContent(string contentPath, VideoSettings videoSettings, TextSettings textSettings, string contentId, bool startPlayback, string objective = "", Action callback = null)
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

            currentVideo = CreateVideo(scaledResolution);

            videoFrame.RectTransform.NonScaledSize += scaledResolution + new Point(borderSize, borderSize);
            videoView.RectTransform.NonScaledSize += scaledResolution;

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

            if (!string.IsNullOrEmpty(objective))
            {
                objectiveTitle.RectTransform.AbsoluteOffset = new Point(-10, textContent.RectTransform.Rect.Height + (int)(textHeight * 1.95f));
                objectiveText.RectTransform.AbsoluteOffset = new Point(-10, textContent.RectTransform.Rect.Height + objectiveTitle.Rect.Height + (int)(textHeight * 2.25f));

                textFrame.RectTransform.NonScaledSize += new Point(0, objectiveFrameHeight);
                objectiveText.RectTransform.NonScaledSize += new Point(textFrame.Rect.Width, textHeight);
                objectiveText.Text = objective;
                objectiveTitle.Visible = objectiveText.Visible = true;
            }
            else
            {
                textFrame.RectTransform.NonScaledSize += new Point(0, borderSize);
                objectiveTitle.Visible = objectiveText.Visible = false;
            }

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

        private Video CreateVideo(Point resolution)
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
