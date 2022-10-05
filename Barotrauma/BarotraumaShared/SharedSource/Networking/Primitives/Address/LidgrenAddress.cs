#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Barotrauma.Networking
{
    sealed class LidgrenAddress : Address
    {
        public readonly IPAddress NetAddress;

        public override string StringRepresentation
            => NetAddress.ToString();

        public override bool IsLocalHost => IPAddress.IsLoopback(NetAddress);

        public LidgrenAddress(IPAddress netAddress)
        {
            if (IPAddress.IsLoopback(netAddress))
            {
                NetAddress = IPAddress.Loopback;
            }
            else
            {
                NetAddress = netAddress;
            }
        }

        public new static Option<LidgrenAddress> Parse(string endpointStr)
        {
            if (endpointStr.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return Option<LidgrenAddress>.Some(new LidgrenAddress(IPAddress.Loopback));
            }
            else if (IPAddress.TryParse(endpointStr, out IPAddress? netEndpoint))
            {
                return Option<LidgrenAddress>.Some(new LidgrenAddress(netEndpoint!));
            }
            return Option<LidgrenAddress>.None();            
        }

        public override bool Equals(object? obj)
            => obj switch
            {
                LidgrenAddress otherAddress => this == otherAddress,
                _ => false
            };

        public override int GetHashCode()
            => NetAddress.GetHashCode();

        public static bool operator ==(LidgrenAddress a, LidgrenAddress b)
        {
            var addressA = a.NetAddress.MapToIPv6();
            var addressB = b.NetAddress.MapToIPv6();

            if (IPAddress.IsLoopback(addressA) && IPAddress.IsLoopback(addressB)) { return true; }
            return addressA.Equals(addressB);
        }

        public static bool operator !=(LidgrenAddress a, LidgrenAddress b)
            => !(a == b);
    }
}
