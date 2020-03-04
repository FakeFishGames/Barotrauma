using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.CharacterEditor;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using EventInput;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    public enum GUISoundType
    {
        UIMessage,
        ChatMessage,
        RadioMessage,
        DeadMessage,
        Click,
        PickItem,
        PickItemFail,
        DropItem
    }

    public enum CursorState
    {
        Default, // Cursor
        Hand, // Hand with a finger
        Move, // arrows pointing to all directions
        IBeam, // Text
        Dragging,// Closed hand
        Waiting, // Hourglass
        WaitingBackground // Cursor + Hourglass
    }
    
    public static class GUI
    {
        public static GUICanvas Canvas => GUICanvas.Instance;
        public static CursorState MouseCursor = CursorState.Default;
        
        public static readonly SamplerState SamplerState = new SamplerState()
        {
            Filter = TextureFilter.Linear,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            BorderColor = Color.White,
            MaxAnisotropy = 4,
            MaxMipLevel = 0,
            MipMapLevelOfDetailBias = -0.8f,
            ComparisonFunction = CompareFunction.Never,
            FilterMode = TextureFilterMode.Default,
        };

        public static readonly SamplerState SamplerStateClamp = new SamplerState()
        {
            Filter = TextureFilter.Linear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            BorderColor = Color.White,
            MaxAnisotropy = 4,
            MaxMipLevel = 0,
            MipMapLevelOfDetailBias = -0.8f,
            ComparisonFunction = CompareFunction.Never,
            FilterMode = TextureFilterMode.Default,
        };


        public static readonly string[] vectorComponentLabels = { "X", "Y", "Z", "W" };
        public static readonly string[] rectComponentLabels = { "X", "Y", "W", "H" };
        public static readonly string[] colorComponentLabels = { "R", "G", "B", "A" };

        public static Vector2 ReferenceResolution => new Vector2(1920f, 1080f);
        public static float Scale => (GameMain.GraphicsWidth / ReferenceResolution.X + GameMain.GraphicsHeight / ReferenceResolution.Y) / 2.0f * GameSettings.HUDScale;
        public static float xScale => GameMain.GraphicsWidth / ReferenceResolution.X * GameSettings.HUDScale;
        public static float yScale => GameMain.GraphicsHeight / ReferenceResolution.Y * GameSettings.HUDScale;
        public static float HorizontalAspectRatio => GameMain.GraphicsWidth / (float)GameMain.GraphicsHeight;
        public static float VerticalAspectRatio => GameMain.GraphicsHeight / (float)GameMain.GraphicsWidth;
        public static float RelativeHorizontalAspectRatio => HorizontalAspectRatio / (ReferenceResolution.X / ReferenceResolution.Y);
        public static float RelativeVerticalAspectRatio => VerticalAspectRatio / (ReferenceResolution.Y / ReferenceResolution.X);

        public static float SlicedSpriteScale
        {
            get 
            {
                if (Math.Abs(1.0f - Scale) < 0.1f) 
                { 
                    //don't scale if very close to the "reference resolution"
                    return 1.0f; 
                }
                return Scale; 
            }
        }

        public static GUIStyle Style;

        private static Texture2D t;
        private static Sprite[] MouseCursorSprites => Style.CursorSprite;

        private static bool debugDrawSounds, debugDrawEvents;

        public static GraphicsDevice GraphicsDevice { get; private set; }

        private static List<GUIMessage> messages = new List<GUIMessage>();
        private static Sound[] sounds;
        private static bool pauseMenuOpen, settingsMenuOpen;
        public static GUIFrame PauseMenu { get; private set; }
        private static Sprite arrow;

        public static bool HideCursor;

        public static KeyboardDispatcher KeyboardDispatcher { get; set; }

        /// <summary>
        /// Has the selected Screen changed since the last time the GUI was drawn.
        /// </summary>
        public static bool ScreenChanged;

        public static ScalableFont Font => Style?.Font;

        // Usable in CJK as a regular font
        public static ScalableFont GlobalFont => Style?.GlobalFont;
        public static ScalableFont UnscaledSmallFont => Style?.UnscaledSmallFont;
        public static ScalableFont SmallFont => Style?.SmallFont;
        public static ScalableFont LargeFont => Style?.LargeFont;
        public static ScalableFont SubHeadingFont => Style?.SubHeadingFont;
        public static ScalableFont DigitalFont => Style?.DigitalFont;
        public static ScalableFont HotkeyFont => Style?.HotkeyFont;

        public static ScalableFont CJKFont { get; private set; }

        public static UISprite UIGlow => Style.UIGlow;
        public static UISprite UIGlowCircular => Style.UIGlowCircular;

        public static Sprite SubmarineIcon
        {
            get;
            private set;
        }

        public static Sprite BrokenIcon
        {
            get;
            private set;
        }

        public static Sprite SpeechBubbleIcon
        {
            get;
            private set;
        }

        public static Sprite Arrow
        {
            get { return arrow; }
        }

        public static bool SettingsMenuOpen
        {
            get { return settingsMenuOpen; }
            set
            {
                if (value == settingsMenuOpen) { return; }
                GameMain.Config.ResetSettingsFrame();
                settingsMenuOpen = value;
            }
        }

        public static bool PauseMenuOpen
        {
            get { return pauseMenuOpen; }
        }

        public static bool PreventPauseMenuToggle = false;

        public static Color ScreenOverlayColor
        {
            get;
            set;
        }

        public static bool DisableHUD, DisableUpperHUD, DisableItemHighlights, DisableCharacterNames;

        public static void Init(GameWindow window, IEnumerable<ContentPackage> selectedContentPackages, GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            var files = ContentPackage.GetFilesOfType(selectedContentPackages, ContentType.UIStyle);
            XElement selectedStyle = null;
            foreach (var file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                if (doc == null) { continue; }
                var mainElement = doc.Root;
                if (doc.Root.IsOverride())
                {
                    mainElement = doc.Root.FirstElement();
                    if (selectedStyle != null)
                    {
                        DebugConsole.NewMessage($"Overriding the ui styles with '{file.Path}'", Color.Yellow);
                    }
                }
                else if (selectedStyle != null)
                {
                    DebugConsole.ThrowError("Another ui style already loaded! Use <override></override> tags to override it.");
                    break;
                }
                selectedStyle = mainElement;
            }
            if (selectedStyle == null)
            {
                DebugConsole.ThrowError("No UI styles defined in the selected content package!");
            }
            else
            {
                Style = new GUIStyle(selectedStyle, graphicsDevice);
            }

            if (CJKFont == null)
            {
                CJKFont = new ScalableFont("Content/Fonts/NotoSans/NotoSansCJKsc-Bold.otf",
                    Font.Size, graphicsDevice, dynamicLoading: true, isCJK: true);
            }
        }

        public static void LoadContent(bool loadSounds = true)
        {
            if (loadSounds)
            {
                sounds = new Sound[Enum.GetValues(typeof(GUISoundType)).Length];

                sounds[(int)GUISoundType.UIMessage] = GameMain.SoundManager.LoadSound("Content/Sounds/UI/UImsg.ogg", false);
                sounds[(int)GUISoundType.ChatMessage] = GameMain.SoundManager.LoadSound("Content/Sounds/UI/ChatMsg.ogg", false);
                sounds[(int)GUISoundType.RadioMessage] = GameMain.SoundManager.LoadSound("Content/Sounds/UI/RadioMsg.ogg", false);
                sounds[(int)GUISoundType.DeadMessage] = GameMain.SoundManager.LoadSound("Content/Sounds/UI/DeadMsg.ogg", false);
                sounds[(int)GUISoundType.Click] = GameMain.SoundManager.LoadSound("Content/Sounds/UI/Click.ogg", false);

                sounds[(int)GUISoundType.PickItem] = GameMain.SoundManager.LoadSound("Content/Sounds/PickItem.ogg", false);
                sounds[(int)GUISoundType.PickItemFail] = GameMain.SoundManager.LoadSound("Content/Sounds/PickItemFail.ogg", false);
                sounds[(int)GUISoundType.DropItem] = GameMain.SoundManager.LoadSound("Content/Sounds/DropItem.ogg", false);
            }
            // create 1x1 texture for line drawing
            CrossThread.RequestExecutionOnMainThread(() =>
            {
                t = new Texture2D(GraphicsDevice, 1, 1);
                t.SetData(new Color[] { Color.White });// fill the texture with white
            });
            
            SubmarineIcon = new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(452, 385, 182, 81), new Vector2(0.5f, 0.5f));
            arrow = new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(392, 393, 49, 45), new Vector2(0.5f, 0.5f));
            SpeechBubbleIcon = new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(385, 449, 66, 60), new Vector2(0.5f, 0.5f));
            BrokenIcon = new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(898, 386, 123, 123), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// By default, all the gui elements are drawn automatically in the same order they appear on the update list. 
        /// </summary>
        public static void Draw(Camera cam, SpriteBatch spriteBatch)
        {
            if (ScreenChanged)
            {
                updateList.Clear();
                updateListSet.Clear();
                Screen.Selected?.AddToGUIUpdateList();
                ScreenChanged = false;
            }

            updateList.ForEach(c => c.DrawAuto(spriteBatch));

            if (ScreenOverlayColor.A > 0.0f)
            {
                DrawRectangle(
                    spriteBatch,
                    new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                    ScreenOverlayColor, true);
            }

#if UNSTABLE
            string line1 = "Barotrauma Unstable v" + GameMain.Version;
            string line2 = "(" + AssemblyInfo.GetBuildString() + ", branch " + AssemblyInfo.GetGitBranch() + ", revision " + AssemblyInfo.GetGitRevision() + ")";

            Rectangle watermarkRect = new Rectangle(-50, GameMain.GraphicsHeight - 80, 50 + (int)(Math.Max(LargeFont.MeasureString(line1).X, Font.MeasureString(line2).X) * 1.2f), 100);
            float alpha = 1.0f;

            int yOffset = 0;

            if (Screen.Selected == GameMain.GameScreen)
            {
                yOffset = -HUDLayoutSettings.ChatBoxArea.Height;
                watermarkRect.Y += yOffset;
            }

            if (Screen.Selected == GameMain.GameScreen || Screen.Selected == GameMain.SubEditorScreen)
            {
                alpha = 0.2f;
            }

            Style.GetComponentStyle("OuterGlow").Sprites[GUIComponent.ComponentState.None][0].Draw(
                spriteBatch, watermarkRect, Color.Black * 0.8f * alpha);
            LargeFont.DrawString(spriteBatch, line1,
                new Vector2(10, GameMain.GraphicsHeight - 30 - LargeFont.MeasureString(line1).Y + yOffset), Color.White * 0.6f * alpha);
            Font.DrawString(spriteBatch, line2,
                new Vector2(10, GameMain.GraphicsHeight - 30 + yOffset), Color.White * 0.6f * alpha);

            if (Screen.Selected != GameMain.GameScreen)
            {
                var buttonRect =
                    new Rectangle(20 + (int)Math.Max(LargeFont.MeasureString(line1).X, Font.MeasureString(line2).X), GameMain.GraphicsHeight - (int)(45 * Scale) + yOffset, (int)(150 * Scale), (int)(40 * Scale));
                if (DrawButton(spriteBatch, buttonRect, "Report Bug", Style.GetComponentStyle("GUIBugButton").Color * 0.8f))
                {
                    GameMain.Instance.ShowBugReporter();
                }
            }
#endif

            if (DisableHUD) { return; }

            if (GameMain.ShowFPS || GameMain.DebugDraw)
            {
                DrawString(spriteBatch, new Vector2(10, 10),
                    "FPS: " + Math.Round(GameMain.PerformanceCounter.AverageFramesPerSecond),
                    Color.White, Color.Black * 0.5f, 0, SmallFont);
            }

            if (GameMain.ShowPerf)
            {
                int y = 10;
                DrawString(spriteBatch, new Vector2(300, y), 
                    "Draw - Avg: " + GameMain.PerformanceCounter.DrawTimeGraph.Average().ToString("0.00") + " ms" +
                    " Max: " + GameMain.PerformanceCounter.DrawTimeGraph.LargestValue().ToString("0.00") + " ms", 
                    GUI.Style.Green, Color.Black * 0.8f, font: SmallFont);
                y += 15;
                GameMain.PerformanceCounter.DrawTimeGraph.Draw(spriteBatch, new Rectangle(300, y, 170, 50), null, 0, GUI.Style.Green);
                y += 50;

                DrawString(spriteBatch, new Vector2(300, y),
                    "Update - Avg: " + GameMain.PerformanceCounter.UpdateTimeGraph.Average().ToString("0.00") + " ms" +
                    " Max: " + GameMain.PerformanceCounter.UpdateTimeGraph.LargestValue().ToString("0.00") + " ms", 
                    Color.LightBlue, Color.Black * 0.8f, font: SmallFont);
                y += 15;
                GameMain.PerformanceCounter.UpdateTimeGraph.Draw(spriteBatch, new Rectangle(300, y, 170, 50), null, 0, Color.LightBlue);
                GameMain.PerformanceCounter.UpdateIterationsGraph.Draw(spriteBatch, new Rectangle(300, y, 170, 50), 20, 0, GUI.Style.Red);
                y += 50;
                foreach (string key in GameMain.PerformanceCounter.GetSavedIdentifiers)
                {
                    float elapsedMillisecs = GameMain.PerformanceCounter.GetAverageElapsedMillisecs(key);
                    DrawString(spriteBatch, new Vector2(300, y),
                        key + ": " + elapsedMillisecs.ToString("0.00"),
                        Color.Lerp(Color.LightGreen, GUI.Style.Red, elapsedMillisecs / 10.0f), Color.Black * 0.5f, 0, SmallFont);

                    y += 15;
                }

                if (Settings.EnableDiagnostics)
                {
                    DrawString(spriteBatch, new Vector2(320, y), "ContinuousPhysicsTime: " + GameMain.World.ContinuousPhysicsTime.TotalMilliseconds, Color.Lerp(Color.LightGreen, GUI.Style.Red, (float)GameMain.World.ContinuousPhysicsTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, SmallFont);
                    DrawString(spriteBatch, new Vector2(320, y + 15), "ControllersUpdateTime: " + GameMain.World.ControllersUpdateTime.TotalMilliseconds, Color.Lerp(Color.LightGreen, GUI.Style.Red, (float)GameMain.World.ControllersUpdateTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, SmallFont);
                    DrawString(spriteBatch, new Vector2(320, y + 30), "AddRemoveTime: " + GameMain.World.AddRemoveTime.TotalMilliseconds, Color.Lerp(Color.LightGreen, GUI.Style.Red, (float)GameMain.World.AddRemoveTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, SmallFont);
                    DrawString(spriteBatch, new Vector2(320, y + 45), "NewContactsTime: " + GameMain.World.NewContactsTime.TotalMilliseconds, Color.Lerp(Color.LightGreen, GUI.Style.Red, (float)GameMain.World.NewContactsTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, SmallFont);
                    DrawString(spriteBatch, new Vector2(320, y + 60), "ContactsUpdateTime: " + GameMain.World.ContactsUpdateTime.TotalMilliseconds, Color.Lerp(Color.LightGreen, GUI.Style.Red, (float)GameMain.World.ContactsUpdateTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, SmallFont);
                    DrawString(spriteBatch, new Vector2(320, y + 75), "SolveUpdateTime: " + GameMain.World.SolveUpdateTime.TotalMilliseconds, Color.Lerp(Color.LightGreen, GUI.Style.Red, (float)GameMain.World.SolveUpdateTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, SmallFont);
                }
            }

            if (GameMain.DebugDraw)
            {
                DrawString(spriteBatch, new Vector2(10, 25),
                    "Physics: " + GameMain.World.UpdateTime,
                    Color.White, Color.Black * 0.5f, 0, SmallFont);

                DrawString(spriteBatch, new Vector2(10, 40),
                    $"Bodies: {GameMain.World.BodyList.Count} ({GameMain.World.BodyList.FindAll(b => b.Awake && b.Enabled).Count} awake, {GameMain.World.BodyList.FindAll(b => b.Awake && b.BodyType == BodyType.Dynamic && b.Enabled).Count} dynamic)",
                    Color.White, Color.Black * 0.5f, 0, SmallFont);

                if (Screen.Selected.Cam != null)
                {
                    DrawString(spriteBatch, new Vector2(10, 55),
                        "Camera pos: " + Screen.Selected.Cam.Position.ToPoint() + ", zoom: " + Screen.Selected.Cam.Zoom,
                        Color.White, Color.Black * 0.5f, 0, SmallFont);
                }

                if (Submarine.MainSub != null)
                {
                    DrawString(spriteBatch, new Vector2(10, 70),
                        "Sub pos: " + Submarine.MainSub.Position.ToPoint(),
                        Color.White, Color.Black * 0.5f, 0, SmallFont);
                }

                DrawString(spriteBatch, new Vector2(10, 90),
                    "Particle count: " + GameMain.ParticleManager.ParticleCount + "/" + GameMain.ParticleManager.MaxParticles,
                    Color.Lerp(GUI.Style.Green, GUI.Style.Red, (GameMain.ParticleManager.ParticleCount / (float)GameMain.ParticleManager.MaxParticles)), Color.Black * 0.5f, 0, SmallFont);

                DrawString(spriteBatch, new Vector2(10, 115),
                    "Loaded sprites: " + Sprite.LoadedSprites.Count() + "\n(" + Sprite.LoadedSprites.Select(s => s.FilePath).Distinct().Count() + " unique textures)",
                    Color.White, Color.Black * 0.5f, 0, SmallFont);

                if (debugDrawSounds)
                {
                    int y = 0;
                    DrawString(spriteBatch, new Vector2(500, y),
                        "Sounds (Ctrl+S to hide): ", Color.White, Color.Black * 0.5f, 0, SmallFont);
                    y += 15;

                    DrawString(spriteBatch, new Vector2(500, y),
                        "Current playback amplitude: " + GameMain.SoundManager.PlaybackAmplitude.ToString(), Color.White, Color.Black * 0.5f, 0, SmallFont);

                    y += 15;

                    DrawString(spriteBatch, new Vector2(500, y),
                        "Compressed dynamic range gain: " + GameMain.SoundManager.CompressionDynamicRangeGain.ToString(), Color.White, Color.Black * 0.5f, 0, SmallFont);

                    y += 15;

                    DrawString(spriteBatch, new Vector2(500, y),
                        "Loaded sounds: " + GameMain.SoundManager.LoadedSoundCount + " (" + GameMain.SoundManager.UniqueLoadedSoundCount + " unique)", Color.White, Color.Black * 0.5f, 0, SmallFont);
                    y += 15;

                    for (int i = 0; i < SoundManager.SOURCE_COUNT; i++)
                    {
                        Color clr = Color.White;
                        string soundStr = i + ": ";
                        SoundChannel playingSoundChannel = GameMain.SoundManager.GetSoundChannelFromIndex(SoundManager.SourcePoolIndex.Default, i);
                        if (playingSoundChannel == null)
                        {
                            soundStr += "none";
                            clr *= 0.5f;
                        }
                        else
                        {
                            soundStr += Path.GetFileNameWithoutExtension(playingSoundChannel.Sound.Filename);

#if DEBUG
                            if (PlayerInput.GetKeyboardState.IsKeyDown(Keys.G))
                            {
                                if (PlayerInput.MousePosition.Y >= y && PlayerInput.MousePosition.Y <= y + 12)
                                {
                                    GameMain.SoundManager.DebugSource(i);
                                }
                            }
#endif

                            if (playingSoundChannel.Looping)
                            {
                                soundStr += " (looping)";
                                clr = Color.Yellow;
                            }
                            if (playingSoundChannel.IsStream)
                            {
                                soundStr += " (streaming)";
                                clr = Color.Lime;
                            }
                            if (!playingSoundChannel.IsPlaying)
                            {
                                soundStr += " (stopped)";
                                clr *= 0.5f;
                            }
                            else if (playingSoundChannel.Muffled)
                            {
                                soundStr += " (muffled)";
                                clr = Color.Lerp(clr, Color.LightGray, 0.5f);
                            }
                        }

                        DrawString(spriteBatch, new Vector2(500, y), soundStr, clr, Color.Black * 0.5f, 0, SmallFont);
                        y += 15;
                    }
                }
                else
                {
                    DrawString(spriteBatch, new Vector2(500, 0),
                        "Ctrl+S to show sound debug info", Color.White, Color.Black * 0.5f, 0, SmallFont);
                }


                if (debugDrawEvents)
                {
                    DrawString(spriteBatch, new Vector2(10, 300),
                        "Ctrl+E to hide EventManager debug info", Color.White, Color.Black * 0.5f, 0, SmallFont);
                    GameMain.GameSession?.EventManager?.DebugDrawHUD(spriteBatch, 315);
                }
                else
                {
                    DrawString(spriteBatch, new Vector2(10, 300),
                        "Ctrl+E to show EventManager debug info", Color.White, Color.Black * 0.5f, 0, SmallFont);
                }

                if (MouseOn != null)
                {
                    DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 500, 20),
                        $"Selected UI Element: {MouseOn.GetType().Name} ({ (MouseOn.Style?.Element.Name.LocalName ?? "no style") }, {MouseOn.Rect})",
                        Color.LightGreen, Color.Black * 0.5f, 0, SmallFont);
                }
            }

            if (HUDLayoutSettings.DebugDraw) HUDLayoutSettings.Draw(spriteBatch);

            if (GameMain.Client != null) GameMain.Client.Draw(spriteBatch);

            if (Character.Controlled?.Inventory != null)
            {
                if (!Character.Controlled.LockHands && Character.Controlled.Stun < 0.1f && !Character.Controlled.IsDead)
                {
                    Inventory.DrawFront(spriteBatch);
                }
            }

            DrawMessages(spriteBatch, cam);

            if (MouseOn != null && !string.IsNullOrWhiteSpace(MouseOn.ToolTip))
            {
                MouseOn.DrawToolTip(spriteBatch);
            }

            if (GameMain.WindowActive && !HideCursor)
            {
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerStateClamp, rasterizerState: GameMain.ScissorTestEnable);
                
                var sprite = MouseCursorSprites[(int) MouseCursor] ?? MouseCursorSprites[(int)CursorState.Default];
                sprite.Draw(spriteBatch, PlayerInput.LatestMousePosition, Color.White, sprite.Origin, 0f, Scale / 1.5f);

                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            }
            HideCursor = false;
        }

        public static void DrawBackgroundSprite(SpriteBatch spriteBatch, Sprite backgroundSprite, float aberrationStrength = 1.0f)
        {
            double aberrationT = (Timing.TotalTime * 0.5f);
            GameMain.GameScreen.PostProcessEffect.Parameters["blurDistance"].SetValue(0.001f * aberrationStrength);
            GameMain.GameScreen.PostProcessEffect.Parameters["chromaticAberrationStrength"].SetValue(new Vector3(-0.025f, -0.01f, -0.05f) *
                (float)(PerlinNoise.CalculatePerlin(aberrationT, aberrationT, 0) + 0.5f) * aberrationStrength);
            GameMain.GameScreen.PostProcessEffect.CurrentTechnique = GameMain.GameScreen.PostProcessEffect.Techniques["BlurChromaticAberration"];
            GameMain.GameScreen.PostProcessEffect.CurrentTechnique.Passes[0].Apply();

            spriteBatch.Begin(SpriteSortMode.Immediate, effect: GameMain.GameScreen.PostProcessEffect);

            float scale = Math.Max(
                (float)GameMain.GraphicsWidth / backgroundSprite.SourceRect.Width, 
                (float)GameMain.GraphicsHeight / backgroundSprite.SourceRect.Height) * 1.1f;
            float paddingX = backgroundSprite.SourceRect.Width * scale - GameMain.GraphicsWidth;
            float paddingY = backgroundSprite.SourceRect.Height * scale - GameMain.GraphicsHeight;
                
            double noiseT = (Timing.TotalTime * 0.02f);
            Vector2 pos = new Vector2((float)PerlinNoise.CalculatePerlin(noiseT, noiseT, 0) - 0.5f, (float)PerlinNoise.CalculatePerlin(noiseT, noiseT, 0.5f) - 0.5f);
            pos = new Vector2(pos.X * paddingX, pos.Y * paddingY);

            spriteBatch.Draw(backgroundSprite.Texture,
                new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) / 2 + pos,
                null, Color.White, 0.0f, backgroundSprite.size / 2,
                scale, SpriteEffects.None, 0.0f);
            
            spriteBatch.End();
        }

        #region Update list
        private static readonly List<GUIComponent> updateList = new List<GUIComponent>();
        //essentially a copy of the update list, used as an optimization to quickly check if the component is present in the update list
        private static readonly HashSet<GUIComponent> updateListSet = new HashSet<GUIComponent>();
        private static readonly Queue<GUIComponent> removals = new Queue<GUIComponent>();
        private static readonly Queue<GUIComponent> additions = new Queue<GUIComponent>();
        // A helpers list for all elements that have a draw order less than 0.
        private static readonly List<GUIComponent> first = new List<GUIComponent>();
        // A helper list for all elements that have a draw order greater than 0.
        private static readonly List<GUIComponent> last = new List<GUIComponent>();

        /// <summary>
        /// Adds the component on the addition queue.
        /// Note: does not automatically add children, because we might want to enforce a custom order for them.
        /// </summary>
        public static void AddToUpdateList(GUIComponent component)
        {
            if (component == null)
            {
                DebugConsole.ThrowError("Trying to add a null component on the GUI update list!");
                return;
            }
            if (!component.Visible) { return; }
            if (component.UpdateOrder < 0)
            {
                first.Add(component);
            }
            else if (component.UpdateOrder > 0)
            {
                last.Add(component);
            }
            else
            {
                additions.Enqueue(component);
            }
        }

        /// <summary>
        /// Adds the component on the removal queue.
        /// Removal list is evaluated last, and thus any item on both lists are not added to update list.
        /// </summary>
        public static void RemoveFromUpdateList(GUIComponent component, bool alsoChildren = true)
        {
            if (updateListSet.Contains(component))
            {
                removals.Enqueue(component);
            }
            if (alsoChildren)
            {
                if (component.RectTransform != null)
                {
                    component.RectTransform.Children.ForEach(c => RemoveFromUpdateList(c.GUIComponent));
                }
                else
                {
                    component.Children.ForEach(c => RemoveFromUpdateList(c));
                }
            }
        }

        public static void ClearUpdateList()
        {
            if (KeyboardDispatcher.Subscriber is GUIComponent && !updateList.Contains(KeyboardDispatcher.Subscriber as GUIComponent))
            {
                KeyboardDispatcher.Subscriber = null;
            }
            updateList.Clear();
            updateListSet.Clear();
        }

        private static void RefreshUpdateList()
        {
            foreach (var component in updateList)
            {
                if (!component.Visible)
                {
                    RemoveFromUpdateList(component);
                }
            }
            ProcessHelperList(first);
            ProcessAdditions();
            ProcessHelperList(last);
            ProcessRemovals();
        }

        private static void ProcessAdditions()
        {
            while (additions.Count > 0)
            {
                var component = additions.Dequeue();
                if (!updateListSet.Contains(component))
                {
                    updateList.Add(component);
                    updateListSet.Add(component);
                }
            }
        }

        private static void ProcessRemovals()
        {
            while (removals.Count > 0)
            {
                var component = removals.Dequeue();
                updateList.Remove(component);
                updateListSet.Remove(component);
                if (component as IKeyboardSubscriber == KeyboardDispatcher.Subscriber)
                {
                    KeyboardDispatcher.Subscriber = null;
                }
            }
        }

        private static void ProcessHelperList(List<GUIComponent> list)
        {
            if (list.Count == 0) { return; }
            foreach (var item in list)
            {
                int index = 0;
                if (updateList.Count > 0)
                {
                    index = updateList.Count - 1;
                    while (updateList[index].UpdateOrder > item.UpdateOrder)
                    {
                        index--;
                        if (index == 0) { break; }
                    }
                }
                if (!updateListSet.Contains(item))
                {
                    updateList.Insert(index, item);
                    updateListSet.Add(item);
                }
            }
            list.Clear();
        }

        private static void HandlePersistingElements(float deltaTime)
        {
            GUIMessageBox.AddActiveToGUIUpdateList();

            if (pauseMenuOpen)
            {
                PauseMenu.AddToGUIUpdateList();
            }
            if (settingsMenuOpen)
            {
                GameMain.Config.SettingsFrame.AddToGUIUpdateList();
            }

            //the "are you sure you want to quit" prompts are drawn on top of everything else
            if (GUIMessageBox.VisibleBox?.UserData as string == "verificationprompt" || GUIMessageBox.VisibleBox?.UserData as string == "bugreporter")
            {
                GUIMessageBox.VisibleBox.AddToGUIUpdateList();
            }
        }
        #endregion

        public static GUIComponent MouseOn { get; private set; }

        public static bool IsMouseOn(GUIComponent target)
        {
            if (target == null) { return false; }
            //if (MouseOn == null) { return true; }
            return target == MouseOn || target.IsParentOf(MouseOn);
        }

        public static void ForceMouseOn(GUIComponent c)
        {
            MouseOn = c;
        }

        /// <summary>
        /// Updated automatically before updating the elements on the update list.
        /// </summary>
        public static GUIComponent UpdateMouseOn()
        {
            GUIComponent prevMouseOn = MouseOn;
            MouseOn = null;
            int inventoryIndex = -1;
            
            if (Inventory.IsMouseOnInventory())
            {
                inventoryIndex = updateList.IndexOf(CharacterHUD.HUDFrame);
            }

            if (!PlayerInput.PrimaryMouseButtonHeld() && !PlayerInput.PrimaryMouseButtonClicked())
            {
                for (var i = updateList.Count - 1; i > inventoryIndex; i--)
                {
                    var c = updateList[i];
                    if (!c.CanBeFocused) { continue; }
                    if (c.MouseRect.Contains(PlayerInput.MousePosition))
                    {
                        if ((!PlayerInput.PrimaryMouseButtonHeld() && !PlayerInput.PrimaryMouseButtonClicked()) || c == prevMouseOn)
                        {
                            MouseOn = c;
                        }
                        break;
                    }
                }
            }
            else
            {
                MouseOn = prevMouseOn;
            }

            MouseCursor = UpdateMouseCursorState(MouseOn);
            return MouseOn;
        }
        
        private static CursorState UpdateMouseCursorState(GUIComponent c)
        {
            // Waiting and drag cursor override everything else
            if (MouseCursor == CursorState.Waiting) { return CursorState.Waiting; }
            if (GUIScrollBar.DraggingBar != null) { return GUIScrollBar.DraggingBar.Bar.HoverCursor; }

            // Wire cursors
            if (Character.Controlled != null)
            {
                if (Character.Controlled.SelectedConstruction?.GetComponent<ConnectionPanel>() != null)
                {
                    if (Connection.DraggingConnected != null)
                    {
                        return CursorState.Dragging;
                    }
                    else if (ConnectionPanel.HighlightedWire != null)
                    {
                        return CursorState.Hand;
                    }
                }
                if (Wire.DraggingWire != null) { return CursorState.Dragging; }
            }
           
            if (c == null || c is GUICustomComponent)
            {
                switch (Screen.Selected)
                {
                    // Character editor limbs
                    case CharacterEditorScreen editor:
                        return editor.GetMouseCursorState();
                    // Portrait area during gameplay
                    case GameScreen _ when !(Character.Controlled?.ShouldLockHud() ?? true):
                        if (HUDLayoutSettings.BottomRightInfoArea.Contains(PlayerInput.MousePosition) ||
                            Rectangle.Union(HUDLayoutSettings.AfflictionAreaLeft, HUDLayoutSettings.HealthBarArea).Contains(PlayerInput.MousePosition))
                        {
                            return CursorState.Hand;
                        }
                        break;
                    // Sub editor drag and highlight
                    case SubEditorScreen editor:
                    {
                        // Portrait area
                        if ((editor.CharacterMode || editor.WiringMode) && 
                            HUDLayoutSettings.BottomRightInfoArea.Contains(PlayerInput.MousePosition))
                        {
                            return CursorState.Hand;
                        }
                        
                        foreach (var mapEntity in MapEntity.mapEntityList)
                        {
                            if (MapEntity.StartMovingPos != Vector2.Zero)
                            {
                                return CursorState.Dragging;
                            }
                            if (mapEntity.IsHighlighted)
                            {
                                return CursorState.Hand;
                            }
                        }
                        break;
                    }
                    
                    // Campaign map highlighted location
                    case LobbyScreen lobby:
                    {
                        if (lobby.CampaignUI?.Campaign.Map.HighlightedLocation != null) { return CursorState.Hand; }
                        break;
                    }
                    
                    case NetLobbyScreen lobby:
                    {
                        if (lobby.CampaignUI?.Campaign.Map.HighlightedLocation != null) { return CursorState.Hand; }
                        break;
                    }
                }
            }
            
            if (c != null && c.Visible)
            {
                // When a button opens a submenu, it increases to the size of the entire screen.
                // And this is of course picked up as clickable area.
                // There has to be a better way of checking this but for now this works.
                var monitorRect = new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
                
                var parent = FindInteractParent(c);
                
                if (c.Enabled)
                {
                    // Some parent elements take priority
                    // but not when the child is a GUIButton or GUITickBox
                    if (!(parent is GUIButton) && !(parent is GUIListBox) || 
                              (c is GUIButton) || (c is GUITickBox))
                    {
                        if (!c.Rect.Equals(monitorRect)) { return c.HoverCursor; }
                    }
                }
                
                // Children in list boxes can be interacted with despite not having
                // a GUIButton inside of them so instead of hard coding we check if
                // the children can be interacted with by checking their hover state
                if (parent is GUIListBox listBox)
                {
                    if (listBox.DraggedElement != null) { return CursorState.Dragging; }
                    if (listBox.CanDragElements) { return CursorState.Move; }
                    
                    var hoverParent = c;
                    while (true)
                    {
                        if (hoverParent == parent || hoverParent == null) { break; }
                        if (hoverParent.State == GUIComponent.ComponentState.Hover) { return CursorState.Hand; }
                        hoverParent = hoverParent.Parent;
                    }
                }
                
                if (parent != null)
                {
                    if (!parent.Rect.Equals(monitorRect)) { return parent.HoverCursor; }
                }
            }
            
            if (Inventory.IsMouseOnInventory()) { return Inventory.GetInventoryMouseCursor(); }

            var character = Character.Controlled;
            // ReSharper disable once InvertIf
            if (character != null)
            {
                // Health menus
                if (character.CharacterHealth.MouseOnElement) { return CursorState.Hand; }
                
                if (character.SelectedCharacter != null)
                {
                    if (character.SelectedCharacter.CharacterHealth.MouseOnElement)
                    {
                        return CursorState.Hand;
                    }
                }

                // Character is hovering over an item placed in the world
                if (character.FocusedItem != null) { return CursorState.Hand; }
            }
            
            return CursorState.Default;

            static GUIComponent FindInteractParent(GUIComponent component)
            {
                while (true)
                {
                    var parent = component.Parent;
                    if (parent == null) { return null; }

                    if (ContainsMouse(parent))
                    {
                        if (parent.Enabled)
                        {
                            switch (parent)
                            {
                                case GUIButton button:
                                    return button;
                                case GUITextBox box:
                                    return box;
                                case GUIListBox list:
                                    return list;
                                case GUIScrollBar bar:
                                    return bar;
                            }
                        }
                        component = parent;
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            
            static bool ContainsMouse(GUIComponent component)
            {
                // If component has a mouse rectangle then use that, if not use it's physical rect
                return !component.MouseRect.Equals(Rectangle.Empty) ?
                    component.MouseRect.Contains(PlayerInput.MousePosition) :
                    component.Rect.Contains(PlayerInput.MousePosition);
            }
        }

        /// <summary>
        /// Set the cursor to an hourglass.
        /// Will automatically revert after 10 seconds or when <see cref="ClearCursorWait"/> is called.
        /// </summary>
        public static void SetCursorWaiting()
        {
            CoroutineManager.StartCoroutine(WaitCursorCoroutine(), "WaitCursorTimeout");

            static IEnumerable<object> WaitCursorCoroutine()
            {
                MouseCursor = CursorState.Waiting;
                var timeOut = DateTime.Now + new TimeSpan(0, 0, 10);
                while (DateTime.Now < timeOut) { yield return CoroutineStatus.Running; }
                if (MouseCursor == CursorState.Waiting) { MouseCursor = CursorState.Default; }
                yield return CoroutineStatus.Success;
            }
        }
        
        public static void ClearCursorWait()
        {
            CoroutineManager.StopCoroutines("WaitCursorTimeout");
            MouseCursor = CursorState.Default;
        }

        public static bool HasSizeChanged(Point referenceResolution, float referenceUIScale, float referenceHUDScale)
        {
            return GameMain.GraphicsWidth != referenceResolution.X || GameMain.GraphicsHeight != referenceResolution.Y ||
                   referenceUIScale != Inventory.UIScale || referenceHUDScale != Scale;
        }

        public static void Update(float deltaTime)
        {
            if (PlayerInput.KeyDown(Keys.LeftControl) && PlayerInput.KeyHit(Keys.S))
            {
                debugDrawSounds = !debugDrawSounds;
            }
            if (PlayerInput.KeyDown(Keys.LeftControl) && PlayerInput.KeyHit(Keys.E))
            {
                debugDrawEvents = !debugDrawEvents;
            }

            HandlePersistingElements(deltaTime);
            RefreshUpdateList();
            UpdateMouseOn();
            Debug.Assert(updateList.Count == updateListSet.Count);
            updateList.ForEach(c => c.UpdateAuto(deltaTime));
            UpdateMessages(deltaTime);
        }

        private static void UpdateMessages(float deltaTime)
        {
            foreach (GUIMessage msg in messages)
            {
                if (msg.WorldSpace) continue;
                msg.Timer -= deltaTime;

                if (msg.Size.X > HUDLayoutSettings.MessageAreaTop.Width)
                {
                    msg.Pos = Vector2.Lerp(Vector2.Zero, new Vector2(-HUDLayoutSettings.MessageAreaTop.Width - msg.Size.X, 0), 1.0f - msg.Timer / msg.LifeTime);
                }
                else
                {
                    //enough space to show the full message, position it at the center of the msg area
                    if (msg.Timer > 1.0f)
                    {
                        msg.Pos = Vector2.Lerp(msg.Pos, new Vector2(-HUDLayoutSettings.MessageAreaTop.Width / 2 - msg.Size.X / 2, 0), Math.Min(deltaTime * 10.0f, 1.0f));
                    }
                    else
                    {
                        msg.Pos = Vector2.Lerp(msg.Pos, new Vector2(-HUDLayoutSettings.MessageAreaTop.Width - msg.Size.X, 0), deltaTime * 10.0f);
                    }
                }
                //only the first message (the currently visible one) is updated at a time
                break;
            }
            
            foreach (GUIMessage msg in messages)
            {
                if (!msg.WorldSpace) continue;
                msg.Timer -= deltaTime;                
                msg.Pos += msg.Velocity * deltaTime;                
            }

            messages.RemoveAll(m => m.Timer <= 0.0f);
        }

        #region Element drawing

        public static void DrawIndicator(SpriteBatch spriteBatch, Vector2 worldPosition, Camera cam, float hideDist, Sprite sprite, Color color)
        {
            Vector2 diff = worldPosition - cam.WorldViewCenter;
            float dist = diff.Length();

            float symbolScale = 64.0f / sprite.size.X;

            if (dist > hideDist)
            {
                float alpha = Math.Min((dist - hideDist) / 100.0f, 1.0f);
                Vector2 targetScreenPos = cam.WorldToScreen(worldPosition);                
                float screenDist = Vector2.Distance(cam.WorldToScreen(cam.WorldViewCenter), targetScreenPos);
                float angle = MathUtils.VectorToAngle(diff);

                Vector2 unclampedDiff = new Vector2(
                    (float)Math.Cos(angle) * screenDist,
                    (float)-Math.Sin(angle) * screenDist);

                Vector2 iconDiff = new Vector2(
                    (float)Math.Cos(angle) * Math.Min(GameMain.GraphicsWidth * 0.4f, screenDist),
                    (float)-Math.Sin(angle) * Math.Min(GameMain.GraphicsHeight * 0.4f, screenDist));

                Vector2 iconPos = cam.WorldToScreen(cam.WorldViewCenter) + iconDiff;
                sprite.Draw(spriteBatch, iconPos, color * alpha, rotate: 0.0f, scale: symbolScale);

                if (unclampedDiff.Length() - 10 > iconDiff.Length())
                {
                    Vector2 normalizedDiff = Vector2.Normalize(targetScreenPos - iconPos);
                    Vector2 arrowOffset = normalizedDiff * sprite.size.X * symbolScale * 0.7f;
                    Arrow.Draw(spriteBatch, iconPos + arrowOffset, color * alpha, MathUtils.VectorToAngle(arrowOffset) + MathHelper.PiOver2, scale: 0.5f);
                }
            }
        }

        public static void DrawLine(SpriteBatch sb, Vector2 start, Vector2 end, Color clr, float depth = 0.0f, int width = 1)
        {
            DrawLine(sb, t, start, end, clr, depth, width);
        }

        public static void DrawLine(SpriteBatch sb, Sprite sprite, Vector2 start, Vector2 end, Color clr, float depth = 0.0f, int width = 1)
        {
            Vector2 edge = end - start;
            // calculate angle to rotate line
            float angle = (float)Math.Atan2(edge.Y, edge.X);

            sb.Draw(sprite.Texture,
                new Rectangle(// rectangle defines shape of line and position of start of line
                    (int)start.X,
                    (int)start.Y,
                    (int)edge.Length(), //sb will strech the texture to fill this rectangle
                    width), //width of line, change this to make thicker line
                sprite.SourceRect,
                clr, //colour of line
                angle,     //angle of line (calulated above)
                new Vector2(0, sprite.SourceRect.Height / 2), // point in line about which to rotate
                SpriteEffects.None,
                depth);
        }

        public static void DrawLine(SpriteBatch sb, Texture2D texture, Vector2 start, Vector2 end, Color clr, float depth = 0.0f, int width = 1)
        {
            Vector2 edge = end - start;
            // calculate angle to rotate line
            float angle = (float)Math.Atan2(edge.Y, edge.X);

            sb.Draw(texture,
                new Rectangle(// rectangle defines shape of line and position of start of line
                    (int)start.X,
                    (int)start.Y,
                    (int)edge.Length(), //sb will strech the texture to fill this rectangle
                    width), //width of line, change this to make thicker line
                null,
                clr, //colour of line
                angle,     //angle of line (calulated above)
                new Vector2(0, texture.Height / 2.0f), // point in line about which to rotate
                SpriteEffects.None,
                depth);
        }

        public static void DrawString(SpriteBatch sb, Vector2 pos, string text, Color color, Color? backgroundColor = null, int backgroundPadding = 0, ScalableFont font = null)
        {
            if (font == null) font = Font;
            if (backgroundColor != null)
            {
                Vector2 textSize = font.MeasureString(text);
                DrawRectangle(sb, pos - Vector2.One * backgroundPadding, textSize + Vector2.One * 2.0f * backgroundPadding, (Color)backgroundColor, true);
            }

            font.DrawString(sb, text, pos, color);
        }

        public static void DrawStringWithColors(SpriteBatch sb, Vector2 pos, string text, Color color, List<ColorData> colorData, Color? backgroundColor = null, int backgroundPadding = 0, ScalableFont font = null, float depth = 0.0f)
        {
            if (font == null) font = Font;
            if (backgroundColor != null)
            {
                Vector2 textSize = font.MeasureString(text);
                DrawRectangle(sb, pos - Vector2.One * backgroundPadding, textSize + Vector2.One * 2.0f * backgroundPadding, (Color)backgroundColor, true, depth, 5);
            }

            font.DrawStringWithColors(sb, text, pos, color, 0.0f, Vector2.Zero, 1f, SpriteEffects.None, depth, colorData);
        }

        public static void DrawRectangle(SpriteBatch sb, Vector2 start, Vector2 size, Color clr, bool isFilled = false, float depth = 0.0f, int thickness = 1)
        {
            if (size.X < 0)
            {
                start.X += size.X;
                size.X = -size.X;
            }
            if (size.Y < 0)
            {
                start.Y += size.Y;
                size.Y = -size.Y;
            }
            DrawRectangle(sb, new Rectangle((int)start.X, (int)start.Y, (int)size.X, (int)size.Y), clr, isFilled, depth, thickness);
        }

        public static void DrawRectangle(SpriteBatch sb, Rectangle rect, Color clr, bool isFilled = false, float depth = 0.0f, int thickness = 1)
        {
            if (isFilled)
            {
                sb.Draw(t, rect, null, clr, 0.0f, Vector2.Zero, SpriteEffects.None, depth);
            }
            else
            {
                sb.Draw(t, new Rectangle(rect.X + thickness, rect.Y, rect.Width - thickness * 2, thickness), null, clr, 0.0f, Vector2.Zero, SpriteEffects.None, depth);
                sb.Draw(t, new Rectangle(rect.X + thickness, rect.Y + rect.Height - thickness, rect.Width - thickness * 2, thickness), null, clr, 0.0f, Vector2.Zero, SpriteEffects.None, depth);

                sb.Draw(t, new Rectangle(rect.X, rect.Y, thickness, rect.Height), null, clr, 0.0f, Vector2.Zero, SpriteEffects.None, depth);
                sb.Draw(t, new Rectangle(rect.X + rect.Width - thickness, rect.Y, thickness, rect.Height), null, clr, 0.0f, Vector2.Zero, SpriteEffects.None, depth);
            }
        }

        public static void DrawRectangle(SpriteBatch sb, Vector2 center, float width, float height, float rotation, Color clr, float depth = 0.0f, int thickness = 1)
        {
            Matrix rotate = Matrix.CreateRotationZ(rotation);

            width *= 0.5f;
            height *= 0.5f;
            Vector2 topLeft = center + Vector2.Transform(new Vector2(-width, -height), rotate);
            Vector2 topRight = center + Vector2.Transform(new Vector2(width, -height), rotate);
            Vector2 bottomLeft = center + Vector2.Transform(new Vector2(-width, height), rotate);
            Vector2 bottomRight = center + Vector2.Transform(new Vector2(width, height), rotate);

            DrawLine(sb, topLeft, topRight, clr, depth, thickness);
            DrawLine(sb, topRight, bottomRight, clr, depth, thickness);
            DrawLine(sb, bottomRight, bottomLeft, clr, depth, thickness);
            DrawLine(sb, bottomLeft, topLeft, clr, depth, thickness);
        }

        public static void DrawRectangle(SpriteBatch sb, Vector2[] corners, Color clr, float depth = 0.0f, int thickness = 1)
        {
            if (corners.Length != 4)
            {
                throw new Exception("Invalid length of the corners array! Must be 4");
            }
            DrawLine(sb, corners[0], corners[1], clr, depth, thickness);
            DrawLine(sb, corners[1], corners[2], clr, depth, thickness);
            DrawLine(sb, corners[2], corners[3], clr, depth, thickness);
            DrawLine(sb, corners[3], corners[0], clr, depth, thickness);
        }

        public static void DrawProgressBar(SpriteBatch sb, Vector2 start, Vector2 size, float progress, Color clr, float depth = 0.0f)
        {
            DrawProgressBar(sb, start, size, progress, clr, new Color(0.5f, 0.57f, 0.6f, 1.0f), depth);
        }

        public static void DrawProgressBar(SpriteBatch sb, Vector2 start, Vector2 size, float progress, Color clr, Color outlineColor, float depth = 0.0f)
        {
            DrawRectangle(sb, new Vector2(start.X, -start.Y), size, outlineColor, false, depth);

            int padding = 2;
            DrawRectangle(sb, new Rectangle((int)start.X + padding, -(int)(start.Y - padding), (int)((size.X - padding * 2) * progress), (int)size.Y - padding * 2),
                clr, true, depth);
        }

        public static bool DrawButton(SpriteBatch sb, Rectangle rect, string text, Color color, bool isHoldable = false)
        {
            bool clicked = false;

            if (rect.Contains(PlayerInput.MousePosition))
            {
                clicked = PlayerInput.PrimaryMouseButtonHeld();

                color = clicked ?
                    new Color((int)(color.R * 0.8f), (int)(color.G * 0.8f), (int)(color.B * 0.8f), color.A) :
                    new Color((int)(color.R * 1.2f), (int)(color.G * 1.2f), (int)(color.B * 1.2f), color.A);

                if (!isHoldable) clicked = PlayerInput.PrimaryMouseButtonClicked();
            }

            DrawRectangle(sb, rect, color, true);

            Vector2 origin;
            try
            {
                origin = Font.MeasureString(text) / 2;
            }
            catch
            {
                origin = Vector2.Zero;
            }

            Font.DrawString(sb, text, new Vector2(rect.Center.X, rect.Center.Y), Color.White, 0.0f, origin, 1.0f, SpriteEffects.None, 0.0f);

            return clicked;
        }

        private static void DrawMessages(SpriteBatch spriteBatch, Camera cam)
        {
            if (messages.Count == 0) return;

            bool useScissorRect = messages.Any(m => !m.WorldSpace);
            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            if (useScissorRect)
            {
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = HUDLayoutSettings.MessageAreaTop;
                spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);
            }

            foreach (GUIMessage msg in messages)
            {
                if (msg.WorldSpace) continue;

                Vector2 drawPos = new Vector2(HUDLayoutSettings.MessageAreaTop.Right, HUDLayoutSettings.MessageAreaTop.Center.Y);

                msg.Font.DrawString(spriteBatch, msg.Text, drawPos + msg.Pos + Vector2.One, Color.Black, 0, msg.Origin, 1.0f, SpriteEffects.None, 0);
                msg.Font.DrawString(spriteBatch, msg.Text, drawPos + msg.Pos, msg.Color, 0, msg.Origin, 1.0f, SpriteEffects.None, 0);
                break;                
            }

            if (useScissorRect)
            {
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred);
            }
            
            foreach (GUIMessage msg in messages)
            {
                if (!msg.WorldSpace) continue;
                
                if (cam != null)
                {
                    float alpha = 1.0f;
                    if (msg.Timer < 1.0f) alpha -= 1.0f - msg.Timer;                    

                    Vector2 drawPos = cam.WorldToScreen(msg.Pos);
                    msg.Font.DrawString(spriteBatch, msg.Text, drawPos + Vector2.One, Color.Black * alpha, 0, msg.Origin, 1.0f, SpriteEffects.None, 0);
                    msg.Font.DrawString(spriteBatch, msg.Text, drawPos, msg.Color * alpha, 0, msg.Origin, 1.0f, SpriteEffects.None, 0);
                }                
            }

            messages.RemoveAll(m => m.Timer <= 0.0f);
        }

        /// <summary>
        /// Draws a bezier curve with dots.
        /// </summary>
        public static void DrawBezierWithDots(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Vector2 control, int pointCount, Color color, int dotSize = 2)
        {
            for (int i = 0; i < pointCount; i++)
            {
                float t = (float)i / (pointCount - 1);
                Vector2 pos = MathUtils.Bezier(start, control, end, t);
                ShapeExtensions.DrawPoint(spriteBatch, pos, color, dotSize);
            }
        }

        public static void DrawSineWithDots(SpriteBatch spriteBatch, Vector2 from, Vector2 dir, float amplitude, float length, float scale, int pointCount, Color color, int dotSize = 2)
        {
            Vector2 up = dir.Right();
            //DrawLine(spriteBatch, from, from + dir, GUI.Style.Red);
            //DrawLine(spriteBatch, from, from + up * dir.Length(), Color.Blue);
            for (int i = 0; i < pointCount; i++)
            {
                Vector2 pos = from;
                if (i > 0)
                {
                    float t = (float)i / (pointCount - 1);
                    float sin = (float)Math.Sin(t / length * scale) * amplitude;
                    pos += (up * sin) + (dir * t);
                }
                ShapeExtensions.DrawPoint(spriteBatch, pos, color, dotSize);
            }
        }
        #endregion

        #region Element creation

        public static Texture2D CreateCircle(int radius, bool filled = false)
        {
            int outerRadius = radius * 2 + 2; // So circle doesn't go out of bounds

            Color[] data = new Color[outerRadius * outerRadius];

            // Colour the entire texture transparent first.
            for (int i = 0; i < data.Length; i++)
                data[i] = Color.Transparent;

            if (filled)
            {
                float diameterSqr = radius * radius;
                for (int x = 0; x < outerRadius; x++)
                {
                    for (int y = 0; y < outerRadius; y++)
                    {
                        Vector2 pos = new Vector2(radius - x, radius - y);
                        if (pos.LengthSquared() <= diameterSqr)
                        {
                            TrySetArray(data, y * outerRadius + x + 1, Color.White);
                        }
                    }
                }
            }
            else
            {
                // Work out the minimum step necessary using trigonometry + sine approximation.
                double angleStep = 1f / radius;

                for (double angle = 0; angle < Math.PI * 2; angle += angleStep)
                {
                    // Use the parametric definition of a circle: http://en.wikipedia.org/wiki/Circle#Cartesian_coordinates
                    int x = (int)Math.Round(radius + radius * Math.Cos(angle));
                    int y = (int)Math.Round(radius + radius * Math.Sin(angle));

                    TrySetArray(data, y * outerRadius + x + 1, Color.White);
                }
            }

            Texture2D texture = null;
            CrossThread.RequestExecutionOnMainThread(() =>
            {
                texture = new Texture2D(GraphicsDevice, outerRadius, outerRadius);
                texture.SetData(data);
            });
            return texture;
        }

        public static Texture2D CreateCapsule(int radius, int height)
        {
            int textureWidth = radius * 2, textureHeight = height + radius * 2;
            
            Color[] data = new Color[textureWidth * textureHeight];

            // Colour the entire texture transparent first.
            for (int i = 0; i < data.Length; i++)
                data[i] = Color.Transparent;

            // Work out the minimum step necessary using trigonometry + sine approximation.
            double angleStep = 1f / radius;

            for (int i = 0; i < 2; i++)
            {
                for (double angle = 0; angle < Math.PI * 2; angle += angleStep)
                {
                    // Use the parametric definition of a circle: http://en.wikipedia.org/wiki/Circle#Cartesian_coordinates
                    int x = (int)Math.Round(radius + radius * Math.Cos(angle));
                    int y = (height - 1) * i + (int)Math.Round(radius + radius * Math.Sin(angle));

                    TrySetArray(data, y * textureWidth + x, Color.White);
                }
            }

            for (int y = radius; y < textureHeight - radius; y++)
            {
                TrySetArray(data, y * textureWidth, Color.White);
                TrySetArray(data, y * textureWidth + (textureWidth - 1), Color.White);
            }

            Texture2D texture = null;
            CrossThread.RequestExecutionOnMainThread(() =>
            {
                texture = new Texture2D(GraphicsDevice, textureWidth, textureHeight);
                texture.SetData(data);
            });
            return texture;
        }

        public static Texture2D CreateRectangle(int width, int height)
        {
            width = Math.Max(width, 1);
            height = Math.Max(height, 1);
            Color[] data = new Color[width * height];

            for (int i = 0; i < data.Length; i++)
                data[i] = Color.Transparent;

            for (int y = 0; y < height; y++)
            {
                TrySetArray(data, y * width, Color.White);
                TrySetArray(data, y * width + (width - 1), Color.White);
            }

            for (int x = 0; x < width; x++)
            {
                TrySetArray(data, x, Color.White);
                TrySetArray(data, (height - 1) * width + x, Color.White);
            }

            Texture2D texture = null;
            CrossThread.RequestExecutionOnMainThread(() =>
            {
                texture = new Texture2D(GraphicsDevice, width, height);
                texture.SetData(data);
            });
            return texture;
        }

        private static bool TrySetArray(Color[] data, int index, Color value)
        {
            if (index >= 0 && index < data.Length)
            {
                data[index] = value;
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Creates multiple buttons with relative size and positions them automatically.
        /// </summary>
        public static List<GUIButton> CreateButtons(int count, Vector2 relativeSize, RectTransform parent,
            Anchor anchor = Anchor.TopLeft, Pivot? pivot = null, Point? minSize = null, Point? maxSize = null,
            int absoluteSpacing = 0, float relativeSpacing = 0, Func<int, int> extraSpacing = null,
            int startOffsetAbsolute = 0, float startOffsetRelative = 0, bool isHorizontal = false,
            Alignment textAlignment = Alignment.Center, string style = "")
        {
            Func<RectTransform, GUIButton> constructor = rectT => new GUIButton(rectT, string.Empty, textAlignment, style);
            return CreateElements(count, relativeSize, parent, constructor, anchor, pivot, minSize, maxSize, absoluteSpacing, relativeSpacing, extraSpacing, startOffsetAbsolute, startOffsetRelative, isHorizontal);
        }

        /// <summary>
        /// Creates multiple buttons with absolute size and positions them automatically.
        /// </summary>
        public static List<GUIButton> CreateButtons(int count, Point absoluteSize, RectTransform parent,
            Anchor anchor = Anchor.TopLeft, Pivot? pivot = null,
            int absoluteSpacing = 0, float relativeSpacing = 0, Func<int, int> extraSpacing = null,
            int startOffsetAbsolute = 0, float startOffsetRelative = 0, bool isHorizontal = false,
            Alignment textAlignment = Alignment.Center, string style = "")
        {
            Func<RectTransform, GUIButton> constructor = rectT => new GUIButton(rectT, string.Empty, textAlignment, style);
            return CreateElements(count, absoluteSize, parent, constructor, anchor, pivot, absoluteSpacing, relativeSpacing, extraSpacing, startOffsetAbsolute, startOffsetRelative, isHorizontal);
        }

        /// <summary>
        /// Creates multiple elements with relative size and positions them automatically.
        /// </summary>
        public static List<T> CreateElements<T>(int count, Vector2 relativeSize, RectTransform parent, Func<RectTransform, T> constructor,
            Anchor anchor = Anchor.TopLeft, Pivot? pivot = null, Point? minSize = null, Point? maxSize = null, 
            int absoluteSpacing = 0, float relativeSpacing = 0, Func<int, int> extraSpacing = null, 
            int startOffsetAbsolute = 0, float startOffsetRelative = 0, bool isHorizontal = false) 
            where T : GUIComponent
        {
            return CreateElements(count, parent, constructor, relativeSize, null, anchor, pivot, minSize, maxSize, absoluteSpacing, relativeSpacing, extraSpacing, startOffsetAbsolute, startOffsetRelative, isHorizontal);
        }

        /// <summary>
        /// Creates multiple elements with absolute size and positions them automatically.
        /// </summary>
        public static List<T> CreateElements<T>(int count, Point absoluteSize, RectTransform parent, Func<RectTransform, T> constructor, 
            Anchor anchor = Anchor.TopLeft, Pivot? pivot = null, 
            int absoluteSpacing = 0, float relativeSpacing = 0, Func<int, int> extraSpacing = null,
            int startOffsetAbsolute = 0, float startOffsetRelative = 0, bool isHorizontal = false)
            where T : GUIComponent
        {
            return CreateElements(count, parent, constructor, null, absoluteSize, anchor, pivot, null, null, absoluteSpacing, relativeSpacing, extraSpacing, startOffsetAbsolute, startOffsetRelative, isHorizontal);
        }

        public static GUIComponent CreateEnumField(Enum value, int elementHeight, string name, RectTransform parent, string toolTip = null, ScalableFont font = null)
        {
            font = font ?? SmallFont;
            var frame = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, elementHeight), parent), color: Color.Transparent);
            new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), name, font: font)
            {
                ToolTip = toolTip
            };
            GUIDropDown enumDropDown = new GUIDropDown(new RectTransform(new Vector2(0.4f, 1), frame.RectTransform, Anchor.TopRight),
                elementCount: Enum.GetValues(value.GetType()).Length)
            {
                ToolTip = toolTip
            };
            foreach (object enumValue in Enum.GetValues(value.GetType()))
            {
                enumDropDown.AddItem(enumValue.ToString(), enumValue);
            }
            enumDropDown.SelectItem(value);
            return frame;
        }

        public static GUIComponent CreateRectangleField(Rectangle value, int elementHeight, string name, RectTransform parent, string toolTip = null, ScalableFont font = null)
        {
            var frame = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, Math.Max(elementHeight, 26)), parent), color: Color.Transparent);
            font = font ?? SmallFont;
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), frame.RectTransform), name, font: font)
            {
                ToolTip = toolTip
            };
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            for (int i = 3; i >= 0; i--)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.22f, 1), inputArea.RectTransform) { MinSize = new Point(50, 0), MaxSize = new Point(150, 50) }, style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), rectComponentLabels[i], font: font, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    GUINumberInput.NumberType.Int)
                {
                    Font = font
                };
                // Not sure if the min value could in any case be negative.
                numberInput.MinValueInt = 0;
                // Just something reasonable to keep the value in the input rect.
                numberInput.MaxValueInt = 9999;
                switch (i)
                {
                    case 0:
                        numberInput.IntValue = value.X;
                        break;
                    case 1:
                        numberInput.IntValue = value.Y;
                        break;
                    case 2:
                        numberInput.IntValue = value.Width;
                        break;
                    case 3:
                        numberInput.IntValue = value.Height;
                        break;
                }
            }
            return frame;
        }

        public static GUIComponent CreatePointField(Point value, int elementHeight, string displayName, RectTransform parent, string toolTip = null)
        {
            var frame = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, Math.Max(elementHeight, 26)), parent), color: Color.Transparent);
            new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), frame.RectTransform), displayName, font: SmallFont)
            {
                ToolTip = toolTip
            };
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            for (int i = 1; i >= 0; i--)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.45f, 1), inputArea.RectTransform), style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), vectorComponentLabels[i], font: SmallFont, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    GUINumberInput.NumberType.Int)
                {
                    Font = SmallFont
                };

                if (i == 0)
                    numberInput.IntValue = value.X;
                else
                    numberInput.IntValue = value.Y;                
            }
            return frame;
        }

        public static GUIComponent CreateVector2Field(Vector2 value, int elementHeight, string name, RectTransform parent, string toolTip = null, ScalableFont font = null, int decimalsToDisplay = 1)
        {
            font = font ?? SmallFont;
            var frame = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, Math.Max(elementHeight, 26)), parent), color: Color.Transparent);
            new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), frame.RectTransform), name, font: font)
            {
                ToolTip = toolTip
            };
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            for (int i = 1; i >= 0; i--)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.45f, 1), inputArea.RectTransform), style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), vectorComponentLabels[i], font: font, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight), GUINumberInput.NumberType.Float) { Font = font };
                switch (i)
                {
                    case 0:
                        numberInput.FloatValue = value.X;
                        break;
                    case 1:
                        numberInput.FloatValue = value.Y;
                        break;
                }
                numberInput.DecimalsToDisplay = decimalsToDisplay;
            }
            return frame;
        }
        #endregion

        #region Element positioning
        private static List<T> CreateElements<T>(int count, RectTransform parent, Func<RectTransform, T> constructor,
            Vector2? relativeSize = null, Point? absoluteSize = null,
            Anchor anchor = Anchor.TopLeft, Pivot? pivot = null, Point? minSize = null, Point? maxSize = null,
            int absoluteSpacing = 0, float relativeSpacing = 0, Func<int, int> extraSpacing = null,
            int startOffsetAbsolute = 0, float startOffsetRelative = 0, bool isHorizontal = false)
            where T : GUIComponent
        {
            var elements = new List<T>();
            int extraTotal = 0;
            for (int i = 0; i < count; i++)
            {
                if (extraSpacing != null)
                {
                    extraTotal += extraSpacing(i);
                }
                if (relativeSize.HasValue)
                {
                    var size = relativeSize.Value;
                    var offsets = CalculateOffsets(size, startOffsetRelative, startOffsetAbsolute, relativeSpacing, absoluteSpacing, i, extraTotal, isHorizontal);
                    elements.Add(constructor(new RectTransform(size, parent, anchor, pivot, minSize, maxSize)
                    {
                        RelativeOffset = offsets.Item1,
                        AbsoluteOffset = offsets.Item2
                    }));
                }
                else
                {
                    var size = absoluteSize.Value;
                    var offsets = CalculateOffsets(size, startOffsetRelative, startOffsetAbsolute, relativeSpacing, absoluteSpacing, i, extraTotal, isHorizontal);
                    elements.Add(constructor(new RectTransform(size, parent, anchor, pivot)
                    {
                        RelativeOffset = offsets.Item1,
                        AbsoluteOffset = offsets.Item2
                    }));
                }
            }
            return elements;
        }

        private static Tuple<Vector2, Point> CalculateOffsets(Vector2 relativeSize, float startOffsetRelative, int startOffsetAbsolute, float relativeSpacing, int absoluteSpacing, int counter, int extra, bool isHorizontal)
        {
            float relX = 0, relY = 0;
            int absX = 0, absY = 0;
            if (isHorizontal)
            {
                relX = CalculateRelativeOffset(startOffsetRelative, relativeSpacing, relativeSize.X, counter);
                absX = CalculateAbsoluteOffset(startOffsetAbsolute, absoluteSpacing, counter, extra);
            }
            else
            {
                relY = CalculateRelativeOffset(startOffsetRelative, relativeSpacing, relativeSize.Y, counter);
                absY = CalculateAbsoluteOffset(startOffsetAbsolute, absoluteSpacing, counter, extra);
            }
            return Tuple.Create(new Vector2(relX, relY), new Point(absX, absY));
        }

        private static Tuple<Vector2, Point> CalculateOffsets(Point absoluteSize, float startOffsetRelative, int startOffsetAbsolute, float relativeSpacing, int absoluteSpacing, int counter, int extra, bool isHorizontal)
        {
            float relX = 0, relY = 0;
            int absX = 0, absY = 0;
            if (isHorizontal)
            {
                relX = CalculateRelativeOffset(startOffsetRelative, relativeSpacing, counter);
                absX = CalculateAbsoluteOffset(startOffsetAbsolute, absoluteSpacing, absoluteSize.X, counter, extra);
            }
            else
            {
                relY = CalculateRelativeOffset(startOffsetRelative, relativeSpacing, counter);
                absY = CalculateAbsoluteOffset(startOffsetAbsolute, absoluteSpacing, absoluteSize.Y, counter, extra);
            }
            return Tuple.Create(new Vector2(relX, relY), new Point(absX, absY));
        }

        private static float CalculateRelativeOffset(float startOffset, float spacing, float size, int counter)
        {
            return startOffset + (spacing + size) * counter;
        }

        private static float CalculateRelativeOffset(float startOffset, float spacing, int counter)
        {
            return startOffset + spacing * counter;
        }

        private static int CalculateAbsoluteOffset(int startOffset, int spacing, int counter, int extra)
        {
            return startOffset + spacing * counter + extra;
        }

        private static int CalculateAbsoluteOffset(int startOffset, int spacing, int size, int counter, int extra)
        {
            return startOffset + (spacing + size) * counter + extra;
        }

        /// <summary>
        /// Attempts to move a set of UI elements further from each other to prevent them from overlapping
        /// </summary>
        /// <param name="elements">UI elements to move</param>
        /// <param name="disallowedAreas">Areas the UI elements are not allowed to overlap with (ignored if null)</param>
        /// <param name="clampArea">The elements will not be moved outside this area. If the parameter is not given, the elements are kept inside the window.</param>
        public static void PreventElementOverlap(IList<GUIComponent> elements, IList<Rectangle> disallowedAreas = null,  Rectangle? clampArea = null)
        {
            Rectangle area = clampArea ?? new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            for (int i = 0; i < elements.Count; i++)
            {
                Point moveAmount = Point.Zero;
                Rectangle rect1 = elements[i].Rect;
                moveAmount.X += Math.Max(area.X - rect1.X, 0);
                moveAmount.X -= Math.Max(rect1.Right - area.Right, 0);
                moveAmount.Y += Math.Max(area.Y - rect1.Y, 0);
                moveAmount.Y -= Math.Max(rect1.Bottom - area.Bottom, 0);
                elements[i].RectTransform.ScreenSpaceOffset += moveAmount;
            }

            bool intersections = true;
            int iterations = 0;
            while (intersections && iterations < 100)
            {
                intersections = false;
                for (int i = 0; i < elements.Count; i++)
                {
                    Rectangle rect1 = elements[i].Rect;
                    for (int j = i + 1; j < elements.Count; j++)
                    {
                        Rectangle rect2 = elements[j].Rect;
                        if (!rect1.Intersects(rect2)) continue;

                        intersections = true;
                        Point centerDiff = rect1.Center - rect2.Center;
                        //move the interfaces away from each other, in a random direction if they're at the same position
                        Vector2 moveAmount = centerDiff == Point.Zero ? Rand.Vector(1.0f) : Vector2.Normalize(centerDiff.ToVector2());

                        //if the horizontal move amount is much larger than vertical, only move horizontally
                        //(= attempt to place the elements side-by-side if they're more apart horizontally than vertically)
                        if (Math.Abs(moveAmount.X) > Math.Abs(moveAmount.Y) * 8.0f)
                        {
                            moveAmount.Y = 0.0f;
                        }
                        //same for the y-axis
                        else if (Math.Abs(moveAmount.Y) > Math.Abs(moveAmount.X) * 8.0f)
                        {
                            moveAmount.X = 0.0f;
                        }

                        //make sure we don't move the interfaces out of the screen
                        Vector2 moveAmount1 = ClampMoveAmount(rect1, area, moveAmount * 10.0f);
                        Vector2 moveAmount2 = ClampMoveAmount(rect2, area, -moveAmount * 10.0f);

                        //move by 10 units in the desired direction and repeat until nothing overlaps
                        //(or after 100 iterations, in which case we'll just give up and let them overlap)
                        elements[i].RectTransform.ScreenSpaceOffset += moveAmount1.ToPoint();
                        elements[j].RectTransform.ScreenSpaceOffset += moveAmount2.ToPoint();
                    }

                    if (disallowedAreas == null) continue;
                    foreach (Rectangle rect2 in disallowedAreas)
                    {
                        if (!rect1.Intersects(rect2)) continue;
                        intersections = true;

                        Point centerDiff = rect1.Center - rect2.Center;
                        //move the interface away from the disallowed area
                        Vector2 moveAmount = centerDiff == Point.Zero ? Rand.Vector(1.0f) : Vector2.Normalize(centerDiff.ToVector2());

                        //make sure we don't move the interface out of the screen
                        Vector2 moveAmount1 = ClampMoveAmount(rect1, area, moveAmount * 10.0f);

                        //move by 10 units in the desired direction and repeat until nothing overlaps
                        //(or after 100 iterations, in which case we'll just give up and let them overlap)
                        elements[i].RectTransform.ScreenSpaceOffset += (moveAmount1).ToPoint();
                    }
                }
                iterations++;
            }

            Vector2 ClampMoveAmount(Rectangle Rect, Rectangle clampTo, Vector2 moveAmount)
            {
                if (Rect.Y < clampTo.Y)
                {
                    moveAmount.Y = Math.Max(moveAmount.Y, 0.0f);
                }
                else if (Rect.Bottom > clampTo.Bottom)
                {
                    moveAmount.Y = Math.Min(moveAmount.Y, 0.0f);
                }
                if (Rect.X < clampTo.X)
                {
                    moveAmount.X = Math.Max(moveAmount.X, 0.0f);
                }
                else if (Rect.Right > clampTo.Right)
                {
                    moveAmount.X = Math.Min(moveAmount.X, 0.0f);
                }
                return moveAmount;
            }
        }

        #endregion

        #region Misc
        public static void TogglePauseMenu()
        {
            if (Screen.Selected == GameMain.MainMenuScreen) return;
            if (PreventPauseMenuToggle) return;

            settingsMenuOpen = false;

            TogglePauseMenu(null, null);

            if (pauseMenuOpen)
            {
                Inventory.draggingItem = null;
                Inventory.DraggingInventory = null;

                PauseMenu = new GUIFrame(new RectTransform(Vector2.One, Canvas), style: null, color: Color.Black * 0.5f);
                    
                var pauseMenuInner = new GUIFrame(new RectTransform(new Vector2(0.13f, 0.3f), PauseMenu.RectTransform, Anchor.Center) { MinSize = new Point(250, 300) });

                var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 0.6f), pauseMenuInner.RectTransform, Anchor.Center))
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                var button = new GUIButton(new RectTransform(new Vector2(0.1f, 0.1f), pauseMenuInner.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point((int)(15 * GUI.Scale)) }, 
                    "", style: "GUIBugButton")
                {
                    IgnoreLayoutGroups = true,
                    ToolTip = TextManager.Get("bugreportbutton"),
                    OnClicked = (btn, userdata) => { GameMain.Instance.ShowBugReporter(); return true; }
                };

                button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonContainer.RectTransform), TextManager.Get("PauseMenuResume"))
                {
                    OnClicked = TogglePauseMenu
                };

                button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonContainer.RectTransform), TextManager.Get("PauseMenuSettings"))
                {
                    OnClicked = (btn, userData) =>
                    {
                        TogglePauseMenu();
                        settingsMenuOpen = !settingsMenuOpen;
                        return true;
                    }
                };

                if (Screen.Selected == GameMain.GameScreen && GameMain.GameSession != null)
                {
                    if (GameMain.GameSession.GameMode is SinglePlayerCampaign spMode)
                    {
                        button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonContainer.RectTransform), TextManager.Get("PauseMenuRetry"));
                        button.OnClicked += (btn, userData) =>
                        {
                            var msgBox = new GUIMessageBox("", TextManager.Get("PauseMenuRetryVerification"), new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") })
                            {
                                UserData = "verificationprompt"
                            };
                            msgBox.Buttons[0].OnClicked = (_, userdata) =>
                            {
                                TogglePauseMenu(btn, userData);
                                GameMain.GameSession.LoadPrevious();
                                GameMain.LobbyScreen.Select();
                                return true;
                            };
                            msgBox.Buttons[0].OnClicked += msgBox.Close;
                            msgBox.Buttons[1].OnClicked = (_, userdata) =>
                            {
                                TogglePauseMenu(btn, userData);
                                msgBox.Close();
                                return true;
                            };
                            return true;
                        };
                    }
                    else if (!GameMain.GameSession.GameMode.IsSinglePlayer && GameMain.Client != null && GameMain.Client.HasPermission(ClientPermissions.ManageRound))
                    {
                        new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonContainer.RectTransform), text: TextManager.Get("EndRound"))
                        {
                            OnClicked = (btn, userdata) =>
                            {
                                if (!GameMain.Client.HasPermission(ClientPermissions.ManageRound)) { return false; }
                                if (!Submarine.MainSub.AtStartPosition && !Submarine.MainSub.AtEndPosition)
                                {
                                    var msgBox = new GUIMessageBox("", TextManager.Get("EndRoundSubNotAtLevelEnd"), new string[] { TextManager.Get("Yes"), TextManager.Get("No") })
                                    {
                                        UserData = "verificationprompt"
                                    };
                                    msgBox.Buttons[0].OnClicked = (_, __) =>
                                    {
                                        TogglePauseMenu(btn, userdata);
                                        GameMain.Client.RequestRoundEnd();
                                        return true;
                                    };
                                    msgBox.Buttons[0].OnClicked += msgBox.Close;
                                    msgBox.Buttons[1].OnClicked += msgBox.Close;
                                }
                                else
                                {
                                    TogglePauseMenu(btn, userdata);
                                    GameMain.Client.RequestRoundEnd();
                                }
                                return true;
                            }
                        };
                    }
                }

                if (Screen.Selected == GameMain.LobbyScreen)
                {
                    if (GameMain.GameSession.GameMode is SinglePlayerCampaign spMode)
                    {
                        button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonContainer.RectTransform), TextManager.Get("PauseMenuSaveQuit"))
                        {
                            UserData = "save"
                        };
                        button.OnClicked += QuitClicked;
                        button.OnClicked += TogglePauseMenu;
                    }
                }
                
                button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonContainer.RectTransform), TextManager.Get("PauseMenuQuit"));
                button.OnClicked += (btn, userData) =>
                {
                    var quitButton = button;
                    if (GameMain.GameSession != null || (Screen.Selected is CharacterEditorScreen || Screen.Selected is SubEditorScreen))
                    {
                        string text = GameMain.GameSession == null ? "PauseMenuQuitVerificationEditor" : "PauseMenuQuitVerification";
                        var msgBox = new GUIMessageBox("", TextManager.Get(text), new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") })
                        {
                            UserData = "verificationprompt"
                        };
                        msgBox.Buttons[0].OnClicked = (yesBtn, userdata) =>
                        {
                            QuitClicked(quitButton, quitButton.UserData);
                            pauseMenuOpen = false;
                            return true;
                        };
                        msgBox.Buttons[0].OnClicked += msgBox.Close;
                        msgBox.Buttons[1].OnClicked = (_, userdata) =>
                        {
                            TogglePauseMenu(btn, userData);
                            msgBox.Close();
                            return true;
                        };
                    }
                    else
                    {
                        QuitClicked(quitButton, quitButton.UserData);
                        pauseMenuOpen = false;
                    }
                    return true;
                };
            }
        }

        private static bool TogglePauseMenu(GUIButton button, object obj)
        {
            pauseMenuOpen = !pauseMenuOpen;
            return true;
        }

        public static bool QuitClicked(GUIButton button, object obj)
        {
            GameMain.QuitToMainMenu(button.UserData as string == "save");
            return true;
        }

        /// <summary>
        /// Displays a message at the center of the screen, automatically preventing overlapping with other centered messages. TODO: Allow to show messages at the middle of the screen (instead of the top center).
        /// </summary>
        public static void AddMessage(string message, Color color, float? lifeTime = null, bool playSound = true, ScalableFont font = null)
        {
            if (messages.Any(msg => msg.Text == message)) { return; }
            messages.Add(new GUIMessage(message, color, lifeTime ?? MathHelper.Clamp(message.Length / 5.0f, 3.0f, 10.0f), font ?? LargeFont));
            if (playSound) PlayUISound(GUISoundType.UIMessage);
        }

        public static void AddMessage(string message, Color color, Vector2 worldPos, Vector2 velocity, float lifeTime = 3.0f, bool playSound = true)
        {
            messages.Add(new GUIMessage(message, color, worldPos, velocity, lifeTime, Alignment.Center, LargeFont));
            if (playSound) PlayUISound(GUISoundType.UIMessage);
        }

        public static void ClearMessages()
        {
            messages.Clear();
        }

        public static void PlayUISound(GUISoundType soundType)
        {
            if (sounds == null) { return; }

            int soundIndex = (int)soundType;
            if (soundIndex < 0 || soundIndex >= sounds.Length) { return; }

            sounds[soundIndex]?.Play(null, "ui");
        }

        public static bool IsFourByThree()
        {
            float aspectRatio = HorizontalAspectRatio;
            return aspectRatio > 1.3f && aspectRatio < 1.4f;
        }
        #endregion
    }
}
