#nullable enable

using System;
using System.Collections.Generic;
using Barotrauma.Networking;

namespace Barotrauma
{
    internal static class HealingCooldown
    {
        private static readonly Dictionary<Client, DateTimeOffset> HealingCooldowns = new();

        // Little bit less than client's 0.5 second cooldown to account for latency
        private const float CooldownDuration = 0.4f;

        public static bool IsOnCooldown(Client client)
        {
            RemoveExpiredCooldowns();
            return HealingCooldowns.ContainsKey(client);
        }

        public static void SetCooldown(Client client)
        {
            RemoveExpiredCooldowns();
            DateTimeOffset newCooldown = DateTimeOffset.UtcNow.AddSeconds(CooldownDuration);
            HealingCooldowns[client] = newCooldown;
        }

        private static void RemoveExpiredCooldowns()
        {
            HashSet<Client>? expiredCooldowns = null;

            DateTimeOffset now = DateTimeOffset.UtcNow;

            foreach (var (client, cooldown) in HealingCooldowns)
            {
                if (now < cooldown) { continue; }

                expiredCooldowns ??= new HashSet<Client>();
                expiredCooldowns.Add(client);
            }

            if (expiredCooldowns is null) { return; }

            foreach (Client expiredCooldown in expiredCooldowns)
            {
                HealingCooldowns.Remove(expiredCooldown);
            }
        }
    }
}