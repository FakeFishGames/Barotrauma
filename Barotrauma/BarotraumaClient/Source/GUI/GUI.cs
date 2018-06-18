using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Collections.Generic;
using EventInput;
using Barotrauma.Extensions;
using Barotrauma.Sounds;

namespace Barotrauma
{
    public enum GUISoundType
    {
        Message,
        RadioMessage,
        DeadMessage,
        Click,
        PickItem,
        PickItemFail,
        DropItem
    }
    
    public static class GUI
    {
        public static GUICanvas Canvas => GUICanvas.Instance;

        // TODO: obsolate?
        public static float Scale
        {
            get { return (GameMain.GraphicsWidth / 1920.0f + GameMain.GraphicsHeight / 1080.0f) / 2.0f; }
        }

        public static GUIStyle Style;

        private static Texture2D t;

        private static Sprite Cursor => Style.CursorSprite;

        private static GraphicsDevice graphicsDevice;
        public static GraphicsDevice GraphicsDevice
        {
            get
            {
                return graphicsDevice;
            }
        }

        private static List<GUIMessage> messages = new List<GUIMessage>();
        private static Sound[] sounds;
        private static bool pauseMenuOpen, settingsMenuOpen;
        private static GUIFrame pauseMenu;
        private static Sprite submarineIcon, arrow, lockIcon, checkmarkIcon;

        public static KeyboardDispatcher KeyboardDispatcher { get; private set; }

        public static ScalableFont Font => Style?.Font;
        public static ScalableFont SmallFont => Style?.SmallFont;
        public static ScalableFont LargeFont => Style?.LargeFont;

