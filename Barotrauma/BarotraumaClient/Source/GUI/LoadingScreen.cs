using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    //Enum for type of loading to do
    public enum LoadType
    {
        Mainmenu = 0,
        Singleplayer = 1,
        Client = 2,
        Server = 3,
        Other = 4
    }

    class LoadingScreen
    {
        
        private Texture2D backgroundTexture,monsterTexture,titleTexture;

        readonly RenderTarget2D renderTarget;

        float state;

        public Vector2 CenterPosition;

        public Vector2 TitlePosition;

        private float? loadState;
#if !LINUX
        Video splashScreenVideo;
        VideoPlayer videoPlayer;
#endif

        //Server Information
        public static LoadType loadType;
        public static string ServerName;
        public static string ServerPort;
        public static Boolean PublicServer;
        public static Boolean UPNP;
        public static string MaxPlayers;
        public static string Password;

        //Client Information
        public static string ClientName;
        public static string GameMode;
        public static string MissionType;
        public static string Submarine;
        public static Boolean IsTraitor;

        //Singleplayer Information
        public string SinglePlayerText;

        public Vector2 TitleSize
        {
            get { return new Vector2(titleTexture.Width, titleTexture.Height); }
        }

        public float Scale
        {
            get;
            private set;
        }

        public float? LoadState
        {
            get { return loadState; }        
            set 
            {
                loadState = value;
                if (GameSettings.VerboseLogging)
                {
                    DebugConsole.NewMessage("Loading: " + value.ToString() + "%", Color.Yellow);
                }
                DrawLoadingText = true;
            }
        }

        public bool DrawLoadingText
        {
            get;
            set;
        }

        public LoadingScreen(GraphicsDevice graphics)
        {
#if !LINUX

            if (GameMain.Config.EnableSplashScreen)
            {
                //NilMod Disable splash for server loading
                if (!GameMain.NilMod.StartToServer)
                {
                    try
                    {
                        splashScreenVideo = GameMain.Instance.Content.Load<Video>("utg_4");
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Failed to load splashscreen", e);
                        GameMain.Config.EnableSplashScreen = false;
                    }
                }
            }
#endif


            backgroundTexture = TextureLoader.FromFile("Content/UI/titleBackground.png");
            monsterTexture = TextureLoader.FromFile("Content/UI/titleMonster.png");
            titleTexture = TextureLoader.FromFile("Content/UI/titleText.png");

            renderTarget = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            DrawLoadingText = true;
        }

        
        public void Draw(SpriteBatch spriteBatch, GraphicsDevice graphics, float deltaTime)
        {
#if !LINUX
            if (GameMain.Config.EnableSplashScreen && splashScreenVideo != null)
            {
                try
                {
                    DrawSplashScreen(spriteBatch);
                    if (videoPlayer != null && videoPlayer.State == MediaState.Playing)
                        return;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Playing splash screen video failed", e);
                    GameMain.Config.EnableSplashScreen = false;
                }
            }
#endif
                        
            drawn = true;

            graphics.SetRenderTarget(renderTarget);

            Scale = GameMain.GraphicsHeight/1500.0f;

            state += deltaTime;

            if (DrawLoadingText)
            {
                CenterPosition = new Vector2(GameMain.GraphicsWidth*0.3f, GameMain.GraphicsHeight/2.0f); 
                TitlePosition = CenterPosition + new Vector2(-0.0f + (float)Math.Sqrt(state) * 220.0f, 0.0f) * Scale;
                TitlePosition.X = Math.Min(TitlePosition.X, (float)GameMain.GraphicsWidth / 2.0f);
            }

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            graphics.Clear(Color.Black);

            spriteBatch.Draw(backgroundTexture, CenterPosition, null, Color.White * Math.Min(state / 5.0f, 1.0f), 0.0f,
                new Vector2(backgroundTexture.Width / 2.0f, backgroundTexture.Height / 2.0f),
                Scale*1.5f, SpriteEffects.None, 0.2f);

            spriteBatch.Draw(monsterTexture,
                CenterPosition + new Vector2((state % 40) * 100.0f - 1800.0f, (state % 40) * 30.0f - 200.0f) * Scale, null,
                Color.White, 0.0f, Vector2.Zero, Scale, SpriteEffects.None, 0.1f);

            spriteBatch.Draw(titleTexture,
                TitlePosition, null,
                Color.White * Math.Min((state - 1.0f) / 5.0f, 1.0f), 0.0f, new Vector2(titleTexture.Width / 2.0f, titleTexture.Height / 2.0f), Scale, SpriteEffects.None, 0.0f);
            
            spriteBatch.End();

            graphics.SetRenderTarget(null);

            if (Hull.renderer != null)
            {
                Hull.renderer.ScrollWater(deltaTime);
                Hull.renderer.RenderBack(spriteBatch, renderTarget, 0.0f);
            }
            
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            
            spriteBatch.Draw(titleTexture,
                TitlePosition, null,
                Color.White * Math.Min((state - 3.0f) / 5.0f, 1.0f), 0.0f, new Vector2(titleTexture.Width / 2.0f, titleTexture.Height / 2.0f), Scale, SpriteEffects.None, 0.0f);

            if (DrawLoadingText)
            {
                string loadText = "";
                //Loading is finished.
                if (loadState == 100.0f)
                {
                    if (loadType == LoadType.Mainmenu)
                    {
                        loadText = "Press any key to enter the main menu." + Environment.NewLine + Environment.NewLine
                            + "Welcome to NilMod (" + NilMod.NilModVersionDate + ")!" + Environment.NewLine + Environment.NewLine
                            + "Use Barotrauma/Data/NilModSettings.xml to configure me!" + Environment.NewLine
                            + "Settings will be controlled by the server you connect to.";
                    }
                    else if (loadType == LoadType.Server)
                    {
                        loadText = "Initializing Nilmod Server Instance (" + NilMod.NilModVersionDate + ")" + Environment.NewLine
                        + "On: " + GameMain.NilMod.ExternalIP + ":" + ServerPort
                        + " UPNP: " + UPNP + Environment.NewLine
                        + "Public: " + PublicServer
                        + " MaxPlayers: " + MaxPlayers;
                    }
                    else if (loadType == LoadType.Client)
                    {
                        loadText = "Press any key to join in." + Environment.NewLine + Environment.NewLine
                            + "Using Nilmod Client : " + NilMod.NilModVersionDate + Environment.NewLine
                            + "With name: " + ClientName + (IsTraitor ? " As a TRAITOR!!" : "") + Environment.NewLine + Environment.NewLine
                            + "GameMode: " + GameMode + " Mission Type: " + MissionType + Environment.NewLine
                            + "On Submarine: " + Submarine;
    }
                    else if (loadType == LoadType.Singleplayer)
                    {
                        loadText = "Press any key to begin your adventure." + Environment.NewLine
                            + "" + Environment.NewLine
                            + "";
                    }
                    else if (loadType == LoadType.Other)
                    {
                        loadText = "" + Environment.NewLine
                            + "" + Environment.NewLine
                            + "";
                    }
                }
                else
                {
                    if (loadType == LoadType.Server)
                    {
                        loadText = "Starting server: " + ServerName + " ..." + Environment.NewLine
                        + " Nilmod Server (" + NilMod.NilModVersionDate + ")" + Environment.NewLine
                        + "On: " + GameMain.NilMod.ExternalIP + ":" + ServerPort
                        + " UPNP: " + UPNP + Environment.NewLine
                        + "Public: " + PublicServer
                        + " MaxPlayers: " + MaxPlayers;


                        if (Password != "")
                        {
                            loadText += " Pass: " + GameMain.NilMod.ServerPassword;
                        }

                        if (loadState != null)
                        {
                            loadText += Environment.NewLine + "        " + (int)loadState + " % Loading Complete";
                        }
                    }
                    else if(loadType == LoadType.Client)
                    {
                        loadText = "Loading: ...";

                        if (loadState != null)
                        {
                            loadText += (int)loadState + " % Ready";
                        }
                        loadText += Environment.NewLine +"Using Nilmod Client : " + NilMod.NilModVersionDate + Environment.NewLine
                            + "With name: " + ClientName + (IsTraitor ? " As a TRAITOR!!" : "") + Environment.NewLine
                            + "GameMode: " + GameMode + " Mission Type: " + MissionType + Environment.NewLine
                            + "On Submarine: " + Submarine;
                    }
                    else if (loadType == LoadType.Singleplayer)
                    {
                        loadText = "Loading Nilmod Singleplayer Instance: ...";

                        if (loadState != null)
                        {
                            loadText += (int)loadState + " % Ready";
                        }
                        loadText += Environment.NewLine + "" + Environment.NewLine
                            + "";
                    }
                    else if(loadType == LoadType.Mainmenu)
                    {
                        loadText = "Loading Game Files: ...";

                        if (loadState != null)
                        {
                            loadText += (int)loadState + " % Ready";
                        }
                        loadText += "Please wait for this to finish" + Environment.NewLine
                            + Environment.NewLine + Environment.NewLine
                            + "Welcome to NilMod (" + NilMod.NilModVersionDate + ")!" + Environment.NewLine + Environment.NewLine
                            + "Use Barotrauma/Data/NilModSettings.xml to configure me!" + Environment.NewLine
                            + "Settings will be disabled/configured by the server however in multiplayer!";
                    }

                }

                if (GUI.LargeFont!=null)
                {
                    GUI.LargeFont.DrawString(spriteBatch, loadText, 
                        new Vector2(GameMain.GraphicsWidth/2.0f - GUI.LargeFont.MeasureString(loadText).X/2.0f, GameMain.GraphicsHeight*0.65f), 
                        Color.White); 
                }
           
            }
            spriteBatch.End();

        }

#if !LINUX
        private void DrawSplashScreen(SpriteBatch spriteBatch)
        {
            if (videoPlayer == null)
            {
                videoPlayer = new VideoPlayer();
                videoPlayer.Play(splashScreenVideo);
                videoPlayer.Volume = GameMain.Config.SoundVolume;
            }
            else
            {
                Texture2D videoTexture = null;

                if (videoPlayer.State == MediaState.Stopped)
                {
                    videoPlayer.Dispose();
                    videoPlayer = null;

                    splashScreenVideo.Dispose();
                    splashScreenVideo = null;
                }
                else
                {
                    videoTexture = videoPlayer.GetTexture();

                    spriteBatch.Begin();
                    spriteBatch.Draw(videoTexture, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
                    spriteBatch.End();

                    if (PlayerInput.KeyHit(Keys.Space) || PlayerInput.KeyHit(Keys.Enter) || PlayerInput.LeftButtonDown())
                    {
                        videoPlayer.Stop();
                    }
                }

            }
        }
#endif
 
        bool drawn;
        public IEnumerable<object> DoLoading(IEnumerable<object> loader)
        {
            drawn = false;
            LoadState = null;

            while (!drawn)
            {
                yield return CoroutineStatus.Running;
            }

            CoroutineManager.StartCoroutine(loader);
            
            yield return CoroutineStatus.Running;

            while (CoroutineManager.IsCoroutineRunning(loader.ToString()))
            {
                yield return CoroutineStatus.Running;
            }

            loadState = 100.0f;

            yield return CoroutineStatus.Success;
        }
    }
}
