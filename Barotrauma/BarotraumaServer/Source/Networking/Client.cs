using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    partial class Client
    {
        private float karma = 1.0f;
        public float Karma
        {
            get
            {
                if (GameMain.Server == null) return 1.0f;
                if (!GameMain.Server.KarmaEnabled) return 1.0f;
                return karma;
            }
            set
            {
                if (GameMain.Server == null) return;
                if (!GameMain.Server.KarmaEnabled) return;
                karma = Math.Min(Math.Max(value, 0.0f), 1.0f);
            }
        }

        public static bool IsValidName(string name, GameServer server)
        {
            char[] disallowedChars = new char[] { ';', ',', '<', '>', '/', '\\', '[', ']', '"', '?' };
            if (name.Any(c => disallowedChars.Contains(c))) return false;

            foreach (char character in name)
            {
                if (!server.AllowedClientNameChars.Any(charRange => (int)character >= charRange.First && (int)character <= charRange.Second)) return false;
            }

            return true;
        }
    }
}
