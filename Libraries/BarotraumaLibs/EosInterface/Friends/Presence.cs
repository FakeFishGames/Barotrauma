using System.Threading.Tasks;
using Barotrauma.Networking;

namespace Barotrauma;

public static partial class EosInterface
{
    public static class Presence
    {
        public readonly record struct JoinGameInfo(
            EpicAccountId RecipientId,
            string JoinCommand);

        public readonly record struct AcceptInviteInfo(
            EpicAccountId RecipientId,
            string JoinCommand);
        
        public readonly record struct ReceiveInviteInfo(
            EpicAccountId RecipientId,
            EpicAccountId SenderId,
            string JoinCommand);

        private static readonly NamedEvent<JoinGameInfo> dummyJoinGameEvent =
            new NamedEvent<JoinGameInfo>();

        private static readonly NamedEvent<AcceptInviteInfo> dummyAcceptInviteEvent =
            new NamedEvent<AcceptInviteInfo>();
        
        private static readonly NamedEvent<ReceiveInviteInfo> dummyReceiveInviteEvent =
            new NamedEvent<ReceiveInviteInfo>();

        public static NamedEvent<JoinGameInfo> OnJoinGame
            => Core.LoadedImplementation.IsInitialized()
                ? Core.LoadedImplementation.OnJoinGame
                : dummyJoinGameEvent;

        public static NamedEvent<AcceptInviteInfo> OnInviteAccepted
            => Core.LoadedImplementation.IsInitialized()
                ? Core.LoadedImplementation.OnInviteAccepted
                : dummyAcceptInviteEvent;
        
        public static NamedEvent<ReceiveInviteInfo> OnInviteReceived
            => Core.LoadedImplementation.IsInitialized()
                ? Core.LoadedImplementation.OnInviteReceived
                : dummyReceiveInviteEvent;

        public enum SetJoinCommandError
        {
            EosNotInitialized,
            FailedToSetCustomInvite,
            FailedToCreatePresenceModification,
            JoinCommandTooLong,
            ServerNameTooLong,
            FailedToSetJoinInfo,
            FailedToGetPuid,
            DescTooLong,
            FailedToSetRichText,
            FailedToSetRecords,
            SetPresenceTimedOut,
            FailedToSetPresence
        }

        public static async Task<Result<Unit, SetJoinCommandError>> SetJoinCommand(
            EpicAccountId epicAccountId, string desc, string serverName, string joinCommand)
            => Core.LoadedImplementation.IsInitialized()
                ? await Core.LoadedImplementation.SetJoinCommand(epicAccountId, desc, serverName, joinCommand)
                : Result.Failure(SetJoinCommandError.EosNotInitialized);

        public enum SendInviteError
        {
            EosNotInitialized,
            FailedToGetSelfPuid,
            FailedToGetRemotePuid,
            TimedOut,
            InternalError
        }

        public static async Task<Result<Unit, SendInviteError>> SendInvite(
            EpicAccountId selfEpicAccountId, EpicAccountId remoteEpicAccountId)
            => Core.LoadedImplementation.IsInitialized()
                ? await Core.LoadedImplementation.SendInvite(selfEpicAccountId, remoteEpicAccountId)
                : Result.Failure(SendInviteError.EosNotInitialized);

        public static void DeclineInvite(EpicAccountId selfEpicAccountId, EpicAccountId senderEpicAccountId)
        {
            if (Core.LoadedImplementation.IsInitialized())
            {
                Core.LoadedImplementation.DeclineInvite(selfEpicAccountId, senderEpicAccountId);
            }
        }
    }

    internal abstract partial class Implementation
    {
        public abstract NamedEvent<Presence.JoinGameInfo> OnJoinGame { get; }
        public abstract NamedEvent<Presence.AcceptInviteInfo> OnInviteAccepted { get; }
        public abstract NamedEvent<Presence.ReceiveInviteInfo> OnInviteReceived { get; }

        public abstract Task<Result<Unit, Presence.SetJoinCommandError>> SetJoinCommand(
            EpicAccountId epicAccountId, string desc, string joinCommand, string s);
        public abstract Task<Result<Unit, Presence.SendInviteError>> SendInvite(
            EpicAccountId selfEpicAccountId, EpicAccountId remoteEpicAccountId);
        public abstract void DeclineInvite(EpicAccountId selfEpicAccountId, EpicAccountId senderEpicAccountId);
    }
}