﻿using System;

namespace Barotrauma.Networking
{
    public enum NetworkConnectionStatus
    {
        Connected = 0x1,
        Disconnected = 0x2
    }

    abstract class NetworkConnection<T> : NetworkConnection where T : Endpoint
    {
        protected NetworkConnection(T endpoint) : base(endpoint) { }

        public new T Endpoint => (base.Endpoint as T)!;
    }
    
    abstract class NetworkConnection
    {
        public const double TimeoutThreshold = 60.0; //full minute for timeout because loading screens can take quite a while
        public const double TimeoutThresholdInGame = 10.0;

        public AccountInfo AccountInfo { get; private set; } = AccountInfo.None;

        public readonly Endpoint Endpoint;

        [Obsolete("TODO: this doesn't belong in layer 1")]
        public LanguageIdentifier Language
        {
            get; set;
        }

        protected NetworkConnection(Endpoint endpoint)
        {
            Endpoint = endpoint;
        }
        
        public bool EndpointMatches(Endpoint endPoint)
            => Endpoint == endPoint;

        public NetworkConnectionStatus Status = NetworkConnectionStatus.Disconnected;

        public void SetAccountInfo(AccountInfo newInfo)
        {
            if (AccountInfo.IsNone) { AccountInfo = newInfo; }
        }

        public sealed override string ToString()
            => Endpoint.StringRepresentation;
    }
}
