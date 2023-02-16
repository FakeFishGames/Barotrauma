using System;
using System.Collections.Generic;
using System.Diagnostics;
using Barotrauma.IO;
using System.Linq;
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
using System.Collections.Immutable;

namespace Barotrauma
{
    public enum GUISoundType
    {
        UIMessage,
        ChatMessage,
        RadioMessage,
        DeadMessage,
        Select,
        PickItem,
        PickItemFail,
        DropItem,
        PopupMenu,
        Decrease,
        Increase,
        UISwitch,
        TickBox,
        ConfirmTransaction,
        Cart,
    }

    public enum CursorState
    {
        Default = 0, // Cursor
        Hand = 1, // Hand with a finger
        Move = 2, // arrows pointing to all directions
        IBeam = 3, // Text
        Dragging = 4,// Closed hand
        Waiting = 5, // Hourglass
        WaitingBackground = 6, // Cursor + Hourglass
    }

    static class GUI
    {
        // Controls where a line is drawn for given coords.
        public enum OutlinePosition
        {
            Default = 0, // Thickness is inside of top left and outside of bottom right coord
            Inside = 1, // Thickness is subtracted from the inside
            Centered = 2, // Thickness is centered on given coords
            Outside = 3, // Tickness is added to the outside
        }
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

        public static readonly string[] VectorComponentLabels = { "X", "Y", "Z", "W" };
        public static readonly string[] RectComponentLabels = { "X", "Y", "W", "H" };
        public static readonly string[] ColorComponentLabels = { "R", "G", "B", "A" };

        private static readonly object mutex = new object();

        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        public static float Scale => (UIWidth / ReferenceResolution.X + GameMain.GraphicsHeight / ReferenceResolution.Y) / 2.0f * GameSettings.CurrentConfig.Graphics.HUDScale;
        public static float xScale => UIWidth / ReferenceResolution.X * GameSettings.CurrentConfig.Graphics.HUDScale;
        public static float yScale => GameMain.GraphicsHeight / ReferenceResolution.Y * GameSettings.CurrentConfig.Graphics.HUDScale;
        public static int IntScale(float f) => (int)(f * Scale);
        public static int IntScaleFloor(float f) => (int)Math.Floor(f * Scale);
        public static int IntScaleCeiling(float f) => (int)Math.Ceiling(f * Scale);
        public static float AdjustForTextScale(float f) => f * GameSettings.CurrentConfig.Graphics.TextScale;
        public static float HorizontalAspectRatio => GameMain.GraphicsWidth / (float)GameMain.GraphicsHeight;
        public static float VerticalAspectRatio => GameMain.GraphicsHeight / (float)GameMain.GraphicsWidth;
        public static float RelativeHorizontalAspectRatio => HorizontalAspectRatio / (ReferenceResolution.X / ReferenceResolution.Y);
        public static float RelativeVerticalAspectRatio => VerticalAspectRatio / (ReferenceResolution.Y / ReferenceResolution.X);
        /// <summary>
        /// A horizontal scaling factor for low aspect ratios (small width relative to height)
        /// </summary>
        public static float AspectRatioAdjustment => HorizontalAspectRatio < 1.4f ? (1.0f - (1.4f - HorizontalAspectRatio)) : 1.0f;

        public static bool IsUltrawide => HorizontalAspectRatio > 2.0f;

        public static int UIWidth
        {
            get
            {
                if (IsUltrawide)
                {
                    return (int)(GameMain.GraphicsHeight * ReferenceResolution.X / ReferenceResolution.Y);
                }
                else
                {
                    return GameMain.GraphicsWidth;
                }
            }
        }

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

        private static Texture2D solidWhiteTexture;
        public static Texture2D WhiteTexture => solidWhiteTexture;
        private static GUICursor MouseCursorSprites => GUIStyle.CursorSprite;

        private static bool debugDrawSounds, debugDrawEvents;

        private static DebugDrawMetaData debugDrawMetaData;

        public struct DebugDrawMetaData
        {
            public bool Enabled;
            public bool FactionMetadata, UpgradeLevels, UpgradePrices;
            public int Offset;
        }

        public static GraphicsDevice GraphicsDevice => GameMain.Instance.GraphicsDevice;

        private static readonly List<GUIMessage> messages = new List<GUIMessage>();

        public static GUIFrame PauseMenu { get; private set; }
        public static GUIFrame SettingsMenuContainer { get; private set; }
        public static Sprite Arrow => GUIStyle.Arrow.Value.Sprite;

        public static bool HideCursor;

        public static KeyboardDispatcher KeyboardDispatcher { get; set; }

        /// <summary>
        /// Has the selected Screen changed since the last time the GUI was drawn.
        /// </summary>
        public static bool ScreenChanged;

