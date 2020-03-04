using System.Collections.Generic;

namespace Barotrauma
{
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
