#nullable enable
using System;
using System.Collections.Immutable;
using Barotrauma.Extensions;
using Barotrauma.Networking;

namespace Barotrauma
{
    class CompositeServerProvider : ServerProvider
    {
        private readonly ImmutableArray<ServerProvider> providers;
        
        public CompositeServerProvider(params ServerProvider[] providers)
        {
            this.providers = providers.ToImmutableArray();
        }
        
        protected override void RetrieveServersImpl(Action<ServerInfo> onServerDataReceived, Action onQueryCompleted)
        {
            int providersFinished = 0;
            void ackFinishedProvider()
            {
                providersFinished++;
                if (providersFinished == providers.Length)
                {
                    onQueryCompleted();
                }
            }
            providers.ForEach(p => p.RetrieveServers(onServerDataReceived, ackFinishedProvider));
        }

        public override void Cancel()
            => providers.ForEach(p => p.Cancel());
    }
}