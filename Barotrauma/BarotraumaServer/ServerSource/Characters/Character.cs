using Barotrauma.Networking;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Character
    {
        public static Character Controlled = null;

        partial void InitProjSpecific(XElement mainElement) { }

        partial void OnAttackedProjSpecific(Character attacker, AttackResult attackResult, float stun)
        {
            GameMain.Server.KarmaManager.OnCharacterHealthChanged(this, attacker, attackResult.Damage, stun, attackResult.Afflictions);
        }

        partial void KillProjSpecific(CauseOfDeathType causeOfDeath, Affliction causeOfDeathAffliction, bool log)
        {
            if (log)
            {
                if (causeOfDeath == CauseOfDeathType.Affliction)
                {
                    GameServer.Log(GameServer.CharacterLogName(this) + " has died (Cause of death: " + causeOfDeathAffliction.Prefab.Name + ")", ServerLog.MessageType.Attack);
                }
                else
                {
                    GameServer.Log(GameServer.CharacterLogName(this) + " has died (Cause of death: " + causeOfDeath + ")", ServerLog.MessageType.Attack);
                }
            }

            healthUpdateTimer = 0.0f;

            if (CauseOfDeath.Killer != null && CauseOfDeath.Killer.IsTraitor && CauseOfDeath.Killer != this)
            {
                var owner = GameMain.Server.ConnectedClients.Find(c => c.Character == this);
                if (owner != null)
                {
                    GameMain.Server.SendDirectChatMessage(TextManager.FormatServerMessage("KilledByTraitorNotification"), owner, ChatMessageType.ServerMessageBoxInGame);
                }
            }
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
