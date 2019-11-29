using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;

namespace Barotrauma
{

    public enum InputType
    {
        Select,
        Use,
        Aim,
        Up, Down, Left, Right,
        Attack,
        Run, Crouch,
        InfoTab, Chat, RadioChat, CrewOrders,
        Ragdoll, Health, Grab,
        SelectNextCharacter,
        SelectPreviousCharacter,
        Voice,
        Deselect,
        Shoot
    }

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
                case MouseButton.Primary:
                    return PlayerInput.PrimaryMouseButtonHeld();
                case MouseButton.Secondary:
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
                case MouseButton.Primary:
                    return PlayerInput.PrimaryMouseButtonClicked();
                case MouseButton.Secondary:
                    return PlayerInput.PrimaryMouseButtonClicked();
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

    class Key
    {
        private bool hit, hitQueue;
        private bool held, heldQueue;


        private InputType inputType;

        public Key(InputType inputType)
        {
            this.inputType = inputType;
        }
#if CLIENT
        private KeyOrMouse binding
        {
            get { return GameMain.Config.KeyBind(inputType); }
        }

        private static bool AllowOnGUI(InputType input)
        {
            switch (input)
            {
                case InputType.Attack:
                case InputType.Shoot:
                    return GUI.MouseOn == null;
                default:
                    return true;
            }
        }

        public KeyOrMouse State
        {
            get { return binding; }
        }

        public void SetState()
        {
            hit = binding.IsHit() && AllowOnGUI(inputType);
            if (hit) hitQueue = true;

            held = binding.IsDown() && AllowOnGUI(inputType);
            if (held) heldQueue = true;
        }
#endif

        public bool Hit
        {
            get
            {
                return hit;
            }
            set
            {
                hit = value;
            }
        }

        public bool Held
        {
            get
            {
                return held;
            }
            set
            {
                held = value;
            }
        }


        public void SetState(bool hit, bool held)
        {
            if (hit) hitQueue = true;
            if (held) heldQueue = true;
        }

        public bool DequeueHit()
        {
            bool value = hitQueue;
            hitQueue = false;
            return value;
        }

        public bool DequeueHeld()
        {
            bool value = heldQueue;
            heldQueue = false;
            return value;
        }

        public bool GetHeldQueue
        {
            get { return heldQueue; }
        }

        public bool GetHitQueue
        {
            get { return hitQueue; }
        }

        public void Reset()
        {
            hit = false;
            held = false;
        }

        public void ResetHit()
        {
            hit = false;
            //stateQueue = false;
        }

        public void ResetHeld()
        {
            held = false;
            //stateQueue = false;
        }
    }
}
