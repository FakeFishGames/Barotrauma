#nullable enable
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    internal enum ReadyStatus
    {
        Unanswered,
        Yes,
        No,
    }

    internal partial class ReadyCheck
    {
        private readonly DateTime endTime;
        private readonly DateTime startTime;
        public readonly Dictionary<byte, ReadyStatus> Clients;
        public bool IsFinished = false;

        public ReadyCheck(List<byte> clients, DateTime startTime, DateTime endTime)
            : this(clients)
        {
            this.startTime = startTime;
            this.endTime = endTime;
        }

        public ReadyCheck(List<byte> clients, float duration)
            : this(clients)
        {
            startTime = DateTime.Now;
            endTime = startTime + new TimeSpan(0, 0, 0, 0, (int)(duration * 1000));
        }

        private ReadyCheck(List<byte> clients)
        {
            Clients = new Dictionary<byte, ReadyStatus>();
            foreach (byte client in clients)
            {
                if (Clients.ContainsKey(client)) { continue; }

                Clients.Add(client, ReadyStatus.Unanswered);
            }
        }

        partial void EndReadyCheck();

        public void Update(float deltaTime)
        {
            if (DateTime.Now < endTime)
            {
#if CLIENT
                UpdateBar();
#endif
                return;
            }

            EndReadyCheck();
        }
    }
}