#nullable enable
using System;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public class GUIColorPicker : GUIComponent, IDisposable
    {
        public delegate bool OnColorSelectedHandler(GUIColorPicker component, Color color);
        public OnColorSelectedHandler? OnColorSelected;

        public float SelectedHue;
        public float SelectedSaturation;
        public float SelectedValue;

        public Color CurrentColor = Color.Black;

        private Rectangle MainArea,
                          HueArea;

        private Texture2D? mainTexture,
                           hueTexture;

        private Color[]? colorData;

        private Rectangle selectedRect;

        private bool mouseHeld;
        private bool isInitialized;

        private readonly Color transparentWhite = Color.White * 0.8f,
                               transparentBlack = Color.Black * 0.8f;

        public GUIColorPicker(RectTransform rectT, string? style = null) : base(style, rectT) { }

        private void Init()
        {
            int tWidth = Rect.Width;
            int sliceWidth = Rect.Width / 8;

            int mainWidth = tWidth - sliceWidth;
            int hueWidth = sliceWidth;

            MainArea = new Rectangle(0, 0, mainWidth, Rect.Height);
            HueArea = new Rectangle(mainWidth, 0, hueWidth, Rect.Height);

            colorData = new Color[MainArea.Width * MainArea.Height];
            
            if (mainTexture == null)
            {
                int width = MainArea.Width,
                    height = MainArea.Height;

                GenerateGradient(ref colorData!, width, height, DrawHVArea);
                mainTexture = CreateGradientTexture(colorData!, MainArea.Width, MainArea.Height);
            }

            if (hueTexture == null)
            {
                int width = HueArea.Width,
                    height = HueArea.Height;

                Color[] hueData = new Color[width * height];

                GenerateGradient(ref hueData, width, height, DrawHueArea);
                hueTexture = CreateGradientTexture(hueData, width, height);
            }
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (mainTexture == null || hueTexture == null || !isInitialized) { return; }

            Rectangle mainArea = MainArea,
                      hueArea = HueArea;

            hueArea.Location += Rect.Location;
            mainArea.Location += Rect.Location;

            Vector2 mainLocation = mainArea.Location.ToVector2(),
                    hueLocation = hueArea.Location.ToVector2();

            spriteBatch.Draw(mainTexture, mainLocation, Color.White);
            spriteBatch.Draw(hueTexture, hueLocation, Color.White);

            float hueY = hueLocation.Y + ((SelectedHue / 360f) * hueArea.Height);
            spriteBatch.DrawLine(hueArea.Left, hueY, hueArea.Right, hueY, transparentWhite, thickness: 3);
            spriteBatch.DrawLine(hueArea.Left, hueY, hueArea.Right, hueY, transparentBlack, thickness: 1);

            float saturationX = mainLocation.X + SelectedSaturation * MainArea.Width;
            float valueY = mainLocation.Y + (1.0f - SelectedValue) * MainArea.Height;

            spriteBatch.DrawLine(saturationX, mainArea.Top,saturationX, mainArea.Bottom, transparentWhite, thickness: 3);
            spriteBatch.DrawLine(mainArea.Left,valueY,  mainArea.Right, valueY, transparentWhite, thickness: 3);

            spriteBatch.DrawLine(saturationX, mainArea.Top,saturationX, mainArea.Bottom, transparentBlack, thickness: 1);
            spriteBatch.DrawLine(mainArea.Left,valueY,  mainArea.Right, valueY, transparentBlack, thickness: 1);
        }

        protected override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!isInitialized)
            {
                Init();
                isInitialized = true;
            }
            
            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                mouseHeld = false;
            }

            if (GUI.MouseOn != this) { return; }

            Rectangle mainArea = MainArea,
                      hueArea = HueArea;

            hueArea.Location += Rect.Location;
            mainArea.Location += Rect.Location;

            if (PlayerInput.PrimaryMouseButtonDown())
            {
                mouseHeld = true;
                if (hueArea.Contains(PlayerInput.MousePosition))
                {
                    selectedRect = HueArea;
                } 
                else if (mainArea.Contains(PlayerInput.MousePosition))
                {
                    selectedRect = MainArea;
                }
                else
                {
                    mouseHeld = false;
                }
            }

            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                mouseHeld = false;
            }

            if (mouseHeld && (PlayerInput.MouseSpeed != Vector2.Zero || PlayerInput.PrimaryMouseButtonDown()))
            {
                if (selectedRect == HueArea)
                {
                    Vector2 pos = PlayerInput.MousePosition - hueArea.Location.ToVector2();
                    SelectedHue = Math.Clamp(pos.Y / hueArea.Height * 360f, 0, 360);
                    RefreshHue();

                } 
                else if (selectedRect == MainArea)
                {
                    var (x, y) = PlayerInput.MousePosition - mainArea.Location.ToVector2();
                    SelectedSaturation = Math.Clamp(x / mainArea.Width, 0, 1);
                    SelectedValue = Math.Clamp(1f - (y / mainArea.Height), 0, 1);
                }

                CurrentColor = ToolBox.HSVToRGB(SelectedHue, SelectedSaturation, SelectedValue);

                OnColorSelected?.Invoke(this, CurrentColor);
            }
        }

        public void Dispose()
        {
            mainTexture?.Dispose();
            mainTexture = null;
            hueTexture?.Dispose();
            hueTexture = null;
        }

        public void RefreshHue()
        {
            if (colorData == null || mainTexture == null) { return; }
            GenerateGradient(ref colorData, mainTexture.Width, mainTexture.Height, DrawHVArea);
            mainTexture.SetData(colorData);
        }

        private Texture2D CreateGradientTexture(Color[] data, int width, int height)
        {
            Texture2D texture = new Texture2D(GameMain.GraphicsDeviceManager.GraphicsDevice, width, height);
            texture.SetData(data);
            return texture;
        }

        private void GenerateGradient(ref Color[] data, int width, int height, Func<float, float, Color> algorithm)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float relativeX = x / (float) width,
                          relativeY = y / (float) height;

                    data[y * width + x] = algorithm(relativeX, relativeY);
                }
            }
        }

        private Color DrawHVArea(float x, float y) => ToolBox.HSVToRGB(SelectedHue, x, 1.0f - y);
        private Color DrawHueArea(float x, float y) => ToolBox.HSVToRGB(y * 360f, 1f, 1f);
    }
}