        public static Sprite SubmarineIcon
        {
            get { return submarineIcon; }
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

        public static Sprite CheckmarkIcon
        {
            get { return checkmarkIcon; }
        }

        public static Sprite LockIcon
        {
            get { return lockIcon; }
        }

        public static bool SettingsMenuOpen
        {
            get { return settingsMenuOpen; }
            set { settingsMenuOpen = value; }
        }

        public static bool PauseMenuOpen
        {
            get { return pauseMenuOpen; }
        }

        public static Color ScreenOverlayColor
        {
            get;
            set;
        }

        public static bool DisableHUD;

        public static void Init(GameWindow window, IEnumerable<ContentPackage> selectedContentPackages, GraphicsDevice graphicsDevice)
        {
            GUI.graphicsDevice = graphicsDevice;
            KeyboardDispatcher = new KeyboardDispatcher(window);
            var uiStyles = ContentPackage.GetFilesOfType(selectedContentPackages, ContentType.UIStyle).ToList();
            if (uiStyles.Count == 0)
            {
                DebugConsole.ThrowError("No UI styles defined in the selected content package!");
                return;
            }
            else if (uiStyles.Count > 1)
            {
                DebugConsole.ThrowError("Multiple UI styles defined in the selected content package! Selecting the first one.");
            }

            Style = new GUIStyle(uiStyles[0], graphicsDevice);
        }

        public static void LoadContent(bool loadSounds = true)
        {
            if (loadSounds)
            {
                sounds = new Sound[Enum.GetValues(typeof(GUISoundType)).Length];

                sounds[(int)GUISoundType.Message] = GameMain.SoundManager.LoadSound("Content/Sounds/UI/UImsg.ogg", false);
                sounds[(int)GUISoundType.RadioMessage] = GameMain.SoundManager.LoadSound("Content/Sounds/UI/radiomsg.ogg", false);
                sounds[(int)GUISoundType.DeadMessage] = GameMain.SoundManager.LoadSound("Content/Sounds/UI/deadmsg.ogg", false);
                sounds[(int)GUISoundType.Click] = GameMain.SoundManager.LoadSound("Content/Sounds/UI/beep-shinymetal.ogg", false);

                sounds[(int)GUISoundType.PickItem] = GameMain.SoundManager.LoadSound("Content/Sounds/pickItem.ogg", false);
                sounds[(int)GUISoundType.PickItemFail] = GameMain.SoundManager.LoadSound("Content/Sounds/pickItemFail.ogg", false);
                sounds[(int)GUISoundType.DropItem] = GameMain.SoundManager.LoadSound("Content/Sounds/dropItem.ogg", false);
            }
            // create 1x1 texture for line drawing
            t = new Texture2D(GraphicsDevice, 1, 1);
            t.SetData(new Color[] { Color.White });// fill the texture with white
            submarineIcon = new Sprite("Content/UI/uiIcons.png", new Rectangle(0, 192, 64, 64));
            submarineIcon.Origin = submarineIcon.size / 2;
            arrow = new Sprite("Content/UI/uiIcons.png", new Rectangle(80, 240, 16, 16));
            arrow.Origin = arrow.size / 2;
            SpeechBubbleIcon = new Sprite("Content/UI/uiIcons.png", new Rectangle(0, 129, 65, 61));
            SpeechBubbleIcon.Origin = SpeechBubbleIcon.size / 2;

            lockIcon = new Sprite("Content/UI/UI_Atlas.png", new Rectangle(996, 677, 21, 25));
            lockIcon.Origin = lockIcon.size / 2;

            checkmarkIcon = new Sprite("Content/UI/UI_Atlas.png", new Rectangle(932, 398, 33, 28));
            checkmarkIcon.Origin = checkmarkIcon.size / 2;
        }

        /// <summary>
        /// By default, all the gui elements are drawn automatically in the same order they appear on the update list. 
        /// </summary>
        public static void Draw(Camera cam, SpriteBatch spriteBatch)
        {
            updateList.ForEach(c => c.DrawAuto(spriteBatch));

            if (ScreenOverlayColor.A > 0.0f)
            {
                DrawRectangle(
                    spriteBatch,
                    new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                    ScreenOverlayColor, true);
            }

            if (GameMain.ShowFPS || GameMain.DebugDraw)
            {
                DrawString(spriteBatch, new Vector2(10, 10),
                    "FPS: " + (int)GameMain.FrameCounter.AverageFramesPerSecond,
                    Color.White, Color.Black * 0.5f, 0, SmallFont);
            }

            if (GameMain.DebugDraw)
            {
                DrawString(spriteBatch, new Vector2(10, 25),
                    "Physics: " + GameMain.World.UpdateTime,
                    Color.White, Color.Black * 0.5f, 0, SmallFont);

                DrawString(spriteBatch, new Vector2(10, 40),
                    "Bodies: " + GameMain.World.BodyList.Count + " (" + GameMain.World.BodyList.FindAll(b => b.Awake && b.Enabled).Count + " awake)",
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
                    Color.Lerp(Color.Green, Color.Red, (GameMain.ParticleManager.ParticleCount / (float)GameMain.ParticleManager.MaxParticles)), Color.Black * 0.5f, 0, SmallFont);

                /*var activeParticles = GameMain.ParticleManager.CountActiveParticles();
                int y = 115;
                foreach (KeyValuePair<Particles.ParticlePrefab, int> particleCount in activeParticles)
                {
                    DrawString(spriteBatch, new Vector2(15, y),
                        particleCount.Key.Name+": "+ particleCount.Value,
                        Color.Lerp(Color.Green, Color.Red, (particleCount.Value / (float)GameMain.ParticleManager.MaxParticles)), Color.Black * 0.5f, 0, SmallFont);
                    y += 15;
                }*/

                for (int i = 0; i < SoundManager.SOURCE_COUNT; i++)
                {
                    Color clr = Color.White;
                    string soundStr = i + ": ";
                    SoundChannel playingSoundChannel = GameMain.SoundManager.GetSoundChannelFromIndex(i);
                    if (playingSoundChannel == null)
                    {
                        soundStr += "none";
                        clr *= 0.5f;
                    }
                    else
                    {
                        soundStr += System.IO.Path.GetFileNameWithoutExtension(playingSoundChannel.Sound.Filename);

#if DEBUG
                        if (PlayerInput.GetKeyboardState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.G))
                        {
                            if (PlayerInput.MousePosition.Y>=i*15 && PlayerInput.MousePosition.Y<=i*15+12)
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

                        //if (playingSoundChannel.Position != null) soundStr += " " + Vector3.Distance(GameMain.SoundManager.ListenerPosition, playingSoundChannel.Position.Value) + " " + playingSoundChannel.Near;
                    }

                    DrawString(spriteBatch, new Vector2(300, i * 15), soundStr, clr, Color.Black * 0.5f, 0, GUI.SmallFont);
                }
            }

            if (HUDLayoutSettings.DebugDraw) HUDLayoutSettings.Draw(spriteBatch);

            if (GameMain.NetworkMember != null) GameMain.NetworkMember.Draw(spriteBatch);

            if (Character.Controlled?.Inventory != null)
            {
                if (!Character.Controlled.LockHands && Character.Controlled.Stun >= -0.1f)
                {
                    Inventory.DrawFront(spriteBatch);
                }
            }

            DrawMessages(spriteBatch, cam);

            if (GameMain.DebugDraw)
            {
                DrawString(spriteBatch, new Vector2(500, 0), "gui components: " + updateList.Count, Color.White, Color.Black * 0.5f, 0, SmallFont);
                DrawString(spriteBatch, new Vector2(500, 20), "mouse on: " + (MouseOn == null ? "null" : MouseOn.ToString()), Color.White, Color.Black * 0.5f, 0, SmallFont);
                DrawString(spriteBatch, new Vector2(500, 40), "scroll bar value: " + (GUIScrollBar.draggingBar == null ? "null" : GUIScrollBar.draggingBar.BarScroll.ToString()), Color.White, Color.Black * 0.5f, 0, SmallFont);

                GameMain.GameSession?.EventManager?.DebugDrawHUD(spriteBatch);
            }
            
            //TODO: move this somewhere else
            //HumanoidAnimParams.DrawEditor(spriteBatch); 

            if (MouseOn != null && !string.IsNullOrWhiteSpace(MouseOn.ToolTip))
            {
                MouseOn.DrawToolTip(spriteBatch);
            }

            if (!DisableHUD)
            {
                Cursor.Draw(spriteBatch, PlayerInput.LatestMousePosition);
            }
        }

        #region Update list
        private static List<GUIComponent> updateList = new List<GUIComponent>();
        private static Queue<GUIComponent> removals = new Queue<GUIComponent>();
        private static Queue<GUIComponent> additions = new Queue<GUIComponent>();
        // A helpers list for all elements that have a draw order less than 0.
        private static List<GUIComponent> first = new List<GUIComponent>();
        // A helper list for all elements that have a draw order greater than 0.
        private static List<GUIComponent> last = new List<GUIComponent>();

        public static IEnumerable<GUIComponent> ComponentsToUpdate => updateList;

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
            if (updateList.Contains(component))
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
                if (!updateList.Contains(component))
                {
                    updateList.Add(component);
                }
            }
        }

