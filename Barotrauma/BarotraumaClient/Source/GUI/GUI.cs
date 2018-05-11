using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Collections.Generic;
using EventInput;
using Barotrauma.Extensions;

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
        public static ScalableFont Font, SmallFont, LargeFont;

        private static Texture2D t;
        private static Sprite cursor;
        private static List<GUIMessage> messages = new List<GUIMessage>();
        private static Sound[] sounds;
        private static bool pauseMenuOpen, settingsMenuOpen;
        private static GUIFrame pauseMenu;
        private static Sprite submarineIcon, arrow;

        public static GraphicsDevice GraphicsDevice => GameMain.GraphicsDeviceManager.GraphicsDevice;
        public static GUIStyle Style { get; set; }
        public static Color ScreenOverlayColor { get; set; }
        public static Sprite SubmarineIcon => submarineIcon;
        public static Sprite SpeechBubbleIcon { get; private set; }
        public static Sprite Arrow => arrow;
        public static bool DisableHUD { get; set; }
        public static bool PauseMenuOpen => pauseMenuOpen;
        public static bool SettingsMenuOpen => settingsMenuOpen;
        public static KeyboardDispatcher KeyboardDispatcher { get; private set; }

        public static void Init(GameWindow window, ContentManager content)
        {
            KeyboardDispatcher = new KeyboardDispatcher(window);
            Font = new ScalableFont("Content/Exo2-Medium.otf", 14, GraphicsDevice);
            SmallFont = new ScalableFont("Content/Exo2-Light.otf", 12, GraphicsDevice);
            LargeFont = new ScalableFont("Content/Code Pro Bold.otf", 22, GraphicsDevice);
            cursor = new Sprite("Content/UI/cursor.png", Vector2.Zero);
            Style = new GUIStyle("Content/UI/style.xml");
        }

        public static void LoadContent(bool loadSounds = true)
        {
            if (loadSounds)
            {
                sounds = new Sound[Enum.GetValues(typeof(GUISoundType)).Length];
                sounds[(int)GUISoundType.Message] = Sound.Load("Content/Sounds/UI/UImsg.ogg", false);
                sounds[(int)GUISoundType.RadioMessage] = Sound.Load("Content/Sounds/UI/radiomsg.ogg", false);
                sounds[(int)GUISoundType.DeadMessage] = Sound.Load("Content/Sounds/UI/deadmsg.ogg", false);
                sounds[(int)GUISoundType.Click] = Sound.Load("Content/Sounds/UI/beep-shinymetal.ogg", false);
                sounds[(int)GUISoundType.PickItem] = Sound.Load("Content/Sounds/pickItem.ogg", false);
                sounds[(int)GUISoundType.PickItemFail] = Sound.Load("Content/Sounds/pickItemFail.ogg", false);
                sounds[(int)GUISoundType.DropItem] = Sound.Load("Content/Sounds/dropItem.ogg", false);
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
        }

        /// <summary>
        /// By default, all the gui elements are drawn automatically in the same order they appear on the update list. 
        /// </summary>
        public static void Draw(float deltaTime, SpriteBatch spriteBatch, Camera cam)
        {
            foreach (var component in updateList)
            {
                if (component.AutoDraw)
                {
                    component.Draw(spriteBatch);
                }
            }

            if (ScreenOverlayColor.A>0.0f)
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

                for (int i = 1; i < Sounds.SoundManager.DefaultSourceCount; i++)
                {
                    Color clr = Color.White;

                    string soundStr = i + ": ";

                    var playingSound = Sounds.SoundManager.GetPlayingSound(i);

                    if (playingSound == null)
                    {
                        soundStr += "none";
                        clr *= 0.5f;
                    }
                    else
                    {
                        soundStr += System.IO.Path.GetFileNameWithoutExtension(playingSound.FilePath);

                        if (Sounds.SoundManager.IsLooping(i))
                        {
                            soundStr += " (looping)";
                            clr = Color.Yellow;
                        }
                    }

                    DrawString(spriteBatch, new Vector2(300, i * 15), soundStr, clr, Color.Black * 0.5f, 0, GUI.SmallFont);
                }
                DrawString(spriteBatch, new Vector2(500, 0), "gui components: " + updateList.Count, Color.White, Color.Black * 0.5f, 0, SmallFont);
                DrawString(spriteBatch, new Vector2(500, 20), "mouse on: " + (MouseOn == null ? "null" : MouseOn.ToString()), Color.White, Color.Black * 0.5f, 0, SmallFont);
                DrawString(spriteBatch, new Vector2(500, 40), "scroll bar value: " + (GUIScrollBar.draggingBar == null ? "null" : GUIScrollBar.draggingBar.BarScroll.ToString()), Color.White, Color.Black * 0.5f, 0, SmallFont);

                HumanoidAnimParams.DrawEditor(spriteBatch);
            }
            
            if (GameMain.NetworkMember != null) GameMain.NetworkMember.Draw(spriteBatch);

            DrawMessages(spriteBatch, (float)deltaTime);

            if (MouseOn != null && !string.IsNullOrWhiteSpace(MouseOn.ToolTip))
            {
                MouseOn.DrawToolTip(spriteBatch);
            }

            if (!DisableHUD)
            {
                cursor.Draw(spriteBatch, PlayerInput.LatestMousePosition);
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
            if (component.DrawOrder < 0)
            {
                first.Add(component);
            }
            else if (component.DrawOrder > 0)
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
            while (removals.Count > 0)
            {
                var component = removals.Dequeue();
                updateList.Remove(component);
                if (component as IKeyboardSubscriber == KeyboardDispatcher.Subscriber)
                {
                    KeyboardDispatcher.Subscriber = null;
                }
            }
            ProcessHelperList(first);
            while (additions.Count > 0)
            {
                var component = additions.Dequeue();
                if (!updateList.Contains(component))
                {
                    updateList.Add(component);
                }
            }
            ProcessHelperList(last);
        }

        private static void ProcessHelperList(List<GUIComponent> list)
        {
            if (list.Count == 0) { return; }
            list.Sort((previous, next) => next.DrawOrder.CompareTo(previous.DrawOrder));
            foreach (var item in list)
            {
                if (!updateList.Contains(item))
                {
                    updateList.Add(item);
                }
            }
            list.Clear();
        }

        private static void HandlePersistingElements(float deltaTime)
        {
            HumanoidAnimParams.UpdateEditor(deltaTime);
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
            //ClearUpdateList();
            HandlePersistingElements(deltaTime);
            RefreshUpdateList();
            UpdateMouseOn();
            updateList.ForEach(c => c.Update(deltaTime));
            //ClearUpdateList();
        }

        #region Element drawing
        public static void DrawLine(SpriteBatch sb, Vector2 start, Vector2 end, Color clr, float depth = 0.0f, int width = 1)
        {
            Vector2 edge = end - start;
            // calculate angle to rotate line
            float angle = (float)Math.Atan2(edge.Y, edge.X);

            sb.Draw(t,
                new Rectangle(// rectangle defines shape of line and position of start of line
                    (int)start.X,
                    (int)start.Y,
                    (int)edge.Length(), //sb will strech the texture to fill this rectangle
                    width), //width of line, change this to make thicker line
                null,
                clr, //colour of line
                angle,     //angle of line (calulated above)
                new Vector2(0, 0), // point in line about which to rotate
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

        private static void DrawMessages(SpriteBatch spriteBatch, float deltaTime)
        {
            if (messages.Count == 0) return;

            Vector2 currPos = new Vector2(GameMain.GraphicsWidth / 2.0f, GameMain.GraphicsHeight * 0.7f);

            int i = 1;
            foreach (GUIMessage msg in messages)
            {
                float alpha = 1.0f;

                if (msg.LifeTime < 1.0f)
                {
                    alpha -= 1.0f - msg.LifeTime;
                }

                if (msg.AutoCenter)
                {
                    msg.Pos = MathUtils.SmoothStep(msg.Pos, currPos, deltaTime * 20.0f);
                    currPos.Y += 30.0f;
                }

                Font.DrawString(spriteBatch, msg.Text,
                    new Vector2((int)msg.Pos.X - 1, (int)msg.Pos.Y - 1),
                    Color.Black * alpha * 0.5f, 0.0f,
                    msg.Origin, 1.0f, SpriteEffects.None, 0.0f);

                Font.DrawString(spriteBatch, msg.Text,
                    new Vector2((int)msg.Pos.X, (int)msg.Pos.Y),
                    msg.Color * alpha, 0.0f,
                    msg.Origin, 1.0f, SpriteEffects.None, 0.0f);


                messages[0].LifeTime -= deltaTime / i;

                i++;
            }

            if (messages[0].LifeTime <= 0.0f) messages.Remove(messages[0]);
        }
        #endregion

        #region Element creation
        public static Texture2D CreateCircle(int radius)
        {
            int outerRadius = radius * 2 + 2; // So circle doesn't go out of bounds
            Texture2D texture = new Texture2D(GraphicsDevice, outerRadius, outerRadius);

            Color[] data = new Color[outerRadius * outerRadius];

            // Colour the entire texture transparent first.
            for (int i = 0; i < data.Length; i++)
                data[i] = Color.Transparent;

            // Work out the minimum step necessary using trigonometry + sine approximation.
            double angleStep = 1f / radius;

            for (double angle = 0; angle < Math.PI * 2; angle += angleStep)
            {
                // Use the parametric definition of a circle: http://en.wikipedia.org/wiki/Circle#Cartesian_coordinates
                int x = (int)Math.Round(radius + radius * Math.Cos(angle));
                int y = (int)Math.Round(radius + radius * Math.Sin(angle));

                data[y * outerRadius + x + 1] = Color.White;
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
                pauseMenu = new GUIFrame(new Rectangle(0, 0, 200, 300), null, Alignment.Center, "");

                int y = 0;
                var button = new GUIButton(new Rectangle(0, y, 0, 30), "Resume", Alignment.CenterX, "", pauseMenu);
                button.OnClicked = TogglePauseMenu;

                y += 60;

                button = new GUIButton(new Rectangle(0, y, 0, 30), "Settings", Alignment.CenterX, "", pauseMenu);
                button.OnClicked = (btn, userData) =>
                {
                    TogglePauseMenu();
                    settingsMenuOpen = !settingsMenuOpen;

                    return true;
                };


                y += 60;

                if (Screen.Selected == GameMain.GameScreen && GameMain.GameSession != null)
                {
                    SinglePlayerCampaign spMode = GameMain.GameSession.GameMode as SinglePlayerCampaign;
                    if (spMode != null)
                    {
                        button = new GUIButton(new Rectangle(0, y, 0, 30), "Load previous", Alignment.CenterX, "", pauseMenu);
                        button.OnClicked += (btn, userData) =>
                        {
                            TogglePauseMenu(btn, userData);
                            GameMain.GameSession.LoadPrevious();
                            return true;
                        };

                        y += 60;
                    }
                }

                if (Screen.Selected == GameMain.LobbyScreen)
                {
                    SinglePlayerCampaign spMode = GameMain.GameSession.GameMode as SinglePlayerCampaign;
                    if (spMode != null)
                    {
                        button = new GUIButton(new Rectangle(0, y, 0, 30), "Save & quit", Alignment.CenterX, "", pauseMenu);
                        button.OnClicked += QuitClicked;
                        button.OnClicked += TogglePauseMenu;
                        button.UserData = "save";

                        y += 60;
                    }
                }


                button = new GUIButton(new Rectangle(0, y, 0, 30), "Quit", Alignment.CenterX, "", pauseMenu);
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
        public static void AddMessage(string message, Color color, float lifeTime = 3.0f, bool playSound = true)
        {
            if (messages.Count > 0 && messages[messages.Count - 1].Text == message)
            {
                messages[messages.Count - 1].LifeTime = lifeTime;
                return;
            }

            Vector2 pos = new Vector2(GameMain.GraphicsWidth / 2.0f, GameMain.GraphicsHeight * 0.7f);
            pos.Y += messages.FindAll(m => m.AutoCenter).Count * 30;

            messages.Add(new GUIMessage(message, color, pos, lifeTime, Alignment.Center, true));
            if (playSound) PlayUISound(GUISoundType.Message);
        }

        /// <summary>
        /// Display and automatically fade out a piece of text at an arbitrary position on the screen
        /// </summary>
        public static void AddMessage(string message, Vector2 position, Alignment alignment, Color color, float lifeTime = 3.0f, bool playSound = true)
        {
            if (messages.Count > 0 && messages[messages.Count - 1].Text == message)
            {
                messages[messages.Count - 1].LifeTime = lifeTime;
                return;
            }

            messages.Add(new GUIMessage(message, color, position, lifeTime, alignment, false));
            if (playSound) PlayUISound(GUISoundType.Message);
        }

        public static void PlayUISound(GUISoundType soundType)
        {
            if (sounds == null) return;

            int soundIndex = (int)soundType;
            if (soundIndex < 0 || soundIndex >= sounds.Length) return;

            sounds[soundIndex].Play();
        }
        #endregion
    }
}
