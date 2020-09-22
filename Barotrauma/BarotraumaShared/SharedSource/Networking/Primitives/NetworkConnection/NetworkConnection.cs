using System;

namespace Barotrauma.Networking
{
    public enum NetworkConnectionStatus
    {
        Connected = 0x1,
        Disconnected = 0x2
    }

    public abstract class NetworkConnection
    {
        public const double TimeoutThreshold = 60.0; //full minute for timeout because loading screens can take quite a while
        public const double TimeoutThresholdInGame = 10.0;

        public string Name;

        public UInt64 SteamID
        {
            get;
            protected set;
        }

        public string EndPointString
        {
            get;
            protected set;
        }

        public string Language
        {
            get; set;
        }

        public abstract bool EndpointMatches(string endPoint);

        public NetworkConnectionStatus Status = NetworkConnectionStatus.Disconnected;
    }
}
