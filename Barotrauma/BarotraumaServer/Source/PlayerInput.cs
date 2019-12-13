using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    public static class PlayerInput
    {
        public static Keys selectKey = Keys.E;

        public static Vector2 MousePosition
        {
            get { return Vector2.Zero; }
        }

        public static Vector2 LatestMousePosition
        {
            get { return Vector2.Zero; }
        }

        //public static MouseState GetMouseState
        //{
        //    get { return mouseState; }
        //}
        //public static MouseState GetOldMouseState
        //{
        //    get { return oldMouseState; }
        //}

        public static bool MouseInsideWindow
        {
            get { return false; }
        }

        public static Vector2 MouseSpeed
        {
            get
            {
                return Vector2.Zero;
            }
        }

        public static KeyboardState GetKeyboardState
        {
            get { return new KeyboardState(); }
        }

        public static KeyboardState GetOldKeyboardState
        {
            get { return new KeyboardState(); }
        }

        public static int ScrollWheelSpeed
        {
            get { return 0; }

        }

        public static bool MouseButtonsSwapped()
        {
            return false;
        }

        public static bool PrimaryMouseButtonHeld()
        {
            return false;
        }

        public static bool PrimaryMouseButtonDown()
        {
            return false;
        }

        public static bool PrimaryMouseButtonReleased()
        {
            return false;
        }

        public static bool PrimaryMouseButtonClicked()
        {
            return false;
        }

        public static bool SecondaryMouseButtonHeld()
        {
            return false;
        }

        public static bool SecondaryMouseButtonDown()
        {
            return false;
        }

        public static bool SecondaryMouseButtonReleased()
        {
            return false;
        }

        public static bool SecondaryMouseButtonClicked()
        {
            return false;
        }

        public static bool LeftButtonHeld()
        {
            return false;
        }

        public static bool LeftButtonDown()
        {
            return false;
        }

        public static bool LeftButtonReleased()
        {
            return false;
        }


        public static bool LeftButtonClicked()
        {
            return false;
        }

        public static bool RightButtonHeld()
        {
            return false;
        }

        public static bool RightButtonClicked()
        {
            return false;
        }

        public static bool MidButtonClicked()
        {
            return false;
        }

        public static bool MidButtonHeld()
        {
            return false;
        }

        public static bool Mouse4ButtonClicked()
        {
            return false;
        }

        public static bool Mouse4ButtonHeld()
        {
            return false;
        }

        public static bool Mouse5ButtonClicked()
        {
            return false;
        }

        public static bool Mouse5ButtonHeld()
        {
            return false;
        }

        public static bool MouseWheelUpClicked()
        {
            return false;
        }

        public static bool MouseWheelDownClicked()
        {
            return false;
        }

        public static bool DoubleClicked()
        {
            return false;
        }

        public static bool KeyHit(InputType inputType)
        {
            return false;
        }

        public static bool KeyDown(InputType inputType)
        {
            return false;
        }

        public static bool KeyUp(InputType inputType)
        {
            return false;
        }

        public static bool KeyHit(Keys button)
        {
            return false;
        }

        public static bool KeyDown(Keys button)
        {
            return false;
        }

        public static bool KeyUp(Keys button)
        {
            return false;
        }

        public static void Update(double deltaTime)
        {

        }

        public static void UpdateVariable()
        {

        }
    }
}
