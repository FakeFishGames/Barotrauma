// #define DISABLE_MISSIONS
using Barotrauma.Networking;
using Lidgren.Network;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
   partial class TraitorManager
    {
        public Traitor.TraitorMission Mission { get; private set; }
        public string CodeWords => Mission?.CodeWords;
        public string CodeResponse => Mission?.CodeResponse;

        public Dictionary<string, Traitor>.ValueCollection Traitors => Mission?.Traitors.Values;

        private float startCountdown = 0.0f;
        private GameServer server;

        public bool IsTraitor(Character character)
        {
            if (Traitors == null)
            {
                return false;
            }
            return Traitors.Any(traitor => traitor.Character == character);
        }

        public TraitorManager()
        {
        }

        public void Start(GameServer server, int traitorCount)
        {
#if DISABLE_MISSIONS
            return;
#endif
            if (traitorCount < 1) //what why how
            {
                traitorCount = 1;
                DebugConsole.ThrowError("Traitor Manager: TraitorCount somehow ended up less than 1, setting it to 1.");
            }
            if (server == null) return;
            
            Traitor.TraitorMission.InitializeRandom();
            this.server = server;
            startCountdown = 90.0f;
        }

        public void Update(float deltaTime)
        {
#if DISABLE_MISSIONS
            return;
#endif
            if (Mission != null)
            {
                Mission.Update(deltaTime);
                if (Mission.IsCompleted)
                {
                    Mission = null;
                    startCountdown = 60.0f;
                }
            }
            else if (startCountdown > 0.0f && server.GameStarted)
            {
                startCountdown -= deltaTime;
                if (startCountdown <= 0.0f)
                {
                    Mission = TraitorMissionPrefab.RandomPrefab()?.Instantiate();
                    if (Mission != null)
                    {
                        Mission.Start(server, "traitor");
                    }
                }
            }
        }
    
        public string GetEndMessage()
        {
#if DISABLE_MISSIONS
            return "";
#endif
            if (GameMain.Server == null || Mission == null) return "";

            return Mission.GlobalEndMessage;
        }
    }
}