        private static bool settingsMenuOpen;
        public static bool SettingsMenuOpen
        {
            get { return settingsMenuOpen; }
            set
            {
                if (value == SettingsMenuOpen) { return; }

                if (value)
                {
                    SettingsMenuContainer = new GUIFrame(new RectTransform(Vector2.One, Canvas, Anchor.Center), style: null);
                    new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, SettingsMenuContainer.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

                    var settingsMenuInner = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.8f), SettingsMenuContainer.RectTransform, Anchor.Center, scaleBasis: ScaleBasis.Smallest) { MinSize = new Point(640, 480) });
                    SettingsMenu.Create(settingsMenuInner.RectTransform);
                }
                else
                {
                    SettingsMenu.Instance?.Close();
                }
                settingsMenuOpen = value;
            }
        }

        public static bool PauseMenuOpen { get; private set; }

        public static bool InputBlockingMenuOpen
        {
            get
            {
                return PauseMenuOpen ||
                    SettingsMenuOpen ||
                    DebugConsole.IsOpen ||
                    GameSession.IsTabMenuOpen ||
                    GameMain.GameSession?.GameMode is { Paused: true } ||
                    CharacterHUD.IsCampaignInterfaceOpen ||
                    GameMain.GameSession?.Campaign is { SlideshowPlayer: { Finished: false, Visible: true } };
            }
        }

        public static bool PreventPauseMenuToggle = false;

        public static Color ScreenOverlayColor
        {
            get;
            set;
        }

        public static bool DisableHUD, DisableUpperHUD, DisableItemHighlights, DisableCharacterNames;

        private static bool isSavingIndicatorEnabled;
        private static Color savingIndicatorColor = Color.Transparent;
        private static bool IsSavingIndicatorVisible => savingIndicatorColor.A > 0;
        private static float savingIndicatorSpriteIndex;
        private static float savingIndicatorColorLerpAmount;
        private static SavingIndicatorState savingIndicatorState = SavingIndicatorState.None;
        private static float? timeUntilSavingIndicatorDisabled;

        private static string loadedSpritesText;
        private static DateTime loadedSpritesUpdateTime;

        private enum SavingIndicatorState
        {
            None,
            FadingIn,
            FadingOut
        }

        public static void Init()
        {
            // create 1x1 texture for line drawing
            CrossThread.RequestExecutionOnMainThread(() =>
            {
                solidWhiteTexture = new Texture2D(GraphicsDevice, 1, 1);
                solidWhiteTexture.SetData(new Color[] { Color.White });// fill the texture with white
            });
        }

        /// <summary>
        /// By default, all the gui elements are drawn automatically in the same order they appear on the update list.
        /// </summary>
        public static void Draw(Camera cam, SpriteBatch spriteBatch)
        {
            lock (mutex)
            {
                usedIndicatorAngles.Clear();

                if (ScreenChanged)
                {
                    updateList.Clear();
                    updateListSet.Clear();
                    Screen.Selected?.AddToGUIUpdateList();
                    ScreenChanged = false;
                }

                foreach (GUIComponent c in updateList)
                {
                    c.DrawAuto(spriteBatch);
                }

                // always draw IME preview on top of everything else
                foreach (GUIComponent c in updateList)
                {
                    if (c is not GUITextBox box) { continue; }
                    box.DrawIMEPreview(spriteBatch);
                }

                if (ScreenOverlayColor.A > 0.0f)
                {
                    DrawRectangle(
                        spriteBatch,
                        new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                        ScreenOverlayColor, true);
                }

#if UNSTABLE
                string line1 = "Barotrauma Unstable v" + GameMain.Version;
                string line2 = "(" + AssemblyInfo.BuildString + ", branch " + AssemblyInfo.GitBranch + ", revision " + AssemblyInfo.GitRevision + ")";

                Rectangle watermarkRect = new Rectangle(-50, GameMain.GraphicsHeight - 80, 50 + (int)(Math.Max(GUIStyle.LargeFont.MeasureString(line1).X, GUIStyle.Font.MeasureString(line2).X) * 1.2f), 100);
                float alpha = 1.0f;

                int yOffset = 0;

                if (Screen.Selected == GameMain.GameScreen)
                {
                    yOffset = (int)(-HUDLayoutSettings.ChatBoxArea.Height * 1.2f);
                    watermarkRect.Y += yOffset;
                }

                if (Screen.Selected == GameMain.GameScreen || Screen.Selected == GameMain.SubEditorScreen)
                {
                    alpha = 0.2f;
                }

                GUIStyle.GetComponentStyle("OuterGlow").Sprites[GUIComponent.ComponentState.None][0].Draw(
                    spriteBatch, watermarkRect, Color.Black * 0.8f * alpha);
                GUIStyle.LargeFont.DrawString(spriteBatch, line1,
                new Vector2(10, GameMain.GraphicsHeight - 30 - GUIStyle.LargeFont.MeasureString(line1).Y + yOffset), Color.White * 0.6f * alpha);
                GUIStyle.Font.DrawString(spriteBatch, line2,
                new Vector2(10, GameMain.GraphicsHeight - 30 + yOffset), Color.White * 0.6f * alpha);

                if (Screen.Selected != GameMain.GameScreen)
                {
                    var buttonRect =
                        new Rectangle(20 + (int)Math.Max(GUIStyle.LargeFont.MeasureString(line1).X, GUIStyle.Font.MeasureString(line2).X), GameMain.GraphicsHeight - (int)(45 * Scale) + yOffset, (int)(150 * Scale), (int)(40 * Scale));
                    if (DrawButton(spriteBatch, buttonRect, "Report Bug", GUIStyle.GetComponentStyle("GUIBugButton").Color * 0.8f))
                    {
                        GameMain.Instance.ShowBugReporter();
                    }
                }
#endif

                if (DisableHUD)
                {
                    DrawSavingIndicator(spriteBatch);
                    return;
                }

                float startY = 10.0f;
                float yStep = AdjustForTextScale(18) * yScale;
                if (GameMain.ShowFPS || GameMain.DebugDraw || GameMain.ShowPerf)
                {
                    float y = startY;
                    DrawString(spriteBatch, new Vector2(10, y),
                        "FPS: " + Math.Round(GameMain.PerformanceCounter.AverageFramesPerSecond),
                        Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                    if (GameMain.GameSession != null && GameMain.GameSession.RoundDuration > 1.0)
                    {
                        y += yStep;
                        DrawString(spriteBatch, new Vector2(10, y),
                            $"Physics: {GameMain.CurrentUpdateRate}",
                            (GameMain.CurrentUpdateRate < Timing.FixedUpdateRate) ? Color.Red : Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                    }
                    if (GameMain.DebugDraw || GameMain.ShowPerf)
                    {
                        y += yStep;
                        DrawString(spriteBatch, new Vector2(10, y),
                            "Active lights: " + Lights.LightManager.ActiveLightCount,
                            Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        y += yStep;
                        DrawString(spriteBatch, new Vector2(10, y),
                            "Physics: " + GameMain.World.UpdateTime.TotalMilliseconds + " ms",
                            Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        y += yStep;
                        try
                        {
                            DrawString(spriteBatch, new Vector2(10, y),
                                $"Bodies: {GameMain.World.BodyList.Count} ({GameMain.World.BodyList.Count(b => b != null && b.Awake && b.Enabled)} awake, {GameMain.World.BodyList.Count(b => b != null && b.Awake && b.BodyType == BodyType.Dynamic && b.Enabled)} dynamic)",
                                Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        }
                        catch (InvalidOperationException)
                        {
                            DebugConsole.AddWarning("Exception while rendering debug info. Physics bodies may have been created or removed while rendering.");
                        }
                        y += yStep;
                        DrawString(spriteBatch, new Vector2(10, y),
                            "Particle count: " + GameMain.ParticleManager.ParticleCount + "/" + GameMain.ParticleManager.MaxParticles,
                            Color.Lerp(GUIStyle.Green, GUIStyle.Red, (GameMain.ParticleManager.ParticleCount / (float)GameMain.ParticleManager.MaxParticles)), Color.Black * 0.5f, 0, GUIStyle.SmallFont);

                    }
                }

                if (GameMain.ShowPerf)
                {
                    float x = 400;
                    float y = startY;
                    DrawString(spriteBatch, new Vector2(x, y),
                        "Draw - Avg: " + GameMain.PerformanceCounter.DrawTimeGraph.Average().ToString("0.00") + " ms" +
                        " Max: " + GameMain.PerformanceCounter.DrawTimeGraph.LargestValue().ToString("0.00") + " ms",
                        GUIStyle.Green, Color.Black * 0.8f, font: GUIStyle.SmallFont);
                        y += yStep;
                    GameMain.PerformanceCounter.DrawTimeGraph.Draw(spriteBatch, new Rectangle((int)x, (int)y, 170, 50), color: GUIStyle.Green);
                    y += yStep * 4;

                    DrawString(spriteBatch, new Vector2(x, y),
                        "Update - Avg: " + GameMain.PerformanceCounter.UpdateTimeGraph.Average().ToString("0.00") + " ms" +
                        " Max: " + GameMain.PerformanceCounter.UpdateTimeGraph.LargestValue().ToString("0.00") + " ms",
                        Color.LightBlue, Color.Black * 0.8f, font: GUIStyle.SmallFont);
                    y += yStep;
                    GameMain.PerformanceCounter.UpdateTimeGraph.Draw(spriteBatch, new Rectangle((int)x, (int)y, 170, 50), color: Color.LightBlue);
                    y += yStep * 4;
                    foreach (string key in GameMain.PerformanceCounter.GetSavedIdentifiers.OrderBy(i => i))
                    {
                        float elapsedMillisecs = GameMain.PerformanceCounter.GetAverageElapsedMillisecs(key);

                        int categoryDepth = key.Count(c => c == ':');
                        //color the more fine-grained counters red more easily (ok for the whole Update to take a longer time than specific part of the update)
                        float runningSlowThreshold = 10.0f / categoryDepth;
                        DrawString(spriteBatch, new Vector2(x + categoryDepth * 15, y),
                            key.Split(':').Last() + ": " + elapsedMillisecs.ToString("0.00"),
                            ToolBox.GradientLerp(elapsedMillisecs / runningSlowThreshold, Color.LightGreen, GUIStyle.Yellow, GUIStyle.Orange, GUIStyle.Red, Color.Magenta), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        y += yStep;
                    }
                    if (Powered.Grids != null)
                    {
                        DrawString(spriteBatch, new Vector2(x, y), "Grids: " + Powered.Grids.Count, Color.LightGreen, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        y += yStep;
                    }
                    if (Settings.EnableDiagnostics)
                    {
                        x += yStep * 2;
                        DrawString(spriteBatch, new Vector2(x, y), "ContinuousPhysicsTime: " + GameMain.World.ContinuousPhysicsTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.ContinuousPhysicsTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        DrawString(spriteBatch, new Vector2(x, y + yStep), "ControllersUpdateTime: " + GameMain.World.ControllersUpdateTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.ControllersUpdateTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        DrawString(spriteBatch, new Vector2(x, y + yStep * 2), "AddRemoveTime: " + GameMain.World.AddRemoveTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.AddRemoveTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        DrawString(spriteBatch, new Vector2(x, y + yStep * 3), "NewContactsTime: " + GameMain.World.NewContactsTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.NewContactsTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        DrawString(spriteBatch, new Vector2(x, y + yStep * 4), "ContactsUpdateTime: " + GameMain.World.ContactsUpdateTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.ContactsUpdateTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        DrawString(spriteBatch, new Vector2(x, y + yStep * 5), "SolveUpdateTime: " + GameMain.World.SolveUpdateTime.TotalMilliseconds.ToString("0.00"), Color.Lerp(Color.LightGreen, GUIStyle.Red, (float)GameMain.World.SolveUpdateTime.TotalMilliseconds / 10.0f), Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                    }
                }

                if (GameMain.DebugDraw && !Submarine.Unloading && !(Screen.Selected is RoundSummaryScreen))
                {
                    float y = startY + yStep * 6;

                    if (Screen.Selected.Cam != null)
                    {
                        y += yStep;
                        DrawString(spriteBatch, new Vector2(10, y),
                            "Camera pos: " + Screen.Selected.Cam.Position.ToPoint() + ", zoom: " + Screen.Selected.Cam.Zoom,
                            Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                    }

                    if (Submarine.MainSub != null)
                    {
                        y += yStep;
                        DrawString(spriteBatch, new Vector2(10, y),
                            "Sub pos: " + Submarine.MainSub.WorldPosition.ToPoint(),
                            Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                    }

                    if (loadedSpritesText == null || DateTime.Now > loadedSpritesUpdateTime)
                    {
                        loadedSpritesText = "Loaded sprites: " + Sprite.LoadedSprites.Count() + "\n(" + Sprite.LoadedSprites.Select(s => s.FilePath).Distinct().Count() + " unique textures)";
                        loadedSpritesUpdateTime = DateTime.Now + new TimeSpan(0, 0, seconds: 5);
                    }
                    y += yStep * 2;
                    DrawString(spriteBatch, new Vector2(10, y), loadedSpritesText, Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);

                    if (debugDrawSounds)
                    {
                        float soundTextY = 0;
                        DrawString(spriteBatch, new Vector2(500, soundTextY),
                            "Sounds (Ctrl+S to hide): ", Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        soundTextY += yStep;

                        DrawString(spriteBatch, new Vector2(500, soundTextY),
                            "Current playback amplitude: " + GameMain.SoundManager.PlaybackAmplitude.ToString(), Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);

                        soundTextY += yStep;

                        DrawString(spriteBatch, new Vector2(500, soundTextY),
                            "Compressed dynamic range gain: " + GameMain.SoundManager.CompressionDynamicRangeGain.ToString(), Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);

                        soundTextY += yStep;

                        DrawString(spriteBatch, new Vector2(500, soundTextY),
                            "Loaded sounds: " + GameMain.SoundManager.LoadedSoundCount + " (" + GameMain.SoundManager.UniqueLoadedSoundCount + " unique)", Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        soundTextY += yStep;

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
                                    if (PlayerInput.MousePosition.Y >= soundTextY && PlayerInput.MousePosition.Y <= soundTextY + 12)
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

                            DrawString(spriteBatch, new Vector2(500, soundTextY), soundStr, clr, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                            soundTextY += yStep;
                        }
                    }
                    else
                    {
                        DrawString(spriteBatch, new Vector2(500, 0),
                            "Ctrl+S to show sound debug info", Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                    }


                    y += 185 * yScale;
                    if (debugDrawEvents)
                    {
                        DrawString(spriteBatch, new Vector2(10, y),
                            "Ctrl+E to hide EventManager debug info", Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        GameMain.GameSession?.EventManager?.DebugDrawHUD(spriteBatch, y + 15 * yScale);
                    }
                    else
                    {
                        DrawString(spriteBatch, new Vector2(10, y),
                            "Ctrl+E to show EventManager debug info", Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                    }

                    if (GameMain.GameSession?.GameMode is CampaignMode campaignMode)
                    {
                        // TODO: TEST THIS
                        if (debugDrawMetaData.Enabled)
                        {
                            string text = "Ctrl+M to hide campaign metadata debug info\n\n" +
                                          $"Ctrl+1 to {(debugDrawMetaData.FactionMetadata ? "hide" : "show")} faction reputations, \n" +
                                          $"Ctrl+2 to {(debugDrawMetaData.UpgradeLevels ? "hide" : "show")} upgrade levels, \n" +
                                          $"Ctrl+3 to {(debugDrawMetaData.UpgradePrices ? "hide" : "show")} upgrade prices";
                            Vector2 textSize = GUIStyle.SmallFont.MeasureString(text);
                            Vector2 pos = new Vector2(GameMain.GraphicsWidth - (textSize.X + 10), 300);
                            DrawString(spriteBatch, pos, text, Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                            pos.Y += textSize.Y + 8;
                            campaignMode.CampaignMetadata?.DebugDraw(spriteBatch, pos, campaignMode, debugDrawMetaData);
                        }
                        else
                        {
                            const string text = "Ctrl+M to show campaign metadata debug info";
                            DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - (GUIStyle.SmallFont.MeasureString(text).X + 10), 300),
                                text, Color.White, Color.Black * 0.5f, 0, GUIStyle.SmallFont);
                        }
                    }

                    IEnumerable<string> strings;
                    if (MouseOn != null)
                    {
                        RectTransform mouseOnRect = MouseOn.RectTransform;
                        bool isAbsoluteOffsetInUse = mouseOnRect.AbsoluteOffset != Point.Zero || mouseOnRect.RelativeOffset == Vector2.Zero;

                        strings = new string[]
                        {
                            $"Selected UI Element: {MouseOn.GetType().Name} ({ MouseOn.Style?.Element.Name.LocalName ?? "no style" }, {MouseOn.Rect}",
                            $"Relative Offset: {mouseOnRect.RelativeOffset} | Absolute Offset: {(isAbsoluteOffsetInUse ? mouseOnRect.AbsoluteOffset : mouseOnRect.ParentRect.MultiplySize(mouseOnRect.RelativeOffset))}{(isAbsoluteOffsetInUse ? "" : " (Calculated from RelativeOffset)")}",
                            $"Anchor: {mouseOnRect.Anchor} | Pivot: {mouseOnRect.Pivot}"
                        };
                    }
                    else
                    {
                        strings = new string[]
                        {
                            $"GUI.Scale: {Scale}",
                            $"GUI.xScale: {xScale}",
                            $"GUI.yScale: {yScale}",
                            $"RelativeHorizontalAspectRatio: {RelativeHorizontalAspectRatio}",
                            $"RelativeVerticalAspectRatio: {RelativeVerticalAspectRatio}",
                        };
                    }

                    strings = strings.Concat(new string[] { $"Cam.Zoom: {Screen.Selected.Cam?.Zoom ?? 0f}" });

                    int padding = IntScale(10);
                    int yPos = padding;

                    foreach (string str in strings)
                    {
                        Vector2 stringSize = GUIStyle.SmallFont.MeasureString(str);

                        DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - (int)stringSize.X - padding, yPos), str, Color.LightGreen, Color.Black, 0, GUIStyle.SmallFont);
                        yPos += (int)stringSize.Y + padding / 2;
                    }
                }

                GameMain.GameSession?.EventManager?.DrawPinnedEvent(spriteBatch);

                if (HUDLayoutSettings.DebugDraw) { HUDLayoutSettings.Draw(spriteBatch); }

                GameMain.Client?.Draw(spriteBatch);

                string factionWaterMark = "FACTION/ENDGAME TEST VERSION - please do not publicly share any material you see here!".ToUpper();
                DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 100 * yScale) - GUIStyle.SubHeadingFont.MeasureString(factionWaterMark) / 2, factionWaterMark,
                    GUIStyle.Red * 0.5f, font: GUIStyle.SubHeadingFont, backgroundColor: Color.Black * 0.5f, backgroundPadding: 10);

                if (Character.Controlled?.Inventory != null)
                {
                    if (Character.Controlled.Stun < 0.1f && !Character.Controlled.IsDead)
                    {
                        Inventory.DrawFront(spriteBatch);
                    }
                }

                DrawMessages(spriteBatch, cam);

                if (MouseOn != null && !MouseOn.ToolTip.IsNullOrWhiteSpace())
                {
                    MouseOn.DrawToolTip(spriteBatch);
                }

                if (SubEditorScreen.IsSubEditor())
                {
                    // Draw our "infinite stack" on the cursor
                    switch (SubEditorScreen.DraggedItemPrefab)
                    {
                        case ItemPrefab itemPrefab:
                            {
                                var sprite = itemPrefab.InventoryIcon ?? itemPrefab.Sprite;
                                sprite?.Draw(spriteBatch, PlayerInput.MousePosition, scale: Math.Min(64 / sprite.size.X, 64 / sprite.size.Y) * Scale);
                                break;
                            }
                        case ItemAssemblyPrefab iPrefab:
                            {
                                var (x, y) = PlayerInput.MousePosition;
                                foreach (var pair in iPrefab.DisplayEntities)
                                {
                                    Rectangle dRect = pair.Item2;
                                    dRect = new Rectangle(x: (int)(dRect.X * iPrefab.Scale + x),
                                                          y: (int)(dRect.Y * iPrefab.Scale - y),
                                                          width: (int)(dRect.Width * iPrefab.Scale),
                                                          height: (int)(dRect.Height * iPrefab.Scale));
                                    MapEntityPrefab prefab = MapEntityPrefab.Find("", pair.Item1);
                                    prefab.DrawPlacing(spriteBatch, dRect, prefab.Scale * iPrefab.Scale);
                                }
                                break;
                            }
                    }
                }

                DrawSavingIndicator(spriteBatch);
                DrawCursor(spriteBatch);
                HideCursor = false;
            }
        }

        public static void DrawMessageBoxesOnly(SpriteBatch spriteBatch)
        {
            bool anyDrawn = false;
            foreach (var component in updateList)
            {
                component.DrawAuto(spriteBatch);
                anyDrawn = true;                
            }
            if (anyDrawn)
            {
                DrawCursor(spriteBatch);
            }
        }

        private static void DrawCursor(SpriteBatch spriteBatch)
        {
            if (GameMain.WindowActive && !HideCursor && MouseCursorSprites.Prefabs.Any())
            {
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerStateClamp, rasterizerState: GameMain.ScissorTestEnable);

                if (GameMain.GameSession?.CrewManager is { DraggedOrderPrefab: { SymbolSprite: { } orderSprite, Color: var color }, DragOrder: true })
                {
                    float spriteSize = Math.Max(orderSprite.size.X, orderSprite.size.Y);
                    orderSprite.Draw(spriteBatch, PlayerInput.LatestMousePosition, color, orderSprite.size / 2f, scale: 32f / spriteSize * Scale);
                }

                var sprite = MouseCursorSprites[MouseCursor] ?? MouseCursorSprites[CursorState.Default];
                sprite.Draw(spriteBatch, PlayerInput.LatestMousePosition, Color.White, sprite.Origin, 0f, Scale / 1.5f);

                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            }
        }

        public static void DrawBackgroundSprite(SpriteBatch spriteBatch, Sprite backgroundSprite, Color color, Rectangle? drawArea = null, SpriteEffects spriteEffects = SpriteEffects.None)
        {
            Rectangle area = drawArea ?? new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            float scale = Math.Max(
                (float)area.Width / backgroundSprite.SourceRect.Width,
                (float)area.Height / backgroundSprite.SourceRect.Height) * 1.1f;
            float paddingX = backgroundSprite.SourceRect.Width * scale - area.Width;
            float paddingY = backgroundSprite.SourceRect.Height * scale - area.Height;

            double noiseT = Timing.TotalTime * 0.02f;
            Vector2 pos = new Vector2((float)PerlinNoise.CalculatePerlin(noiseT, noiseT, 0) - 0.5f, (float)PerlinNoise.CalculatePerlin(noiseT, noiseT, 0.5f) - 0.5f);
            pos = new Vector2(pos.X * paddingX, pos.Y * paddingY);

            spriteBatch.Draw(backgroundSprite.Texture,
                area.Center.ToVector2() + pos,
                null, color, 0.0f, backgroundSprite.size / 2,
                scale, spriteEffects, 0.0f);
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
            lock (mutex)
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
        }

        /// <summary>
        /// Adds the component on the removal queue.
        /// Removal list is evaluated last, and thus any item on both lists are not added to update list.
        /// </summary>
        public static void RemoveFromUpdateList(GUIComponent component, bool alsoChildren = true)
        {
            lock (mutex)
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
        }

        public static void ClearUpdateList()
        {
            lock (mutex)
            {
                if (KeyboardDispatcher.Subscriber is GUIComponent && !updateList.Contains(KeyboardDispatcher.Subscriber as GUIComponent))
                {
                    KeyboardDispatcher.Subscriber = null;
                }
                updateList.Clear();
                updateListSet.Clear();
            }
        }

        private static void RefreshUpdateList()
        {
            lock (mutex)
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
        }

        private static void ProcessAdditions()
        {
            lock (mutex)
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
        }

        private static void ProcessRemovals()
        {
            lock (mutex)
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
        }

        private static void ProcessHelperList(List<GUIComponent> list)
        {
            lock (mutex)
            {
                if (list.Count == 0) { return; }
                foreach (var item in list)
                {
                    int index = 0;
                    if (updateList.Count > 0)
                    {
                        index = updateList.Count;
                        while (index > 0 && updateList[index-1].UpdateOrder > item.UpdateOrder)
                        {
                            index--;
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
        }

        private static void HandlePersistingElements(float deltaTime)
        {
            lock (mutex)
            {
                GUIMessageBox.AddActiveToGUIUpdateList();
                GUIContextMenu.AddActiveToGUIUpdateList();

                if (PauseMenuOpen)
                {
                    PauseMenu.AddToGUIUpdateList();
                }
                if (SettingsMenuOpen)
                {
                    SettingsMenuContainer.AddToGUIUpdateList();
                }

                //the "are you sure you want to quit" prompts are drawn on top of everything else
                if (GUIMessageBox.VisibleBox?.UserData as string == "verificationprompt" || GUIMessageBox.VisibleBox?.UserData as string == "bugreporter")
                {
                    GUIMessageBox.VisibleBox.AddToGUIUpdateList();
                }
            }
        }

        public static IEnumerable<GUIComponent> GetAdditions()
        {
            return additions;
        }
        #endregion

        public static GUIComponent MouseOn { get; private set; }

        public static bool IsMouseOn(GUIComponent target)
        {
            lock (mutex)
            {
                if (target == null) { return false; }
                //if (MouseOn == null) { return true; }
                return target == MouseOn || target.IsParentOf(MouseOn);
            }
        }

        public static void ForceMouseOn(GUIComponent c)
        {
            lock (mutex)
            {
                MouseOn = c;
            }
        }

        /// <summary>
        /// Updated automatically before updating the elements on the update list.
        /// </summary>
        public static GUIComponent UpdateMouseOn()
        {
            lock (mutex)
            {
                GUIComponent prevMouseOn = MouseOn;
                MouseOn = null;
                int inventoryIndex = -1;

                Inventory.RefreshMouseOnInventory();
                if (Inventory.IsMouseOnInventory)
                {
                    inventoryIndex = updateList.IndexOf(CharacterHUD.HUDFrame);
                }

                if ((!PlayerInput.PrimaryMouseButtonHeld() && !PlayerInput.PrimaryMouseButtonClicked()) ||
                    (prevMouseOn == null && !PlayerInput.SecondaryMouseButtonHeld() && !Inventory.DraggingItems.Any()))
                {
                    for (var i = updateList.Count - 1; i > inventoryIndex; i--)
                    {
                        var c = updateList[i];
                        if (!c.CanBeFocused) { continue; }
                        if (c.MouseRect.Contains(PlayerInput.MousePosition))
                        {
                            if ((!PlayerInput.PrimaryMouseButtonHeld() && !PlayerInput.PrimaryMouseButtonClicked()) || c == prevMouseOn || prevMouseOn == null)
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
        }

        private static CursorState UpdateMouseCursorState(GUIComponent c)
        {
            lock (mutex)
            {
                // Waiting and drag cursor override everything else
                if (MouseCursor == CursorState.Waiting) { return CursorState.Waiting; }
                if (GUIScrollBar.DraggingBar != null) { return GUIScrollBar.DraggingBar.Bar.HoverCursor; }

                if (SubEditorScreen.IsSubEditor() && SubEditorScreen.DraggedItemPrefab != null) { return CursorState.Hand; }

                // Wire cursors
                if (Character.Controlled != null)
                {
                    if (Character.Controlled.SelectedItem?.GetComponent<ConnectionPanel>() != null)
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
                            if (CharacterHUD.MouseOnCharacterPortrait() || CharacterHealth.IsMouseOnHealthBar())
                            {
                                return CursorState.Hand;
                            }
                            break;
                        // Sub editor drag and highlight
                        case SubEditorScreen editor:
                        {
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
                    }
                }

                if (c != null && c.Visible)
                {
                    if (c.AlwaysOverrideCursor) { return c.HoverCursor; }

                    // When a button opens a submenu, it increases to the size of the entire screen.
                    // And this is of course picked up as clickable area.
                    // There has to be a better way of checking this but for now this works.
                    var monitorRect = new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight);

                    var parent = FindInteractParent(c);

                    if (c.Enabled)
                    {
                        var dragHandle = c as GUIDragHandle ?? parent as GUIDragHandle;
                        if (dragHandle != null)
                        {
                            return dragHandle.Dragging ? CursorState.Dragging : CursorState.Hand;
                        }
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
                    if (parent is GUIListBox listBox && c.Parent == listBox.Content)
                    {
                        if (listBox.DraggedElement != null) { return CursorState.Dragging; }
                        if (listBox.CurrentDragMode != GUIListBox.DragMode.NoDragging) { return CursorState.Move; }

                        if (listBox.HoverCursor != CursorState.Default)
                        {
                            var hoverParent = c;
                            while (true)
                            {
                                if (hoverParent == parent || hoverParent == null) { break; }
                                if (hoverParent.State == GUIComponent.ComponentState.Hover) { return CursorState.Hand; }
                                hoverParent = hoverParent.Parent;
                            }
                        }
                    }

                    if (parent != null && parent.CanBeFocused)
                    {
                        if (!parent.Rect.Equals(monitorRect)) { return parent.HoverCursor; }
                    }
                }

                if (Inventory.IsMouseOnInventory) { return Inventory.GetInventoryMouseCursor(); }

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
                                    case GUIDragHandle dragHandle:
                                        return dragHandle;
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
        }

        /// <summary>
        /// Set the cursor to an hourglass.
        /// Will automatically revert after 10 seconds or when <see cref="ClearCursorWait"/> is called.
        /// </summary>
        public static void SetCursorWaiting(int waitSeconds = 10, Func<bool> endCondition = null)
        {
            CoroutineManager.StartCoroutine(WaitCursorCoroutine(), "WaitCursorTimeout");

            IEnumerable<CoroutineStatus> WaitCursorCoroutine()
            {
                MouseCursor = CursorState.Waiting;
                var timeOut = DateTime.Now + new TimeSpan(0, 0, waitSeconds);
                while (DateTime.Now < timeOut)
                {
                    if (endCondition != null)
                    {
                        try
                        {
                            if (endCondition.Invoke()) { break; }
                        }
                        catch { break; }
                    }
                    yield return CoroutineStatus.Running;
                }
                if (MouseCursor == CursorState.Waiting) { MouseCursor = CursorState.Default; }
                yield return CoroutineStatus.Success;
            }
        }

        public static void ClearCursorWait()
        {
            lock (mutex)
            {
                CoroutineManager.StopCoroutines("WaitCursorTimeout");
                MouseCursor = CursorState.Default;
            }
        }

        public static bool HasSizeChanged(Point referenceResolution, float referenceUIScale, float referenceHUDScale)
        {
            return GameMain.GraphicsWidth != referenceResolution.X || GameMain.GraphicsHeight != referenceResolution.Y ||
                   referenceUIScale != Inventory.UIScale || referenceHUDScale != Scale;
        }

        public static void Update(float deltaTime)
        {
            lock (mutex)
            {
                if (PlayerInput.KeyDown(Keys.LeftControl) && PlayerInput.KeyHit(Keys.S))
                {
                    debugDrawSounds = !debugDrawSounds;
                }
                if (PlayerInput.KeyDown(Keys.LeftControl) && PlayerInput.KeyHit(Keys.E))
                {
                    debugDrawEvents = !debugDrawEvents;
                }
                if (PlayerInput.IsCtrlDown() && PlayerInput.KeyHit(Keys.M))
                {
                    debugDrawMetaData.Enabled = !debugDrawMetaData.Enabled;
                }

                if (debugDrawMetaData.Enabled)
                {
                    if (PlayerInput.KeyHit(Keys.Up))
                    {
                        debugDrawMetaData.Offset--;
                    }
                    if (PlayerInput.KeyHit(Keys.Down))
                    {
                        debugDrawMetaData.Offset++;
                    }
                    if (PlayerInput.IsCtrlDown())
                    {
                        if (PlayerInput.KeyHit(Keys.D1))
                        {
                            debugDrawMetaData.FactionMetadata = !debugDrawMetaData.FactionMetadata;
                            debugDrawMetaData.Offset = 0;
                        }
                        if (PlayerInput.KeyHit(Keys.D2))
                        {
                            debugDrawMetaData.UpgradeLevels = !debugDrawMetaData.UpgradeLevels;
                            debugDrawMetaData.Offset = 0;
                        }
                        if (PlayerInput.KeyHit(Keys.D3))
                        {
                            debugDrawMetaData.UpgradePrices = !debugDrawMetaData.UpgradePrices;
                            debugDrawMetaData.Offset = 0;
                        }
                    }
                }

                HandlePersistingElements(deltaTime);
                RefreshUpdateList();
                UpdateMouseOn();
                Debug.Assert(updateList.Count == updateListSet.Count);
                foreach (var c in updateList)
                {
                    c.UpdateAuto(deltaTime);
                }
                UpdateMessages(deltaTime);
                UpdateSavingIndicator(deltaTime);
            }
        }

        public static void UpdateGUIMessageBoxesOnly(float deltaTime)
        {
            GUIMessageBox.AddActiveToGUIUpdateList();
            RefreshUpdateList();
            UpdateMouseOn(); 
            foreach (var c in updateList)
            {
                c.UpdateAuto(deltaTime);
            }
        }

        private static void UpdateMessages(float deltaTime)
        {
            lock (mutex)
            {
                foreach (GUIMessage msg in messages)
                {
                    if (msg.WorldSpace) { continue; }
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
                    if (!msg.WorldSpace) { continue; }
                    msg.Timer -= deltaTime;
                    msg.Pos += msg.Velocity * deltaTime;
                }

                messages.RemoveAll(m => m.Timer <= 0.0f);
            }
        }

        private static void UpdateSavingIndicator(float deltaTime)
        {
            if (GUIStyle.SavingIndicator == null) { return; }
            lock (mutex)
            {
                if (timeUntilSavingIndicatorDisabled.HasValue)
                {
                    timeUntilSavingIndicatorDisabled -= deltaTime;
                    if (timeUntilSavingIndicatorDisabled <= 0.0f)
                    {
                        isSavingIndicatorEnabled = false;
                        timeUntilSavingIndicatorDisabled = null;
                    }
                }
                if (isSavingIndicatorEnabled)
                {
                    if (savingIndicatorColor == Color.Transparent)
                    {
                        savingIndicatorState = SavingIndicatorState.FadingIn;
                        savingIndicatorColorLerpAmount = 0.0f;
                    }
                    else if (savingIndicatorColor == Color.White)
                    {
                        savingIndicatorState = SavingIndicatorState.None;
                    }
                }
                else
                {
                    if (savingIndicatorColor == Color.White)
                    {
                        savingIndicatorState = SavingIndicatorState.FadingOut;
                        savingIndicatorColorLerpAmount = 0.0f;
                    }
                    else if (savingIndicatorColor == Color.Transparent)
                    {
                        savingIndicatorState = SavingIndicatorState.None;
                    }
                }
                if (savingIndicatorState != SavingIndicatorState.None)
                {
                    bool isFadingIn = savingIndicatorState == SavingIndicatorState.FadingIn;
                    Color lerpStartColor = isFadingIn ? Color.Transparent : Color.White;
                    Color lerpTargetColor = isFadingIn ? Color.White : Color.Transparent;
                    savingIndicatorColorLerpAmount += (isFadingIn ? 2.0f : 0.5f) * deltaTime;
                    savingIndicatorColor = Color.Lerp(lerpStartColor, lerpTargetColor, savingIndicatorColorLerpAmount);
                }
                if (IsSavingIndicatorVisible)
                {
                    savingIndicatorSpriteIndex = (savingIndicatorSpriteIndex + 15.0f * deltaTime) % (GUIStyle.SavingIndicator.FrameCount + 1);
                }
            }
        }

#region Element drawing

        private static readonly List<float> usedIndicatorAngles = new List<float>();

        /// <param name="createOffset">Should the indicator move based on the camera position?</param>
        /// <param name="overrideAlpha">Override the distance-based alpha value with the specified alpha value</param>
        public static void DrawIndicator(SpriteBatch spriteBatch, in Vector2 worldPosition, Camera cam, in Range<float> visibleRange, Sprite sprite, in Color color,
            bool createOffset = true, float scaleMultiplier = 1.0f, float? overrideAlpha = null, LocalizedString label = null)
        {
            Vector2 diff = worldPosition - cam.WorldViewCenter;
            float dist = diff.Length();

            float symbolScale = Math.Min(64.0f / sprite.size.X, 1.0f) * scaleMultiplier * Scale;

            if (overrideAlpha.HasValue || visibleRange.Contains(dist))
            {
                float alpha = overrideAlpha ?? MathUtils.Min((dist - visibleRange.Start) / 100.0f, 1.0f - ((dist - visibleRange.End + 100f) / 100.0f), 1.0f);
                Vector2 targetScreenPos = cam.WorldToScreen(worldPosition);

                if (!createOffset)
                {
                    sprite.Draw(spriteBatch, targetScreenPos, color * alpha, rotate: 0.0f, scale: symbolScale);
                    return;
                }

                float screenDist = Vector2.Distance(cam.WorldToScreen(cam.WorldViewCenter), targetScreenPos);
                float angle = MathUtils.VectorToAngle(diff);
                float originalAngle = angle;

                const float minAngleDiff = 0.05f;
                bool overlapFound = true;
                int iterations = 0;
                while (overlapFound && iterations < 10)
                {
                    overlapFound = false;
                    foreach (float usedIndicatorAngle in usedIndicatorAngles)
                    {
                        float shortestAngle = MathUtils.GetShortestAngle(angle, usedIndicatorAngle);
                        if (MathUtils.NearlyEqual(shortestAngle, 0.0f)) { shortestAngle = 0.01f; }
                        if (Math.Abs(shortestAngle) < minAngleDiff)
                        {
                            angle -= Math.Sign(shortestAngle) * (minAngleDiff - Math.Abs(shortestAngle));
                            overlapFound = true;
                            break;
                        }
                    }
                    iterations++;
                }

                usedIndicatorAngles.Add(angle);

                Vector2 iconDiff = new Vector2(
                    (float)Math.Cos(angle) * Math.Min(GameMain.GraphicsWidth * 0.4f, screenDist + 10),
                    (float)-Math.Sin(angle) * Math.Min(GameMain.GraphicsHeight * 0.4f, screenDist + 10));

                angle = MathHelper.Lerp(originalAngle, angle, MathHelper.Clamp(((screenDist + 10f) - iconDiff.Length()) / 10f, 0f, 1f));

                iconDiff = new Vector2(
                    (float)Math.Cos(angle) * Math.Min(GameMain.GraphicsWidth * 0.4f, screenDist),
                    (float)-Math.Sin(angle) * Math.Min(GameMain.GraphicsHeight * 0.4f, screenDist));

                Vector2 iconPos = cam.WorldToScreen(cam.WorldViewCenter) + iconDiff;
                sprite.Draw(spriteBatch, iconPos, color * alpha, rotate: 0.0f, scale: symbolScale);

                if (label != null)
                {
                    float cursorDist = Vector2.Distance(PlayerInput.MousePosition, iconPos);
                    if (cursorDist < sprite.size.X * symbolScale)
                    {
                        Vector2 textSize = GUIStyle.Font.MeasureString(label);
                        Vector2 textPos = iconPos + new Vector2(sprite.size.X * symbolScale * 0.7f * Math.Sign(-iconDiff.X), -textSize.Y / 2);
                        if (iconDiff.X > 0) { textPos.X -= textSize.X; }
                        DrawString(spriteBatch, textPos + Vector2.One, label, Color.Black);
                        DrawString(spriteBatch, textPos, label,   color);
                    }
                }

                if (screenDist - 10 > iconDiff.Length())
                {
                    Vector2 normalizedDiff = Vector2.Normalize(targetScreenPos - iconPos);
                    Vector2 arrowOffset = normalizedDiff * sprite.size.X * symbolScale * 0.7f;
                    Arrow.Draw(spriteBatch, iconPos + arrowOffset, color * alpha, MathUtils.VectorToAngle(arrowOffset) + MathHelper.PiOver2, scale: 0.5f);
                }
            }
        }

        public static void DrawIndicator(SpriteBatch spriteBatch, Vector2 worldPosition, Camera cam, float hideDist, Sprite sprite, Color color,
            bool createOffset = true, float scaleMultiplier = 1.0f, float? overrideAlpha = null)
        {
            DrawIndicator(spriteBatch, worldPosition, cam, new Range<float>(hideDist, float.PositiveInfinity), sprite, color, createOffset, scaleMultiplier, overrideAlpha);
        }

        public static void DrawLine(SpriteBatch sb, Vector2 start, Vector2 end, Color clr, float depth = 0.0f, float width = 1)
        {
            DrawLine(sb, solidWhiteTexture, start, end, clr, depth, (int)width);
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

        public static void DrawString(SpriteBatch sb, Vector2 pos, LocalizedString text, Color color, Color? backgroundColor = null, int backgroundPadding = 0, GUIFont font = null, ForceUpperCase forceUpperCase = ForceUpperCase.Inherit)
        {
            DrawString(sb, pos, text.Value, color, backgroundColor, backgroundPadding, font, forceUpperCase);
        }

        public static void DrawString(SpriteBatch sb, Vector2 pos, string text, Color color, Color? backgroundColor = null, int backgroundPadding = 0, GUIFont font = null, ForceUpperCase forceUpperCase = ForceUpperCase.Inherit)
        {
            if (color.A == 0) { return; }
            if (font == null) { font = GUIStyle.Font; }
            if (backgroundColor != null && backgroundColor.Value.A > 0)
            {
                Vector2 textSize = font.MeasureString(text);
                DrawRectangle(sb, pos - Vector2.One * backgroundPadding, textSize + Vector2.One * 2.0f * backgroundPadding, (Color)backgroundColor, true);
            }

            font.DrawString(sb, text, pos, color, forceUpperCase: forceUpperCase);
        }

        public static void DrawStringWithColors(SpriteBatch sb, Vector2 pos, string text, Color color, in ImmutableArray<RichTextData>? richTextData, Color? backgroundColor = null, int backgroundPadding = 0, GUIFont font = null, float depth = 0.0f)
        {
            if (font == null) font = GUIStyle.Font;
            if (backgroundColor != null)
            {
                Vector2 textSize = font.MeasureString(text);
                DrawRectangle(sb, pos - Vector2.One * backgroundPadding, textSize + Vector2.One * 2.0f * backgroundPadding, (Color)backgroundColor, true, depth, 5);
            }

            font.DrawStringWithColors(sb, text, pos, color, 0.0f, Vector2.Zero, 1f, SpriteEffects.None, depth, richTextData);
        }

        private const int DonutSegments = 30;
        private static readonly ImmutableArray<Vector2> canonicalCircle
            = Enumerable.Range(0, DonutSegments)
                .Select(i => i * (2.0f * MathF.PI / DonutSegments))
                .Select(angle => new Vector2(MathF.Cos(angle), MathF.Sin(angle)))
                .ToImmutableArray();
        private static readonly VertexPositionColorTexture[] donutVerts = new VertexPositionColorTexture[DonutSegments * 4];

        public static void DrawDonutSection(
            SpriteBatch sb, Vector2 center, Range<float> radii, float sectionRad, Color clr, float depth = 0.0f)
        {
            float getRadius(int vertexIndex)
                => (vertexIndex % 4) switch
                {
                    0 => radii.End,
                    1 => radii.End,
                    2 => radii.Start,
                    3 => radii.Start,
                    _ => throw new InvalidOperationException()
                };
            static int getDirectionIndex(int vertexIndex)
                => (vertexIndex % 4) switch
                {
                    0 => (vertexIndex / 4) + 0,
                    1 => (vertexIndex / 4) + 1,
                    2 => (vertexIndex / 4) + 0,
                    3 => (vertexIndex / 4) + 1,
                    _ => throw new InvalidOperationException()
                };

            float sectionProportion = sectionRad / (MathF.PI * 2.0f);
            int maxDirectionIndex = Math.Min(DonutSegments, (int)MathF.Ceiling(sectionProportion * DonutSegments));

            Vector2 getDirection(int vertexIndex)
            {
                int directionIndex = getDirectionIndex(vertexIndex);
                Vector2 dir = canonicalCircle[directionIndex % DonutSegments];
                if (maxDirectionIndex > 0 && directionIndex >= maxDirectionIndex)
                {
                    float maxSectionProportion = (float)maxDirectionIndex / DonutSegments;
                    dir = Vector2.Lerp(
                        canonicalCircle[maxDirectionIndex - 1],
                        canonicalCircle[maxDirectionIndex % DonutSegments],
                        1.0f - (maxSectionProportion - sectionProportion) * DonutSegments);
                }

                return new Vector2(dir.Y, -dir.X);
            }

            for (int vertexIndex = 0; vertexIndex < maxDirectionIndex * 4; vertexIndex++)
            {
                donutVerts[vertexIndex].Color = clr;
                donutVerts[vertexIndex].Position = new Vector3(center + getDirection(vertexIndex) * getRadius(vertexIndex), 0.0f);
            }
            sb.Draw(solidWhiteTexture, donutVerts, depth, count: maxDirectionIndex);
        }
        
        public static void DrawRectangle(SpriteBatch sb, Vector2 start, Vector2 size, Color clr, bool isFilled = false, float depth = 0.0f, float thickness = 1)
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

        public static void DrawRectangle(SpriteBatch sb, Rectangle rect, Color clr, bool isFilled = false, float depth = 0.0f, float thickness = 1)
        {
            if (isFilled)
            {
                sb.Draw(solidWhiteTexture, rect, null, clr, 0.0f, Vector2.Zero, SpriteEffects.None, depth);
            }
            else
            {
                Rectangle srcRect = new Rectangle(0, 0, 1, 1);
                sb.Draw(solidWhiteTexture, new Vector2(rect.X, rect.Y), srcRect, clr, 0.0f, Vector2.Zero, new Vector2(thickness, rect.Height), SpriteEffects.None, depth);
                sb.Draw(solidWhiteTexture, new Vector2(rect.X + thickness, rect.Y), srcRect, clr, 0.0f, Vector2.Zero, new Vector2(rect.Width - thickness, thickness), SpriteEffects.None, depth);
                sb.Draw(solidWhiteTexture, new Vector2(rect.X + thickness, rect.Bottom - thickness), srcRect, clr, 0.0f, Vector2.Zero, new Vector2(rect.Width - thickness, thickness), SpriteEffects.None, depth);
                sb.Draw(solidWhiteTexture, new Vector2(rect.Right - thickness, rect.Y + thickness), srcRect, clr, 0.0f, Vector2.Zero, new Vector2(thickness, rect.Height - thickness * 2f), SpriteEffects.None, depth);
            }
        }

        public static void DrawRectangle(SpriteBatch sb, Vector2 position, Vector2 size, Vector2 origin, float rotation, Color clr, float depth = 0.0f, float thickness = 1, OutlinePosition outlinePos = OutlinePosition.Centered)
        {
            Vector2 topLeft = new Vector2(-origin.X, -origin.Y);
            Vector2 topRight = new Vector2(-origin.X + size.X, -origin.Y);
            Vector2 bottomLeft = new Vector2(-origin.X, -origin.Y + size.Y);
            Vector2 actualSize = size;

            switch(outlinePos)
            {
                case OutlinePosition.Default:
                    actualSize += new Vector2(thickness);
                    break;
                case OutlinePosition.Centered:
                    topLeft -= new Vector2(thickness * 0.5f);
                    topRight -= new Vector2(thickness * 0.5f);
                    bottomLeft -= new Vector2(thickness * 0.5f);
                    actualSize += new Vector2(thickness);
                    break;
                case OutlinePosition.Inside:
                    topRight -= new Vector2(thickness, 0.0f);
                    bottomLeft -= new Vector2(0.0f, thickness);
                    break;
                case OutlinePosition.Outside:
                    topLeft -= new Vector2(thickness);
                    topRight -= new Vector2(0.0f, thickness);
                    bottomLeft -= new Vector2(thickness, 0.0f);
                    actualSize += new Vector2(thickness * 2.0f);
                    break;
            }

            Matrix rotate = Matrix.CreateRotationZ(rotation);
            topLeft = Vector2.Transform(topLeft, rotate) + position;
            topRight = Vector2.Transform(topRight, rotate) + position;
            bottomLeft = Vector2.Transform(bottomLeft, rotate) + position;

            Rectangle srcRect = new Rectangle(0, 0, 1, 1);
            sb.Draw(solidWhiteTexture, topLeft, srcRect, clr, rotation, Vector2.Zero, new Vector2(thickness, actualSize.Y), SpriteEffects.None, depth);
            sb.Draw(solidWhiteTexture, topLeft, srcRect, clr, rotation, Vector2.Zero, new Vector2(actualSize.X, thickness), SpriteEffects.None, depth);
            sb.Draw(solidWhiteTexture, topRight, srcRect, clr, rotation, Vector2.Zero, new Vector2(thickness, actualSize.Y), SpriteEffects.None, depth);
            sb.Draw(solidWhiteTexture, bottomLeft, srcRect, clr, rotation, Vector2.Zero, new Vector2(actualSize.X, thickness), SpriteEffects.None, depth);
        }

        public static void DrawFilledRectangle(SpriteBatch sb, Vector2 position, Vector2 size, Vector2 pivot, float rotation, Color clr, float depth = 0.0f)
        {
            Rectangle srcRect = new Rectangle(0, 0, 1, 1);
            sb.Draw(solidWhiteTexture, position, srcRect, clr, rotation, (pivot/size), size, SpriteEffects.None, depth);
        }

        public static void DrawFilledRectangle(SpriteBatch sb, RectangleF rect, Color clr, float depth = 0.0f)
        {
            DrawFilledRectangle(sb, rect.Location, rect.Size, clr, depth);
        }

        public static void DrawFilledRectangle(SpriteBatch sb, Vector2 start, Vector2 size, Color clr, float depth = 0.0f)
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

            sb.Draw(solidWhiteTexture, start, null, clr, 0f, Vector2.Zero, size, SpriteEffects.None, depth);
        }

        public static void DrawRectangle(SpriteBatch sb, Vector2 center, float width, float height, float rotation, Color clr, float depth = 0.0f, float thickness = 1)
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

        public static void DrawRectangle(SpriteBatch sb, Vector2[] corners, Color clr, float depth = 0.0f, float thickness = 1)
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
                origin = GUIStyle.Font.MeasureString(text) / 2;
            }
            catch
            {
                origin = Vector2.Zero;
            }

            GUIStyle.Font.DrawString(sb, text, new Vector2(rect.Center.X, rect.Center.Y), Color.White, 0.0f, origin, 1.0f, SpriteEffects.None, 0.0f);

            return clicked;
        }

        private static void DrawMessages(SpriteBatch spriteBatch, Camera cam)
        {
            if (messages.Count == 0) { return; }

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
                if (msg.WorldSpace) { continue; }

                Vector2 drawPos = new Vector2(HUDLayoutSettings.MessageAreaTop.Right, HUDLayoutSettings.MessageAreaTop.Center.Y);

                msg.Font.DrawString(spriteBatch, msg.Text, drawPos + msg.DrawPos + Vector2.One, Color.Black, 0, msg.Origin, 1.0f, SpriteEffects.None, 0);
                msg.Font.DrawString(spriteBatch, msg.Text, drawPos + msg.DrawPos, msg.Color, 0, msg.Origin, 1.0f, SpriteEffects.None, 0);
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
                if (!msg.WorldSpace) { continue; }

                if (cam != null)
                {
                    float alpha = 1.0f;
                    if (msg.Timer < 1.0f) { alpha -= 1.0f - msg.Timer; }

                    Vector2 drawPos = cam.WorldToScreen(msg.DrawPos);
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
            //DrawLine(spriteBatch, from, from + dir, GUIStyle.Red);
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

        private static void DrawSavingIndicator(SpriteBatch spriteBatch)
        {
            if (!IsSavingIndicatorVisible || GUIStyle.SavingIndicator == null) { return; }
            var sheet = GUIStyle.SavingIndicator;
            Vector2 pos = new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) - new Vector2(HUDLayoutSettings.Padding) - 2 * Scale * sheet.FrameSize.ToVector2();
            sheet.Draw(spriteBatch, (int)Math.Floor(savingIndicatorSpriteIndex), pos, savingIndicatorColor, origin: Vector2.Zero, rotate: 0.0f, scale: new Vector2(Scale));
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
            int textureWidth = Math.Max(radius * 2, 1);
            int textureHeight = Math.Max(height + radius * 2, 1);

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

        public static GUIComponent CreateEnumField(Enum value, int elementHeight, LocalizedString name, RectTransform parent, string toolTip = null, GUIFont font = null)
        {
            font = font ?? GUIStyle.SmallFont;
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

        public static GUIComponent CreateRectangleField(Rectangle value, int elementHeight, LocalizedString name, RectTransform parent, LocalizedString toolTip = null, GUIFont font = null)
        {
            var frame = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, Math.Max(elementHeight, 26)), parent), color: Color.Transparent);
            font = font ?? GUIStyle.SmallFont;
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
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), RectComponentLabels[i], font: font, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    NumberType.Int)
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

        public static GUIComponent CreatePointField(Point value, int elementHeight, LocalizedString displayName, RectTransform parent, LocalizedString toolTip = null)
        {
            var frame = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, Math.Max(elementHeight, 26)), parent), color: Color.Transparent);
            new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), frame.RectTransform), displayName, font: GUIStyle.SmallFont)
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
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), VectorComponentLabels[i], font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    NumberType.Int)
                {
                    Font = GUIStyle.SmallFont
                };

                if (i == 0)
                    numberInput.IntValue = value.X;
                else
                    numberInput.IntValue = value.Y;
            }
            return frame;
        }

        public static GUIComponent CreateVector2Field(Vector2 value, int elementHeight, LocalizedString name, RectTransform parent, LocalizedString toolTip = null, GUIFont font = null, int decimalsToDisplay = 1)
        {
            font = font ?? GUIStyle.SmallFont;
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
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), VectorComponentLabels[i], font: font, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight), NumberType.Float) { Font = font };
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

        public static void NotifyPrompt(LocalizedString header, LocalizedString body)
        {
            GUIMessageBox msgBox = new GUIMessageBox(header, body, new[] { TextManager.Get("Ok") }, new Vector2(0.2f, 0.175f), minSize: new Point(300, 175));
            msgBox.Buttons[0].OnClicked = delegate
            {
                msgBox.Close();
                return true;
            };
        }

        public static GUIMessageBox AskForConfirmation(LocalizedString header, LocalizedString body, Action onConfirm, Action onDeny = null)
        {
            LocalizedString[] buttons = { TextManager.Get("Ok"), TextManager.Get("Cancel") };
            GUIMessageBox msgBox = new GUIMessageBox(header, body, buttons, new Vector2(0.2f, 0.175f), minSize: new Point(300, 175));

            // Cancel button
            msgBox.Buttons[1].OnClicked = delegate
            {
                onDeny?.Invoke();
                msgBox.Close();
                return true;
            };

            // Ok button
            msgBox.Buttons[0].OnClicked = delegate
            {
                onConfirm.Invoke();
                msgBox.Close();
                return true;
            };
            return msgBox;
        }

        public static GUIMessageBox PromptTextInput(LocalizedString header, string body, Action<string> onConfirm)
        {
            LocalizedString[] buttons = { TextManager.Get("Ok"), TextManager.Get("Cancel") };
            GUIMessageBox msgBox = new GUIMessageBox(header, string.Empty, buttons, new Vector2(0.2f, 0.175f), minSize: new Point(300, 175));
            GUITextBox textBox = new GUITextBox(new RectTransform(Vector2.One, msgBox.Content.RectTransform), text: body)
            {
                OverflowClip = true
            };

            // Cancel button
            msgBox.Buttons[1].OnClicked = delegate
            {
                msgBox.Close();
                return true;
            };

            // Ok button
            msgBox.Buttons[0].OnClicked = delegate
            {
                onConfirm.Invoke(textBox.Text);
                msgBox.Close();
                return true;
            };
            return msgBox;
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
            List<GUIComponent> sortedElements = elements.OrderByDescending(e => e.Rect.Width + e.Rect.Height).ToList();

            Rectangle area = clampArea ?? new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            for (int i = 0; i < sortedElements.Count; i++)
            {
                Point moveAmount = Point.Zero;
                Rectangle rect1 = sortedElements[i].Rect;
                moveAmount.X += Math.Max(area.X - rect1.X, 0);
                moveAmount.X -= Math.Max(rect1.Right - area.Right, 0);
                moveAmount.Y += Math.Max(area.Y - rect1.Y, 0);
                moveAmount.Y -= Math.Max(rect1.Bottom - area.Bottom, 0);
                sortedElements[i].RectTransform.ScreenSpaceOffset += moveAmount;
            }

            bool intersections = true;
            int iterations = 0;
            while (intersections && iterations < 100)
            {
                intersections = false;
                for (int i = 0; i < sortedElements.Count; i++)
                {
                    Rectangle rect1 = sortedElements[i].Rect;
                    for (int j = i + 1; j < sortedElements.Count; j++)
                    {
                        Rectangle rect2 = sortedElements[j].Rect;
                        if (!rect1.Intersects(rect2)) { continue; }

                        intersections = true;
                        Point centerDiff = rect1.Center - rect2.Center;
                        //move the interfaces away from each other, in a random direction if they're at the same position
                        Vector2 moveAmount = centerDiff == Point.Zero ? Vector2.UnitX + Rand.Vector(0.1f) : Vector2.Normalize(centerDiff.ToVector2());

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
                        sortedElements[i].RectTransform.ScreenSpaceOffset += moveAmount1.ToPoint();
                        sortedElements[j].RectTransform.ScreenSpaceOffset += moveAmount2.ToPoint();
                    }

                    if (disallowedAreas == null) { continue; }
                    foreach (Rectangle rect2 in disallowedAreas)
                    {
                        if (!rect1.Intersects(rect2)) { continue; }
                        intersections = true;

                        Point centerDiff = rect1.Center - rect2.Center;
                        //move the interface away from the disallowed area
                        Vector2 moveAmount = centerDiff == Point.Zero ? Rand.Vector(1.0f) : Vector2.Normalize(centerDiff.ToVector2());

                        //make sure we don't move the interface out of the screen
                        Vector2 moveAmount1 = ClampMoveAmount(rect1, area, moveAmount * 10.0f);

                        //move by 10 units in the desired direction and repeat until nothing overlaps
                        //(or after 100 iterations, in which case we'll just give up and let them overlap)
                        sortedElements[i].RectTransform.ScreenSpaceOffset += (moveAmount1).ToPoint();
                    }
                }
                iterations++;
            }

            static Vector2 ClampMoveAmount(Rectangle Rect, Rectangle clampTo, Vector2 moveAmount)
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
            if (Screen.Selected == GameMain.MainMenuScreen) { return; }
            if (PreventPauseMenuToggle) { return; }

            SettingsMenuOpen = false;

            TogglePauseMenu(null, null);

            if (PauseMenuOpen)
            {
                Inventory.DraggingItems.Clear();
                Inventory.DraggingInventory = null;

                PauseMenu = new GUIFrame(new RectTransform(Vector2.One, Canvas, Anchor.Center), style: null);
                new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, PauseMenu.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

                var pauseMenuInner = new GUIFrame(new RectTransform(new Vector2(0.13f, 0.3f), PauseMenu.RectTransform, Anchor.Center) { MinSize = new Point(250, 300) });

                var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 0.6f), pauseMenuInner.RectTransform, Anchor.Center))
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                new GUIButton(new RectTransform(new Vector2(0.1f, 0.1f), pauseMenuInner.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point((int)(15 * GUI.Scale)) },
                    "", style: "GUIBugButton")
                {
                    IgnoreLayoutGroups = true,
                    ToolTip = TextManager.Get("bugreportbutton") + $" (v{GameMain.Version})",
                    OnClicked = (btn, userdata) => { GameMain.Instance.ShowBugReporter(); return true; }
                };

                CreateButton("PauseMenuResume", buttonContainer, null);
                CreateButton("PauseMenuSettings", buttonContainer, () => SettingsMenuOpen = true);

                bool IsFriendlyOutpostLevel() => GameMain.GameSession != null && Level.IsLoadedFriendlyOutpost;
                if (Screen.Selected == GameMain.GameScreen && GameMain.GameSession != null)
                {
                    if (GameMain.GameSession.GameMode is SinglePlayerCampaign spMode)
                    {
                        CreateButton("PauseMenuRetry", buttonContainer, verificationTextTag: "PauseMenuRetryVerification", action: () =>
                        {
                            if (GameMain.GameSession.RoundSummary?.Frame != null)
                            {
                                GUIMessageBox.MessageBoxes.Remove(GameMain.GameSession.RoundSummary.Frame);
                            }
                            GUIMessageBox.MessageBoxes.RemoveAll(mb => mb.UserData as string == "ConversationAction");
                            GameMain.GameSession.LoadPreviousSave();
                        });

                        if (IsFriendlyOutpostLevel())
                        {
                            CreateButton("PauseMenuSaveQuit", buttonContainer, verificationTextTag: "PauseMenuSaveAndReturnToMainMenuVerification", action: () =>
                            {
                                if (IsFriendlyOutpostLevel()) { GameMain.QuitToMainMenu(save: true); }
                            });
                        }
                    }
                    else if (GameMain.GameSession.GameMode is TestGameMode)
                    {
                        CreateButton("PauseMenuReturnToEditor", buttonContainer, action: () =>
                        {
                            GameMain.GameSession?.EndRound("");
                        });
                    }
                    else if (!GameMain.GameSession.GameMode.IsSinglePlayer && GameMain.Client != null && GameMain.Client.HasPermission(ClientPermissions.ManageRound))
                    {
                        bool canSave = GameMain.GameSession.GameMode is CampaignMode && IsFriendlyOutpostLevel();
                        if (canSave)
                        {
                            CreateButton("PauseMenuSaveQuit", buttonContainer, verificationTextTag: "PauseMenuSaveAndReturnToServerLobbyVerification", action: () =>
                            {
                                GameMain.Client?.RequestRoundEnd(save: true);
                            });
                        }

                        CreateButton(GameMain.GameSession.GameMode is CampaignMode ? "ReturnToServerlobby" : "EndRound", buttonContainer,
                            verificationTextTag: GameMain.GameSession.GameMode is CampaignMode ? "PauseMenuReturnToServerLobbyVerification" : "EndRoundSubNotAtLevelEnd",
                            action: () =>
                            {
                                GameMain.Client?.RequestRoundEnd(save: false);
                            });
                    }
                }

                if (GameMain.GameSession != null || Screen.Selected is CharacterEditorScreen || Screen.Selected is SubEditorScreen)
                {
                    CreateButton("PauseMenuQuit", buttonContainer,
                        verificationTextTag: GameMain.GameSession == null ? "PauseMenuQuitVerificationEditor" : "PauseMenuQuitVerification",
                        action: () =>
                        {
                            GameMain.QuitToMainMenu(save: false);
                        });
                }
                else
                {
                    CreateButton("PauseMenuQuit", buttonContainer, action: () => { GameMain.QuitToMainMenu(save: false); });
                }

                GUITextBlock.AutoScaleAndNormalize(buttonContainer.Children.Where(c => c is GUIButton).Select(c => ((GUIButton)c).TextBlock));
            }

            void CreateButton(string textTag, GUIComponent parent, Action action, string verificationTextTag = null)
            {
                new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), parent.RectTransform), TextManager.Get(textTag))
                {
                    OnClicked = (btn, userData) =>
                    {
                        if (string.IsNullOrEmpty(verificationTextTag))
                        {
                            PauseMenuOpen = false;
                            action?.Invoke();
                        }
                        else
                        {
                            CreateVerificationPrompt(verificationTextTag, action);
                        }
                        return true;
                    }
                };
            }

            void CreateVerificationPrompt(string textTag, Action confirmAction)
            {
                var msgBox = new GUIMessageBox("", TextManager.Get(textTag),
                    new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") })
                {
                    UserData = "verificationprompt"
                };
                msgBox.Buttons[0].OnClicked = (_, __) =>
                {
                    PauseMenuOpen = false;
                    confirmAction?.Invoke();
                    return true;
                };
                msgBox.Buttons[0].OnClicked += msgBox.Close;
                msgBox.Buttons[1].OnClicked += msgBox.Close;
            }
        }

        private static bool TogglePauseMenu(GUIButton button, object obj)
        {
            PauseMenuOpen = !PauseMenuOpen;
            if (!PauseMenuOpen && PauseMenu != null)
            {
                PauseMenu.RectTransform.Parent = null;
                PauseMenu = null;
            }
            return true;
        }

        /// <summary>
        /// Displays a message at the center of the screen, automatically preventing overlapping with other centered messages. TODO: Allow to show messages at the middle of the screen (instead of the top center).
        /// </summary>
        /// 
        public static void AddMessage(LocalizedString message, Color color, float? lifeTime = null, bool playSound = true, GUIFont font = null)
        {
            AddMessage(message.Value, color, lifeTime, playSound, font);
        }

        public static void AddMessage(LocalizedString message, Color color, Vector2 pos, Vector2 velocity, float lifeTime = 3.0f, bool playSound = true, GUISoundType soundType = GUISoundType.UIMessage, int subId = -1)
        {
            AddMessage(message.Value, color, pos, velocity, lifeTime, playSound, soundType, subId);
        }

        public static void AddMessage(string message, Color color, float? lifeTime = null, bool playSound = true, GUIFont font = null)
        {
            lock (mutex)
            {
                if (messages.Any(msg => msg.Text == message)) { return; }
                messages.Add(new GUIMessage(message, color, lifeTime ?? MathHelper.Clamp(message.Length / 5.0f, 3.0f, 10.0f), font ?? GUIStyle.LargeFont));
            }
            if (playSound) { SoundPlayer.PlayUISound(GUISoundType.UIMessage); }
        }

        public static void AddMessage(string message, Color color, Vector2 pos, Vector2 velocity, float lifeTime = 3.0f, bool playSound = true, GUISoundType soundType = GUISoundType.UIMessage, int subId = -1)
        {
            Submarine sub = Submarine.Loaded.FirstOrDefault(s => s.ID == subId);

            var newMessage = new GUIMessage(message, color, pos, velocity, lifeTime, Alignment.Center, GUIStyle.Font, sub: sub);
            if (playSound) { SoundPlayer.PlayUISound(soundType); }

            lock (mutex)
            {
                bool overlapFound = true;
                int tries = 0;
                while (overlapFound)
                {
                    overlapFound = false;
                    foreach (var otherMessage in messages)
                    {
                        float xDiff = otherMessage.Pos.X - newMessage.Pos.X;
                        if (Math.Abs(xDiff) > (newMessage.Size.X + otherMessage.Size.X) / 2) { continue; }
                        float yDiff = otherMessage.Pos.Y - newMessage.Pos.Y;
                        if (Math.Abs(yDiff) > (newMessage.Size.Y + otherMessage.Size.Y) / 2) { continue; }
                        Vector2 moveDir = -(new Vector2(xDiff, yDiff) + Rand.Vector(1.0f));
                        if (moveDir.LengthSquared() > 0.0001f)
                        {
                            moveDir = Vector2.Normalize(moveDir);
                        }
                        else
                        {
                            moveDir = Rand.Vector(1.0f);
                        }
                        moveDir.Y = -Math.Abs(moveDir.Y);
                        newMessage.Pos -= Vector2.UnitY * 10;
                    }
                    tries++;
                    if (tries > 20) { break; }
                }
                messages.Add(newMessage);
            }
        }

        public static void ClearMessages()
        {
            lock (mutex)
            {
                messages.Clear();
            }
        }

        public static bool IsFourByThree()
        {
            float aspectRatio = HorizontalAspectRatio;
            return aspectRatio > 1.3f && aspectRatio < 1.4f;
        }

        public static void SetSavingIndicatorState(bool enabled)
        {
            if (enabled)
            {
                timeUntilSavingIndicatorDisabled = null;
            }
            isSavingIndicatorEnabled = enabled;
        }

        public static void DisableSavingIndicatorDelayed(float delay = 3.0f)
        {
            if (!isSavingIndicatorEnabled) { return; }
            timeUntilSavingIndicatorDisabled = delay;
        }
#endregion
    }
}
