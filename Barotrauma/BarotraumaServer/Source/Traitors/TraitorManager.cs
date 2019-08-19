// #define DISABLE_MISSIONS

using System;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
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
        
        private readonly Dictionary<ulong, int> traitorCountsBySteamId = new Dictionary<ulong, int>();
        private readonly Dictionary<string, int> traitorCountsByEndPoint = new Dictionary<string, int>();

        public int GetTraitorCount(Tuple<ulong, string> steamIdAndEndPoint)
        {
            if (steamIdAndEndPoint.Item1 > 0 && traitorCountsBySteamId.TryGetValue(steamIdAndEndPoint.Item1, out var steamIdResult))
            {
                return steamIdResult;
            }
            return traitorCountsByEndPoint.TryGetValue(steamIdAndEndPoint.Item2, out var endPointResult) ? endPointResult : 0;
        }

        public void SetTraitorCount(Tuple<ulong, string> steamIdAndEndPoint, int count)
        {
            if (steamIdAndEndPoint.Item1 > 0)
            {
                traitorCountsBySteamId[steamIdAndEndPoint.Item1] = count;
            }
            traitorCountsByEndPoint[steamIdAndEndPoint.Item2] = count;
        }

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
            //TODO: configure countdowns in xml
            startCountdown = MathHelper.Lerp(90.0f, 180.0f, (float)Traitor.TraitorMission.RandomDouble());
            traitorCountsBySteamId.Clear();
            traitorCountsByEndPoint.Clear();
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
                    foreach (var traitor in Mission.Traitors.Values)
                    {
                        traitor.UpdateCurrentObjective("");
                    }
                    Mission = null;
                    //TODO: configure countdowns in xml
                    startCountdown = MathHelper.Lerp(90.0f, 180.0f, (float)Traitor.TraitorMission.RandomDouble());
                }
            }
            else if (startCountdown > 0.0f && server.GameStarted)
            {
                startCountdown -= deltaTime;
                if (startCountdown <= 0.0f)
                {
                    Mission = TraitorMissionPrefab.RandomPrefab()?.Instantiate();
                    if (Mission == null || !Mission.Start(server, this, "traitor"))
                    {
                        Mission = null;
                        startCountdown = 60.0f;
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
