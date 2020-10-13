using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;

namespace Barotrauma.Networking
{
    public partial class ServerLog
    {
        private struct LogMessage
        {
            public readonly string Text;
            public readonly string SanitizedText;
            public readonly MessageType Type;
            public readonly List<RichTextData> RichData;

            public LogMessage(string text, MessageType type)
            {
                if (type.HasFlag(MessageType.Chat))
                {
                    Text = $"[{DateTime.Now}]\n  {text}";
                }
                else
                {
                    Text = $"[{DateTime.Now}]\n  {TextManager.GetServerMessage(text)}";
                }
                RichData = RichTextData.GetRichTextData(Text, out SanitizedText);

                Type = type;
            }
        }

        public enum MessageType
        {
            Chat,
            ItemInteraction,
            Inventory,
            Attack,
            Spawning,
            Wiring,
            ServerMessage,
            ConsoleUsage,
            Karma,
            Error,
        }

        private static readonly Dictionary<MessageType, Color> messageColor = new Dictionary<MessageType, Color>
        {
            { MessageType.Chat, Color.LightBlue },
            { MessageType.ItemInteraction, new Color(205, 205, 180) },
            { MessageType.Inventory, new Color(255, 234, 85) },
            { MessageType.Attack, new Color(204, 74, 78) },
            { MessageType.Spawning, new Color(163, 73, 164) },
            { MessageType.Wiring, new Color(255, 157, 85) },
            { MessageType.ServerMessage, new Color(157, 225, 160) },
            { MessageType.ConsoleUsage, new Color(0, 162, 232) },
            { MessageType.Karma, new Color(75, 88, 255) },
            { MessageType.Error, Color.Red },
        };

        private static readonly Dictionary<MessageType, string> messageTypeName = new Dictionary<MessageType, string>
        {
            { MessageType.Chat, "ChatMessage" },
            { MessageType.ItemInteraction, "ItemInteraction" },
            { MessageType.Inventory, "InventoryUsage" },
            { MessageType.Attack, "AttackDeath" },
            { MessageType.Spawning, "Spawning" },
            { MessageType.Wiring, "Wiring" },
            { MessageType.ServerMessage, "ServerMessage" },
            { MessageType.ConsoleUsage, "ConsoleUsage" },
            { MessageType.Karma, "Karma" },
            { MessageType.Error, "Error" }
        };

        public ServerLog()
        {
            foreach (MessageType messageType in Enum.GetValues(typeof(MessageType)))
            {
                System.Diagnostics.Debug.Assert(messageColor.ContainsKey(messageType));
                System.Diagnostics.Debug.Assert(messageTypeName.ContainsKey(messageType));
            }
        }
    }
}
