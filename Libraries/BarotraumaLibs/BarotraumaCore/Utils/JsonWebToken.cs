using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Barotrauma;

public static class UnixTime
{
    public static readonly DateTime UtcEpoch = new DateTime(year: 1970, month: 1, day: 1, hour: 0, minute: 0, second: 0, kind: DateTimeKind.Utc);

    public static Option<DateTime> ParseUtc(string str)
    {
        if (!ulong.TryParse(str, out var seconds)) { return Option.None; }
        return Option.Some(UtcEpoch + TimeSpan.FromSeconds(seconds));
    }
}

/// <summary>
/// URL-safe Base64. See https://datatracker.ietf.org/doc/html/rfc4648#section-5
/// </summary>
public static class Base64Url
{
    public static bool IsBase64Url(this string str)
        => str.All(c
            => c is
                (>= 'A' and <= 'Z')
                or (>= 'a' and <= 'z')
                or (>= '0' and <= '9')
                or '-' or '_');

    public static Option<string> DecodeUtf8String(string encodedForm)
    {
        return DecodeBytes(encodedForm).Select(bytes => Encoding.UTF8.GetString(bytes.AsSpan()));
    }

    public static Option<ImmutableArray<byte>> DecodeBytes(string encodedForm)
    {
        if (!encodedForm.IsBase64Url()) { return Option.None; }
        string base64Form = encodedForm.Replace("-", "+").Replace("_", "/");
        base64Form += new string('=', (4 - (base64Form.Length % 4)) % 4);
        return Option.Some(Convert.FromBase64String(base64Form).ToImmutableArray());
    }
}

/// <summary>
/// Rudimentary JSON Web Token implementation. See https://en.wikipedia.org/wiki/JSON_Web_Token.
/// This is used by continuance tokens and ID tokens as part of their internal representation.
/// We can use the data encoded in them for some things, such as determining a token's expiry time.
/// </summary>
public readonly record struct JsonWebToken(
    string Header,
    string Payload,
    string Signature)
{
    public bool IsValid => Header.IsBase64Url() && Payload.IsBase64Url() && Signature.IsBase64Url();

    public override string ToString()
        => $"{Header}.{Payload}.{Signature}";

    public string HeaderDecoded => Base64Url.DecodeUtf8String(Header).Fallback("");
    public string PayloadDecoded => Base64Url.DecodeUtf8String(Payload).Fallback("");

    public static Option<JsonWebToken> Parse(string str)
    {
        if (str.Split(".") is not { Length: 3 } split) { return Option.None; }
        var newToken = new JsonWebToken(
            Header: split[0],
            Payload: split[1],
            Signature: split[2]);
        if (!newToken.IsValid) { return Option.None; }
        return Option.Some(newToken);
    }
}