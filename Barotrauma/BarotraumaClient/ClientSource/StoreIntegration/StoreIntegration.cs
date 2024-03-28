using System;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Barotrauma.Steam;

namespace Barotrauma;

static class StoreIntegration
{
    public enum Store
    {
        None,
        Steam,
        Epic
    }

    public static Store CurrentStore { get; private set; } = Store.None;

    public static void Init(ref string[] programArgs)
    {
#if DEBUG
        if (EosInterface.Login.ParseEgsExchangeCode(programArgs).IsNone())
        {
            // If the dev tool is running on port 8730 with a credential of name localdev,
            // we can ask it to give us an exchange code so we can test the launcher args parsing
            try
            {
                var devAuthToolHttp = new HttpClient();
                devAuthToolHttp.BaseAddress = new UriBuilder(scheme: "http", host: "127.0.0.1", portNumber: 8730).Uri;
                var response = devAuthToolHttp.Send(new HttpRequestMessage(HttpMethod.Get, "localdev/exchange_code"));
                if (response.IsSuccessStatusCode)
                {
                    string responseContent = response.Content.ReadAsStringAsync().Result;
                    var match = Regex.Match(input: responseContent,
                        @"\s*{\s*""exchange_code""\s*:\s*""([0-9a-fA-F]+)""\s*}\s*");
                    if (match.Groups.Count > 1)
                    {
                        programArgs = programArgs.Concat(new[]
                        {
                            $"-AUTH_PASSWORD={match.Groups[1].Value}",
                            "-AUTH_TYPE=exchangecode"
                        }).ToArray();
                    }
                }
            }
            catch { /* do nothing */ }
        }
#endif
        if (EosInterface.Login.ParseEgsExchangeCode(programArgs).IsNone() && SteamManager.SteamworksLibExists)
        {
            // Didn't get EGS exchange code, assume we're on Steam
            // and do not initialize EOS SDK until player consent is confirmed
            SteamManager.Initialize();
            CurrentStore = Store.Steam;
        }
        else
        {
            // Got an EGS exchange code or Steamworks is not present in the files,
            // assume we're on EGS and initialize EOS SDK immediately.
            if (EosInterface.Core.Init(EosInterface.ApplicationCredentials.Client, enableOverlay: RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    .TryUnwrapFailure(out var initError))
            {
                DebugConsole.ThrowError($"EOS failed to initialize: {initError}");
            }
            CurrentStore = Store.Epic;
        }
    }
}