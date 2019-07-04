using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace Barotrauma
{
    class KarmaManager
    {
        public static readonly string ConfigFile = "Data" + Path.DirectorySeparatorChar + "karmasettings.xml";

        [Serialize(0.1f, false)]
        public float KarmaDecay { get; set; }

        [Serialize(50.0f, false)]
        public float KarmaDecayThreshold { get; set; }

        [Serialize(0.15f, false)]
        public float KarmaIncrease { get; set; }

        [Serialize(50.0f, false)]
        public float KarmaIncreaseThreshold { get; set; }

        [Serialize(0.05f, false)]
        public float StructureRepairKarmaIncrease { get; set; }
        [Serialize(0.1f, false)]
        public float StructureDamageKarmaDecrease { get; set; }

        [Serialize(0.05f, false)]
        public float ItemRepairKarmaIncrease { get; set; }

        [Serialize(0.1f, false)]
        public float DamageEnemyKarmaIncrease { get; set; }
        [Serialize(0.25f, false)]
        public float DamageFriendlyKarmaDecrease { get; set; }

        [Serialize(1.0f, false)]
        public float KickBanThreshold { get; set; }

        private readonly List<Client> bannedClients = new List<Client>();

        public KarmaManager()
        {
            XDocument doc = XMLExtensions.TryLoadXml(ConfigFile);
            SerializableProperty.DeserializeProperties(this, doc?.Root);
        }

        public void UpdateClients(IEnumerable<Client> clients, float deltaTime)
        {
            bannedClients.Clear();
            foreach (Client client in clients)
            {
                UpdateClient(client, deltaTime);
            }

            foreach (Client bannedClient in bannedClients)
            {
                GameMain.Server.BanClient(bannedClient, $"KarmaBanned~[banthreshold]={(int)KickBanThreshold}", duration: TimeSpan.FromSeconds(GameMain.Server.ServerSettings.AutoBanTime));
            }
        }

        private void UpdateClient(Client client, float deltaTime)
        {
            if (client.Karma > KarmaDecayThreshold)
            {
                client.Karma -= KarmaDecay * deltaTime;
            }
            else if (client.Karma < KarmaIncreaseThreshold)
            {
                client.Karma += KarmaIncrease * deltaTime;
            }

            if (client.Karma < KickBanThreshold && client.Connection != GameMain.Server.OwnerConnection)
            {
                bannedClients.Add(client);
            }
        }

        public void OnCharacterAttacked(Character target, Character attacker, AttackResult attackResult)
        {
            if (target == null || attacker == null) { return; }

            if (target.AIController is EnemyAIController || target.TeamID != attacker.TeamID)
            {
                AdjustKarma(attacker, attackResult.Damage * DamageEnemyKarmaIncrease);
            }
            else
            {
                AdjustKarma(attacker, -attackResult.Damage * DamageFriendlyKarmaDecrease);
            }
        }

        public void OnStructureHealthChanged(Structure structure, Character attacker, float damageAmount)
        {
            if (damageAmount > 0)
            {
                AdjustKarma(attacker, -damageAmount * StructureDamageKarmaDecrease);
            }
            else
            {
                AdjustKarma(attacker, -damageAmount * StructureRepairKarmaIncrease);
            }
        }

        public void OnItemRepaired(Character character, Repairable repairable, float repairAmount)
        {
            AdjustKarma(character, repairAmount * ItemRepairKarmaIncrease);
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
