using Barotrauma.Media;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    class CampaignEndScreen : Screen
    {
        private Video video;

        private readonly CreditsPlayer creditsPlayer;

        private readonly Camera cam;

        public Action OnFinished;

        private string textOverlay;
        private float textOverlayTimer;
        private Vector2 textOverlaySize;

        public CampaignEndScreen()
        {
            creditsPlayer = new CreditsPlayer(new RectTransform(Vector2.One, Frame.RectTransform), "Content/Texts/Credits.xml")
            {
                AutoRestart = false,
                ScrollBarEnabled = false,
                AllowMouseWheelScroll = false
            };
            new GUIButton(new RectTransform(new Vector2(0.1f), creditsPlayer.RectTransform, Anchor.BottomRight, maxSize: new Point(300, 50)) { AbsoluteOffset = new Point(GUI.IntScale(20)) },
                TextManager.Get("close"))
            {
                OnClicked = (btn, userdata) =>
                {
                    creditsPlayer.Scroll = 1.0f;
                    return true;
                }
            };
            cam = new Camera();
        }

        public override void Select()
        {
            base.Select();

            textOverlay = ToolBox.WrapText(TextManager.Get("campaignend1"), GameMain.GraphicsWidth / 3, GUI.Font);
            textOverlaySize = GUI.Font.MeasureString(textOverlay);
            textOverlayTimer = 0.0f;

            video = Video.Load(GameMain.GraphicsDeviceManager.GraphicsDevice, GameMain.SoundManager, "Content/SplashScreens/Ending.webm");
            video.Play();
            creditsPlayer.Restart();
            creditsPlayer.Visible = false;
            SteamAchievementManager.UnlockAchievement("campaigncompleted", unlockClients: true);
        }

        public override void Deselect()
        {
            video?.Dispose();
            video = null;
            GUI.HideCursor = false;
            SoundPlayer.OverrideMusicType = null;
        }

        public override void Update(double deltaTime)
        {
            if (creditsPlayer.Finished)
            {
                OnFinished?.Invoke();
                SoundPlayer.OverrideMusicType = null;
            }
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin();
            graphics.Clear(Color.Black);
            if (video.IsPlaying)
            {
                GUI.HideCursor = !GUI.PauseMenuOpen;
                spriteBatch.Draw(video.GetTexture(), new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
            }
            else
            {
                SoundPlayer.OverrideMusicType = "ending";
                float duration = 20.0f;
                float creditsDelay = 3.0f;
                if (textOverlayTimer < duration + creditsDelay)
                {
                    float textAlpha;
                    float fadeInTime = 5.0f, fadeOutTime = 3.0f;
                    textOverlayTimer += (float)deltaTime;
                    if (textOverlayTimer < fadeInTime)
                    {
                        textAlpha = textOverlayTimer / fadeInTime;
                    }
                    else if (textOverlayTimer > duration - fadeOutTime)
                    {
                        textAlpha = Math.Min((duration - textOverlayTimer) / fadeOutTime, 1.0f);
                    }
                    else
                    {
                        textAlpha = 1.0f;
                    }
                    GUI.Font.DrawString(spriteBatch, textOverlay, new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) / 2 - textOverlaySize / 2, Color.White * textAlpha);
                }
                else
                {
                    GUI.HideCursor = false;
                    creditsPlayer.Visible = true;
                }
            }
            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);
            GUI.Draw(cam, spriteBatch);
            spriteBatch.End();
        }
    }
}
