using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    partial class KarmaManager : ISerializableEntity
    {
        private class ClientMemory
        {
            public List<Pair<Wire, float>> WireDisconnectTime = new List<Pair<Wire, float>>();

            public struct TimeAmount
            {
                public double Time;
                public float Amount;
            }

            public List<TimeAmount> KarmaDecreasesInPastMinute = new List<TimeAmount>();

            public float PreviousNotifiedKarma;

            public double PreviousKarmaNotificationTime;

            public float StructureDamageAccumulator;
            
            private float structureDamagePerSecond;
            public float StructureDamagePerSecond
            {
                get { return Math.Max(StructureDamageAccumulator, structureDamagePerSecond); }
                set { structureDamagePerSecond = value; }
            }

            public List<TimeAmount> StunsInPastMinute = new List<TimeAmount>();
            public float StunKarmaDecreaseMultiplier;

            //when did a given character last attack this one
            public Dictionary<Character, double> LastAttackTime
            {
                get;
                private set;
            } = new Dictionary<Character, double>();
        }

        public bool TestMode = false;

        private readonly Dictionary<Client, ClientMemory> clientMemories = new Dictionary<Client, ClientMemory>();
        private readonly List<Client> bannedClients = new List<Client>();

        private DateTime perSecondUpdate;
        
        public void UpdateClients(IEnumerable<Client> clients, float deltaTime)
        {
            if (!GameMain.Server.GameStarted) { return; }

            bannedClients.Clear();
            foreach (Client client in clients)
            {
                UpdateClient(client, deltaTime);

                if (perSecondUpdate < DateTime.Now)
                {
                    var clientMemory = GetClientMemory(client);
                    clientMemory.StructureDamagePerSecond = clientMemory.StructureDamageAccumulator;
                    clientMemory.StructureDamageAccumulator = 0.0f;

                    clientMemory.StunsInPastMinute.RemoveAll(s => s.Time + 60.0f < Timing.TotalTime);

                    if (!clientMemory.StunsInPastMinute.Any())
                    {
                        clientMemory.StunKarmaDecreaseMultiplier = 1.0f;
                    }

                    var toRemove = clientMemory.LastAttackTime.Where(pair => pair.Value < Timing.TotalTime - AllowedRetaliationTime).Select(pair => pair.Key).ToList();
                    foreach (var lastAttacker in toRemove)
                    {
                        clientMemory.LastAttackTime.Remove(lastAttacker);
                    }
                }
            }
            if (perSecondUpdate < DateTime.Now)
            {
                foreach (Client client in clients)
                {
                    SendKarmaNotifications(client);
                }
                perSecondUpdate = DateTime.Now + new TimeSpan(0, 0, 1);
            }
            
            foreach (Client bannedClient in bannedClients)
            {
                bannedClient.KarmaKickCount++;
                if (bannedClient.KarmaKickCount <= KicksBeforeBan)
                {
                    GameMain.Server.KickClient(bannedClient, $"KarmaKicked~[banthreshold]={(int)KickBanThreshold}", resetKarma: true);            
                }
                else
                {
                    GameMain.Server.BanClient(bannedClient, $"KarmaBanned~[banthreshold]={(int)KickBanThreshold}", duration: TimeSpan.FromSeconds(GameMain.Server.ServerSettings.AutoBanTime));
                }
            }
        }

        private void SendKarmaNotifications(Client client, string debugKarmaChangeReason = "")
        {
            //send a notification about karma changing if the karma has changed by x%

            var clientMemory = GetClientMemory(client);
            float karmaChange = client.Karma - clientMemory.PreviousNotifiedKarma;
            if (Math.Abs(karmaChange) > 1.0f && TestMode)
            {
                string msg =
                    karmaChange < 0 ? $"Your karma has decreased to {client.Karma}" : $"Your karma has increased to {client.Karma}";
                if (!string.IsNullOrEmpty(debugKarmaChangeReason))
                {
                    msg += $". Reason: {debugKarmaChangeReason}";
                }
                GameMain.Server.SendDirectChatMessage(msg, client);
                clientMemory.PreviousNotifiedKarma = client.Karma;
                clientMemory.PreviousKarmaNotificationTime = Timing.TotalTime;
            }
            else if (Timing.TotalTime >= clientMemory.PreviousKarmaNotificationTime + 5.0f &&
                     clientMemory.PreviousNotifiedKarma >= KickBanThreshold + KarmaNotificationInterval &&
                     client.Karma < KickBanThreshold + KarmaNotificationInterval)
            {
                GameMain.Server.SendDirectChatMessage(TextManager.Get("KarmaBanWarning"), client);
                GameServer.Log(GameServer.ClientLogName(client) + " has been warned for having dangerously low karma.", ServerLog.MessageType.Karma);
                clientMemory.PreviousNotifiedKarma = client.Karma;
                clientMemory.PreviousKarmaNotificationTime = Timing.TotalTime;
            }
        }

        private void UpdateClient(Client client, float deltaTime)
        {
            if (client.Character != null && !client.Character.Removed && !client.Character.IsDead)
            {
                if (client.Karma > KarmaDecayThreshold)
                {
                    client.Karma -= KarmaDecay * deltaTime;
                }
                else if (client.Karma < KarmaIncreaseThreshold)
                {
                    client.Karma += KarmaIncrease * deltaTime;
                }

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
                    GameServer.Log($"{GameServer.ClientLogName(client)} has contracted space herpes due to low karma.", ServerLog.MessageType.Karma);
                    GameMain.NetworkMember.LastClientListUpdateID++;
                }
                else if (existingAffliction != null)
                {
                    existingAffliction.Strength = herpesStrength;
                    if (herpesStrength <= 0.0f)
                    {
                        client.Character.CharacterHealth.ReduceAffliction(null, "invertcontrols", 100.0f);
                    }
                }

                //check if the client has disconnected an excessive number of wires
                var clientMemory = GetClientMemory(client);
                if (clientMemory.WireDisconnectTime.Count > (int)AllowedWireDisconnectionsPerMinute)
                {
                    clientMemory.WireDisconnectTime.RemoveRange(0, clientMemory.WireDisconnectTime.Count - (int)AllowedWireDisconnectionsPerMinute);
                    if (clientMemory.WireDisconnectTime.All(w => Timing.TotalTime - w.Second < 60.0f))
                    {
                        float karmaDecrease = -WireDisconnectionKarmaDecrease;
                        //engineers don't lose as much karma for removing lots of wires
                        if (client.Character.Info?.Job.Prefab.Identifier == "engineer") { karmaDecrease *= 0.5f; }
                        AdjustKarma(client.Character, karmaDecrease, "Disconnected excessive number of wires");
                    }
                }                
                
                if (client.Character?.Info?.Job.Prefab.Identifier == "captain" && client.Character.SelectedConstruction != null)
                {
                    if (client.Character.SelectedConstruction.GetComponent<Steering>() != null)
                    {
                        AdjustKarma(client.Character, SteerSubKarmaIncrease * deltaTime, "Steering the sub");
                    }
                }
            }

            if (client.Karma < KickBanThreshold && client.Connection != GameMain.Server.OwnerConnection)
            {
                if (TestMode)
                {
                    client.Karma = 50.0f;
                    GameMain.Server.SendDirectChatMessage("BANNED! (not really because karma test mode is enabled)", client);
                }
                else
                {
                    bannedClients.Add(client);
                }
            }
        }

        public void OnRoundEnded()
        {
            if (ResetKarmaBetweenRounds)
            {
                clientMemories.Clear();
                foreach (Client client in GameMain.Server.ConnectedClients)
                {
                    client.Karma = Math.Max(50.0f, client.Karma);
                }
            }
        }

        public void OnClientDisconnected(Client client)
        {
            clientMemories.Remove(client);
        }

        // ReSharper disable once UseNegatedPatternMatching, LoopCanBeConvertedToQuery
        public void OnItemTakenFromPlayer(CharacterInventory inventory, Client yoinker, Item item)
        {
            Client targetClient = GameMain.Server.ConnectedClients.Find(c => c.Character == inventory.Owner);
            
            Character yoinkerCharacter = yoinker?.Character;
            Character targetCharacter = inventory.Owner as Character;

            if (yoinker == null || item == null || yoinkerCharacter == null || targetCharacter == null || yoinkerCharacter == targetCharacter) { return; }
            
            if (targetClient == null && (!DangerousItemStealBots || targetCharacter.AIController == null)) { return; }

            // Only if the target is alive and they are stunned, unconscious or handcuffed
            if (targetCharacter.IsDead || targetCharacter.Removed || !(targetCharacter.Stun > 0) && !targetCharacter.IsUnconscious && !targetCharacter.LockHands) { return; }
            
            if (GameMain.Server.TraitorManager?.Traitors != null)
            {
                if (GameMain.Server.TraitorManager.Traitors.Any(t => t.Character == targetCharacter ||  t.Character == yoinkerCharacter))
                {
                    // Don't penalize traitors
                    return;
                }
            }
            
            var foundItem = Inventory.FindItemRecursive(item, it => it.Prefab.Identifier == "idcard" || it.GetComponent<RangedWeapon>() != null || it.GetComponent<MeleeWeapon>() != null);

            if (foundItem == null) { return; }

            bool isIdCard = foundItem.prefab.Identifier == "idcard";
            bool isWeapon = foundItem.GetComponent<RangedWeapon>() != null || foundItem.GetComponent<MeleeWeapon>() != null;

            if (isIdCard)
            {
                string name = string.Empty;

                foreach (var tag in foundItem.Tags.Split(','))
                {
                    string[] split = tag.Split(':');
                    string key = split.Length > 0 ? split[0] : string.Empty;
                    string value = split.Length > 1 ? split[1] : string.Empty;
                    if (key == "name") { name = value; }
                }

                // Name tag doesn't belong to anyone in particular or we own the ID card
                if (name == null || name == yoinkerCharacter.Name) { return; }
            }

            if (MathUtils.NearlyEqual(DangerousItemStealKarmaDecrease, 0)) { return; }

            const float calcUpper = 1, calcLower = -1;

            float upper = DangerousItemStealKarmaDecrease + 10.0f;
            float lower = DangerousItemStealKarmaDecrease - 10.0f;

            if (lower < 0)
            {
                upper += Math.Abs(lower);
                lower = 0;
            }

            // If we're stealing from a bot assume the bot has 50 karma
            var targetKarma = targetClient?.Karma ?? 50;

            float karmaDifference = Math.Clamp((targetKarma - yoinker.Karma) / 50.0f, calcLower, calcUpper);
            float karmaDecrease = lower + (karmaDifference - calcLower) * (upper - lower) / (calcUpper - calcLower);

            JobPrefab clientJob = yoinker.CharacterInfo?.Job?.Prefab;

            // security officers receive less karma penalty
            if (clientJob != null && clientJob.Identifier == "securityofficer" && isWeapon)
            {
                karmaDecrease *= 0.5f;
            }

            AdjustKarma(yoinkerCharacter, -karmaDecrease, "Stolen dangerous item");
        }

        public void OnCharacterHealthChanged(Character target, Character attacker, float damage, float stun, IEnumerable<Affliction> appliedAfflictions = null)
        {
            if (target == null || attacker == null) { return; }
            if (target == attacker) { return; }

            //damaging dead characters doesn't affect karma
            if (target.IsDead || target.Removed) { return; }

            bool isEnemy = target.AIController is EnemyAIController || target.TeamID != attacker.TeamID;
            if (GameMain.Server.TraitorManager?.Traitors != null)
            {
                if (GameMain.Server.TraitorManager.Traitors.Any(t => t.Character == target))
                {
                    //traitors always count as enemies
                    isEnemy = true;
                }
                if (GameMain.Server.TraitorManager.Traitors.Any(t => 
                    t.Character == attacker &&
                    t.CurrentObjective != null &&
                    t.CurrentObjective.IsEnemy(target)))
                {
                    //target counts as an enemy to the traitor
                    isEnemy = true;
                }
            }

            bool targetIsHusk = target.CharacterHealth?.GetAffliction<AfflictionHusk>("huskinfection")?.State == AfflictionHusk.InfectionState.Active;
            bool attackerIsHusk = attacker.CharacterHealth?.GetAffliction<AfflictionHusk>("huskinfection")?.State == AfflictionHusk.InfectionState.Active;
            //huskified characters count as enemies to healthy characters and vice versa
            if (targetIsHusk != attackerIsHusk) { isEnemy = true; }

            if (appliedAfflictions != null)
            {
               foreach (Affliction affliction in appliedAfflictions)
               {
                   if (MathUtils.NearlyEqual(affliction.Prefab.KarmaChangeOnApplied, 0.0f)) { continue; }
                   damage -= affliction.Prefab.KarmaChangeOnApplied * affliction.Strength;
               }
            }

            Client targetClient = GameMain.Server.ConnectedClients.Find(c => c.Character == target);
            if (damage > 0 && targetClient != null)
            {
                var targetMemory = GetClientMemory(targetClient);
                targetMemory.LastAttackTime[attacker] = Timing.TotalTime;
            }            

            Client attackerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == attacker);
            ClientMemory attackerMemory = GetClientMemory(attackerClient);
            if (attackerMemory != null)
            {
                //if the attacker has been attacked by the target within the last x seconds, ignore the damage
                //(= no karma penalty from retaliating against someone who attacked you)
                if (attackerMemory.LastAttackTime.ContainsKey(target) &&
                    attackerMemory.LastAttackTime[target] > Timing.TotalTime - AllowedRetaliationTime)
                {
                    damage = Math.Min(damage, 0);
                    stun = 0.0f;
                }
            }

            //attacking/healing clowns has a smaller effect on karma
            if (target.HasEquippedItem("clownmask") &&
                target.HasEquippedItem("clowncostume"))
            {
                damage *= 0.5f;
                stun *= 0.5f;
            }

            //smaller karma penalty for attacking someone who's aiming with a weapon
            if (damage > 0.0f &&
                target.IsKeyDown(InputType.Aim) &&
                target.SelectedItems.Any(it => it != null && (it.GetComponent<MeleeWeapon>() != null || it.GetComponent<RangedWeapon>() != null)))
            {
                damage *= 0.5f;
                stun *= 0.5f;
            }

            //damage scales according to the karma of the target
            //(= smaller karma penalty from attacking someone who has a low karma)
            if (damage > 0 && targetClient != null)
            {
                damage *= MathUtils.InverseLerp(0.0f, 50.0f, targetClient.Karma);
            }
            
            if (isEnemy)
            {
                if (damage > 0)
                {
                    float karmaIncrease = damage * DamageEnemyKarmaIncrease;
                    if (attacker?.Info?.Job.Prefab.Identifier == "securityofficer") { karmaIncrease *= 2.0f; }
                    AdjustKarma(attacker, karmaIncrease, "Damaged enemy");
                }
            }
            else
            {
                if (stun > 0 && attackerMemory != null)
                {
                    //GameServer.Log(GameServer.CharacterLogName(attacker) + " stunned " + GameServer.CharacterLogName(target) + $" ({stun})", ServerLog.MessageType.Karma);
                    attackerMemory.StunsInPastMinute.Add(new ClientMemory.TimeAmount() { Time = Timing.TotalTime, Amount = stun });

                    if (attackerMemory.StunsInPastMinute.Count > 1)
                    {
                        float avgStunsInflicted = attackerMemory.StunsInPastMinute[0].Amount / (float)(attackerMemory.StunsInPastMinute[1].Time - attackerMemory.StunsInPastMinute[0].Time);
                        for (int i = 1; i < attackerMemory.StunsInPastMinute.Count; i++)
                        {
                            avgStunsInflicted += attackerMemory.StunsInPastMinute[i].Amount / (float)(attackerMemory.StunsInPastMinute[i].Time - attackerMemory.StunsInPastMinute[i - 1].Time);
                        }

                        //GameServer.Log(avgStunsInflicted.ToString(), ServerLog.MessageType.Karma);

                        if (avgStunsInflicted > StunFriendlyKarmaDecreaseThreshold ||
                            attackerMemory.StunKarmaDecreaseMultiplier > 1.0f)
                        {
                            AdjustKarma(attacker, -StunFriendlyKarmaDecrease * attackerMemory.StunKarmaDecreaseMultiplier, "Stunned friendly");
                            attackerMemory.StunKarmaDecreaseMultiplier *= 2.0f;
                        }
                    }
                }

                if (damage > 0)
                {
                    AdjustKarma(attacker, -damage * DamageFriendlyKarmaDecrease, "Damaged friendly");
                }
                else
                {
                    float karmaIncrease = -damage * HealFriendlyKarmaIncrease;
                    if (attacker?.Info?.Job.Prefab.Identifier == "medicaldoctor") { karmaIncrease *= 2.0f; }
                    AdjustKarma(attacker, karmaIncrease, "Healed friendly");
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
                if (StructureDamageKarmaDecrease <= 0.0f) { return; }
                 if (GameMain.Server.TraitorManager?.Traitors != null)
                {
                    if (GameMain.Server.TraitorManager.Traitors.Any(t =>
                        t.Character == attacker &&
                        t.CurrentObjective != null &&
                        t.CurrentObjective.IsAllowedToDamage(structure)))
                    {
                        //traitor tasked to flood the sub -> damaging structures is ok
                        return;
                    }
                }

                Client client = GameMain.Server.ConnectedClients.Find(c => c.Character == attacker);
                if (client != null)
                {
                    //cap the damage so the karma can't decrease by more than MaxStructureDamageKarmaDecreasePerSecond per second
                    var clientMemory = GetClientMemory(client);
                    clientMemory.StructureDamageAccumulator += damageAmount;
                    if (clientMemory.StructureDamagePerSecond + damageAmount >= MaxStructureDamageKarmaDecreasePerSecond / StructureDamageKarmaDecrease)
                    {
                        damageAmount -= (MaxStructureDamageKarmaDecreasePerSecond / StructureDamageKarmaDecrease) - clientMemory.StructureDamagePerSecond;
                        if (damageAmount <= 0.0f) { return; }
                    }
                }
                AdjustKarma(attacker, -damageAmount * StructureDamageKarmaDecrease, "Damaged structures");
            }
            else
            {
                float karmaIncrease = -damageAmount * StructureRepairKarmaIncrease;
                //mechanics get twice as much karma for repairing walls
                if (attacker.Info?.Job.Prefab.Identifier == "mechanic") { karmaIncrease *= 2.0f; }
                AdjustKarma(attacker, karmaIncrease, "Repaired structures");
            }
        }

        public void OnItemRepaired(Character character, Repairable repairable, float repairAmount)
        {
            float karmaIncrease = repairAmount * ItemRepairKarmaIncrease;
            if (repairable.HasRequiredSkills(character)) { karmaIncrease *= 2.0f; }
            AdjustKarma(character, karmaIncrease, "Repaired item");
        }

        public void OnReactorOverHeating(Character character, float deltaTime)
        {
            AdjustKarma(character, -ReactorOverheatKarmaDecrease * deltaTime, "Caused reactor to overheat");
        }

        public void OnReactorMeltdown(Character character)
        {
            AdjustKarma(character, -ReactorMeltdownKarmaDecrease, "Caused a reactor meltdown");
        }

        public void OnExtinguishingFire(Character character, float deltaTime)
        {
            AdjustKarma(character, ExtinguishFireKarmaIncrease * deltaTime, "Extinguished a fire");
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

        private ClientMemory GetClientMemory(Client client)
        {
            if (client == null) { return null; }
            if (!clientMemories.ContainsKey(client))
            {
                clientMemories[client] = new ClientMemory()
                {
                    PreviousNotifiedKarma = client.Karma
                };
            }
            return clientMemories[client];
        }

        public void OnSpamFilterTriggered(Client client)
        {
            if (client != null)
            {
                client.Karma -= SpamFilterKarmaDecrease;
                SendKarmaNotifications(client, "Triggered the spam filter");
            }
        }

        private void AdjustKarma(Character target, float amount, string debugKarmaChangeReason = "")
        {
            if (target == null) { return; }

            Client client = GameMain.Server.ConnectedClients.Find(c => c.Character == target);
            if (client == null) { return; }

            //all penalties/rewards are halved when wearing a clown costume
            if (target.HasEquippedItem("clownmask") &&
                target.HasEquippedItem("clowncostume"))
            {
                amount *= 0.5f;
            }

            client.Karma += amount;

            if (amount < 0.0f)
            {
                float? herpesStrength = client.Character?.CharacterHealth.GetAfflictionStrength("spaceherpes");
                var clientMemory = GetClientMemory(client);
                clientMemory.KarmaDecreasesInPastMinute.RemoveAll(ta => ta.Time + 60.0f < Timing.TotalTime);
                float aggregate = clientMemory.KarmaDecreasesInPastMinute.Select(ta => ta.Amount).DefaultIfEmpty().Aggregate((a, b) => a + b);
                clientMemory.KarmaDecreasesInPastMinute.Add(new ClientMemory.TimeAmount() { Time = Timing.TotalTime, Amount = -amount });

                if (herpesStrength.HasValue && herpesStrength <= 0.0f && aggregate - amount > 25.0f && aggregate <= 25.0f)
                {
                    GameServer.Log($"{GameServer.ClientLogName(client)} has lost more than 25 karma in the past minute.", ServerLog.MessageType.Karma);
                }
            }

            if (TestMode)
            {
                SendKarmaNotifications(client, debugKarmaChangeReason);
            }
        }
    }
}
