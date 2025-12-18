#nullable enable
using DnsClient;
using System.Linq;

namespace Barotrauma.Networking
{
    internal static class DnsSrvResolver
    {
        private static readonly LookupClient lookup = new();
        private const string Service = "_barotrauma";
        private const string Proto = "_udp";

        /// <summary>Returns true if a SRV record was found.</summary>
        public static bool TryResolve(string host, string service, string proto, out string targetHost, out int targetPort)
        {
            targetHost = "";
            targetPort = 0;
            var queryName = $"{service}.{proto}.{host.TrimEnd('.')}";

            IDnsQueryResponse result;
            try
            {
                result = lookup.Query(queryName, QueryType.SRV);
            }
            catch
            {
                return false;
            }

            var record = result.Answers
                               .SrvRecords()
                               .OrderBy(r => r.Priority)
                               .ThenByDescending(r => r.Weight)
                               .FirstOrDefault();
            if (record is null) return false;
            
            targetHost = record.Target.Value.TrimEnd('.');
            targetPort = record.Port;
            
            return true;
        }

        /// <summary>Returns true if a SRV record was found.</summary>
        public static bool TryResolve(string host, out string targetHost, out int targetPort)
            => TryResolve(host, Service, Proto, out targetHost, out targetPort);
    }
}
