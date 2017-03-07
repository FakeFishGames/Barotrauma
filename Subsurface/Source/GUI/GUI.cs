using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    [Flags]
    public enum Alignment 
    { 
        CenterX = 1, Left = 2, Right = 4, CenterY = 8, Top = 16, Bottom = 32 ,
        TopRight = (Top | Right), TopLeft = (Top | Left), TopCenter = (CenterX | Top),
        Center = (CenterX | CenterY),
        BottomRight = (Bottom | Right), BottomLeft = (Bottom | Left), BottomCenter = (CenterX | Bottom)
    }

    public enum GUISoundType
    {
        Message,
        RadioMessage,
        DeadMessage,
        Click,
        Inventory,
    }
    
    public class GUI
    {

        public static GUIStyle Style;

        private static Texture2D t;
        public static ScalableFont Font, SmallFont, LargeFont;

        private static Sprite cursor;

        private static GraphicsDevice graphicsDevice;
        public static GraphicsDevice GraphicsDevice
        {
            get
            {
                return graphicsDevice;
            }
            set
            {
                graphicsDevice = value;
            }
        }

        private static List<GUIMessage> messages = new List<GUIMessage>();

        private static Sound[] sounds;

        private static bool pauseMenuOpen, settingsMenuOpen;
        private static GUIFrame pauseMenu;

        public static Color ScreenOverlayColor;

        private static Sprite submarineIcon, arrow;

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

        public static bool DisableHUD;

        public static void Init(ContentManager content)
        {
            Font = new ScalableFont("Content/Exo2-Medium.otf", 14, graphicsDevice);
            SmallFont = new ScalableFont("Content/Exo2-Light.otf", 12, graphicsDevice);
            LargeFont = new ScalableFont("Content/CODE Bold.otf", 22, graphicsDevice);

            cursor = new Sprite("Content/UI/cursor.png", Vector2.Zero);

            Style = new GUIStyle("Content/UI/style.xml");
        }

        public static bool PauseMenuOpen
        {
            get { return pauseMenuOpen; }
        }

        public static bool SettingsMenuOpen
        {
            get { return settingsMenuOpen; }
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

                sounds[(int)GUISoundType.Inventory] = Sound.Load("Content/Sounds/pickItem.ogg", false);

            }

            // create 1x1 texture for line drawing
            t = new Texture2D(graphicsDevice, 1, 1);
            t.SetData(new Color[] { Color.White });// fill the texture with white

            submarineIcon = new Sprite("Content/UI/uiIcons.png", new Rectangle(0, 192, 64, 64), null);
            submarineIcon.Origin = submarineIcon.size / 2;

            arrow = new Sprite("Content/UI/uiIcons.png", new Rectangle(80, 240, 16, 16), null);
            arrow.Origin = arrow.size / 2;

            SpeechBubbleIcon = new Sprite("Content/UI/uiIcons.png", new Rectangle(0, 129, 65, 61), null);
            SpeechBubbleIcon.Origin = SpeechBubbleIcon.size / 2;
        }

        public static void TogglePauseMenu()
        {
            if (Screen.Selected == GameMain.MainMenuScreen) return;

            settingsMenuOpen = false;

            TogglePauseMenu(null, null);

            if (pauseMenuOpen)
            {
                pauseMenu = new GUIFrame(new Rectangle(0, 0, 200, 300), null, Alignment.Center, Style);

                int y = 0;
                var button = new GUIButton(new Rectangle(0, y, 0, 30), "Resume", Alignment.CenterX, Style, pauseMenu);
                button.OnClicked = TogglePauseMenu;

                y += 60;

                button = new GUIButton(new Rectangle(0, y, 0, 30), "Settings", Alignment.CenterX, Style, pauseMenu);
                button.OnClicked = (btn, userData) => 
                {
                    TogglePauseMenu();
                    settingsMenuOpen = !settingsMenuOpen;
                    
                    return true; 
                };


                y += 60;

                if (Screen.Selected == GameMain.GameScreen && GameMain.GameSession != null)
                {
                    SinglePlayerMode spMode = GameMain.GameSession.gameMode as SinglePlayerMode;
                    if (spMode != null)
                    {
                        button = new GUIButton(new Rectangle(0, y, 0, 30), "Load previous", Alignment.CenterX, Style, pauseMenu);
                        button.OnClicked += TogglePauseMenu;
                        button.OnClicked += GameMain.GameSession.LoadPrevious;

                        y += 60;
                    }
                }

                if (Screen.Selected == GameMain.LobbyScreen)
                {
                    SinglePlayerMode spMode = GameMain.GameSession.gameMode as SinglePlayerMode;
                    if (spMode != null)
                    {
                        button = new GUIButton(new Rectangle(0, y, 0, 30), "Save & quit", Alignment.CenterX, Style, pauseMenu);
                        button.OnClicked += QuitClicked;
                        button.OnClicked += TogglePauseMenu;
                        button.UserData = "save";

                        y += 60;
                    }
                }


                button = new GUIButton(new Rectangle(0, y, 0, 30), "Quit", Alignment.CenterX, Style, pauseMenu);
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
                SaveUtil.SaveGame(GameMain.GameSession.SaveFile);
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
        
        public static void DrawString(SpriteBatch sb, Vector2 pos, string text, Color color, Color? backgroundColor=null, int backgroundPadding=0, ScalableFont font = null)
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


        public static Texture2D CreateCircle(int radius)
        {
            int outerRadius = radius * 2 + 2; // So circle doesn't go out of bounds
            Texture2D texture = new Texture2D(graphicsDevice, outerRadius, outerRadius);

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

            Texture2D texture = new Texture2D(graphicsDevice, textureWidth, textureHeight);
            Color[] data = new Color[textureWidth * textureHeight];

            // Colour the entire texture transparent first.
            for (int i = 0; i < data.Length; i++)
                data[i] = Color.Transparent;

            // Work out the minimum step necessary using trigonometry + sine approximation.
            double angleStep = 1f / radius;

            for (int i = 0; i < 2; i++ )
            {
                for (double angle = 0; angle < Math.PI * 2; angle += angleStep)
                {
                    // Use the parametric definition of a circle: http://en.wikipedia.org/wiki/Circle#Cartesian_coordinates
                    int x = (int)Math.Round(radius + radius * Math.Cos(angle));
                    int y = (height-1)*i + (int)Math.Round(radius + radius * Math.Sin(angle));

                    data[y * textureWidth + x] = Color.White;
                }
            }

            for (int y = radius; y<textureHeight-radius; y++)
            {
                data[y * textureWidth] = Color.White;
                data[y * textureWidth + (textureWidth-1)] = Color.White;
            }

            texture.SetData(data);
            return texture;
        }

        public static Texture2D CreateRectangle(int width, int height)
        {
            Texture2D texture = new Texture2D(graphicsDevice, width, height);
            Color[] data = new Color[width * height];

            for (int i = 0; i < data.Length; i++)
                data[i] = Color.Transparent;

            for (int y = 0; y < height; y++)
            {
                data[y * width] = Color.White;
                data[y * width + (width-1)] = Color.White;
            }

            for (int x = 0; x < width; x++)
            {
                data[x] = Color.White;
                data[(height - 1) * width + x] = Color.White;
            }

            texture.SetData(data);
            return texture;
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
                origin = Font.MeasureString(text)/2;
            }
            catch
            {
                origin = Vector2.Zero;
            }

            Font.DrawString(sb, text, new Vector2(rect.Center.X, rect.Center.Y) , Color.White, 0.0f, origin, 1.0f, SpriteEffects.None, 0.0f);

            return clicked;
        }

        public static void Draw(float deltaTime, SpriteBatch spriteBatch, Camera cam)
        {
            if (ScreenOverlayColor.A>0.0f)
            {
                DrawRectangle(
                    spriteBatch,
                    new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                    ScreenOverlayColor, true);
            }

            if (GameMain.DebugDraw)
            {
                DrawString(spriteBatch, new Vector2(10, 10), 
                    "FPS: " + (int)GameMain.FrameCounter.AverageFramesPerSecond,
                    Color.White, Color.Black * 0.5f, 0, SmallFont);

                DrawString(spriteBatch, new Vector2(10, 20),
                    "Physics: " + GameMain.World.UpdateTime,
                    Color.White, Color.Black * 0.5f, 0, SmallFont);

                DrawString(spriteBatch, new Vector2(10, 30),
                    "Bodies: " + GameMain.World.BodyList.Count + " (" + GameMain.World.BodyList.FindAll(b => b.Awake && b.Enabled).Count + " awake)",
                    Color.White, Color.Black * 0.5f, 0, SmallFont);

                if (Screen.Selected.Cam != null)
                {
                    DrawString(spriteBatch, new Vector2(10, 40),
                        "Camera pos: " + Screen.Selected.Cam.Position.ToPoint(),
                        Color.White, Color.Black * 0.5f, 0, SmallFont);
                }

                if (Submarine.MainSub != null)
                {
                    DrawString(spriteBatch, new Vector2(10, 50),
                        "Sub pos: " + Submarine.MainSub.Position.ToPoint(),
                        Color.White, Color.Black * 0.5f, 0, SmallFont);
                }

                for (int i = 1; i < Sounds.SoundManager.DefaultSourceCount; i++)
                {
                    Color clr = Color.White;

                    string soundStr = i+": ";

                    var playingSound = Sounds.SoundManager.GetPlayingSound(i);

                    if (playingSound == null)
                    {
                        soundStr+= "none";
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

                    GUI.DrawString(spriteBatch, new Vector2(200, i * 15), soundStr, clr, Color.Black * 0.5f, 0, GUI.SmallFont);
                }
            }
            
            if (GameMain.NetworkMember != null) GameMain.NetworkMember.Draw(spriteBatch);

            DrawMessages(spriteBatch, (float)deltaTime);

            if (GUIMessageBox.MessageBoxes.Count>0)
            {
                var messageBox = GUIMessageBox.MessageBoxes.Peek();
                if (messageBox != null) messageBox.Draw(spriteBatch);
            }

            if (pauseMenuOpen)
            {
                pauseMenu.Draw(spriteBatch);
            }

            if (settingsMenuOpen)
            {
                GameMain.Config.SettingsFrame.Draw(spriteBatch);
            }
            
            DebugConsole.Draw(spriteBatch);
            
            if (GUIComponent.MouseOn != null && !string.IsNullOrWhiteSpace(GUIComponent.MouseOn.ToolTip)) GUIComponent.MouseOn.DrawToolTip(spriteBatch);
            
            if (!GUI.DisableHUD)
                cursor.Draw(spriteBatch, PlayerInput.MousePosition);            
        }

        public static void AddToGUIUpdateList()
        {
            if (pauseMenuOpen)
            {
                pauseMenu.AddToGUIUpdateList();
            }

            if (settingsMenuOpen)
            {
                GameMain.Config.SettingsFrame.AddToGUIUpdateList();
            }

            if (GUIMessageBox.MessageBoxes.Count > 0)
            {
                var messageBox = GUIMessageBox.MessageBoxes.Peek();
                if (messageBox != null)
                {
                    messageBox.AddToGUIUpdateList();
                }
            }
        }

        public static void Update(float deltaTime)
        {
            if (pauseMenuOpen)
            {
                pauseMenu.Update(deltaTime);
            }

            if (settingsMenuOpen)
            {
                GameMain.Config.SettingsFrame.Update(deltaTime);
            }

            if (GUIMessageBox.MessageBoxes.Count > 0)
            {
                var messageBox = GUIMessageBox.MessageBoxes.Peek();
                if (messageBox != null)
                {
                    messageBox.Update(deltaTime);
                }
            }
        }

        public static void AddMessage(string message, Color color, float lifeTime = 3.0f, bool playSound = true)
        {
            if (messages.Count>0 && messages[messages.Count-1].Text == message)
            {
                messages[messages.Count - 1].LifeTime = lifeTime;
                return;
            }

            Vector2 currPos = new Vector2(GameMain.GraphicsWidth / 2.0f, GameMain.GraphicsHeight * 0.7f);
            currPos.Y += messages.Count * 30;

            messages.Add(new GUIMessage(message, color, currPos, lifeTime));
            if (playSound) PlayUISound(GUISoundType.Message);
        }

        public static void PlayUISound(GUISoundType soundType)
        {
            if (sounds == null) return;

            int soundIndex = (int)soundType;
            if (soundIndex < 0 || soundIndex >= sounds.Length) return;

            sounds[soundIndex].Play();
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

                msg.Pos = MathUtils.SmoothStep(msg.Pos, currPos, deltaTime*20.0f);

                Font.DrawString(spriteBatch, msg.Text,
                    new Vector2((int)msg.Pos.X - 1, (int)msg.Pos.Y - 1),
                    Color.Black * alpha*0.5f, 0.0f,
                    new Vector2((int)(0.5f * msg.Size.X), (int)(0.5f * msg.Size.Y)), 1.0f, SpriteEffects.None, 0.0f);

                Font.DrawString(spriteBatch, msg.Text,
                    new Vector2((int)msg.Pos.X, (int)msg.Pos.Y), 
                    msg.Color * alpha, 0.0f,
                    new Vector2((int)(0.5f * msg.Size.X), (int)(0.5f * msg.Size.Y)), 1.0f, SpriteEffects.None, 0.0f);



                currPos.Y += 30.0f;

                messages[0].LifeTime -= deltaTime/i;

                i++;
            }
            
            if (messages[0].LifeTime <= 0.0f) messages.Remove(messages[0]);
        }
    }
}
