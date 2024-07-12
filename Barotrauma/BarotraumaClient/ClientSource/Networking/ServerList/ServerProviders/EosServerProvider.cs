#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Barotrauma.Networking;

namespace Barotrauma;

sealed class EosServerProvider : ServerProvider
{
    public sealed class DataSource : ServerInfo.DataSource
    {
        public readonly string SteamPingLocation;

        public DataSource(string steamPingLocation)
        {
            SteamPingLocation = steamPingLocation;
        }

        public override void Write(XElement element) { /* do nothing */ }
    }

    protected override void RetrieveServersImpl(Action<ServerInfo, ServerProvider> onServerDataReceived, Action onQueryCompleted)
    {
        if (EosInterface.IdQueries.GetLoggedInPuids() is not { Length: > 0 } loggedInPuids) { return; }

        int finishedTaskCount = 0;
        int totalTaskCount = EosInterface.Sessions.MaxBucketIndex + 1 - EosInterface.Sessions.MinBucketIndex;

        void countTaskFinished()
        {
            finishedTaskCount++;
            if (finishedTaskCount == totalTaskCount)
            {
                onQueryCompleted();
            }
        }

        void onTaskFinished(Task t)
        {
            using var janitor = Janitor.Start();
            janitor.AddAction(countTaskFinished);

            if (!t.TryGetResult(
                    out Result<ImmutableArray<EosInterface.Sessions.RemoteSession>, EosInterface.Sessions.RemoteSession.Query.Error>? result))
            {
                return;
            }

            if (!result.TryUnwrapSuccess(out var sessions))
            {
                return;
            }

            var addedEndpoints = new HashSet<Endpoint>();
            foreach (var session in sessions)
            {
                if (!session.Attributes.TryGetValue("ServerName".ToIdentifier(), out var serverName))
                {
                    continue;
                }

                var endpointOption = Endpoint.Parse(session.HostAddress);
                if (!endpointOption.TryUnwrap(out var primaryEndpoint))
                {
                    continue;
                }

                var endpoints = new List<Endpoint> { primaryEndpoint };
                if (primaryEndpoint is EosP2PEndpoint
                    && session.Attributes.TryGetValue("SteamP2PEndpoint".ToIdentifier(), out var steamIdStr)
                    && SteamP2PEndpoint.Parse(steamIdStr).TryUnwrap(out var steamP2PEndpoint))
                {
                    endpoints.Add(steamP2PEndpoint);
                }
                else if (primaryEndpoint is LidgrenEndpoint
                         {
                             Address: LidgrenAddress address, Port: NetConfig.DefaultPort
                         }
                         && session.Attributes.TryGetValue("Port".ToIdentifier(), out var portStr)
                         && ushort.TryParse(portStr, out var port))
                {
                    // Port isn't included as part of the host address
                    // because it's filled in by EOS automatically,
                    // so extract the port from a separate attribute and
                    // fix up the endpoint here
                    primaryEndpoint = new LidgrenEndpoint(address.NetAddress, port);
                    endpoints[0] = primaryEndpoint;
                }

                // Prevent duplicate entries
                if (endpoints.Intersect(addedEndpoints).Any())
                {
                    continue;
                }

                addedEndpoints.UnionWith(endpoints);

                var serverInfo = new ServerInfo(endpoints.ToImmutableArray())
                {
                    ServerName = serverName
                };
                serverInfo.UpdateInfo(key =>
                    session.Attributes.TryGetValue(key.ToIdentifier(), out var value) ? value : string.Empty);
                serverInfo.EosCrossplay = true;
                serverInfo.Checked = true;

                if (session.Attributes.TryGetValue("steampinglocation".ToIdentifier(), out var steamPingLocation))
                {
                    serverInfo.MetadataSource = Option.Some((ServerInfo.DataSource)new DataSource(steamPingLocation));
                }

                onServerDataReceived(serverInfo, this);
            }
        };
        
        for (int bucketIndex = EosInterface.Sessions.MinBucketIndex; bucketIndex <= EosInterface.Sessions.MaxBucketIndex; bucketIndex++)
        {
            var query = new EosInterface.Sessions.RemoteSession.Query(
                BucketIndex: bucketIndex,
                LocalUserId: loggedInPuids.First(),
                MaxResults: 200,
                Attributes: ImmutableDictionary<Identifier, string>.Empty);

            TaskPool.Add(
                $"{nameof(EosServerProvider)}.{nameof(RetrieveServersImpl)}",
                query.Run(),
                onTaskFinished);
        }
    }

    public override void Cancel()
    {
        
    }
}