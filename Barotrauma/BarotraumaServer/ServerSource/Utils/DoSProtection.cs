#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Barotrauma.Networking;

namespace Barotrauma
{
    internal sealed class DoSProtection
    {
        /// <summary>
        /// A struct that executes an action when it's created and another one when it's disposed.
        /// </summary>
        public readonly ref struct DoSAction
        {
            private readonly Client sender;
            private readonly Action<Client> end;

            public DoSAction(Client sender, Action<Client> start, Action<Client> end)
            {
                this.sender = sender;
                this.end = end;
                start(sender);
            }

            public void Dispose()
            {
                end(sender);
            }
        }

        private sealed class OffenseData
        {
            /// <summary>
            /// Timer that keeps track of how long it takes to process a packet.
            /// </summary>
            public readonly Stopwatch Stopwatch = new();

            /// <summary>
            /// Amount of strikes the client has received for causing the server to slow down.
            /// </summary>
            public int Strikes;

            /// <summary>
            /// How many packets have been sent in the last minute.
            /// </summary>
            public int PacketCount;

            /// <summary>
            /// Resets the strikes and packet count.
            /// </summary>
            public void ResetStrikes()
            {
                Strikes = 0;
                PacketCount = 0;
            }

            /// <summary>
            /// Resets the timer.
            /// </summary>
            public void ResetTimer() => Stopwatch.Reset();
        }

        private readonly Dictionary<Client, OffenseData> clients = new();

        private float stopwatchResetTimer,
                      strikesResetTimer;

        private const int StopwatchResetInterval = 1,
                          StrikesResetInterval = 60,
                          StrikeThreshold = 6;

        /// <summary>
        /// Called when the server receives a packet to start logging how much time it takes to process.
        /// </summary>
        /// <param name="client">The client to start a timer for.</param>
        /// <returns>Nothing useful. Required for the "using" keyword.</returns>
        /// <remarks>
        /// Calling stop is not required, the timer will be stopped automatically when the function it was started in returns.
        /// </remarks>
        /// <example>
        /// <code>
        /// public void ServerRead(IReadMessage msg, Client c)
        /// {
        ///     // start the timer
        ///     using var _ = dosProtection.Start(connectedClient);
        /// 
        ///     if (condition)
        ///     {
        ///         // the timer will be stopped here.
        ///         return;
        ///     }
        ///
        ///     ProcessMessage(msg);
        ///     // the timer will be stopped here.
        /// }
        /// </code>
        /// </example>
        public DoSAction Start(Client client) => new DoSAction(client, StartFor, EndFor);

        /// <summary>
        /// Temporary pauses the timer for the client.
        /// Used when we know a packet is going to slow down the server but we don't want to count it as a strike.
        /// For example when a client is starting a round.
        /// </summary>
        /// <param name="client">The client to pause the timer for.</param>
        /// <returns>Nothing useful. Required for the "using" keyword.</returns>
        /// <remarks>
        /// Calling resume is not required, the timer will be resumed automatically when the using block ends.
        /// </remarks>
        /// <example>
        /// <code>
        /// using (dos.Pause(client))
        /// {
        ///     // do something that will slow down the server
        /// }
        /// // the timer will be resumed here
        /// </code>
        /// </example>
        public DoSAction Pause(Client client) => new DoSAction(client, PauseFor, ResumeFor);

        private void StartFor(Client client)
        {
            if (!clients.ContainsKey(client))
            {
                clients.Add(client, new OffenseData());
            }

            clients[client].Stopwatch.Start();
        }

        private void EndFor(Client client)
        {
            if (GetData(client) is not { } data) { return; }

            data.PacketCount++;
            data.Stopwatch.Stop();
            UpdateOffense(client, data);
        }

        // stops the clock but doesn't update offenses
        private void PauseFor(Client client) => GetData(client)?.Stopwatch.Stop();

        private void ResumeFor(Client client) => GetData(client)?.Stopwatch.Start();

        private void UpdateOffense(Client client, OffenseData data)
        {
            if (GameMain.Server?.ServerSettings is not { } settings) { return; }

            // client is sending too many packets, kick them
            if (data.PacketCount > settings.MaxPacketAmount && settings.MaxPacketAmount > ServerSettings.PacketLimitMin)
            {
                AttemptKickClient(client, TextManager.Get("PacketLimitKicked"));
                clients.Remove(client);
                return;
            }

            // if the stopwatch has been running for an entire second without the Update() method resetting it (which it does every second) then something is wrong
            if (data.Stopwatch.ElapsedMilliseconds < 100) { return; }

            data.Strikes++;
            data.ResetTimer();

            GameServer.Log($"{NetworkMember.ClientLogName(client)} is causing the server to slow down.", ServerLog.MessageType.DoSProtection);

            // too many strikes, get them out of here
            if (data.Strikes < StrikeThreshold) { return; }

            if (settings.EnableDoSProtection)
            {
                AttemptKickClient(client, TextManager.Get("DoSProtectionKicked"));
            }

            clients.Remove(client);

            static void AttemptKickClient(Client client, LocalizedString reason)
            {
                // ReSharper disable once ConvertToConstant.Local
                bool doesRateLimitAffectClient =
#if DEBUG
                    true; // for testing
#else
                    !RateLimiter.IsExempt(client);
#endif

                if (!doesRateLimitAffectClient)
                {
                    return;
                }

                GameMain.Server?.KickClient(client, reason.Value);
            }
        }

        public void Update(float deltaTime)
        {
            stopwatchResetTimer += deltaTime;
            strikesResetTimer += deltaTime;

            // reset the stopwatch every second
            if (stopwatchResetTimer > StopwatchResetInterval)
            {
                stopwatchResetTimer = 0;
                foreach (OffenseData data in clients.Values)
                {
                    data.ResetTimer();
                }
            }

            // reset the strikes every minute
            if (strikesResetTimer > StrikesResetInterval)
            {
                strikesResetTimer = 0;
                foreach (var (client, data) in clients)
                {
                    if (GameMain.Server?.ServerSettings is { MaxPacketAmount: > ServerSettings.PacketLimitMin } settings)
                    {
                        if (data.PacketCount > settings.MaxPacketAmount * 0.9f)
                        {
                            GameServer.Log($"{NetworkMember.ClientLogName(client)} is sending a lot of packets and almost got kicked! ({data.PacketCount}).", ServerLog.MessageType.DoSProtection);
                        }
                    }

                    data.ResetStrikes();
                }
            }
        }

        private OffenseData? GetData(Client client) => clients.TryGetValue(client, out OffenseData? data) ? data : null;
    }
}