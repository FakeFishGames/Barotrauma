using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;
using System.Collections.Generic;

namespace Barotrauma
{
    class SpriteSheetPlayer
    {
        private SpriteSheet[] playingSheets;
        private SpriteSheet currentSheet;
        private List<PreloadedContent> preloadedSheets;

        private GUIFrame background, videoFrame;
        private GUITextBlock title;
        private GUICustomComponent sheetView;

        private float totalElapsed = 0;
        private float animationSpeed = 0.1f;
        private float loopTimer = 0.0f;
        private float loopDelay = 0.0f;

        private int currentSheetIndex = 0;
        private int currentFrameIndex = 0;

        private Color backgroundColor = new Color(0f, 0f, 0f, 1f);
        private Action callbackOnStop;

        private bool isPlaying;
        public bool IsPlaying
        {
            get { return isPlaying; }
            private set
            {
                if (isPlaying == value) return;
                isPlaying = value;
            }
        }

        private readonly Vector2 defaultResolution = new Vector2(520, 300);
        private readonly int borderSize = 20;

        private class PreloadedContent
        {
            public string ContentName;
            public string ContentTag;
            public SpriteSheet[] Sheets;

            public PreloadedContent(string name, string tag, SpriteSheet[] sheets)
            {
                ContentName = name;
                ContentTag = tag;
                Sheets = sheets;
            }
        }

