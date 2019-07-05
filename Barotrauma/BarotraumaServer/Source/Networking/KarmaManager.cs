using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class KarmaManager : ISerializableEntity
    {
        private readonly List<Client> bannedClients = new List<Client>();
        
        public void UpdateClients(IEnumerable<Client> clients, float deltaTime)
        {
            if (!GameMain.Server.GameStarted) { return; }

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

            if (client.Character != null && !client.Character.Removed)
            {
                float herpesStrength = 0.0f;
                if (client.Karma < 20)                
                    herpesStrength = 100.0f;                
                else if (client.Karma < 30)                
                    herpesStrength = 0.6f;                
                else if (client.Karma < 40.0f)
                    herpesStrength = 0.3f;
                
                var existingAffliction = client.Character.CharacterHealth.GetAffliction<AfflictionSpaceHerpes>("spaceherpes");
                if (existingAffliction == null && herpesStrength > 0.0f)
                {
                    client.Character.CharacterHealth.ApplyAffliction(null, new Affliction(herpesAffliction, herpesStrength));
                }
                else
                {
                    existingAffliction.Strength = herpesStrength;
                }
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
            if (attacker == null) { return; }
            //damaging/repairing ruin structures or enemy subs doesn't affect karma
            if (structure.Submarine == null || structure.Submarine.TeamID != attacker.TeamID)
            {
                return;
            }

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

        public void OnReactorMeltdown(Character character)
        {
            AdjustKarma(character, -ReactorMeltdownKarmaDecrease);
        }

        private void AdjustKarma(Character target, float amount)
        {
            if (target == null) { return; }

            Client client = GameMain.Server.ConnectedClients.Find(c => c.Character == target);
            if (client == null) { return; }

            client.Karma += amount;
        }

        public void Save()
        {
            XDocument doc = new XDocument(new XElement(Name));
            SerializableProperty.SerializeProperties(this, doc.Root, true);

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };

            using (var writer = XmlWriter.Create(ConfigFile, settings))
            {
                doc.Save(writer);
            }
        }

    }
}
