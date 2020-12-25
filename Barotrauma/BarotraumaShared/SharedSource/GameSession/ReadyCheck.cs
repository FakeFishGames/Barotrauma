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
        private readonly float endTime;
        private float time;
        public readonly Dictionary<byte, ReadyStatus> Clients;
        public bool IsFinished = false;

        public ReadyCheck(List<byte> clients, float duration = 30)
        {
            Clients = new Dictionary<byte, ReadyStatus>();
            foreach (byte client in clients)
            {
                if (Clients.ContainsKey(client)) { continue; }

                Clients.Add(client, ReadyStatus.Unanswered);
            }

            time = duration;
            endTime = duration;
#if CLIENT
            lastSecond = (int) Math.Ceiling(duration);
#endif
        }

        partial void EndReadyCheck();

        public void Update(float deltaTime)
        {
            if (time > 0)
            {
#if CLIENT
                UpdateBar();
#endif
                time -= deltaTime;
                return;
            }

            EndReadyCheck();
        }
    }
}