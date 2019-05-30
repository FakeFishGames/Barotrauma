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

        partial void UpdateTransportingProjSpecific(float deltaTime)
        {
            if (shuttleTransportTimer + deltaTime > 15.0f && shuttleTransportTimer <= 15.0f &&
                GameMain.Client?.Character != null &&
                GameMain.Client.Character.Submarine == respawnShuttle)
            {
                GameMain.Client.AddChatMessage("ServerMessage.ShuttleLeaving", ChatMessageType.Server);
            }
        }
    }
}
