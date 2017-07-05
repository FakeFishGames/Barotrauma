using System;

namespace Barotrauma.Networking
{
    class GameClient : NetworkMember
    {
        public GameClient(string newName)
        {
            throw new Exception("Tried to create GameClient in dedicated server build");
        }
    }
}
