using Microsoft.Xna.Framework.Input;

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
        Chat, RadioChat, CrewOrders,
        Ragdoll, Health, Grab,
        SelectNextCharacter,
        SelectPreviousCharacter,
        Voice
    }

    public class KeyOrMouse
    {
        public Keys Key { get; private set; }
        public int? MouseButton { get; private set; }

        public KeyOrMouse(Keys keyBinding)
        {
            this.Key = keyBinding;
        }

        public KeyOrMouse(int mouseButton)
        {
            this.MouseButton = mouseButton;
        }

        public bool IsDown()
        {
            switch (MouseButton)
            {
                case null:
                    return PlayerInput.KeyDown(Key);
                case 0:
                    return PlayerInput.LeftButtonHeld();
                case 1:
                    return PlayerInput.RightButtonHeld();
                case 2:
                    return PlayerInput.MidButtonHeld();
                case 3:
                    return PlayerInput.Mouse4ButtonHeld();
                case 4:
                    return PlayerInput.Mouse5ButtonHeld();
                case 5: // No real way of "holding" a mouse wheel key, but then again it makes no sense to bind the key to this kind of task.
                    return PlayerInput.MouseWheelUpClicked();
                case 6:
                    return PlayerInput.MouseWheelDownClicked();
            }

            return false;
        }

        public bool IsHit()
        {
            switch (MouseButton)
            {
                case null:
                    return PlayerInput.KeyHit(Key);
                case 0:
                    return PlayerInput.LeftButtonClicked();
                case 1:
                    return PlayerInput.RightButtonClicked();
                case 2:
                    return PlayerInput.MidButtonClicked();
                case 3:
                    return PlayerInput.Mouse4ButtonClicked();
                case 4:
                    return PlayerInput.Mouse5ButtonClicked();
                case 5:
                    return PlayerInput.MouseWheelUpClicked();
                case 6:
                    return PlayerInput.MouseWheelDownClicked();
            }

            return false;
        }

        public override string ToString()
        {
            switch (MouseButton)
            {
                case null:
                    return Key.ToString();
                case 0:
                    return "Mouse1";
                case 1:
                    return "Mouse2";
                case 2:
                    return "Mouse3";
                case 3:
                    return "Mouse4";
                case 4:
                    return "Mouse5";
                case 5:
                    return "MouseWheelUp";
                case 6:
                    return "MouseWheelDown";
            }

            return "None";
        }
    }

    class Key
    {
        private bool hit, hitQueue;
        private bool held, heldQueue;

#if CLIENT
        private InputType inputType;

        public Key(InputType inputType)
        {
            this.inputType = inputType;
        }

        private KeyOrMouse binding
        {
            get { return GameMain.Config.KeyBind(inputType); }
        }
#else
        private KeyOrMouse binding;

        public Key(KeyOrMouse binding)
        {
            this.binding = binding;
        }
#endif

        public KeyOrMouse State
        {
            get { return binding; }
        }

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

        public void SetState()
        {
            hit = binding.IsHit();
            if (hit) hitQueue = true;

            held = binding.IsDown();
            if (held) heldQueue = true;
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
