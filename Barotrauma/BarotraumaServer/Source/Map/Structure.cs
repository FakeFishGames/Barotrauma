using Barotrauma.Networking;
using Lidgren.Network;

namespace Barotrauma
{
    partial class Structure : MapEntity, IDamageable, IServerSerializable, ISerializableEntity
    {
        partial void OnHealthChangedProjSpecific(Character attacker, float damageAmount)
        {
            GameMain.Server.KarmaManager.OnStructureHealthChanged(this, attacker, damageAmount);
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write((byte)Sections.Length);
            for (int i = 0; i < Sections.Length; i++)
            {
                msg.WriteRangedSingle(Sections[i].damage / Health, 0.0f, 1.0f, 8);
            }
        }
    }
}
