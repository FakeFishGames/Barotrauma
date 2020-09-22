using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Barotrauma
{
    public static class IPExtensions
    {
        //TODO: remove?
        //workaround for .NET Framework 4.5 bug; presumably fixed in later versions
        //see https://stackoverflow.com/questions/23608829/why-does-ipaddress-maptoipv4-throw-argumentoutofrangeexception
        public static IPAddress MapToIPv4NoThrow(this IPAddress address)
        {
            byte[] addressBytes = address.GetAddressBytes();

            return new IPAddress(addressBytes.Skip(addressBytes.Length - 4).ToArray());
        }
    }
}
