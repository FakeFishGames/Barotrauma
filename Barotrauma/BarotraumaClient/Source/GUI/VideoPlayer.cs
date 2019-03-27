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

        private GUIFrame background, videoFrame;
        private GUITextBlock title;
        private GUICustomComponent videoView;

        private Color backgroundColor = new Color(0f, 0f, 0f, 1f);
        private Action callbackOnStop;

        private bool isPlaying;

        public bool IsPlaying()
        {
            return isPlaying;
            /*if (currentVideo == null) return false;
            return currentVideo.IsPlaying;*/
        }

        private readonly Point defaultResolution = new Point(520, 300);
        private readonly int borderSize = 20;

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

        public VideoPlayer()
        {
            int width = defaultResolution.X;
            int height = defaultResolution.Y;

            background = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), GUI.Canvas, Anchor.Center), "InnerFrame", backgroundColor);
            videoFrame = new GUIFrame(new RectTransform(new Point(width + borderSize, height + borderSize), background.RectTransform, Anchor.Center), "SonarFrame");
            videoView = new GUICustomComponent(new RectTransform(new Point(width, height), videoFrame.RectTransform, Anchor.Center),
            (spriteBatch, guiCustomComponent) => { DrawVideo(spriteBatch, guiCustomComponent.Rect); });
            title = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), videoFrame.RectTransform, Anchor.TopCenter, Pivot.BottomCenter), string.Empty, font: GUI.LargeFont, textAlignment: Alignment.Center);

            preloadedVideos = new List<PreloadedContent>();
        }

        public void PreloadContent(string contentPath, string contentTag, string contentId, XElement contentElement)
        {
            if (preloadedVideos.Find(s => s.ContentName == contentId) != null) return; // Already loaded
            Point resolution = new Point(contentElement.GetAttributeInt("width", 0), contentElement.GetAttributeInt("height", 0));

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

        public void LoadContent(string contentPath, XElement videoElement, string contentId, bool startPlayback, bool hasButton, Action callback = null)
        {
            callbackOnStop = callback;

            if (!File.Exists(contentPath))
            {
                DebugConsole.ThrowError("No video found at: " + contentPath);
                DisposeVideo(null, null);
                return;
            }

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

            if (currentVideo == null) // No preloaded sheets found, create sheets
            {
                resolution = new Point(videoElement.GetAttributeInt("width", 0), videoElement.GetAttributeInt("height", 0));

                if (resolution.X == 0 || resolution.Y == 0)
                {
                    resolution = defaultResolution;
                }

                currentVideo = CreateVideo(contentPath, resolution);
            }

            videoFrame.RectTransform.NonScaledSize = resolution + new Point(borderSize, borderSize);
            videoView.RectTransform.NonScaledSize = resolution;

            title.Text = TextManager.Get(contentId);
            title.RectTransform.NonScaledSize = new Point(resolution.X, 30);

            if (hasButton)
            {
                var okButton = new GUIButton(new RectTransform(new Point(160, 50), videoFrame.RectTransform, Anchor.BottomCenter, Pivot.TopCenter) { AbsoluteOffset = new Point(0, -10) },
                    TextManager.Get("OK"))
                {
                    OnClicked = DisposeVideo
                };
            }

            if (startPlayback) Play();
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