        public SpriteSheetPlayer()
        {
            int width = (int)defaultResolution.X;
            int height = (int)defaultResolution.Y;

            background = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), GUI.Canvas, Anchor.Center), "InnerFrame", backgroundColor);
            videoFrame = new GUIFrame(new RectTransform(new Point(width + borderSize, height + borderSize), background.RectTransform, Anchor.Center), "SonarFrame");
            sheetView = new GUICustomComponent(new RectTransform(new Point(width, height), videoFrame.RectTransform, Anchor.Center),
            (spriteBatch, guiCustomComponent) => { DrawSheetView(spriteBatch, guiCustomComponent.Rect); }, UpdateSheetView);
            title = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), videoFrame.RectTransform, Anchor.TopCenter, Pivot.BottomCenter), string.Empty, font: GUI.LargeFont, textAlignment: Alignment.Center);

            preloadedSheets = new List<PreloadedContent>();
        }

        public void PreloadContent(string contentPath, string contentTag, string contentId, XElement contentElement)
        {
            if (preloadedSheets.Find(s => s.ContentName == contentId) != null) return; // Already loaded
            preloadedSheets.Add(new PreloadedContent(contentId, contentTag, CreateSpriteSheets(contentPath, contentElement)));
        }

        public void RemoveAllPreloaded()
        {
            if (preloadedSheets == null || preloadedSheets.Count == 0) return;

            for (int i = 0; i < preloadedSheets.Count; i++)
            {
                for (int j = 0; j < preloadedSheets[i].Sheets.Length; j++)
                {
                    preloadedSheets[i].Sheets[j].Remove();
                }
            }

            preloadedSheets.Clear();
        }

        public void RemovePreloadedByTag(string tag)
        {
            if (preloadedSheets == null || preloadedSheets.Count == 0) return;

            for (int i = 0; i < preloadedSheets.Count; i++)
            {
                if (preloadedSheets[i].ContentTag != tag) continue;
                for (int j = 0; j < preloadedSheets[i].Sheets.Length; j++)
                {
                    preloadedSheets[i].Sheets[j].Remove();
                }

                preloadedSheets[i] = null;
                preloadedSheets.RemoveAt(i);
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
        }

        private bool OKButtonClicked(GUIButton button, object userData)
        {
            Stop();
            callbackOnStop?.Invoke();
            return true;
        }

        public void AddToGUIUpdateList()
        {
            if (!isPlaying) return;
            background.AddToGUIUpdateList();
        }

        public void LoadContent(string contentPath, XElement videoElement, string contentId, bool startPlayback, bool hasButton, Action callback = null)
        {
            totalElapsed = loopTimer = 0.0f;
            animationSpeed = videoElement.GetAttributeFloat("animationspeed", 0.1f);
            loopDelay = videoElement.GetAttributeFloat("loopdelay", 0.0f);

            if (playingSheets != null)
            {
                foreach (SpriteSheet existingSheet in playingSheets)
                {
                    existingSheet.Remove();
                }
                playingSheets = null;
            }

            playingSheets = preloadedSheets.Find(s => s.ContentName == contentId).Sheets;

            if (playingSheets == null) // No preloaded sheets found, create sheets
            {
                playingSheets = CreateSpriteSheets(contentPath, videoElement);
            }

            currentSheet = playingSheets[0];

            Point resolution = currentSheet.FrameSize;

            videoFrame.RectTransform.NonScaledSize = resolution + new Point(borderSize, borderSize);
            sheetView.RectTransform.NonScaledSize = resolution;

            title.Text = TextManager.Get(contentId);
            title.RectTransform.NonScaledSize = new Point(resolution.X, 30);

            callbackOnStop = callback;

            if (hasButton)
            {
                var okButton = new GUIButton(new RectTransform(new Point(160, 50), videoFrame.RectTransform, Anchor.BottomCenter, Pivot.TopCenter) { AbsoluteOffset = new Point(0, -10) },
                    TextManager.Get("OK"))
                {
                    OnClicked = OKButtonClicked
                };
            }

            if (startPlayback) Play();
        }

        private SpriteSheet[] CreateSpriteSheets(string contentPath, XElement videoElement)
        {
            SpriteSheet[] sheets = null;

            try
            {
                List<XElement> sheetElements = new List<XElement>();

                foreach (var sheetElement in videoElement.Elements("Sheet"))
                {
                    sheetElements.Add(sheetElement);
                }

                sheets = new SpriteSheet[sheetElements.Count];

                for (int i = 0; i < sheetElements.Count; i++)
                {
                    sheets[i] = new SpriteSheet(sheetElements[i], contentPath, sheetElements[i].GetAttributeString("path", ""), sheetElements[i].GetAttributeInt("empty", 0));
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error loading sprite sheet content " + contentPath + "!", e);
            }

            return sheets;
        }

        private void UpdateSheetView(float deltaTime, GUICustomComponent viewContainer)
        {
            if (!isPlaying) return;
            if (loopTimer > 0.0f)
            {
                loopTimer -= deltaTime;

                if (loopTimer <= 0.0f)
                {
                    currentSheetIndex = 0;
                    currentFrameIndex = 0;
                    currentSheet = playingSheets[currentSheetIndex];
                }
                else
                {
                    return;
                }
            }

            totalElapsed += deltaTime;
            if (totalElapsed > animationSpeed)
            {
                totalElapsed -= animationSpeed;
                currentFrameIndex++;

                if (currentFrameIndex > currentSheet.FrameCount - 1)
                {
                    currentSheetIndex++;

                    if (currentSheetIndex > playingSheets.Length - 1)
                    {
                        if (loopDelay > 0.0f)
                        {
                            loopTimer = loopDelay;
                            return;
                        }

                        currentSheetIndex = 0;
                    }

                    currentFrameIndex = 0;
                    currentSheet = playingSheets[currentSheetIndex];
                }
            }
        }

        private void DrawSheetView(SpriteBatch spriteBatch, Rectangle rect)
        {
            if (!isPlaying) return;
            currentSheet.Draw(spriteBatch, currentFrameIndex, rect.Center.ToVector2(), Color.White, currentSheet.Origin, 0f, Vector2.One);
        }

        public void Remove()
        {
            if (playingSheets != null)
            {
                foreach (SpriteSheet existingSheet in playingSheets)
                {
                    existingSheet.Remove();
                }
                playingSheets = null;
            }

            RemoveAllPreloaded();
        }
    }
}
