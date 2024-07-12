using System.Threading.Tasks;
using Barotrauma.Networking;

namespace Barotrauma;

public static partial class EosInterface
{
    public enum GetEgsSelfIdTokenError
    {
        EosNotInitialized,
        NotLoggedIn,
        InvalidToken,
        UnhandledErrorCondition
    }

    public enum VerifyEgsIdTokenResult
    {
        Verified,
        Failed
    }

    /// <summary>
    /// Represents an Epic Games ID Token, used to authenticate an Epic Account ID.
    /// This is distinct from <see cref="EosIdToken" />, which represents an EOS ID Token.
    /// </summary>
    public abstract class EgsIdToken
    {
        public abstract EpicAccountId AccountId { get; }

        public static Option<EgsIdToken> Parse(string str)
            => Core.LoadedImplementation.IsInitialized()
                ? Core.LoadedImplementation.ParseEgsIdToken(str)
                : Option.None;

        public static Result<EgsIdToken, GetEgsSelfIdTokenError> FromEpicAccountId(EpicAccountId accountId)
            => Core.LoadedImplementation.IsInitialized()
                ? Core.LoadedImplementation.GetEgsIdTokenForEpicAccountId(accountId)
                : Result.Failure(GetEgsSelfIdTokenError.EosNotInitialized);

        public abstract override string ToString();

        public abstract Task<VerifyEgsIdTokenResult> Verify(AccountId accountId);
    }

    internal abstract partial class Implementation
    {
        public abstract Option<EgsIdToken> ParseEgsIdToken(string str);

        public abstract Result<EgsIdToken, GetEgsSelfIdTokenError> GetEgsIdTokenForEpicAccountId(
            EpicAccountId accountId);
    }
}
