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
        Attack,
        Run, Crouch,
        Chat, CrewOrders
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
            get { return new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight).Contains(MousePosition); }
        }

		public static Vector2 MouseSpeed
		{
			get 
			{
                return GameMain.WindowActive ? MousePosition - new Vector2(oldMouseState.X, oldMouseState.Y) : Vector2.Zero; 
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
			get {  return GameMain.WindowActive ? mouseState.ScrollWheelValue - oldMouseState.ScrollWheelValue : 0; }
			
		}

		public static bool LeftButtonHeld()
		{
            return GameMain.WindowActive && mouseState.LeftButton == ButtonState.Pressed;
		}

        public static bool LeftButtonDown()
        {
            return GameMain.WindowActive && 
                oldMouseState.LeftButton == ButtonState.Released &&
                mouseState.LeftButton == ButtonState.Pressed;
        }

		public static bool LeftButtonReleased()
		{
            return GameMain.WindowActive && mouseState.LeftButton == ButtonState.Released;
		}
        

		public static bool LeftButtonClicked()
		{
            return (GameMain.WindowActive && 
                oldMouseState.LeftButton == ButtonState.Pressed
				&& mouseState.LeftButton == ButtonState.Released);
		}

        public static bool RightButtonHeld()
        {
            return GameMain.WindowActive && mouseState.RightButton == ButtonState.Pressed;
        }

        public static bool RightButtonClicked()
		{
            return (GameMain.WindowActive && 
                oldMouseState.RightButton == ButtonState.Pressed
				&& mouseState.RightButton == ButtonState.Released);
		}

		public static bool DoubleClicked()
		{
            return GameMain.WindowActive && doubleClicked;
		}

        public static bool KeyHit(InputType inputType)
        {
            return GameMain.WindowActive && GameMain.Config.KeyBind(inputType).IsHit();
        }

        public static bool KeyDown(InputType inputType)
        {
            return GameMain.WindowActive && GameMain.Config.KeyBind(inputType).IsDown();
        }

        public static bool KeyUp(InputType inputType)
        {
            return GameMain.WindowActive && !GameMain.Config.KeyBind(inputType).IsDown();
        }

		public static bool KeyHit(Keys button)
		{
            return (GameMain.WindowActive && oldKeyboardState.IsKeyDown(button) && keyboardState.IsKeyUp(button));
		}

		public static bool KeyDown(Keys button)
		{
			return (GameMain.WindowActive && keyboardState.IsKeyDown(button));
		}

        public static bool KeyUp(Keys button)
        {
            return GameMain.WindowActive && keyboardState.IsKeyUp(button);
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
		}
	}
}
