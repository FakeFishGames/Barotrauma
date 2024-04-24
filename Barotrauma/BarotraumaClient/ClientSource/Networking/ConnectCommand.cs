#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Steam;

namespace Barotrauma.Networking;

readonly record struct ConnectCommand(
    Option<ConnectCommand.NameAndP2PEndpoints> NameAndP2PEndpointsOption,
    Option<ConnectCommand.NameAndLidgrenEndpoint> NameAndLidgrenEndpointOption,
    Option<ConnectCommand.SteamLobbyId> SteamLobbyIdOption)
{
    public bool IsClientConnectedToEndpoint()
    {
        if (GameMain.Client?.ClientPeer == null) { return false; }
        if (NameAndP2PEndpointsOption.TryUnwrap(out var nameAndP2PEndpoints))
        {
            if (nameAndP2PEndpoints.Endpoints.Any(e => e.Equals(GameMain.Client.ClientPeer.ServerEndpoint))) { return true; }
        }
        if (NameAndLidgrenEndpointOption.TryUnwrap(out var nameAndLidgrenEndpoint))
        {
            if (nameAndLidgrenEndpoint.Endpoint.Equals(GameMain.Client.ClientPeer.ServerEndpoint)) { return true; }
        }
        if (SteamLobbyIdOption.TryUnwrap(out var steamLobbyId))
        {
            if (SteamManager.CurrentLobbyID == steamLobbyId.Value) { return true; }
        }
        return false;
    }

    public readonly record struct NameAndP2PEndpoints(
        string ServerName,
        ImmutableArray<P2PEndpoint> Endpoints);

    public readonly record struct NameAndLidgrenEndpoint(
        string ServerName,
        LidgrenEndpoint Endpoint);

    public readonly record struct SteamLobbyId(ulong Value);

    public ConnectCommand(string serverName, Endpoint endpoint)
        : this(
            NameAndP2PEndpointsOption: endpoint is P2PEndpoint p2pEndpoint
                ? Option.Some(new NameAndP2PEndpoints(ServerName: serverName, p2pEndpoint.ToEnumerable().ToImmutableArray()))
                : Option.None,
            NameAndLidgrenEndpointOption: endpoint is LidgrenEndpoint lidgrenEndpoint
                ? Option.Some(new NameAndLidgrenEndpoint(ServerName: serverName, lidgrenEndpoint))
                : Option.None,
            SteamLobbyIdOption: Option.None) { }

    public ConnectCommand(string serverName, ImmutableArray<P2PEndpoint> endpoints)
        : this(
            NameAndP2PEndpointsOption: Option.Some(new NameAndP2PEndpoints(ServerName: serverName, Endpoints: endpoints)),
            NameAndLidgrenEndpointOption: Option.None,
            SteamLobbyIdOption: Option.None) { }

    public ConnectCommand(string serverName, LidgrenEndpoint endpoint)
        : this(
            NameAndP2PEndpointsOption: Option.None,
            NameAndLidgrenEndpointOption: Option.Some(new NameAndLidgrenEndpoint(ServerName: serverName, Endpoint: endpoint)),
            SteamLobbyIdOption: Option.None) { }

    public ConnectCommand(SteamLobbyId lobbyId)
        : this(
            NameAndP2PEndpointsOption: Option.None,
            NameAndLidgrenEndpointOption: Option.None,
            SteamLobbyIdOption: Option.Some(lobbyId)) { }

    public static Option<ConnectCommand> Parse(string str)
        => Parse(ToolBox.SplitCommand(str));

    public static Option<ConnectCommand> Parse(IReadOnlyList<string>? args)
    {
        if (args == null || args.Count < 2) { return Option.None; }

        if (args[0].Equals("-connect", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count < 3) { return Option.None; }

            var serverName = args[1];

            var endpointStrs = args[2].Split(",");
            var endpoints = endpointStrs.Select(Endpoint.Parse).NotNone().ToImmutableArray();
            if (endpoints.Length != endpointStrs.Length) { return Option.None; }

            if (endpoints.All(e => e is P2PEndpoint))
            {
                return Option.Some(
                    new ConnectCommand(serverName, endpoints.Cast<P2PEndpoint>().ToImmutableArray()));
            }
            else if (endpoints.Length == 1 && endpoints[0] is LidgrenEndpoint lidgrenEndpoint)
            {
                return Option.Some(
                    new ConnectCommand(serverName, lidgrenEndpoint));
            }

            return Option.None;
        }
        else if (args[0].Equals("+connect_lobby", StringComparison.OrdinalIgnoreCase))
        {
            return UInt64.TryParse(args[1], out var lobbyId)
                ? Option.Some(new ConnectCommand(new SteamLobbyId(lobbyId)))
                : Option.None;
        }
        return Option.None;
    }

    public override string ToString()
    {
        if (SteamLobbyIdOption.TryUnwrap(out var steamLobbyId))
        {
            return $"+connect_lobby {steamLobbyId.Value}";
        }

        if (NameAndP2PEndpointsOption.TryUnwrap(out var nameAndP2PEndpoints))
        {
            var escapedName = nameAndP2PEndpoints.ServerName.Replace("\"", "\\\"");
            var escapedEndpoints = nameAndP2PEndpoints.Endpoints.Select(e => e.StringRepresentation).JoinEscaped(',');
            return $"-connect \"{escapedName}\" {escapedEndpoints}";
        }

        if (NameAndLidgrenEndpointOption.TryUnwrap(out var nameAndLidgrenEndpoint))
        {
            var escapedName = nameAndLidgrenEndpoint.ServerName.Replace("\"", "\\\"");
            var endpoint = nameAndLidgrenEndpoint.Endpoint.StringRepresentation;
            return $"-connect \"{escapedName}\" {endpoint}";
        }

        return "";
    }
}
