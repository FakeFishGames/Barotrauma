using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;
using System.Collections.Generic;

namespace Barotrauma
{
    class SpriteSheetPlayer
    {
        private SpriteSheet[] playableSheets;
        private SpriteSheet currentSheet;

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

        public SpriteSheetPlayer()
        {
            int width = (int)defaultResolution.X;
            int height = (int)defaultResolution.Y;

            background = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), GUI.Canvas, Anchor.Center), "InnerFrame", backgroundColor);
            videoFrame = new GUIFrame(new RectTransform(new Point(width + borderSize, height + borderSize), background.RectTransform, Anchor.Center), "SonarFrame");
            sheetView = new GUICustomComponent(new RectTransform(new Point(width, height), videoFrame.RectTransform, Anchor.Center),
            (spriteBatch, guiCustomComponent) => { DrawSheetView(spriteBatch, guiCustomComponent.Rect); }, UpdateSheetView);
            title = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), videoFrame.RectTransform, Anchor.TopCenter, Pivot.BottomCenter), string.Empty, font: GUI.LargeFont, textAlignment: Alignment.Center);
        }

        public void Play()
        {
            isPlaying = true;
        }

        public void Stop()
        {
            isPlaying = false;
        }

        public void AddToGUIUpdateList()
        {
            if (!isPlaying) return;
            background.AddToGUIUpdateList();
        }

        public void SetContent(string contentPath, XElement videoElement, string titleTag, bool startPlayback)
        {
            totalElapsed = loopTimer = 0.0f;
            animationSpeed = videoElement.GetAttributeFloat("animationspeed", 0.1f);
            loopDelay = videoElement.GetAttributeFloat("loopdelay", 0.0f); ;

            CreateSpriteSheets(contentPath, videoElement);
            currentSheet = playableSheets[0];

            Point resolution = currentSheet.FrameSize;

            videoFrame.RectTransform.NonScaledSize = resolution + new Point(borderSize, borderSize);
            sheetView.RectTransform.NonScaledSize = resolution;

            title.Text = TextManager.Get(titleTag);
            title.RectTransform.NonScaledSize = new Point(resolution.X, 30);

            if (startPlayback) Play();
        }

        private void CreateSpriteSheets(string contentPath, XElement videoElement)
        {
            if (playableSheets != null)
            {
                foreach (SpriteSheet existingSheet in playableSheets)
                {
                    existingSheet.Remove();
                }
                playableSheets = null;
            }

            try
            {
                List<XElement> sheetElements = new List<XElement>();

                foreach (var sheetElement in videoElement.Elements("Sheet"))
                {
                    sheetElements.Add(sheetElement);
                }

                playableSheets = new SpriteSheet[sheetElements.Count];

                for (int i = 0; i < sheetElements.Count; i++)
                {
                    playableSheets[i] = new SpriteSheet(sheetElements[i], contentPath, sheetElements[i].GetAttributeString("path", ""), sheetElements[i].GetAttributeInt("empty", 0));
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error loading sprite sheet content " + contentPath + "!", e);
            }
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
                    currentSheet = playableSheets[currentSheetIndex];
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

                    if (currentSheetIndex > playableSheets.Length - 1)
                    {
                        if (loopDelay > 0.0f)
                        {
                            loopTimer = loopDelay;
                            return;
                        }

                        currentSheetIndex = 0;
                    }

                    currentFrameIndex = 0;
                    currentSheet = playableSheets[currentSheetIndex];
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
            if (playableSheets != null)
            {
                foreach (SpriteSheet existingSheet in playableSheets)
                {
                    existingSheet.Remove();
                }
                playableSheets = null;
            }
        }
    }
}
