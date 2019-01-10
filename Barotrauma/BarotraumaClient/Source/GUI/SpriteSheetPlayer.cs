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
            (spriteBatch, guiCustomComponent) => { DrawSheetView(spriteBatch, guiCustomComponent.Rect); }, UpdateSheetView);
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
            sheetView.AddToGUIUpdateList();
        }

        public void SetContent(string contentPath, XElement videoElement, bool startPlayback)
        {
            totalElapsed = 0.0f;
            animationSpeed = videoElement.GetAttributeFloat("animationspeed", 0.1f);

            CreateSpriteSheets(contentPath, videoElement);
            currentSheet = playableSheets[0];
            sheetView.RectTransform.RelativeSize = currentSheet.FrameSize.ToVector2();

            if (startPlayback) Play();
        }

        private void CreateSpriteSheets(string contentPath, XElement videoElement)
        {
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
                    playableSheets[i] = new SpriteSheet(sheetElements[i], contentPath, sheetElements[i].GetAttributeString("path", ""));
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

        private void DrawSheetView(SpriteBatch spriteBatch, Rectangle rect)
        {
            if (!isPlaying) return;
            currentSheet.Draw(spriteBatch, currentFrameIndex, rect.Center.ToVector2(), Color.White, currentSheet.Origin, 0f, Vector2.One);
        }
    }
}
