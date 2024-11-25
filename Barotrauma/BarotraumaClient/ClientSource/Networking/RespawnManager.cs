using System;

namespace Barotrauma.Networking
{
    partial class RespawnManager
    {
        private DateTime lastShuttleLeavingWarningTime;

        public int PendingRespawnCount

        {
            get; private set;
        }

        public int RequiredRespawnCount
        {
            get; private set;
        }

        public bool ForceSpawnInMainSub
        {
            get; private set;
        }

        public DateTime ReturnTime { get; private set; }
        public DateTime RespawnTime { get; private set; }
        public State CurrentState { get; private set; }
        public bool ReturnCountdownStarted { get; private set; }
        public bool RespawnCountdownStarted { get; private set; }

        public static void ShowDeathPromptIfNeeded(float delay = 1.0f)
        {
            if (UseDeathPrompt)
            {
                DeathPrompt.Create(delay);
            }
        }

        partial void UpdateTransportingProjSpecific(TeamSpecificState teamSpecificState, float deltaTime)
        {
            if (GameMain.Client?.Character == null || 
                GameMain.Client.Character.Submarine is not { IsRespawnShuttle: true } ||
                GameMain.Client.Character.TeamID != teamSpecificState.TeamID) 
            {
                return; 
            }
            if (!teamSpecificState.ReturnCountdownStarted) { return; }

            //show a warning when there's 20 seconds until the shuttle leaves
            if ((teamSpecificState.ReturnTime - DateTime.Now).TotalSeconds < 20.0f && 
                (DateTime.Now - lastShuttleLeavingWarningTime).TotalSeconds > 30.0f)
            {
                lastShuttleLeavingWarningTime = DateTime.Now;
                GameMain.Client.AddChatMessage("ServerMessage.ShuttleLeaving", ChatMessageType.Server);
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            var myTeamId = (CharacterTeamType)msg.ReadByte();
            foreach (var teamSpecificState in teamSpecificStates.Values)
            {
                var teamId = (CharacterTeamType)msg.ReadByte();

                bool respawnPromptPending = false;
                var newState = (State)msg.ReadRangedInteger(0, Enum.GetNames(typeof(State)).Length);
                switch (newState)
                {
                    case State.Transporting:
                        teamSpecificState.ReturnCountdownStarted = msg.ReadBoolean();
                        maxTransportTime        = msg.ReadSingle();
                        float transportTimeLeft = msg.ReadSingle();

                        teamSpecificState.ReturnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(transportTimeLeft * 1000.0f));
                        teamSpecificState.RespawnCountdownStarted = false;
                        break;
                    case State.Waiting:
                        teamSpecificState.PendingRespawnCount = msg.ReadUInt16();
                        teamSpecificState.RequiredRespawnCount = msg.ReadUInt16();
                        respawnPromptPending = msg.ReadBoolean();
                        teamSpecificState.RespawnCountdownStarted = msg.ReadBoolean();
                        ResetShuttle(teamSpecificState);
                        float newRespawnTime = msg.ReadSingle();
                        teamSpecificState.RespawnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(newRespawnTime * 1000.0f));
                        break;
                    case State.Returning:
                        teamSpecificState.RespawnCountdownStarted = false;
                        break;
                }
                teamSpecificState.CurrentState = newState;

                if (respawnPromptPending)
                {
                    GameMain.Client.HasSpawned = true;
                    DeathPrompt.Create(delay: 1.0f);
                }

                if (teamId == myTeamId)
                {
                    PendingRespawnCount = teamSpecificState.PendingRespawnCount;
                    RequiredRespawnCount = teamSpecificState.RequiredRespawnCount;
                    ReturnTime = teamSpecificState.ReturnTime;
                    RespawnTime = teamSpecificState.RespawnTime;
                    CurrentState = teamSpecificState.CurrentState;
                    ReturnCountdownStarted = teamSpecificState.ReturnCountdownStarted;
                    RespawnCountdownStarted = teamSpecificState.RespawnCountdownStarted;
                }
            }
            
            msg.ReadPadBits();
        }
    }
}
