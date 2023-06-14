#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;
using Barotrauma.Networking;
using Barotrauma.Steam;

namespace Barotrauma
{
    sealed class SteamDedicatedServerProvider : ServerProvider
    {
        public class DataSource : ServerInfo.DataSource
        {
            public readonly UInt16 QueryPort;

            public DataSource(UInt16 queryPort)
            {
                QueryPort = queryPort;
            }

            /// Method is invoked via reflection,
            /// see <see cref="ServerInfo.DataSource.Parse" />
            public new static Option<DataSource> Parse(XElement element)
                => element.TryGetAttributeInt("QueryPort", out var result)
                    ? result switch
                    {
                        var invalidPort when invalidPort <= 0 || invalidPort > UInt16.MaxValue => Option<DataSource>.None(),
                        var queryPort => Option<DataSource>.Some(new DataSource((UInt16)queryPort))
                    }
                    : Option<DataSource>.None();
            
            public override void Write(XElement element) => element.SetAttributeValue("QueryPort", QueryPort);
        }
        
        private static Option<ServerInfo> InfoFromListEntry(Steamworks.Data.ServerInfo entry) =>
            entry.Name.IsNullOrEmpty() || entry.Address is null
                ? Option<ServerInfo>.None()
                : Option<ServerInfo>.Some(new ServerInfo(new LidgrenEndpoint(entry.Address, entry.ConnectionPort))
                    {
                        ServerName = entry.Name,
                        HasPassword = entry.Passworded,
                        PlayerCount = entry.Players,
                        MaxPlayers = entry.MaxPlayers,
                        MetadataSource = Option<ServerInfo.DataSource>.Some(new DataSource((UInt16)entry.QueryPort))
                    });

        private static void HandleResponsiveServer(Steamworks.Data.ServerInfo entry, Action<ServerInfo> onServerDataReceived)
        {
            TaskPool.Add($"QueryServerRules (GetServers, {entry.Name}, {entry.Address})", entry.QueryRulesAsync(),
                t =>
                {
                    if (t.Status == TaskStatus.Faulted)
                    {
                        TaskPool.PrintTaskExceptions(t, $"Failed to retrieve rules for {entry.Name}");
                        return;
                    }

                    if (!t.TryGetResult(out Dictionary<string, string> rules)) { return; }
                    if (rules is null) { return; }
                    if (!InfoFromListEntry(entry).TryUnwrap(out var serverInfo)) { return; }
                    serverInfo.UpdateInfo(key =>
                    {
                        if (rules.TryGetValue(key, out var val)) { return val; }
                        return null;
                    });
                    serverInfo.Checked = true; //rules != null;

                    onServerDataReceived(serverInfo);
                });
        }
        
        private static void HandleUnresponsiveServer(Steamworks.Data.ServerInfo entry, Action<ServerInfo> onServerDataReceived)
        {
            //TODO: do we still want to list unresponsive servers?
            if (!InfoFromListEntry(entry).TryUnwrap(out var serverInfo)) { return; }
            onServerDataReceived(serverInfo);
        }

        private Steamworks.ServerList.Internet? serverQuery;
        private CoroutineHandle? queryCoroutine;
        
        protected override void RetrieveServersImpl(Action<ServerInfo> onServerDataReceived, Action onQueryCompleted)
        {
            if (!SteamManager.IsInitialized)
            {
                onQueryCompleted();
                return;
            }
            
            // All lambdas in here must only capture this call's
            // query, not the provider's latest query
            var selfServerQuery = new Steamworks.ServerList.Internet();
            serverQuery = selfServerQuery;

            ConcurrentQueue<Steamworks.Data.ServerInfo> responsiveServers =
                new ConcurrentQueue<Steamworks.Data.ServerInfo>();
            ConcurrentQueue<Steamworks.Data.ServerInfo> unresponsiveServers =
                new ConcurrentQueue<Steamworks.Data.ServerInfo>();

            selfServerQuery.OnResponsiveServer = responsiveServers.Enqueue;
            selfServerQuery.OnUnresponsiveServer = unresponsiveServers.Enqueue;

            void dequeue(int? limit = null)
            {
                for (int i = 0; (!limit.HasValue || i < limit) && responsiveServers.TryDequeue(out var serverInfo); i++)
                {
                    HandleResponsiveServer(serverInfo, onServerDataReceived);
                }

                for (int i = 0; (!limit.HasValue || i < limit) && unresponsiveServers.TryDequeue(out var serverInfo); i++)
                {
                    HandleUnresponsiveServer(serverInfo, onServerDataReceived);
                }
            }
            
            IEnumerable<CoroutineStatus> dequeueCoroutine()
            {
                while (true)
                {
                    dequeue(limit: 20);
                    yield return new WaitForSeconds(0.1f, ignorePause: true);
                }
            }
            var selfQueryCoroutine = CoroutineManager.StartCoroutine(dequeueCoroutine(),
                $"{nameof(SteamDedicatedServerProvider)}.{nameof(RetrieveServers)}.{nameof(dequeueCoroutine)}");
            queryCoroutine = selfQueryCoroutine;
            
            TaskPool.Add("RunServerQuery", selfServerQuery.RunQueryAsync(timeoutSeconds: 30f),
                t =>
                {
                    try
                    {
                        // Clear the callbacks because it's too late now, we want to get this over with
                        selfServerQuery.OnResponsiveServer = null;
                        selfServerQuery.OnUnresponsiveServer = null;
                        
                        CoroutineManager.StopCoroutines(selfQueryCoroutine);
                        dequeue();

                        if (t.Status == TaskStatus.Faulted) { TaskPool.PrintTaskExceptions(t, "Failed to retrieve servers"); }
                        
                        selfServerQuery.Dispose();
                    }
                    finally
                    {
                        onQueryCompleted();
                    }
                });
        }

        public override void Cancel()
        {
            if (queryCoroutine != null) { CoroutineManager.StopCoroutines(queryCoroutine); }
            serverQuery?.Dispose();
            serverQuery = null;
        }
    }
}