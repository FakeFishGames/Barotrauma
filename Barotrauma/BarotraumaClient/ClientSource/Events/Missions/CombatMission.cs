using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class CombatMission : Mission
    {
        private readonly Dictionary<byte, int> clientKills = new Dictionary<byte, int>();
        private readonly Dictionary<byte, int> clientDeaths = new Dictionary<byte, int>();

        private readonly Dictionary<ushort, int> botKills = new Dictionary<ushort, int>();
        private readonly Dictionary<ushort, int> botDeaths = new Dictionary<ushort, int>();

        public override LocalizedString Description
        {
            get
            {
                if (descriptions == null) { return ""; }

                if (GameMain.Client?.Character == null)
                {
                    //non-team-specific description
                    return descriptions[0];
                }

                //team specific
                return descriptions[GameMain.Client.Character.TeamID == CharacterTeamType.Team1 ? 1 : 2];
            }
        }

        public override bool DisplayAsCompleted => false;
        public override bool DisplayAsFailed => false;

        public override IEnumerable<(LocalizedString Label, Vector2 Position)> SonarLabels
        {
            get
            {
                if (targetSubmarine == null)
                {
                    yield break;
                }
                else
                {
                    yield return (targetSubmarineSonarLabel is { Loaded: true } ? targetSubmarineSonarLabel : targetSubmarine.Info.DisplayName, targetSubmarine.WorldPosition);
                }
            }
        }

        public static Color GetTeamColor(CharacterTeamType teamID)
        {
            if (teamID == CharacterTeamType.Team1)
            {
                return GUIStyle.GetComponentStyle("CoalitionIcon")?.Color ?? GUIStyle.Blue;
            }
            else if (teamID == CharacterTeamType.Team2)
            {
                return GUIStyle.GetComponentStyle("SeparatistIcon")?.Color ?? GUIStyle.Orange;
            }
            return Color.White;
        }

        public int GetClientKillCount(Client client)
        {
            if (clientKills.TryGetValue(client.SessionId, out int kills))
            {
                return kills;
            }
            return 0;
        }

        public int GetClientDeathCount(Client client)
        {
            if (clientDeaths.TryGetValue(client.SessionId, out int deaths))
            {
                return deaths;
            }
            return 0;
        }

        public int GetBotKillCount(CharacterInfo botInfo)
        {
            if (botKills.TryGetValue(botInfo.ID, out int kills))
            {
                return kills;
            }
            return 0;
        }

        public int GetBotDeathCount(CharacterInfo botInfo)
        {
            if (botDeaths.TryGetValue(botInfo.ID, out int deaths))
            {
                return deaths;
            }
            return 0;
        }

        public override void ClientRead(IReadMessage msg)
        {
            base.ClientRead(msg);
            Scores[0] = msg.ReadUInt16();
            Scores[1] = msg.ReadUInt16();

            uint clientCount = msg.ReadVariableUInt32();
            for (int i = 0; i < clientCount; i++)
            {
                byte clientId = msg.ReadByte();
                clientDeaths[clientId] = (int)msg.ReadVariableUInt32();
                clientKills[clientId] = (int)msg.ReadVariableUInt32();
            }

            uint botCount = msg.ReadVariableUInt32();
            for (int i = 0; i < botCount; i++)
            {
                ushort botId = msg.ReadUInt16();
                botDeaths[botId] = (int)msg.ReadVariableUInt32();
                botKills[botId] = (int)msg.ReadVariableUInt32();
            }

        }
    }
}
