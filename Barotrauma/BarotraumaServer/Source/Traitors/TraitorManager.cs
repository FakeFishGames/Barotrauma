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

        public Dictionary<string, Traitor>.ValueCollection Traitors => Mission.Traitors.Values;

        public bool IsTraitor(Character character)
        {
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
            Mission = TraitorMissionPrefab.RandomPrefab()?.Instantiate();

            // TODO(xxx): Make sure we don't mess up the level seed
            Rand.SetSyncedSeed((int)System.DateTime.UtcNow.Ticks);
            if (Mission != null)
            {
                Mission.Start(server, "traitor");
            }
        }

        public void Update(float deltaTime)
        {
#if DISABLE_MISSIONS
            return;
#endif
            if (Mission != null)
            {
                Mission.Update(deltaTime);
            }
        }
    
        public string GetEndMessage()
        {
#if DISABLE_MISSIONS
            return "";
#endif
            if (GameMain.Server == null || Mission == null) return "";

            return Mission.EndMessage;
        }
    }
}
