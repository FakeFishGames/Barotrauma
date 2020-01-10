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
            if (GameMain.Client?.Character == null || GameMain.Client.Character.Submarine != RespawnShuttle) { return; }
            if (!ReturnCountdownStarted) { return; }

            //show a warning when there's 20 seconds until the shuttle leaves
            if ((ReturnTime - DateTime.Now).TotalSeconds < 20.0f && 
                (DateTime.Now - lastShuttleLeavingWarningTime).TotalSeconds > 30.0f)
            {
                lastShuttleLeavingWarningTime = DateTime.Now;
                GameMain.Client.AddChatMessage("ServerMessage.ShuttleLeaving", ChatMessageType.Server);
            }
        }
        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            var newState = (State)msg.ReadRangedInteger(0, Enum.GetNames(typeof(State)).Length);

            switch (newState)
            {
                case State.Transporting:
                    ReturnCountdownStarted  = msg.ReadBoolean();
                    maxTransportTime        = msg.ReadSingle();
                    float transportTimeLeft = msg.ReadSingle();

                    ReturnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(transportTimeLeft * 1000.0f));
                    RespawnCountdownStarted = false;
                    if (CurrentState != newState)
                    {
                        CoroutineManager.StopCoroutines("forcepos");
                        //CoroutineManager.StartCoroutine(ForceShuttleToPos(Level.Loaded.StartPosition - Vector2.UnitY * Level.ShaftHeight, 100.0f), "forcepos");
                    }
                    break;
                case State.Waiting:
                    RespawnCountdownStarted = msg.ReadBoolean();
                    ResetShuttle();
                    float newRespawnTime = msg.ReadSingle();
                    RespawnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(newRespawnTime * 1000.0f));
                    break;
                case State.Returning:
                    RespawnCountdownStarted = false;
                    break;
            }
            CurrentState = newState;

            msg.ReadPadBits();
        }
    }
}
