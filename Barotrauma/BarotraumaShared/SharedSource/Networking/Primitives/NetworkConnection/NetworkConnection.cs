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

        public UInt64 OwnerSteamID
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

        public virtual bool SetSteamIDIfUnknown(UInt64 id)
        {
            //by default, don't allow setting the ID, this is only done
            //with Lidgren connections since those are initialized before
            //the SteamID can be known; it's set once the Steam auth ticket
            //is received by the server.
            return false;
        }

        public bool SetOwnerSteamIDIfUnknown(UInt64 id)
        {
            //we know that for both Lidgren and SteamP2P, the
            //owner id isn't known until the auth ticket is
            //processed, so this method is the same for both
            if (OwnerSteamID != 0) { return false; }
            OwnerSteamID = id;
            return true;
        }
    }
}
