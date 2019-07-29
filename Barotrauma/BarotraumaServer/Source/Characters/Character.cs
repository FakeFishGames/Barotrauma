using Barotrauma.Networking;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Character
    {
        public static Character Controlled = null;

        partial void InitProjSpecific(XElement mainElement) { }

        partial void AdjustKarma(Character attacker, AttackResult attackResult)
        {
            if (attacker == null) return;

            Client attackerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == attacker);
            if (attackerClient == null) return;

            Client targetClient = GameMain.Server.ConnectedClients.Find(c => c.Character == this);
            if (targetClient != null)
            {
                if (attacker.TeamID == TeamID)
                {
                    attackerClient.Karma -= attackResult.Damage * 0.01f;
                    if (CharacterHealth.MaxVitality <= CharacterHealth.MinVitality) attackerClient.Karma = 0.0f;
                }
            }
        }

        partial void KillProjSpecific(CauseOfDeathType causeOfDeath, Affliction causeOfDeathAffliction)
        {
            if (causeOfDeath == CauseOfDeathType.Affliction)
            {
                GameServer.Log(LogName + " has died (Cause of death: " + causeOfDeathAffliction.Prefab.Name + ")", ServerLog.MessageType.Attack);
            }
            else
            {
                GameServer.Log(LogName + " has died (Cause of death: " + causeOfDeath + ")", ServerLog.MessageType.Attack);
            }

            healthUpdateTimer = 0.0f;

            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                if (client.InGame)
                {
                    client.PendingPositionUpdates.Enqueue(this);
                }
            }
        }
    }
}
