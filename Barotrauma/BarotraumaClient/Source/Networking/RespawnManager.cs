using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma.Networking
{
    partial class RespawnManager
    {
        private DateTime lastShuttleLeavingWarningTime;

        partial void UpdateTransportingProjSpecific(float deltaTime)
        {
            if ((TransportTime - DateTime.Now).TotalSeconds < 20.0f && 
                (DateTime.Now - lastShuttleLeavingWarningTime).TotalSeconds > 30.0f &&
                GameMain.Client?.Character != null &&
                GameMain.Client.Character.Submarine == RespawnShuttle)
            {
                lastShuttleLeavingWarningTime = DateTime.Now;
                GameMain.Client.AddChatMessage("ServerMessage.ShuttleLeaving", ChatMessageType.Server);
            }
        }
        
        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            var newState = (State)msg.ReadRangedInteger(0, Enum.GetNames(typeof(State)).Length);

            switch (newState)
            {
                case State.Transporting:
                    maxTransportTime = msg.ReadSingle();
                    float transportTimeLeft = msg.ReadSingle();
                    TransportTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(transportTimeLeft * 1000.0f));
                    CountdownStarted = false;

                    if (CurrentState != newState)
                    {
                        CoroutineManager.StopCoroutines("forcepos");
                        //CoroutineManager.StartCoroutine(ForceShuttleToPos(Level.Loaded.StartPosition - Vector2.UnitY * Level.ShaftHeight, 100.0f), "forcepos");
                    }
                    break;
                case State.Waiting:
                    CountdownStarted = msg.ReadBoolean();
                    ResetShuttle();
                    float newRespawnTime = msg.ReadSingle();
                    RespawnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(newRespawnTime * 1000.0f));
                    break;
                case State.Returning:
                    CountdownStarted = false;
                    break;
            }
            CurrentState = newState;

            msg.ReadPadBits();
        }
    }
}
