using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace Barotrauma
{
    public enum MouseButton
    {
        None = -1,
        PrimaryMouse = 0,
        SecondaryMouse = 1,
        MiddleMouse = 2,
        MouseButton4 = 3,
        MouseButton5 = 4,
        MouseWheelUp = 5,
        MouseWheelDown = 6
    }

    public class KeyOrMouse
    {
        public readonly Keys Key;

        private LocalizedString name;

        public LocalizedString Name
        {
            get
            {
                if (name == null) { name = GetName(); }
                return name;
            }
        }

        public MouseButton MouseButton { get; private set; }

        public static implicit operator KeyOrMouse(Keys key) { return new KeyOrMouse(key); }
        public static implicit operator KeyOrMouse(MouseButton mouseButton) { return new KeyOrMouse(mouseButton); }

        public KeyOrMouse(Keys keyBinding)
        {
            this.Key = keyBinding;
            this.MouseButton = MouseButton.None;
        }

        public KeyOrMouse(MouseButton mouseButton)
        {
            this.Key = Keys.None;
            this.MouseButton = mouseButton;
        }

        public bool IsDown()
        {
            switch (MouseButton)
            {
                case MouseButton.None:
                    if (Key == Keys.None) { return false; }
                    return PlayerInput.KeyDown(Key);
                case MouseButton.PrimaryMouse:
                    return PlayerInput.PrimaryMouseButtonHeld();
                case MouseButton.SecondaryMouse:
                    return PlayerInput.SecondaryMouseButtonHeld();
                case MouseButton.MiddleMouse:
                    return PlayerInput.MidButtonHeld();
                case MouseButton.MouseButton4:
                    return PlayerInput.Mouse4ButtonHeld();
                case MouseButton.MouseButton5:
                    return PlayerInput.Mouse5ButtonHeld();
                case MouseButton.MouseWheelUp: // No real way of "holding" a mouse wheel key, but then again it makes no sense to bind the key to this kind of task.
                    return PlayerInput.MouseWheelUpClicked();
                case MouseButton.MouseWheelDown:
                    return PlayerInput.MouseWheelDownClicked();
            }

            return false;
        }

        public bool IsHit()
        {
            switch (MouseButton)
            {
                case MouseButton.None:
                    if (Key == Keys.None) { return false; }
                    return PlayerInput.KeyHit(Key);
                case MouseButton.PrimaryMouse:
                    return PlayerInput.PrimaryMouseButtonDown();
                case MouseButton.SecondaryMouse:
                    return PlayerInput.SecondaryMouseButtonDown();
                case MouseButton.MiddleMouse:
                    return PlayerInput.MidButtonDown();
                case MouseButton.MouseButton4:
                    return PlayerInput.Mouse4ButtonDown();
                case MouseButton.MouseButton5:
                    return PlayerInput.Mouse5ButtonDown();
                case MouseButton.MouseWheelUp:
                    return PlayerInput.MouseWheelUpClicked();
                case MouseButton.MouseWheelDown:
                    return PlayerInput.MouseWheelDownClicked();
            }

            return false;
        }

        public override bool Equals(object obj)
        {
            if (obj is KeyOrMouse keyOrMouse)
            {
                return this == keyOrMouse;
            }
            else
            {
                return false;
            }
        }

        public static bool operator ==(KeyOrMouse a, KeyOrMouse b)
        {
            if (a is null)
            {
                return b is null;
            }
            else if (a.MouseButton != MouseButton.None)
            {
                return !(b is null) && a.MouseButton == b.MouseButton;
            }
            else
            {
                return !(b is null) && a.Key.Equals(b.Key);
            }
        }

        public static bool operator !=(KeyOrMouse a, KeyOrMouse b)
        {
            return !(a == b);
        }

        public static bool operator ==(KeyOrMouse keyOrMouse, Keys key)
        {
            if (keyOrMouse.MouseButton != MouseButton.None) { return false; }
            return keyOrMouse.Key == key;
        }

        public static bool operator !=(KeyOrMouse keyOrMouse, Keys key)
        {
            return !(keyOrMouse == key);
        }

        public static bool operator ==(Keys key, KeyOrMouse keyOrMouse)
        {
            return keyOrMouse == key;
        }

        public static bool operator !=(Keys key, KeyOrMouse keyOrMouse)
        {
            return keyOrMouse != key;
        }

        public static bool operator ==(KeyOrMouse keyOrMouse, MouseButton mb)
        {
            return keyOrMouse.MouseButton == mb && keyOrMouse.Key == Keys.None;
        }

        public static bool operator !=(KeyOrMouse keyOrMouse, MouseButton mb)
        {
            return !(keyOrMouse == mb);
        }

        public static bool operator ==(MouseButton mb, KeyOrMouse keyOrMouse)
        {
            return keyOrMouse == mb;
        }

        public static bool operator !=(MouseButton mb, KeyOrMouse keyOrMouse)
        {
            return keyOrMouse != mb;
        }

        public override string ToString()
        {
            switch (MouseButton)
            {
                case MouseButton.None:
                    return Key.ToString();
                default:
                    return MouseButton.ToString();
            }
        }

        public override int GetHashCode()
        {
            var hashCode = int.MinValue;
            hashCode = hashCode * -1521134295 + Key.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<int?>.Default.GetHashCode((int)MouseButton);
            return hashCode;
        }

        public LocalizedString GetName()
        {
            if (PlayerInput.NumberKeys.Contains(Key))
            {
                return Key.ToString().Substring(1, 1);
            }
            if (MouseButton != MouseButton.None)
            {
                switch (MouseButton)
                {
                    case MouseButton.PrimaryMouse:
                        return PlayerInput.PrimaryMouseLabel;
                    case MouseButton.SecondaryMouse:
                        return PlayerInput.SecondaryMouseLabel;
                    default:
                        return TextManager.Get($"Input.{MouseButton}");
                }
            }
            else
            {
                return  Key.ToString();
            }            
        }
    }

    public static class PlayerInput
    {
        static MouseState mouseState, oldMouseState;
        static MouseState latestMouseState; //the absolute latest state, do NOT use for player interaction
        static KeyboardState keyboardState, oldKeyboardState;

        static double timeSinceClick;
        static Point lastClickPosition;

        const float DoubleClickDelay = 0.4f;
        public static float MaxDoubleClickDistance 
        {
            get { return Math.Max(15.0f * Math.Max(GameMain.GraphicsHeight / 1920.0f, GameMain.GraphicsHeight / 1080.0f), 10.0f); }
        }

        static bool doubleClicked;

        static bool allowInput;
        static bool wasWindowActive;

        public static readonly List<Keys> NumberKeys = new List<Keys> { Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9 };

#if WINDOWS
        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int smIndex);
        private const int SM_SWAPBUTTON = 23;

        public static bool MouseButtonsSwapped()
        {
            return GetSystemMetrics(SM_SWAPBUTTON) != 0;
        }
#else
        public static bool MouseButtonsSwapped()
        {
            return false; //TODO: implement on other platforms?
        }
#endif

        public static readonly LocalizedString PrimaryMouseLabel = TextManager.Get($"Input.{(!MouseButtonsSwapped() ? "Left" : "Right")}Mouse");
        public static readonly LocalizedString SecondaryMouseLabel = TextManager.Get($"Input.{(!MouseButtonsSwapped() ? "Right" : "Left")}Mouse");

        public static Vector2 MousePosition
        {
            get { return new Vector2(mouseState.Position.X, mouseState.Position.Y); }
        }

        public static Vector2 LatestMousePosition
        {
            get { return new Vector2(latestMouseState.Position.X, latestMouseState.Position.Y); }
        }

        public static bool MouseInsideWindow
        {
            get { return new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight).Contains(MousePosition); }
        }

        public static Vector2 MouseSpeed
        {
            get
            {
                return AllowInput ? MousePosition - new Vector2(oldMouseState.X, oldMouseState.Y) : Vector2.Zero;
            }
        }

        private static bool AllowInput
        {
            get { return GameMain.WindowActive && allowInput; }
        }

        public static Vector2 MouseSpeedPerSecond { get; private set; }

        public static KeyboardState GetKeyboardState
        {
            get { return keyboardState; }
        }

        public static KeyboardState GetOldKeyboardState
        {
            get { return oldKeyboardState; }
        }

        public static int ScrollWheelSpeed
        {
            get { return AllowInput ? mouseState.ScrollWheelValue - oldMouseState.ScrollWheelValue : 0; }

        }

        public static bool PrimaryMouseButtonHeld()
        {
            return AllowInput && mouseState.LeftButton == ButtonState.Pressed;
        }

        public static bool PrimaryMouseButtonDown()
        {
            return AllowInput &&
                oldMouseState.LeftButton == ButtonState.Released &&
                mouseState.LeftButton == ButtonState.Pressed;
        }

        public static bool PrimaryMouseButtonReleased()
        {
            return AllowInput && mouseState.LeftButton == ButtonState.Released;
        }


        public static bool PrimaryMouseButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.LeftButton == ButtonState.Pressed
                && mouseState.LeftButton == ButtonState.Released);
        }

        public static bool SecondaryMouseButtonHeld()
        {
            return AllowInput && mouseState.RightButton == ButtonState.Pressed;
        }

        public static bool SecondaryMouseButtonDown()
        {
            return AllowInput &&
                oldMouseState.RightButton == ButtonState.Released &&
                mouseState.RightButton == ButtonState.Pressed;
        }

        public static bool SecondaryMouseButtonReleased()
        {
            return AllowInput && mouseState.RightButton == ButtonState.Released;
        }

        public static bool SecondaryMouseButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.RightButton == ButtonState.Pressed
                && mouseState.RightButton == ButtonState.Released);
        }


        public static bool MidButtonHeld()
        {
            return AllowInput && mouseState.MiddleButton == ButtonState.Pressed;
        }

        public static bool MidButtonDown()
        {
            return AllowInput &&
                oldMouseState.MiddleButton == ButtonState.Released
                && mouseState.MiddleButton == ButtonState.Pressed;
        }

        public static bool MidButtonReleased()
        {
            return AllowInput && mouseState.MiddleButton == ButtonState.Released;
        }

        public static bool MidButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.MiddleButton == ButtonState.Pressed
                && mouseState.MiddleButton == ButtonState.Released);
        }

        public static bool Mouse4ButtonHeld()
        {
            return AllowInput && mouseState.XButton1 == ButtonState.Pressed;
        }

        public static bool Mouse4ButtonDown()
        {
            return (AllowInput &&
                oldMouseState.XButton1 == ButtonState.Released
                && mouseState.XButton1 == ButtonState.Pressed);
        }

        public static bool Mouse4ButtonReleased()
        {
            return AllowInput && mouseState.XButton1 == ButtonState.Released;
        }

        public static bool Mouse4ButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.XButton1 == ButtonState.Pressed
                && mouseState.XButton1 == ButtonState.Released);
        }

        public static bool Mouse5ButtonHeld()
        {
            return AllowInput && mouseState.XButton2 == ButtonState.Pressed;
        }

        public static bool Mouse5ButtonDown()
        {
            return (AllowInput &&
                oldMouseState.XButton2 == ButtonState.Released
                && mouseState.XButton2 == ButtonState.Pressed);
        }

        public static bool Mouse5ButtonReleased()
        {
            return AllowInput && mouseState.XButton2 == ButtonState.Released;
        }

        public static bool Mouse5ButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.XButton2 == ButtonState.Pressed
                && mouseState.XButton2 == ButtonState.Released);
        }

        public static bool MouseWheelUpClicked()
        {
            return (AllowInput && ScrollWheelSpeed > 0);
        }

        public static bool MouseWheelDownClicked()
        {
            return (AllowInput && ScrollWheelSpeed < 0);
        }

        public static bool DoubleClicked()
        {
            return AllowInput && doubleClicked;
        }

        public static bool KeyHit(InputType inputType)
        {
            return AllowInput && GameSettings.CurrentConfig.KeyMap.Bindings[inputType].IsHit();
        }

        public static bool KeyDown(InputType inputType)
        {
            return AllowInput && GameSettings.CurrentConfig.KeyMap.Bindings[inputType].IsDown();
        }

        public static bool KeyUp(InputType inputType)
        {
            return AllowInput && !GameSettings.CurrentConfig.KeyMap.Bindings[inputType].IsDown();
        }

        public static bool KeyHit(Keys button)
        {
            return AllowInput && oldKeyboardState.IsKeyUp(button) && keyboardState.IsKeyDown(button);
        }

        public static bool InventoryKeyHit(int index)
        {
            if (index == -1) return false;
            return AllowInput && GameSettings.CurrentConfig.InventoryKeyMap.Bindings[index].IsHit();
        }

        public static bool KeyDown(Keys button)
        {
            return AllowInput && keyboardState.IsKeyDown(button);
        }

        public static bool KeyUp(Keys button)
        {
            return AllowInput && keyboardState.IsKeyUp(button);
        }

        public static bool IsShiftDown()
        {
            return KeyDown(Keys.LeftShift) || KeyDown(Keys.RightShift);
        }
        
        public static bool IsCtrlDown()
        {
#if !OSX
            return KeyDown(Keys.LeftControl) || KeyDown(Keys.RightControl);
#else
            return KeyDown(Keys.LeftWindows) || KeyDown(Keys.RightWindows);
#endif
        }

        public static bool IsAltDown()
        {
            return KeyDown(Keys.LeftAlt) || KeyDown(Keys.RightAlt);
        }

        public static void Update(double deltaTime)
        {
            timeSinceClick += deltaTime;

            if (!GameMain.WindowActive)
            {
                wasWindowActive = false;
                return;
            }

            //window was not active during the previous frame -> ignore inputs from this frame
            if (!wasWindowActive)
            {
                wasWindowActive = true;
                allowInput = false;
            }
            else
            {
                allowInput = true;
            }

            oldMouseState = mouseState;
            mouseState = latestMouseState;
            UpdateVariable();

            oldKeyboardState = keyboardState;
            keyboardState = Keyboard.GetState();

            MouseSpeedPerSecond = MouseSpeed / (float)deltaTime;

            // Split into two to not accept drag & drop releasing as part of a double-click
            doubleClicked = false;
            if (PrimaryMouseButtonClicked())
            {
                float dist = (mouseState.Position - lastClickPosition).ToVector2().Length();

                if (timeSinceClick < DoubleClickDelay && dist < MaxDoubleClickDistance)
                {
                    doubleClicked = true;
                    timeSinceClick = DoubleClickDelay;
                }
                else if (timeSinceClick < DoubleClickDelay)
                {
                    lastClickPosition = mouseState.Position;
                }
                if (!doubleClicked && dist < MaxDoubleClickDistance)
                {
                    timeSinceClick = 0.0;
                }
            }           

            if (PrimaryMouseButtonDown())
            {
                lastClickPosition = mouseState.Position;                
            }
        }

        public static void UpdateVariable()
        {
            //do NOT use this for actual interaction with the game, this is to be used for debugging and rendering ONLY

            latestMouseState = Mouse.GetState();
        }
    }
}
