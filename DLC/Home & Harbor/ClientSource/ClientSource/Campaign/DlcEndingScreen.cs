using Barotrauma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Media;

namespace HomeAndHarbor;

public class DlcEndingScreen : Screen
{
    private readonly CreditsPlayer creditsPlayer;

    private readonly Camera cam;

    public Action? OnFinished;

    protected SlideshowPlayer? slideshowPlayer;

    private SlideshowPrefab slideshowPrefab;

    /// <summary>
    /// The video starts playing as soon as it's loaded, so we only load it once the client has finished ending the round
    /// (otherwise unloading the round in the middle of the video causes it to lag).
    /// </summary>
    private Video? video;
    private bool videoStarted;

    /// <summary>
    /// How long to wait for the server to end the round before starting the video anyway.
    /// </summary>
    private const float MaxWaitForRoundEnd = 15f;
    private float waitForRoundEndTimer;

    private bool IsVideoFinished => videoStarted && (video == null || !video.IsPlaying);

    public DlcEndingScreen(SlideshowPrefab slideshowPrefab)
    {
        this.slideshowPrefab = slideshowPrefab;

        creditsPlayer = new CreditsPlayer(new RectTransform(Vector2.One, Frame.RectTransform), "Content/Texts/Credits.xml")
        {
            AutoRestart = false,
            ScrollBarEnabled = false,
            AllowMouseWheelScroll = false
        };
        creditsPlayer.CloseButton.OnClicked = (btn, userdata) =>
        {
            creditsPlayer.Scroll = 1.0f;
            return true;
        };

        cam = new Camera();
    }

    public override void Select()
    {
        base.Select();
        GUI.DisableHUD = false;
        slideshowPlayer = new SlideshowPlayer(GUICanvas.Instance, slideshowPrefab);
        creditsPlayer.Restart();
        creditsPlayer.Visible = false;

        videoStarted = false;
        waitForRoundEndTimer = 0.0f;
    }

    public override void Deselect()
    {
        GUI.HideCursor = false;
        SoundPlayer.OverrideMusicType = Identifier.Empty;
        SoundPlayer.OverrideMusicDuration = null;
        video?.Dispose();
        video = null;
    }

    private void StartVideo()
    {
        videoStarted = true;

        string videoPath = Path.Combine(ContentPackageRetrieval.ContentPackage.Dir, "Content/Video/DLC_end_video.webm");
        video = Video.Load(GameMain.Instance.GraphicsDevice, GameMain.SoundManager, videoPath);
        if (video == null)
        {
            DebugConsole.ThrowError($"Failed to load the DLC ending video \"{videoPath}\".");
        }
    }

    public override void Update(double deltaTime)
    {
        if (!videoStarted)
        {
            // In multiplayer the server ends the round after the ending has been triggered,
            // wait for the client to finish ending and unloading the round before starting the video
            waitForRoundEndTimer += (float)deltaTime;
            if (GameMain.Client is not { GameStarted: true } || waitForRoundEndTimer > MaxWaitForRoundEnd)
            {
                StartVideo();
            }
            return;
        }

        if (IsVideoFinished)
        {
            slideshowPlayer?.UpdateManually((float)deltaTime);
            if (creditsPlayer.Finished)
            {
                OnFinished?.Invoke();
                SoundPlayer.OverrideMusicType = Identifier.Empty;
            }
        }
    }

    public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
    {
        if (!videoStarted)
        {
            graphics.Clear(Color.Black);
        }
        else if (video != null && video.IsPlaying)
        {
            graphics.Clear(Color.Black);
            float videoAspectRatio = (float)video.Width / (float)video.Height;
            int width; int height;
            if (GameMain.GraphicsHeight * videoAspectRatio > GameMain.GraphicsWidth)
            {
                width = GameMain.GraphicsWidth;
                height = (int)(GameMain.GraphicsWidth / videoAspectRatio);
            }
            else
            {
                width = (int)(GameMain.GraphicsHeight * videoAspectRatio);
                height = GameMain.GraphicsHeight;
            }

            spriteBatch.Begin();
            spriteBatch.Draw(
                video.GetTexture(),
                destinationRectangle: new Rectangle(
                    GameMain.GraphicsWidth / 2 - width / 2,
                    GameMain.GraphicsHeight / 2 - height / 2,
                    width,
                    height),
                sourceRectangle: new Rectangle(0, 0, video.Width, video.Height),
                Color.White,
                rotation: 0.0f,
                origin: Vector2.Zero,
                SpriteEffects.None,
                layerDepth: 0.0f);
            spriteBatch.End();
        }
        else
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);
            graphics.Clear(Color.Black);
            if (slideshowPlayer != null && !slideshowPlayer.Finished)
            {
                slideshowPlayer.DrawManually(spriteBatch);
            }
            else
            {
                GUI.HideCursor = false;
                creditsPlayer.Visible = true;
            }
            GUI.Draw(cam, spriteBatch);
            spriteBatch.End();
        }
    }
}