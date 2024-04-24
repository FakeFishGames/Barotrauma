#nullable enable
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;
using Barotrauma.Steam;

namespace Barotrauma.Eos;

static class EosSessionManager
{
    public static Option<EosInterface.Sessions.OwnedSession> CurrentOwnedSession;

    public static void LeaveSession()
    {
        if (!CurrentOwnedSession.TryUnwrap(out var ownedSession)) { return; }
        ownedSession.Dispose();
        CurrentOwnedSession = Option.None;
    }

    public static void UpdateOwnedSession(Endpoint endpoint, ServerSettings serverSettings)
        => UpdateOwnedSession(Option.Some(endpoint), serverSettings);

    public static void UpdateOwnedSession(Option<Endpoint> endpoint, ServerSettings serverSettings)
    {
        if (!EosInterface.Core.IsInitialized) { return; }

        if (!serverSettings.IsPublic)
        {
            // Sessions can only be public, so if that's not what we want then
            // destroy the current one if it exists and do not attempt to create
            // or update one
            LeaveSession();
            return;
        }

        var selfPuids = EosInterface.IdQueries.GetLoggedInPuids();
        if (!CurrentOwnedSession.TryUnwrap(out var ownedSession))
        {
            if (!TaskPool.IsTaskRunning("CreateOwnedSession"))
            {
                TaskPool.Add(
                    "CreateOwnedSession",
                    EosInterface.Sessions.CreateSession(selfPuids.Any() ? Option.Some(selfPuids.First()) : Option.None, internalId: "OwnedSession".ToIdentifier(), maxPlayers: serverSettings.MaxPlayers),
                    t =>
                    {
                        LeaveSession();
                        if (!t.TryGetResult(out Result<EosInterface.Sessions.OwnedSession, EosInterface.Sessions.CreateError>? result)) { return; }
                        if (!result.TryUnwrapSuccess(out var newOwnedSession))
                        {
                            if (result.TryUnwrapFailure(out var error) &&
                                error is EosInterface.Sessions.CreateError.SessionAlreadyExists)
                            {
                                // If the session already exists then this failure is not a problem
                                return;
                            }
                            DebugConsole.ThrowError($"Failed to create session: {result}");
                            return;
                        }
                        CurrentOwnedSession = Option.Some(newOwnedSession);
                        UpdateOwnedSession(endpoint, serverSettings);
                    });
            }
            return;
        }

        if (selfPuids.Length > 0)
        {
            endpoint = Option<Endpoint>.Some(new EosP2PEndpoint(selfPuids.First()));
        }
        ownedSession.HostAddress = endpoint.Select(e1 => e1.StringRepresentation);
        if (endpoint.TryUnwrap(out var e2) && e2 is LidgrenEndpoint { Port: var port })
        {
            SetAttributeValue("Port".ToIdentifier(), port.ToString());
        }
        else if (serverSettings.Port != 0)
        {
            SetAttributeValue("Port".ToIdentifier(), serverSettings.Port.ToString());
        }

        if (SteamManager.GetSteamId().TryUnwrap(out var steamId))
        {
            SetAttributeValue("SteamP2PEndpoint".ToIdentifier(), steamId.StringRepresentation);
        }

        serverSettings.UpdateServerListInfo(SetAttributeValue);
        TaskPool.Add(
            "UpdateOwnedSessionAttributes",
            ownedSession.UpdateAttributes(),
            t =>
            {
                if (!t.TryGetResult(out Result<Unit, EosInterface.Sessions.AttributeUpdateError>? result)) { return; }
                DebugConsole.Log($"EOS UpdateOwnedSessionAttributes result: {result}");
            });


        void SetAttributeValue(Identifier attributeKey, object value)
        {
            string valueStr = value.ToString() ?? "";

            if (attributeKey == "contentpackages" && value is IEnumerable<ContentPackage> contentPackages)
            {
                int contentPackageIndex = 0;
                foreach (var contentPackage in contentPackages)
                {
                    ownedSession.Attributes[$"contentpackage{contentPackageIndex}".ToIdentifier()]
                        = new ServerListContentPackageInfo(contentPackage).ToString();
                    contentPackageIndex++;
                }
            }
            else
            {
                ownedSession.Attributes[attributeKey] = valueStr;
            }
        }

    }
}
