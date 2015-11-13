using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{

    public enum InputType 
    { 
        Select, 
        Use,
        Aim,
        Up, Down, Left, Right,
        Run, Chat
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
                return PlayerInput.LeftButtonDown();
            }
            else if (mouseButton == 1)
            {
                return PlayerInput.RightButtonDown();
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

		public bool Dequeue
		{
			get
			{
				bool value = hitQueue;
				hitQueue = false;
				return value;
			}
		}

        public bool DequeueHeld
        {
            get
            {
                bool value = heldQueue;
                heldQueue = false;
                return value;
            }
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

	public static class PlayerInput
	{
		static MouseState mouseState, oldMouseState;
		static KeyboardState keyboardState, oldKeyboardState;

		static double timeSinceClick;

		const double doubleClickDelay = 0.4;

		static bool doubleClicked;

		public static Keys selectKey = Keys.E;

		public static Vector2 MousePosition
		{
			get { return new Vector2(mouseState.Position.X, mouseState.Position.Y); }
		}

		public static MouseState GetMouseState
		{
			get { return mouseState; }
		}
		public static MouseState GetOldMouseState
		{
			get { return oldMouseState; }
		}

		public static Vector2 MouseSpeed
		{
			get 
			{ 
				return MousePosition - new Vector2(oldMouseState.X, oldMouseState.Y); 
			}
		}

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
			get { return mouseState.ScrollWheelValue - oldMouseState.ScrollWheelValue; }
			
		}

		public static bool LeftButtonDown()
		{
			return mouseState.LeftButton == ButtonState.Pressed;
		}

		public static bool LeftButtonClicked()
		{
			return (oldMouseState.LeftButton == ButtonState.Pressed
				&& mouseState.LeftButton == ButtonState.Released);
		}

        public static bool RightButtonDown()
        {
            return mouseState.RightButton == ButtonState.Pressed;
        }

        public static bool RightButtonClicked()
		{
			return (oldMouseState.RightButton == ButtonState.Pressed
				&& mouseState.RightButton == ButtonState.Released);
		}

		public static bool DoubleClicked()
		{
			return doubleClicked;
		}

        public static bool KeyHit(InputType inputType)
        {
            return GameMain.Config.KeyBind(inputType).IsHit();
        }

        public static bool KeyDOwn(InputType inputType)
        {
            return GameMain.Config.KeyBind(inputType).IsDown();
        }

		public static bool KeyHit(Keys button)
		{
			return (oldKeyboardState.IsKeyDown(button) && keyboardState.IsKeyUp(button));
		}

		public static bool KeyDown(Keys button)
		{
			return (keyboardState.IsKeyDown(button));
		}

		public static void Update(double deltaTime)
		{
			timeSinceClick += deltaTime;

			oldMouseState = mouseState;
			mouseState = Mouse.GetState();

			oldKeyboardState = keyboardState;
			keyboardState = Keyboard.GetState();

			doubleClicked = false;
			if (LeftButtonClicked())
			{
				if (timeSinceClick < doubleClickDelay) doubleClicked = true;
				timeSinceClick = 0.0;
			}

#if LINUX
            foreach (Keys key in keyboardState.GetPressedKeys())
            {
                if (!oldKeyboardState.IsKeyUp(key)) continue;

                char Character = (char)key;

                if (keyboardState.IsKeyUp(Keys.LeftShift) && keyboardState.IsKeyUp(Keys.RightShift))
                {
                    Character = char.ToLower(Character);
                }

                EventInput.EventInput.OnCharEntered(Character);
            }
#endif

		}
	}
}
