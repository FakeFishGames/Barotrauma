using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class KarmaManager : ISerializableEntity
    {
        private class ClientMemory
        {
            public List<Pair<Wire, float>> WireDisconnectTime = new List<Pair<Wire, float>>();

            //the client's karma value when they were last sent a notification about it (e.g. "your karma is very low")
            public float PreviousNotifiedKarma;
        }

        private readonly Dictionary<Client, ClientMemory> clientMemories = new Dictionary<Client, ClientMemory>();
        private readonly List<Client> bannedClients = new List<Client>();

        private double KarmaNotificationTime;

        public void UpdateClients(IEnumerable<Client> clients, float deltaTime)
        {
            if (!GameMain.Server.GameStarted) { return; }

            bannedClients.Clear();
            foreach (Client client in clients)
            {
                if (!clientMemories.ContainsKey(client))
                {
                    clientMemories[client] = new ClientMemory()
                    {
                        PreviousNotifiedKarma = client.Karma
                    };
                }
                UpdateClient(client, deltaTime);
            }

            if (Timing.TotalTime > KarmaNotificationTime)
            {
                SendKarmaNotifications(clients);
                KarmaNotificationTime = Timing.TotalTime + KarmaNotificationInterval;
            }

            foreach (Client bannedClient in bannedClients)
            {
                GameMain.Server.BanClient(bannedClient, $"KarmaBanned~[banthreshold]={(int)KickBanThreshold}", duration: TimeSpan.FromSeconds(GameMain.Server.ServerSettings.AutoBanTime));
            }
        }

        private void SendKarmaNotifications(IEnumerable<Client> clients)
        {
            foreach (Client client in clients)
            {
                float karmaChange = client.Karma - clientMemories[client].PreviousNotifiedKarma;
                if (karmaChange > KarmaNotificationInterval)
                {
                    if (Math.Abs(KickBanThreshold - client.Karma) < KarmaNotificationInterval)
                    {
                        GameMain.Server.SendDirectChatMessage(TextManager.Get("KarmaBanWarning"), client);

                    }
                    else
                    {
#if DEBUG
                        GameMain.Server.SendDirectChatMessage(
                            (karmaChange < 0 ? $"You karma has decreased to {client.Karma}" : $"You karma has increased to {client.Karma}") + " (this message should not appear in release builds)", client);

#else
                        GameMain.Server.SendDirectChatMessage(TextManager.Get(karmaChange < 0 ? "KarmaDecreasedUnknownAmount" : "KarmaIncreasedUnknownAmount"), client);
#endif
                    }
                    clientMemories[client].PreviousNotifiedKarma = client.Karma;
                }
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
                //increase the strength of the herpes affliction in steps instead of linearly
                //otherwise clients could determine their exact karma value from the strength
                float herpesStrength = 0.0f;
                if (client.Karma < 20)                
                    herpesStrength = 100.0f;                
                else if (client.Karma < 30)                
                    herpesStrength = 60.0f;                
                else if (client.Karma < 40.0f)
                    herpesStrength = 30.0f;
                
                var existingAffliction = client.Character.CharacterHealth.GetAffliction<AfflictionSpaceHerpes>("spaceherpes");
                if (existingAffliction == null && herpesStrength > 0.0f)
                {
                    client.Character.CharacterHealth.ApplyAffliction(null, new Affliction(herpesAffliction, herpesStrength));
                }
                else if (existingAffliction != null)
                {
                    existingAffliction.Strength = herpesStrength;
                }

                //check if the client has disconnected an excessive number of wires
                if (clientMemories.ContainsKey(client))
                {
                    var clientMemory = clientMemories[client];
                    if (clientMemory.WireDisconnectTime.Count > (int)AllowedWireDisconnectionsPerMinute)
                    {
                        clientMemory.WireDisconnectTime.RemoveRange(0, clientMemory.WireDisconnectTime.Count - (int)AllowedWireDisconnectionsPerMinute);
                        if (clientMemory.WireDisconnectTime.All(w => Timing.TotalTime - w.Second < 60.0f))
                        {
                            float karmaDecrease = -WireDisconnectionKarmaDecrease;
                            //engineers don't lose as much karma for removing lots of wires
                            if (client.Character.Info?.Job.Prefab.Identifier == "engineer") { karmaDecrease *= 0.5f; }
                            AdjustKarma(client.Character, karmaDecrease);
                            clientMemory.WireDisconnectTime.Clear();
                        }
                    }
                }

                if (client.Character?.Info?.Job.Prefab.Identifier == "captain" && client.Character.SelectedConstruction != null)
                {
                    if (client.Character.SelectedConstruction.GetComponent<Steering>() != null)
                    {
                        client.Karma += SteerSubKarmaIncrease * deltaTime;
                    }
                }
            }

            if (client.Karma < KickBanThreshold && client.Connection != GameMain.Server.OwnerConnection)
            {
                bannedClients.Add(client);
            }
        }

        public void OnClientDisconnected(Client client)
        {
            clientMemories.Remove(client);
        }

        public void OnCharacterHealthChanged(Character target, Character attacker, float damage)
        {
            if (target == null || attacker == null) { return; }

            bool isEnemy = target.AIController is EnemyAIController || target.TeamID != attacker.TeamID;
            if (GameMain.Server.TraitorManager != null)
            {
                if (GameMain.Server.TraitorManager.TraitorList.Any(t => t.Character == target))
                {
                    //traitors always count as enemies
                    isEnemy = true;
                }
                if (GameMain.Server.TraitorManager.TraitorList.Any(t => t.Character == attacker && t.TargetCharacter == target))
                {
                    //target counts as an enemy to the traitor
                    isEnemy = true;
                }
            }

            if (target.AIController is EnemyAIController || target.TeamID != attacker.TeamID)
            {
                if (damage > 0) { AdjustKarma(attacker, damage * DamageEnemyKarmaIncrease); }
            }
            else
            {
                if (damage > 0)
                {
                    AdjustKarma(attacker, -damage * DamageFriendlyKarmaDecrease);
                }
                else
                {
                    AdjustKarma(attacker, -damage * HealFriendlyKarmaIncrease);
                }
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
                float karmaIncrease = -damageAmount * StructureRepairKarmaIncrease;
                //mechanics get twice as much karma for repairing walls
                if (attacker.Info?.Job.Prefab.Identifier == "mechanic") { karmaIncrease *= 2.0f; }
                AdjustKarma(attacker, karmaIncrease);
            }
        }

        public void OnItemRepaired(Character character, Repairable repairable, float repairAmount)
        {
            float karmaIncrease = repairAmount * ItemRepairKarmaIncrease;
            if (repairable.HasRequiredSkills(character)) { karmaIncrease *= 2.0f; }
            AdjustKarma(character, karmaIncrease);
        }

        public void OnReactorOverHeating(Character character, float deltaTime)
        {
            AdjustKarma(character, -ReactorOverheatKarmaDecrease * deltaTime);
        }

        public void OnReactorMeltdown(Character character)
        {
            AdjustKarma(character, -ReactorMeltdownKarmaDecrease);
        }

        public void OnExtinguishingFire(Character character, float deltaTime)
        {
            AdjustKarma(character, ExtinguishFireKarmaIncrease * deltaTime);
        }

        public void OnWireDisconnected(Character character, Wire wire)
        {
            if (character == null || wire == null) { return; }
            Client client = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
            if (client == null) { return; }

            if (!clientMemories.ContainsKey(client)) { clientMemories[client] = new ClientMemory(); }

            clientMemories[client].WireDisconnectTime.RemoveAll(w => w.First == wire);
            clientMemories[client].WireDisconnectTime.Add(new Pair<Wire, float>(wire, (float)Timing.TotalTime));
        }

        public void OnSpamFilterTriggered(Client client)
        {
            if (client != null)
            {
                client.Karma -= SpamFilterKarmaDecrease;
            }
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

            foreach (KeyValuePair<string, XElement> preset in Presets)
            {
                if (preset.Key.ToLowerInvariant() == "custom") { continue; }
                doc.Root.Add(preset.Value);
            }

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
