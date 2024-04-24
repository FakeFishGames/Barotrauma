using System.Collections.Immutable;
using System.Threading.Tasks;
using Barotrauma.Networking;

namespace Barotrauma;

public static partial class EosInterface
{
    public static class IdQueries
    {
        private static Implementation? LoadedImplementation => Core.LoadedImplementation;

        public static bool IsLoggedIntoEosConnect
            => GetLoggedInPuids() is { Length: > 0 };

        /// <summary>
        /// Gets all of the <see cref="Barotrauma.EosInterface.ProductUserId" />s the player has logged in with.
        /// For most players, this is expected to return one ID.
        /// It may return two IDs if a Steam user has chosen to link their account to an Epic Account.
        /// </summary>
        public static ImmutableArray<ProductUserId> GetLoggedInPuids()
            => LoadedImplementation.IsInitialized()
                ? LoadedImplementation.GetLoggedInPuids()
                : ImmutableArray<ProductUserId>.Empty;

        /// <summary>
        /// Gets all of the <see cref="Barotrauma.Networking.EpicAccountId" />s the player has logged in with.
        /// This is expected to return at most one ID.
        /// <br /><br />
        /// This should return exactly one ID for any Epic Games Store player.
        /// <br />
        /// Steam players may choose to link their account to only one Epic Games account.
        /// </summary>
        public static ImmutableArray<EpicAccountId> GetLoggedInEpicIds()
            => LoadedImplementation.IsInitialized()
                ? LoadedImplementation.GetLoggedInEpicIds()
                : ImmutableArray<EpicAccountId>.Empty;

        public enum GetSelfExternalIdError
        {
            EosNotInitialized,
            Inaccessible,
            Timeout,
            InvalidUser,
            ParseError,
            UnhandledErrorCondition
        }

        public static async Task<Result<ImmutableArray<AccountId>, GetSelfExternalIdError>> GetSelfExternalAccountIds(
            ProductUserId puid)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.GetSelfExternalAccountIds(puid)
                : Result.Failure(GetSelfExternalIdError.EosNotInitialized);
    }

    internal abstract partial class Implementation
    {
        public abstract ImmutableArray<ProductUserId> GetLoggedInPuids();
        public abstract ImmutableArray<EpicAccountId> GetLoggedInEpicIds();

        public abstract Task<Result<ImmutableArray<AccountId>, IdQueries.GetSelfExternalIdError>>
            GetSelfExternalAccountIds(ProductUserId puid);
    }
}