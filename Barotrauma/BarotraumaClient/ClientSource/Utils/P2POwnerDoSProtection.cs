using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    internal sealed class P2POwnerDoSProtection
    {
        /// <summary>
        /// Delegate to be called when a client has sent too many packets in a short time.
        /// </summary>
        /// <param name="endpoint">The endpoint of the client.</param>
        /// <param name="shouldBan">A suggestion to ban the client due to too many kicks.</param>
        public delegate void ExcessivePacketDelegate(P2PEndpoint endpoint, bool shouldBan);

        private readonly Dictionary<P2PEndpoint, int> packetCounts = new();
        private readonly Dictionary<P2PEndpoint, int> kicksByEndpoint = new();

        private readonly ExcessivePacketDelegate onExcessivePackets;
        private double nextCheckTime;

        // check every 10 seconds
        private const int PacketCheckTimer = 10;

        public P2POwnerDoSProtection(ExcessivePacketDelegate onExcessivePackets)
        {
            this.onExcessivePackets = onExcessivePackets;
            nextCheckTime = Timing.TotalTime + PacketCheckTimer;
        }

        private static int MaxPacketCount
        {
            get
            {
                // Normally the packet limit is per second, but we want to check faster than that.
                // multiply by 1.2 to allow for some leeway to allow the DoS protection deeper in the stack
                // to handle this first.
                const float limitMultiplier = (PacketCheckTimer / 60f) * 1.2f;

                if (GameMain.Client?.ServerSettings is not { } serverSettings)
                {
                    // Shouldn't happen, but just in case.
                    return (int)MathF.Ceiling(ServerSettings.PacketLimitDefault * limitMultiplier);
                }

                return (int)MathF.Ceiling(serverSettings.MaxPacketAmount * MathF.Max(serverSettings.TickRate / (float)ServerSettings.DefaultTickRate, 1f) * limitMultiplier);
            }
        }

        private static bool ShouldCheck()
        {
            if (GameMain.Client?.ServerSettings is { } serverSettings)
            {
                return serverSettings.EnableDoSProtection && serverSettings.MaxPacketAmount > ServerSettings.PacketLimitMin;
            }

            return false;
        }

        public void OnPacket(P2PEndpoint endpoint)
        {
            if (!ShouldCheck()) { return; }

            // count = default(int), if the endpoint is not in the dictionary
            packetCounts.TryGetValue(endpoint, out int count);
            packetCounts[endpoint] = ++count;

            if (Timing.TotalTime > nextCheckTime)
            {
                foreach (P2PEndpoint e in packetCounts.Keys.ToArray())
                {
                    CheckForExcessivePackets(e, count);
                }
                packetCounts.Clear();
                nextCheckTime = Timing.TotalTime + PacketCheckTimer;
            }
        }

        private void CheckForExcessivePackets(P2PEndpoint endpoint, int count)
        {
            if (count > MaxPacketCount)
            {
                kicksByEndpoint.TryGetValue(endpoint, out int kickCount);
                kicksByEndpoint[endpoint] = ++kickCount;

                onExcessivePackets(endpoint, kickCount > 3);

                packetCounts.Remove(endpoint);
            }
        }
    }
}