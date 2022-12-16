using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Structure : MapEntity, IDamageable, IServerSerializable, ISerializableEntity
    {
        partial void OnHealthChangedProjSpecific(Character attacker, float damageAmount)
        {
            GameMain.Server.KarmaManager.OnStructureHealthChanged(this, attacker, damageAmount);
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteByte((byte)Sections.Length);
            for (int i = 0; i < Sections.Length; i++)
            {
                msg.WriteRangedSingle(Sections[i].damage / MaxHealth, 0.0f, 1.0f, 8);
            }
        }
    }
}
