using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;

namespace Barotrauma.Source.GUI
{
    class SpriteSheetPlayer
    {
        private SpriteSheet[] playableSheets;
        private SpriteSheet currentSheet;

        private GUICustomComponent sheetView;
        private float totalElapsed = 0;
        private float animationSpeed = 0.1f;

        private int currentSheetIndex = 0;
        private int currentFrameIndex = 0;

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

        public SpriteSheetPlayer()
        {
            sheetView = new GUICustomComponent(new RectTransform(defaultResolution, null, Anchor.Center),
            (spriteBatch, guiCustomComponent) => { DrawTutorialView(spriteBatch, guiCustomComponent.Rect); }, UpdateTutorialView);
        }

        public void Play()
        {
            isPlaying = true;
        }

        public void Stop()
        {
            isPlaying = false;
        }

        public void SetContent(string contentPath, XElement videoElement, bool startPlayback)
        {
            animationSpeed = videoElement.GetAttributeFloat("animationspeed", 0.1f);

            playableSheets = GetSheets(contentPath, videoElement);
            currentSheet = playableSheets[0];
            sheetView.RectTransform.RelativeSize = currentSheet.FrameSize.ToVector2();

            isPlaying = startPlayback;
        }

        private SpriteSheet[] GetSheets(string contentPath, XElement videoElement)
        {
            SpriteSheet[] sheets = null;
            try
            {
                XElement[] sheetElements = videoElement.Elements("Sheet") as XElement[];
                sheets = new SpriteSheet[sheetElements.Length];

                for (int i = 0; i < sheetElements.Length; i++)
                {
                    sheets[i] = new SpriteSheet(sheetElements[i], contentPath, sheetElements[i].GetAttributeString("path", ""));
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error loading sprite sheet content " + contentPath + "!", e);
            }

            return sheets;
        }

        private void UpdateTutorialView(float deltaTime, GUICustomComponent viewContainer)
        {
            if (!isPlaying || playableSheets == null || currentSheet == null) return;

            totalElapsed += deltaTime;
            if (totalElapsed > animationSpeed)
            {
                totalElapsed -= animationSpeed;
                currentFrameIndex++;

                if (currentFrameIndex >= currentSheet.FrameCount - 1)
                {
                    currentFrameIndex = 0;
                    currentSheetIndex++;

                    if (currentSheetIndex >= playableSheets.Length - 1)
                    {
                        currentSheetIndex = 0;
                    }

                    currentSheet = playableSheets[currentSheetIndex];
                }
            }
        }

        private void DrawTutorialView(SpriteBatch spriteBatch, Rectangle rect)
        {
            if (!isPlaying || playableSheets == null || currentSheet == null) return;
            currentSheet.Draw(spriteBatch, currentFrameIndex, rect.Center.ToVector2(), Color.White, currentSheet.Origin, 0f, Vector2.One);
        }
    }
}
