using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using NLog;

namespace Barotrauma.Networking
{
    public partial class ServerLog
    {
        private readonly static Logger Log = LogManager.GetCurrentClassLogger();
        public readonly static string InternalLoggerName = Log.Name;

        public static void WriteLine(string line, MessageType messageType)
        {
            Log.Info("{messageType:l}: {message:l}", messageType.ToString(), line);

            var newText = new LogMessage(line, messageType);

            DebugConsole.NewMessage(newText.SanitizedText, messageColor[messageType], logToNLog: false);
        }
    }
}
