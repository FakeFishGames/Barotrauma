#nullable enable
using System;
using Barotrauma.Networking;

namespace Barotrauma
{
    abstract class ServerProvider
    {
        public void RetrieveServers(Action<ServerInfo> onServerDataReceived, Action onQueryCompleted)
        {
            Cancel();
            RetrieveServersImpl(onServerDataReceived, onQueryCompleted);
        }
        protected abstract void RetrieveServersImpl(Action<ServerInfo> onServerDataReceived, Action onQueryCompleted);
        public abstract void Cancel();
    }
}