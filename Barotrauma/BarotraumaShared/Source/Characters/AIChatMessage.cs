using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    class AIChatMessage
    {
        public readonly string Message;

        /// <summary>
        /// An arbitrary identifier that can be used to determine what kind of a message this is 
        /// and prevent characters from saying the same kind of line too often.
        /// </summary>
        public readonly string Identifier;

        public ChatMessageType? MessageType;

        public float SendDelay;
        public double SendTime;

        public AIChatMessage(string message, ChatMessageType? type, string identifier = "", float delay = 0.0f)
        {
            Message = message;
            MessageType = type;
            Identifier = identifier;
            SendDelay = delay;
        }
    }
}
