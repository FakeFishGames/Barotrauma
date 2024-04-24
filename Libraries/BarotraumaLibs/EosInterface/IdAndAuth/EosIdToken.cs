using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Barotrauma.Networking;

namespace Barotrauma;

public static partial class EosInterface
{
    public enum GetEosSelfIdTokenError
    {
        EosNotInitialized,
        NotLoggedIn,
        InvalidToken,
        CouldNotParseJwt,
        UnhandledErrorCondition
    }

    public enum VerifyEosIdTokenError
    {
        EosNotInitialized,
        TimedOut,
        ProductIdDidNotMatch,
        CouldNotParseExternalAccountId,
        UnhandledErrorCondition
    }

    /// <summary>
    /// Represents an EOS ID Token, used to authenticate a Product User ID.
    /// This is distinct from <see cref="EgsIdToken" />, which represents an Epic Games ID Token.
    /// </summary>
    public readonly record struct EosIdToken(
        ProductUserId ProductUserId,
        JsonWebToken JsonWebToken)
    {
        public async Task<Result<AccountId, VerifyEosIdTokenError>> Verify()
            => Core.LoadedImplementation is { } loadedImplementation
                ? await loadedImplementation.VerifyEosIdToken(this)
                : Result.Failure(VerifyEosIdTokenError.EosNotInitialized);

        public static Option<EosIdToken> Parse(string str)
        {
            var jsonReader = new Utf8JsonReader(Encoding.UTF8.GetBytes(str));
            JsonDocument? jsonDoc = null;
            try
            {
                if (!JsonDocument.TryParseValue(ref jsonReader, out jsonDoc))
                {
                    return Option.None;
                }

                if (!jsonDoc.RootElement.TryGetProperty(nameof(ProductUserId), out var puidElement))
                {
                    return Option.None;
                }

                if (!jsonDoc.RootElement.TryGetProperty(nameof(JsonWebToken), out var jwtElement))
                {
                    return Option.None;
                }

                var puidStr = puidElement.ToString();
                if (!puidStr.IsHexString())
                {
                    return Option.None;
                }

                var puid = new ProductUserId(puidStr);

                var jwtStr = jwtElement.ToString();
                if (!JsonWebToken.Parse(jwtStr).TryUnwrap(out var jsonWebToken))
                {
                    return Option.None;
                }

                var newToken = new EosIdToken(puid, jsonWebToken);

                return Option.Some(newToken);
            }
            catch
            {
                return Option.None;
            }
            finally
            {
                jsonDoc?.Dispose();
            }
        }

        public static Result<EosIdToken, GetEosSelfIdTokenError> FromProductUserId(ProductUserId puid)
            => Core.LoadedImplementation.IsInitialized()
                ? Core.LoadedImplementation.GetEosIdTokenForProductUserId(puid)
                : Result.Failure(GetEosSelfIdTokenError.EosNotInitialized);

        public override string ToString()
        {
            using var memoryStream = new System.IO.MemoryStream();
            using var jsonWriter = new Utf8JsonWriter(memoryStream);
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString(nameof(ProductUserId), ProductUserId.Value);
            jsonWriter.WriteString(nameof(JsonWebToken), JsonWebToken.ToString());
            jsonWriter.WriteEndObject();
            jsonWriter.Flush();
            memoryStream.Flush();
            return Encoding.UTF8.GetString(memoryStream.ToArray());
        }
    }

    internal abstract partial class Implementation
    {
        public abstract Task<Result<AccountId, VerifyEosIdTokenError>> VerifyEosIdToken(EosIdToken token);
        public abstract Result<EosIdToken, GetEosSelfIdTokenError> GetEosIdTokenForProductUserId(ProductUserId puid);
    }
}