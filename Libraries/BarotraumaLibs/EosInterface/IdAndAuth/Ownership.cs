using System.Threading.Tasks;
using Barotrauma.Networking;

namespace Barotrauma;

public static partial class EosInterface
{
    public static class Ownership
    {
        private static Implementation? LoadedImplementation => Core.LoadedImplementation;

        public static async Task<Option<Ownership.Token>> GetGameOwnershipToken(EpicAccountId selfEpicAccountId)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.GetGameOwnershipToken(selfEpicAccountId)
                : Option.None;

        public readonly record struct Token(JsonWebToken Jwt)
        {
            public async Task<Option<EpicAccountId>> Verify()
                => LoadedImplementation.IsInitialized()
                    ? await LoadedImplementation.VerifyGameOwnershipToken(this)
                    : Option.None;
        }
    }

    internal abstract partial class Implementation
    {
        public abstract Task<Option<Ownership.Token>> GetGameOwnershipToken(EpicAccountId selfEpicAccountId);

        public abstract Task<Option<EpicAccountId>> VerifyGameOwnershipToken(Ownership.Token token);
    }
}