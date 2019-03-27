using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;
using System.Collections.Generic;
using Barotrauma.Media;
using System.IO;

namespace Barotrauma
{
    class VideoPlayer
    {
        private Video currentVideo;
        private List<PreloadedContent> preloadedVideos;

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

        private class PreloadedContent
        {
            public string ContentName;
            public string ContentTag;
            public Video Video;
            public Point Resolution;

            public PreloadedContent(string name, string tag, Video video, Point resolution)
            {
                ContentName = name;
                ContentTag = tag;
                Video = video;
                Resolution = resolution;
            }
        }
        
        public struct TextSettings
        {
            public string Text;
            public int Width;

            public TextSettings(XElement element, params object[] args)
            {
                Text = TextManager.GetFormatted(element.GetAttributeString("tag", string.Empty), true, args);
                Width = element.GetAttributeInt("width", 300);
            }
        }

        public struct VideoSettings
        {
            public int Width;
            public int Height;

            public VideoSettings(XElement element)
            {
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

            textFrame = new GUIFrame(new RectTransform(new Point(width + borderSize, height + borderSize), background.RectTransform, Anchor.Center, Pivot.CenterLeft), "SonarFrame");
            textFrame.RectTransform.AbsoluteOffset = new Point(borderSize, 0);

            videoView = new GUICustomComponent(new RectTransform(new Point(width, height), videoFrame.RectTransform, Anchor.Center),
            (spriteBatch, guiCustomComponent) => { DrawVideo(spriteBatch, guiCustomComponent.Rect); });
            title = new GUITextBlock(new RectTransform(Point.Zero, textFrame.RectTransform, Anchor.TopCenter, Pivot.TopLeft) { AbsoluteOffset = new Point(-225, 10) }, string.Empty, font: GUI.VideoTitleFont, textColor: new Color(253, 174, 0), textAlignment: Alignment.Left);

            textContent = new GUITextBlock(new RectTransform(new Vector2(1f, .8f), textFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter) { AbsoluteOffset = new Point(0, borderSize / 2 + titleHeight) }, string.Empty, font: GUI.Font, textAlignment: Alignment.TopLeft);

            objectiveTitle = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), textFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter), string.Empty, font: GUI.ObjectiveTitleFont, textAlignment: Alignment.CenterRight, textColor: Color.White);
            objectiveTitle.Text = TextManager.Get("NewObjective");
            objectiveText = new GUITextBlock(new RectTransform(new Point(textFrame.Rect.Width, textHeight), textFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter), string.Empty, font: GUI.ObjectiveNameFont, textColor: new Color(4, 180, 108), textAlignment: Alignment.CenterRight);

            preloadedVideos = new List<PreloadedContent>();
        }

        public void PreloadContent(string contentPath, string contentTag, string contentId, VideoSettings videoSettings)
        {
            if (preloadedVideos.Find(s => s.ContentName == contentId) != null) return; // Already loaded
            Point resolution = new Point(videoSettings.Width, videoSettings.Height);

            if (resolution.X == 0 || resolution.Y == 0)
            {
                resolution = defaultResolution;
            }

            preloadedVideos.Add(new PreloadedContent(contentId, contentTag, CreateVideo(contentPath, resolution), resolution));
        }

        public void RemoveAllPreloaded()
        {
            if (preloadedVideos == null || preloadedVideos.Count == 0) return;

            for (int i = 0; i < preloadedVideos.Count; i++)
            {
                preloadedVideos[i] = null;
            }

            preloadedVideos.Clear();
        }

        public void RemovePreloadedByTag(string tag)
        {
            if (preloadedVideos == null || preloadedVideos.Count == 0) return;

            for (int i = 0; i < preloadedVideos.Count; i++)
            {
                if (preloadedVideos[i].ContentTag != tag) continue;
                preloadedVideos[i] = null;
                preloadedVideos.RemoveAt(i);
                i--;
            }
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

        public void AddToGUIUpdateList()
        {
            if (!IsPlaying()) return;
            background.AddToGUIUpdateList();
        }

        public void LoadContentWithObjective(string contentPath, VideoSettings videoSettings, TextSettings textSettings, string contentId, bool startPlayback, string objective, Action callback = null)
        {
            callbackOnStop = callback;

            if (!File.Exists(contentPath))
            {
                DebugConsole.ThrowError("No video found at: " + contentPath);
                DisposeVideo(null, null);
                return;
            }

            ResetFrameSizes();

            if (currentVideo != null)
            {
                currentVideo.Dispose();
                currentVideo = null;
            }

            PreloadedContent preloaded = null;
            Point resolution = new Point(0, 0);

            if (preloadedVideos != null && preloadedVideos.Count > 0)
            {
                preloaded = preloadedVideos.Find(s => s.ContentName == contentId);

                if (preloaded != null)
                {
                    currentVideo = preloaded.Video;
                    resolution = preloaded.Resolution;
                }
            }

            if (currentVideo == null) // No preloaded video found
            {
                resolution = new Point(videoSettings.Width, videoSettings.Height);

                if (resolution.X == 0 || resolution.Y == 0)
                {
                    resolution = defaultResolution;
                }

                currentVideo = CreateVideo(contentPath, resolution);
            }

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

            objectiveTitle.RectTransform.AbsoluteOffset = new Point(0, textContent.RectTransform.Rect.Height + (int)(textHeight * 1.5f));
            objectiveText.RectTransform.AbsoluteOffset = new Point(0, textContent.RectTransform.Rect.Height + objectiveTitle.Rect.Height + textHeight * 2);

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

            if (!File.Exists(contentPath))
            {
                DebugConsole.ThrowError("No video found at: " + contentPath);
                DisposeVideo(null, null);
                return;
            }

            ResetFrameSizes();

            if (currentVideo != null)
            {
                currentVideo.Dispose();
                currentVideo = null;
            }

            PreloadedContent preloaded = null;
            Point resolution = new Point(0, 0);

            if (preloadedVideos != null && preloadedVideos.Count > 0)
            {
                preloaded = preloadedVideos.Find(s => s.ContentName == contentId);

                if (preloaded != null)
                {
                    currentVideo = preloaded.Video;
                    resolution = preloaded.Resolution;
                }
            }

            if (currentVideo == null) // No preloaded video found
            {
                resolution = new Point(videoSettings.Width, videoSettings.Height);

                if (resolution.X == 0 || resolution.Y == 0)
                {
                    resolution = defaultResolution;
                }

                currentVideo = CreateVideo(contentPath, resolution);
            }

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

        private Video CreateVideo(string contentPath, Point resolution)
        {
            Video video = null;

            try
            {
                //video = new Video(GameMain.Instance.GraphicsDevice, GameMain.SoundManager, "Content/splashscreen.mp4", 1280, 720);
                video = new Video(GameMain.Instance.GraphicsDevice, GameMain.SoundManager, contentPath, (uint)resolution.X, (uint)resolution.Y);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error loading video content " + contentPath + "!", e);
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

            RemoveAllPreloaded();
        }
    }
}
