using Barotrauma.Networking;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Character : Entity, IDamageable, ISerializableEntity, IClientSerializable, IServerSerializable
    {
        public static Character Controlled = null;

        partial void InitProjSpecific(XDocument doc)
        {
            keys = null;
        }

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
    }
}
