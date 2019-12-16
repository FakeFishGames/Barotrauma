using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    // TODO implement into DebugConsole.cs
    // TODO decide what namespace this falls under. Utils? Root? Also probably a better class name name
    /// <summary>
    /// A class used for handling special key actions in chat boxes.
    /// For example tab completion or up/down arrow key history.
    /// </summary>
    public class ChatManager
    {
        // Maximum items we want to store in the history
        private const int maxCount = 10;

        // List of previously stored messages
        private readonly List<string> messageList = new List<string> { string.Empty };

        // Selector index
        private int index;

        // Local changes we've made into previously stored messages
        private string[] localChanges = new string[maxCount];

        /// <summary>
        /// Registers special input actions to the selected input field
        /// </summary>
        /// <param name="element">GUI Element we want to register</param>
        /// <param name="manager">Instance</param>
        public static void RegisterKeys(GUITextBox element, ChatManager manager)
        {
            element.OnKeyHit += (sender, key) =>
            {
                switch (key)
                {
                    // Up/Down Arrow key history action
                    case Keys.Up:
                    case Keys.Down:
                    {
                        // Up arrow key? go up. Down arrow key? go down. Everything else gets binned
                        Direction direction = key == Keys.Up ? Direction.Up : (key == Keys.Down ? Direction.Down : Direction.Other);

                        string newMessage = manager.SelectMessage(direction, element.Text);
                        // Don't do anything if we didn't find anything
                        if (newMessage == null) { return; }

                        element.Text = newMessage;
                        break;
                    }
                    case Keys.Tab:
                        // TODO tab completion behavior, maybe?
                        break;
                }
            };
        }

        // Store a new object
        public void Store(string message)
        {
            Clear();
            // inset to the second position as the first position is reserved for the original message if any
            messageList.Insert(1, message);
            // we don't want to add too many messages... just in case
            if (messageList.Count > maxCount)
            {
                messageList.RemoveAt(messageList.Count - 1);
            }
        }

        /// <summary>
        /// Call this function whenever we should stop doing special stuff and return normal behavior.
        /// For example when you deselect the chat box.
        /// </summary>
        public void Clear()
        {
            index = 0;
            localChanges = new string[maxCount];
        }

        /// <summary>
        /// Scroll up or down on the message history and return a message
        /// </summary>
        /// <param name="direction">Direction we want to scroll the stack</param>
        /// <param name="original">Leftover text that is in the chat box when we override it</param>
        /// <returns>A message or null</returns>
        private string SelectMessage(Direction direction, string original)
        {
            if (direction == Direction.Other) { return null; }
            
            // temporarily save our changes in case we fat-finger and want to go back
            localChanges[index] = original;

            int dir = (int) direction;
            
            int nextIndex = (index + dir);
            // if we are at the end, there is nothing more to scroll
            if (nextIndex > (messageList.Count - 1))
            {
                return null;
            }
            
            return nextIndex < 0 ? localChanges.FirstOrDefault() : entryAt(index = nextIndex);
            
            string entryAt(int i)
            {
                // if we've previously edited the entry then give us that, else give us the original message
                return localChanges[i] ?? messageList[i];
            }
        }

        // Used for Up/Down arrow key action
        private enum Direction
        {
            Up = 1,
            Down = -1,
            Other = 0
        }
    }
}