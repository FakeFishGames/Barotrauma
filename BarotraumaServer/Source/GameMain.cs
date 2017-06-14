using System;
using System.Diagnostics;
using System.Reflection;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Barotrauma.Networking;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class GameMain
    {
        public static World World;

        public static GameServer Server;

        public static GameSession GameSession;

        public static GameClient Client
        {
            get { return null; }
        }

        public static NetworkMember NetworkMember
        {
            get { return Server as NetworkMember; }
        }

        public static Screen EditMapScreen
        {
            get { return null; }
        }
    }
}
