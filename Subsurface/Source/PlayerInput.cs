using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Subsurface
{

    public enum InputType 
    { 
        Select, 
        ActionHit, ActionHeld, 
        SecondaryHit, SecondaryHeld,
        Left, Right, Up, Down,
        Run
    }

	class Key
	{
		private bool state, stateQueue;
		private bool canBeHeld;

        public bool CanBeHeld
        {
            get { return canBeHeld; }
        }
		
		public Key(bool canBeHeld)
		{
			this.canBeHeld = canBeHeld;
		}

		public bool State
		{
			get 
			{
				return state; 
			}
			set
			{
				//if (value == false) return;
				state = value;
				//if (value) stateQueue = value;
			}
		}

		public void SetState(bool value)
		{
			state = value;
			if (value) stateQueue = value;
		}

		public bool Dequeue
		{
			get
			{
				bool value = stateQueue;
				stateQueue = false;
				return value;
			}
			//set
			//{
			//    stateQueue = value;
			//}
		}

		public void Reset()
		{
			if (!canBeHeld) state = false;
			//stateQueue = false;
		}
	}

	static class PlayerInput
	{
		static MouseState mouseState, oldMouseState;
		static KeyboardState keyboardState, oldKeyboardState;

		static double timeSinceClick;

		const double doubleClickDelay = 0.4;

		static bool doubleClicked;

		public static Keys selectKey = Keys.E;

		public static Vector2 MousePosition
		{
			get { return new Vector2(mouseState.X, mouseState.Y); }
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

		public static bool RightButtonClicked()
		{
			return (oldMouseState.RightButton == ButtonState.Pressed
				&& mouseState.RightButton == ButtonState.Released);
		}

		public static bool DoubleClicked()
		{
			return doubleClicked;
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
		}
	}
}