        private static void ProcessRemovals()
        {
            while (removals.Count > 0)
            {
                var component = removals.Dequeue();
                updateList.Remove(component);
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
                int i = updateList.Count - 1;
                while (updateList[i].UpdateOrder > item.UpdateOrder)
                {
                    i--;
                }
                if (!updateList.Contains(item))
                {
                    updateList.Insert(Math.Max(i, 0), item);
                }
            }
            list.Clear();
        }

        private static void HandlePersistingElements(float deltaTime)
        {
            //TODO: move this somewhere else
            //HumanoidAnimParams.UpdateEditor(deltaTime);
            GUIMessageBox.VisibleBox?.AddToGUIUpdateList();
            if (pauseMenuOpen)
            {
                pauseMenu.AddToGUIUpdateList();
            }
            if (settingsMenuOpen)
            {
                GameMain.Config.SettingsFrame.AddToGUIUpdateList();
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
            MouseOn = null;
            for (int i = updateList.Count - 1; i >= 0; i--)
            {
                GUIComponent c = updateList[i];
                if (c.MouseRect.Contains(PlayerInput.MousePosition))
                {
                    MouseOn = c;
                    break;
                }
            }
            return MouseOn;
        }

        public static void Update(float deltaTime)
        {
            HandlePersistingElements(deltaTime);
            RefreshUpdateList();
            UpdateMouseOn();
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
                sprite.Draw(spriteBatch, iconPos, color * alpha);

                if (unclampedDiff.Length() - 10 > iconDiff.Length())
                {
                    Vector2 normalizedDiff = Vector2.Normalize(targetScreenPos - iconPos);
                    Vector2 arrowOffset = normalizedDiff * sprite.size.X * 0.7f;
                    Arrow.Draw(spriteBatch, iconPos + arrowOffset, color * alpha, MathUtils.VectorToAngle(arrowOffset) + MathHelper.PiOver2);
                }
            }
        }

