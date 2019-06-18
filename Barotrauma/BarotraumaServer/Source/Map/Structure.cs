using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Structure : MapEntity, IDamageable, IServerSerializable, ISerializableEntity
    {
        partial void AdjustKarma(IDamageable attacker, float amount)
        {
            if (GameMain.Server != null)
            {
                if (Submarine == null) return;
                if (attacker == null) return;
                if (attacker is Character attackerCharacter)
                {
                    Client attackerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == attackerCharacter);
                    if (attackerClient != null)
                    {
                        if (attackerCharacter.TeamID == Submarine.TeamID)
                        {
                            attackerClient.Karma -= amount * 0.001f;
                        }
                    }
                }
            }
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            for (int i = 0; i < Sections.Length; i++)
            {
                msg.WriteRangedSingle(Sections[i].damage / Health, 0.0f, 1.0f, 8);
            }
        }
    }
}
