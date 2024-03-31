#nullable enable

using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Barotrauma.Networking;
using Barotrauma;

namespace EosInterfacePrivate;

static partial class OwnershipPrivate
{
    internal static async Task<Option<EosInterface.Ownership.Token>> GetGameOwnershipToken(EpicAccountId selfEpicAccountId)
    {
        if (CorePrivate.EcomInterface is not { } ecomInterface) { return Option.None; }

        var epicAccountIdInternal =
            Epic.OnlineServices.EpicAccountId.FromString(selfEpicAccountId.EosStringRepresentation);

        var queryOwnershipTokenOptions = new Epic.OnlineServices.Ecom.QueryOwnershipTokenOptions
        {
            LocalUserId = epicAccountIdInternal,
            CatalogItemIds = new Epic.OnlineServices.Utf8String[]
            {
                AudienceItemId,
                //"Completely arbitrary string!"

                // IDEA:
                // As of 2023-06-21, QueryOwnershipToken will succeed even if given obviously fake catalog item IDs.
                // This could be useful to us! We could use this to add an audience parameter to this method and fix
                // the impersonation exploit without requiring our own persistent service.
                // We should ask Epic about this before actually trying it, this is certainly a hack and might get patched.
            }
        };
        var callbackWaiter = new CallbackWaiter<Epic.OnlineServices.Ecom.QueryOwnershipTokenCallbackInfo>();
        ecomInterface.QueryOwnershipToken(options: ref queryOwnershipTokenOptions, clientData: null, completionDelegate: callbackWaiter.OnCompletion);
        var queryOwnershipTokenResultOption = await callbackWaiter.Task;
        if (!queryOwnershipTokenResultOption.TryUnwrap(out var queryOwnershipTokenResult)) { return Option.None; }
        if (queryOwnershipTokenResult.ResultCode != Epic.OnlineServices.Result.Success) { return Option.None; }

        var jwtOption = JsonWebToken.Parse(queryOwnershipTokenResult.OwnershipToken);
        return jwtOption.Select(jwt => new EosInterface.Ownership.Token(jwt));
    }

    internal static async Task<Option<EpicAccountId>> VerifyGameOwnershipToken(EosInterface.Ownership.Token token)
    {
        JsonWebToken jwt = token.Jwt;

        // Decode header
        string kidProperty;
        string algProperty;
        try
        {
            var jsonDoc = JsonDocument.Parse(jwt.HeaderDecoded);
            kidProperty = jsonDoc.RootElement.GetProperty("kid").GetString() ?? "";
            algProperty = jsonDoc.RootElement.GetProperty("alg").GetString() ?? "";
        }
        catch
        {
            // Header JSON decode failed, can't verify token
            return Option.None;
        }

        // Basic header sanity checks
        if (algProperty != "RS512") { return Option.None; }
        if (!kidProperty.IsBase64Url()) { return Option.None; }

        // Decode payload
        string epicAccountIdStr;
        string catalogItemId;
        Option<DateTime> expirationOption;
        bool owned;
        try
        {
            var jsonDoc = JsonDocument.Parse(jwt.PayloadDecoded);
            epicAccountIdStr = jsonDoc.RootElement.GetProperty("sub").GetString() ?? "";
            var entProperty = jsonDoc.RootElement.GetProperty("ent").EnumerateArray().First();
            catalogItemId = entProperty.GetProperty("catalogItemId").GetString() ?? "";
            expirationOption = UnixTime.ParseUtc(jsonDoc.RootElement.GetProperty("exp").GetUInt64().ToString());
            owned = entProperty.GetProperty("owned").GetBoolean();
        }
        catch
        {
            // Payload JSON decode failed, can't verify token
            return Option.None;
        }

        // Check that the payload is actually what we want
        if (catalogItemId != AudienceItemId) { return Option.None; }
        if (!owned) { return Option.None; }
        if (!expirationOption.TryUnwrap(out var expiration)) { return Option.None; }
        if (DateTime.UtcNow >= expiration) { return Option.None; }

        // Get the public key required to verify this token
        string modulus;
        string exponent;
        try
        {
            string url =
                "https://ecommerceintegration-public-service-ecomprod02.ol.epicgames.com/ecommerceintegration/api/public/publickeys/"
                + kidProperty;
            using var httpClient = new HttpClient();
            var response = await httpClient.SendAsync(new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(url)));
            if (!response.IsSuccessStatusCode) { return Option.None; }
            var responseStr = await response.Content.ReadAsStringAsync();
            
            var responseJsonDoc = JsonDocument.Parse(responseStr);
            if (kidProperty != responseJsonDoc.RootElement.GetProperty("kid").GetString()) { return Option.None; }

            modulus = responseJsonDoc.RootElement.GetProperty("n").GetString() ?? "";
            exponent = responseJsonDoc.RootElement.GetProperty("e").GetString() ?? "";
        }
        catch
        {
            // Failed to query EG Ecom web API, can't verify token
            return Option.None;
        }

        // Prepare RSA-SHA512 and verify token
        var modulusBytesOption = Base64Url.DecodeBytes(modulus);
        if (!modulusBytesOption.TryUnwrap(out var modulusBytes)) { return Option.None; }
        var exponentBytesOption = Base64Url.DecodeBytes(exponent);
        if (!exponentBytesOption.TryUnwrap(out var exponentBytes)) { return Option.None; }

        var signatureBytesOption = Base64Url.DecodeBytes(jwt.Signature);
        if (!signatureBytesOption.TryUnwrap(out var signatureBytes)) { return Option.None; }

        using var rsa = RSA.Create();
        using var sha = SHA512.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Exponent = exponentBytes.ToArray(),
            Modulus = modulusBytes.ToArray(),
        });
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(jwt.Header + "." + jwt.Payload));
        var deformatter = new RSAPKCS1SignatureDeformatter(rsa);
        deformatter.SetHashAlgorithm("SHA512");
        bool verified = deformatter.VerifySignature(hash, signatureBytes.ToArray());
        if (!verified) { return Option.None; }

        return EpicAccountId.Parse(epicAccountIdStr);
    }
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    public override Task<Option<EosInterface.Ownership.Token>> GetGameOwnershipToken(EpicAccountId selfEpicAccountId)
        => TaskScheduler.Schedule(() => OwnershipPrivate.GetGameOwnershipToken(selfEpicAccountId));

    public override Task<Option<EpicAccountId>> VerifyGameOwnershipToken(EosInterface.Ownership.Token token)
        => TaskScheduler.Schedule(() => OwnershipPrivate.VerifyGameOwnershipToken(token));
}