        public static void DrawLine(SpriteBatch sb, Vector2 start, Vector2 end, Color clr, float depth = 0.0f, int width = 1)
        {
            DrawLine(sb, t, start, end, clr, depth, width);
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
                clicked = PlayerInput.LeftButtonHeld();

                color = clicked ?
                    new Color((int)(color.R * 0.8f), (int)(color.G * 0.8f), (int)(color.B * 0.8f), color.A) :
                    new Color((int)(color.R * 1.2f), (int)(color.G * 1.2f), (int)(color.B * 1.2f), color.A);

                if (!isHoldable) clicked = PlayerInput.LeftButtonClicked();
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
            if (useScissorRect) spriteBatch.GraphicsDevice.ScissorRectangle = HUDLayoutSettings.MessageAreaTop;

            foreach (GUIMessage msg in messages)
            {
                if (msg.WorldSpace) continue;

                Vector2 drawPos = new Vector2(HUDLayoutSettings.MessageAreaTop.Right, HUDLayoutSettings.MessageAreaTop.Center.Y);

                msg.Font.DrawString(spriteBatch, msg.Text, drawPos + msg.Pos + Vector2.One, Color.Black, 0, msg.Origin, 1.0f, SpriteEffects.None, 0);
                msg.Font.DrawString(spriteBatch, msg.Text, drawPos + msg.Pos, msg.Color, 0, msg.Origin, 1.0f, SpriteEffects.None, 0);
                break;                
            }

            if (useScissorRect) spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
            
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

        #endregion

        #region Element creation

        public static Texture2D CreateCircle(int radius, bool filled = false)
        {
            int outerRadius = radius * 2 + 2; // So circle doesn't go out of bounds
            Texture2D texture = new Texture2D(GraphicsDevice, outerRadius, outerRadius);

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
                            data[y * outerRadius + x + 1] = Color.White;
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

                    data[y * outerRadius + x + 1] = Color.White;
                }
            }


