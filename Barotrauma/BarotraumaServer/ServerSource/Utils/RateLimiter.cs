#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.Networking;

namespace Barotrauma
{
    public enum RateLimitAction
    {
        Invalid,
        OnLimitReached,
        OnLimitDoubled,
    }

    public enum RateLimitPunishment
    {
        None, // just ignore
        Announce, // announce to the server
        Kick, // kick the player
        Ban // ban the player
    }

    internal sealed class RateLimiter
    {
        private sealed record RateLimit(DateTimeOffset Expiry)
        {
            public int RequestAmount;
        }

        private readonly Dictionary<Client, RateLimit> rateLimits = new();
        private readonly HashSet<Client> expiredRateLimits = new();
        private readonly Dictionary<Client, DateTimeOffset> recentlyAnnouncedOffenders = new();

        private readonly int maxRequests, expiryInSeconds;

        private readonly ImmutableDictionary<RateLimitAction, RateLimitPunishment> punishments;

        public RateLimiter(int maxRequests, int expiryInSeconds, params (RateLimitAction Action, RateLimitPunishment Punishment)[] punishmentRules)
        {
            this.maxRequests = maxRequests;
            this.expiryInSeconds = expiryInSeconds;

            punishments = punishmentRules.ToImmutableDictionary(
                static pair => pair.Action,
                static pair => pair.Punishment);
        }

        public bool IsLimitReached(Client client)
        {
#if !DEBUG
            if (IsExempt(client)) { return false; }
#endif
            expiredRateLimits.Clear();

            foreach (var (c, limit) in rateLimits)
            {
                if (limit.Expiry < DateTimeOffset.Now)
                {
                    expiredRateLimits.Add(c);
                }
            }

            foreach (Client c in expiredRateLimits)
            {
                rateLimits.Remove(c);
            }

            if (!rateLimits.TryGetValue(client, out RateLimit? rateLimit))
            {
                rateLimit = new RateLimit(DateTimeOffset.Now.AddSeconds(expiryInSeconds));
                rateLimits.Add(client, rateLimit);
            }

            rateLimit.RequestAmount++;

            if (rateLimit.RequestAmount > maxRequests)
            {
                ProcessPunishment(client, rateLimit.RequestAmount);
                return true;
            }

            return false;
        }

        private void ProcessPunishment(Client client, int requests)
        {
            bool isDosProtectionEnabled = GameMain.Server is { ServerSettings.EnableDoSProtection: true };

            foreach (var (action, punishment) in punishments)
            {
                switch (action)
                {
                    case RateLimitAction.Invalid:
                        continue;
                    case RateLimitAction.OnLimitReached when requests >= maxRequests:
                    case RateLimitAction.OnLimitDoubled when requests >= maxRequests * 2:
                        switch (punishment)
                        {
                            case RateLimitPunishment.None:
                                continue;
                            case RateLimitPunishment.Announce:
                                AnnounceOffender(client);
                                break;
                            case RateLimitPunishment.Ban when isDosProtectionEnabled:
                                GameMain.Server?.BanClient(client, TextManager.Get("SpamFilterKicked").Value);
                                break;
                            case RateLimitPunishment.Kick when isDosProtectionEnabled:
                                GameMain.Server?.KickClient(client, TextManager.Get("SpamFilterKicked").Value);
                                break;
                        }
                        break;
                }
            }
        }

        private void AnnounceOffender(Client client)
        {
            if (recentlyAnnouncedOffenders.TryGetValue(client, out DateTimeOffset expiry))
            {
                if (expiry > DateTimeOffset.Now) { return; }

                recentlyAnnouncedOffenders.Remove(client);
            }

            GameServer.Log($"{NetworkMember.ClientLogName(client)} is sending too many packets!", ServerLog.MessageType.DoSProtection);
            recentlyAnnouncedOffenders.Add(client, DateTimeOffset.Now.AddSeconds(expiryInSeconds));
        }

        public static bool IsExempt(Client client) =>
            (GameMain.Server.OwnerConnection != null && client.Connection == GameMain.Server.OwnerConnection)
            || client.HasPermission(ClientPermissions.SpamImmunity);
    }
}