using System;

namespace Barotrauma.Networking
{
    partial class RespawnManager
    {
        partial void UpdateWaiting(float deltaTime)
        {
            if (CountdownStarted)
            {
                respawnTimer = Math.Max(0.0f, respawnTimer - deltaTime);
            }
        }
    }
}