            texture.SetData(data);
            return texture;
        }

        public static Texture2D CreateCapsule(int radius, int height)
        {
            int textureWidth = radius * 2, textureHeight = height + radius * 2;

            Texture2D texture = new Texture2D(GraphicsDevice, textureWidth, textureHeight);
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

                    data[y * textureWidth + x] = Color.White;
                }
            }

            for (int y = radius; y < textureHeight - radius; y++)
            {
                data[y * textureWidth] = Color.White;
                data[y * textureWidth + (textureWidth - 1)] = Color.White;
            }

            texture.SetData(data);
            return texture;
        }

        public static Texture2D CreateRectangle(int width, int height)
        {
            Texture2D texture = new Texture2D(GraphicsDevice, width, height);
            Color[] data = new Color[width * height];

            for (int i = 0; i < data.Length; i++)
                data[i] = Color.Transparent;

            for (int y = 0; y < height; y++)
            {
                data[y * width] = Color.White;
                data[y * width + (width - 1)] = Color.White;
            }

            for (int x = 0; x < width; x++)
            {
                data[x] = Color.White;
                data[(height - 1) * width + x] = Color.White;
            }

            texture.SetData(data);
            return texture;
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
        #endregion

        #region Misc
        public static void TogglePauseMenu()
        {
            if (Screen.Selected == GameMain.MainMenuScreen) return;

            settingsMenuOpen = false;

            TogglePauseMenu(null, null);

            if (pauseMenuOpen)
            {
                pauseMenu = new GUIFrame(new RectTransform(Vector2.One, Canvas), style: null, color: Color.Black * 0.5f);
                    
                var pauseMenuInner = new GUIFrame(new RectTransform(new Vector2(0.13f, 0.3f), pauseMenu.RectTransform, Anchor.Center) { MinSize = new Point(200, 300) });

                var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.85f, 0.85f), pauseMenuInner.RectTransform, Anchor.Center))
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                var button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonContainer.RectTransform), "Resume", style: "GUIButtonLarge")
                {
                    OnClicked = TogglePauseMenu
                };

                button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonContainer.RectTransform), "Settings", style: "GUIButtonLarge")
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
                        //TODO: communicate more clearly what this button does
                        button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonContainer.RectTransform), "Load previous", style: "GUIButtonLarge");
                        button.OnClicked += (btn, userData) =>
                        {
                            TogglePauseMenu(btn, userData);
                            GameMain.GameSession.LoadPrevious();
                            return true;
                        };
                    }
                }

                if (Screen.Selected == GameMain.LobbyScreen)
                {
                    if (GameMain.GameSession.GameMode is SinglePlayerCampaign spMode)
                    {
                        button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonContainer.RectTransform), "Save & quit", style: "GUIButtonLarge")
                        {
                            UserData = "save"
                        };
                        button.OnClicked += QuitClicked;
                        button.OnClicked += TogglePauseMenu;
                    }
                }
                
                button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonContainer.RectTransform), "Quit", style: "GUIButtonLarge");
                button.OnClicked += QuitClicked;
                button.OnClicked += TogglePauseMenu;
            }
        }

        private static bool TogglePauseMenu(GUIButton button, object obj)
        {
            pauseMenuOpen = !pauseMenuOpen;
            return true;
        }

        private static bool QuitClicked(GUIButton button, object obj)
        {
            if (button.UserData as string == "save")
            {
                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            }

            if (GameMain.NetworkMember != null)
            {
                GameMain.NetworkMember.Disconnect();
                GameMain.NetworkMember = null;
            }

            CoroutineManager.StopCoroutines("EndCinematic");

            GameMain.GameSession = null;

            GameMain.MainMenuScreen.Select();
            //Game1.MainMenuScreen.SelectTab(null, (int)MainMenuScreen.Tabs.Main);

            return true;
        }

        /// <summary>
        /// Displays a message at the center of the screen, automatically preventing overlapping with other centered messages
        /// </summary>
        public static void AddMessage(string message, Color color, bool playSound = true)
        {
            float lifeTime = MathHelper.Clamp(message.Length / 5.0f, 3.0f, 10.0f);
            foreach (GUIMessage msg in messages)
            {
                if (msg.Text == message) return;
            }
            
            messages.Add(new GUIMessage(message, color, lifeTime, LargeFont));
            if (playSound) PlayUISound(GUISoundType.Message);
        }

        public static void AddMessage(string message, Color color, Vector2 worldPos, Vector2 velocity, float lifeTime = 3.0f, bool playSound = true)
        {
            messages.Add(new GUIMessage(message, color, worldPos, velocity, lifeTime, Alignment.Center, LargeFont));
            if (playSound) PlayUISound(GUISoundType.Message);
        }

        public static void PlayUISound(GUISoundType soundType)
        {
            if (sounds == null) return;

            int soundIndex = (int)soundType;
            if (soundIndex < 0 || soundIndex >= sounds.Length) return;

            sounds[soundIndex].Play(null, "ui");
        }
        #endregion
    }
}
