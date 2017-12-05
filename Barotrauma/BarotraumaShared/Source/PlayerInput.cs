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
        Ragdoll
    }

    public class KeyOrMouse
    {
        Keys keyBinding;
        int? mouseButton;

        public Keys Key
        {
            get { return keyBinding; }
        }
        public int? MouseButton
        {
            get { return mouseButton; }
        }

        public KeyOrMouse(Keys keyBinding)
        {
            this.keyBinding = keyBinding;
        }

        public KeyOrMouse(int mouseButton)
        {
            this.mouseButton = mouseButton;
        }

        public bool IsDown()
        { 
            if (mouseButton==null)
            {
                return PlayerInput.KeyDown(keyBinding);
            }
            else if (mouseButton == 0)
            {
                return PlayerInput.LeftButtonHeld();
            }
            else if (mouseButton == 1)
            {
                return PlayerInput.RightButtonHeld();
            }

            return false;
        }

        public bool IsHit()
        {
            if (mouseButton == null)
            {
                return PlayerInput.KeyHit(keyBinding);
            }
            else if (mouseButton == 0)
            {
                return PlayerInput.LeftButtonClicked();
            }
            else if (mouseButton == 1)
            {
                return PlayerInput.RightButtonClicked();
            }

            return false;
        }

        public override string ToString()
        {
            if (mouseButton==null)
            {
                return keyBinding.ToString();
            }
            else if (mouseButton==0)
            {
                return "Mouse1";
            }
            else if (mouseButton==1)
            {
                return "Mouse2";
            }

            return "None";
        }
    }

	class Key
	{
		private bool hit, hitQueue;
        private bool held, heldQueue;


        KeyOrMouse binding;

        //public bool CanBeHeld
        //{
        //    get { return canBeHeld; }
        //}
		
		public Key(KeyOrMouse binding)
		{
            this.binding = binding;
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

        public KeyOrMouse State
        {
            get { return binding; }
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
