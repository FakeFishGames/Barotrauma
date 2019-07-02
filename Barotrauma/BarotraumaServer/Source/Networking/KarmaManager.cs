using Barotrauma.Items.Components;
using Barotrauma.Networking;

namespace Barotrauma
{
    class KarmaManager
    {
        public void OnCharacterAttacked(Character target, Character attacker, AttackResult attackResult)
        {
        }

        public void OnStructureHealthChanged(Structure structure, Character attacker, float damageAmount)
        {
        }

        public void OnItemRepaired(Character character, Repairable repairable)
        {
        }

        private void AdjustKarma(Character target, float amount)
        {
            if (target == null) { return; }

            Client client = GameMain.Server.ConnectedClients.Find(c => c.Character == target);
            if (client == null) { return; }

            client.Karma += amount;
        }
    }
}
