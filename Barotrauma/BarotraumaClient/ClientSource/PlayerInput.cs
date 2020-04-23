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
        LeftMouse = 0,
        RightMouse = 1,
        MiddleMouse = 2,
        MouseButton4 = 3,
        MouseButton5 = 4,
        MouseWheelUp = 5,
        MouseWheelDown = 6,
        PrimaryMouse,
        SecondaryMouse
    }

    public class KeyOrMouse
    {
        public Keys Key { get; private set; }
        public MouseButton MouseButton { get; private set; }

        public KeyOrMouse(Keys keyBinding)
        {
            this.Key = keyBinding;
            this.MouseButton = MouseButton.None;
        }

        public KeyOrMouse(MouseButton mouseButton)
        {
            this.MouseButton = mouseButton;
        }

        public bool IsDown()
        {
            switch (MouseButton)
            {
                case MouseButton.None:
                    return PlayerInput.KeyDown(Key);
                case MouseButton.PrimaryMouse:
                    return PlayerInput.PrimaryMouseButtonHeld();
                case MouseButton.SecondaryMouse:
                    return PlayerInput.SecondaryMouseButtonHeld();
                case MouseButton.LeftMouse:
                    return PlayerInput.LeftButtonHeld();
                case MouseButton.RightMouse:
                    return PlayerInput.RightButtonHeld();
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
                    return PlayerInput.KeyHit(Key);
                case MouseButton.PrimaryMouse:
                    return PlayerInput.PrimaryMouseButtonClicked();
                case MouseButton.SecondaryMouse:
                    return PlayerInput.SecondaryMouseButtonClicked();
                case MouseButton.LeftMouse:
                    return PlayerInput.LeftButtonClicked();
                case MouseButton.RightMouse:
                    return PlayerInput.RightButtonClicked();
                case MouseButton.MiddleMouse:
                    return PlayerInput.MidButtonClicked();
                case MouseButton.MouseButton4:
                    return PlayerInput.Mouse4ButtonClicked();
                case MouseButton.MouseButton5:
                    return PlayerInput.Mouse5ButtonClicked();
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
                if (MouseButton != MouseButton.None)
                {
                    return keyOrMouse.MouseButton == MouseButton;
                }
                else
                {
                    return keyOrMouse.Key.Equals(Key);
                }
            }
            else
            {
                return false;
            }
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

#if WINDOWS
        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int smIndex);

        public static bool MouseButtonsSwapped()
        {
            return GetSystemMetrics(23) != 0; //SM_SWAPBUTTON
        }
#else
        public static bool MouseButtonsSwapped()
        {
            return false; //TODO: implement on other platforms?
        }
#endif

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
            if (MouseButtonsSwapped())
            {
                return RightButtonHeld();
            }
            return LeftButtonHeld();
        }

        public static bool PrimaryMouseButtonDown()
        {
            if (MouseButtonsSwapped())
            {
                return RightButtonDown();
            }
            return LeftButtonDown();
        }

        public static bool PrimaryMouseButtonReleased()
        {
            if (MouseButtonsSwapped())
            {
                return RightButtonReleased();
            }
            return LeftButtonReleased();
        }

        public static bool PrimaryMouseButtonClicked()
        {
            if (MouseButtonsSwapped())
            {
                return RightButtonClicked();
            }
            return LeftButtonClicked();
        }

        public static bool SecondaryMouseButtonHeld()
        {
            if (!MouseButtonsSwapped())
            {
                return RightButtonHeld();
            }
            return LeftButtonHeld();
        }

        public static bool SecondaryMouseButtonDown()
        {
            if (!MouseButtonsSwapped())
            {
                return RightButtonDown();
            }
            return LeftButtonDown();
        }

        public static bool SecondaryMouseButtonReleased()
        {
            if (!MouseButtonsSwapped())
            {
                return RightButtonReleased();
            }
            return LeftButtonReleased();
        }

        public static bool SecondaryMouseButtonClicked()
        {
            if (!MouseButtonsSwapped())
            {
                return RightButtonClicked();
            }
            return LeftButtonClicked();
        }

        public static bool LeftButtonHeld()
        {
            return AllowInput && mouseState.LeftButton == ButtonState.Pressed;
        }

        public static bool LeftButtonDown()
        {
            return AllowInput &&
                oldMouseState.LeftButton == ButtonState.Released &&
                mouseState.LeftButton == ButtonState.Pressed;
        }

        public static bool LeftButtonReleased()
        {
            return AllowInput && mouseState.LeftButton == ButtonState.Released;
        }


        public static bool LeftButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.LeftButton == ButtonState.Pressed
                && mouseState.LeftButton == ButtonState.Released);
        }

        public static bool RightButtonHeld()
        {
            return AllowInput && mouseState.RightButton == ButtonState.Pressed;
        }

        public static bool RightButtonDown()
        {
            return AllowInput &&
                oldMouseState.RightButton == ButtonState.Released &&
                mouseState.RightButton == ButtonState.Pressed;
        }

        public static bool RightButtonReleased()
        {
            return AllowInput && mouseState.RightButton == ButtonState.Released;
        }

        public static bool RightButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.RightButton == ButtonState.Pressed
                && mouseState.RightButton == ButtonState.Released);
        }

        public static bool MidButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.MiddleButton == ButtonState.Pressed
                && mouseState.MiddleButton == ButtonState.Released);
        }

        public static bool MidButtonHeld()
        {
            return AllowInput && mouseState.MiddleButton == ButtonState.Pressed;
        }

        public static bool Mouse4ButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.XButton1 == ButtonState.Pressed
                && mouseState.XButton1 == ButtonState.Released);
        }

        public static bool Mouse4ButtonHeld()
        {
            return AllowInput && mouseState.XButton1 == ButtonState.Pressed;
        }

        public static bool Mouse5ButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.XButton2 == ButtonState.Pressed
                && mouseState.XButton2 == ButtonState.Released);
        }

        public static bool Mouse5ButtonHeld()
        {
            return AllowInput && mouseState.XButton2 == ButtonState.Pressed;
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
            return AllowInput && GameMain.Config.KeyBind(inputType).IsHit();
        }

        public static bool KeyDown(InputType inputType)
        {
            return AllowInput && GameMain.Config.KeyBind(inputType).IsDown();
        }

        public static bool KeyUp(InputType inputType)
        {
            return AllowInput && !GameMain.Config.KeyBind(inputType).IsDown();
        }

        public static bool KeyHit(Keys button)
        {
            return (AllowInput && oldKeyboardState.IsKeyDown(button) && keyboardState.IsKeyUp(button));
        }

        public static bool KeyDown(Keys button)
        {
            return (AllowInput && keyboardState.IsKeyDown(button));
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
            return KeyDown(Keys.LeftControl) || KeyDown(Keys.RightControl);
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
                if (timeSinceClick < DoubleClickDelay &&
                    (mouseState.Position - lastClickPosition).ToVector2().Length() < MaxDoubleClickDistance)
                {
                    doubleClicked = true;
                    timeSinceClick = DoubleClickDelay;
                }
                else if (timeSinceClick < DoubleClickDelay)
                {
                    lastClickPosition = mouseState.Position;
                }

                timeSinceClick = 0.0;
            }           

            if (PrimaryMouseButtonDown())
            {
                if (timeSinceClick > DoubleClickDelay)
                {
                    lastClickPosition = mouseState.Position;
                }
            }
        }

        public static void UpdateVariable()
        {
            //do NOT use this for actual interaction with the game, this is to be used for debugging and rendering ONLY

            latestMouseState = Mouse.GetState();
        }
    }
}
