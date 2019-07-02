using Barotrauma.Networking;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Character
    {
        public static Character Controlled = null;

        partial void InitProjSpecific(XDocument doc)
        {
        }

        partial void OnAttackedProjSpecific(Character attacker, AttackResult attackResult)
        {
            GameMain.Server.KarmaManager.OnCharacterAttacked(this, attacker, attackResult);
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
