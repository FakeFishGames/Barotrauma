#nullable enable
using System.Linq;
using System.Net;

namespace Barotrauma.Networking
{
    sealed class LidgrenEndpoint : Endpoint
    {
        public readonly IPEndPoint NetEndpoint;

        public int Port => NetEndpoint.Port;

        public override string StringRepresentation
            => NetEndpoint.ToString();

        public override LocalizedString ServerTypeString { get; } = TextManager.Get("DedicatedServer");

        public LidgrenEndpoint(IPAddress address, int port) : this(new IPEndPoint(address, port)) { }

        public LidgrenEndpoint(IPEndPoint netEndpoint) : base(new LidgrenAddress(netEndpoint.Address))
        {
            NetEndpoint = netEndpoint;
        }

        public new static Option<LidgrenEndpoint> Parse(string endpointStr)
        {
            if (IPEndPoint.TryParse(endpointStr, out IPEndPoint? netEndpoint))
            {
                return Option<LidgrenEndpoint>.Some(new LidgrenEndpoint(netEndpoint!));
            }

            if (endpointStr.Count(c => c == ':') == 1)
            {
                string[] split = endpointStr.Split(':');
                string hostName = split[0];
                if (LidgrenAddress.Parse(hostName).TryUnwrap(out var adr)
                    && int.TryParse(split[1], out var port))
                {
                    return Option<LidgrenEndpoint>.Some(new LidgrenEndpoint(adr.NetAddress, port));
                }
            }

            return LidgrenAddress.Parse(endpointStr)
                .Select(adr => new LidgrenEndpoint(adr.NetAddress, NetConfig.DefaultPort));
        }

        public override bool Equals(object? obj)
            => obj switch
            {
                LidgrenEndpoint otherEndpoint => this == otherEndpoint,
                _ => false
            };

        public override int GetHashCode()
            => NetEndpoint.GetHashCode();

        public static bool operator ==(LidgrenEndpoint a, LidgrenEndpoint b)
            => a.Address.Equals(b.Address) && a.Port == b.Port;

        public static bool operator !=(LidgrenEndpoint a, LidgrenEndpoint b)
            => !(a == b);
    }
}
