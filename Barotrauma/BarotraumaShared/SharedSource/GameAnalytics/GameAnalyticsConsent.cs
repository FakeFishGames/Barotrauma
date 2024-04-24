#nullable enable
using Barotrauma.Steam;
using RestSharp;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Barotrauma
{
    static partial class GameAnalyticsManager
    {
        /// <summary>
        /// The protocol used to communicate with the remote consent server may change.
        /// This number tells the server which version the game is using so we can implement backwards-compatibility.
        /// </summary>
        private const string RemoteRequestVersion = "3";

        public enum Consent
        {
            /// <summary>
            /// No attempt to contact the consent server has been made
            /// </summary>
            Unknown,

            /// <summary>
            /// An error occurred while attempting to retrieve consent status
            /// </summary>
            Error,

            /// <summary>
            /// The consent status was not saved on the remote database
            /// </summary>
            Ask,

            /// <summary>
            /// The user explicitly denied consent
            /// </summary>
            No,

            /// <summary>
            /// The user explicitly granted consent
            /// </summary>
            Yes
        }

        public static Consent UserConsented { get; private set; } = Consent.Unknown;

        public static bool SendUserStatistics => UserConsented == Consent.Yes && loadedImplementation != null;

        private static bool ConsentTextAvailable
            => TextManager.ContainsTag("statisticsconsentheader")
                && TextManager.ContainsTag("statisticsconsenttext");
 
        private const string consentServerUrl = "https://barotraumagame.com/baromaster/";
        private const string consentServerFile = "consentserver.php";

        enum Platform
        {
            Steam,
            EOS,
            None
        }

        private class AuthTicket
        {
            public readonly string Token;
            public readonly Platform Platform;

            public AuthTicket(string token, Platform platform)
            {
                Token = token ?? string.Empty;
                Platform = platform;
            }
        }

        private static async Task<AuthTicket> GetAuthTicket()
        {
            if (SteamManager.IsInitialized)
            {
                return await GetSteamAuthTicket();
            }
            else if (EosInterface.IdQueries.IsLoggedIntoEosConnect)
            {
                return await GetEOSAuthTicket();
            }
            return new AuthTicket(string.Empty, Platform.None);
        }

        private static async Task<AuthTicket> GetSteamAuthTicket()
        {
            var authTicket = await SteamManager.GetAuthTicketForGameAnalyticsConsent();
            return authTicket.TryUnwrap(out var ticketUnwrapped) && ticketUnwrapped.Data is { Length: > 0 }
                ? new AuthTicket(ToolBoxCore.ByteArrayToHexString(ticketUnwrapped.Data), Platform.Steam) //convert byte array to hex
                : throw new Exception("Could not retrieve Steamworks authentication ticket for GameAnalytics");
        }

        private static async Task<AuthTicket> GetEOSAuthTicket()
        {
            var puid = EosInterface.IdQueries.GetLoggedInPuids().First();
            var tokenResult = EosInterface.EosIdToken.FromProductUserId(puid);
            if (tokenResult.TryUnwrapFailure(out var error))
            {
                throw new Exception($"Could not retrieve EOS authentication ticket for GameAnalytics. {error}");
            }
            else if (tokenResult.TryUnwrapSuccess(out var token))
            {
                return new AuthTicket(token.JsonWebToken.ToString(), Platform.EOS);
            }
            throw new UnreachableCodeException();
        }

        /// <summary>
        /// Sets the consent status. This method cannot be called to
        /// set the status to Consent.Yes; only a positive response from
        /// the database or the user accepting via the privacy policy
        /// prompt should enable it.
        /// </summary>
        public static void SetConsent(Consent consent, Action? onAnswerSent = null)
        {
            if (consent == Consent.Yes)
            {
                throw new Exception(
                    "Cannot call SetConsent with value Consent.Yes, must only be set to this value via consent prompt");
            }
            SetConsentInternal(consent, onAnswerSent);
        }
        
        /// <summary>
        /// Implementation of the bulk of SetConsent.
        /// DO NOT CALL THIS UNLESS NEEDED.
        /// </summary>
        private static void SetConsentInternal(Consent consent, Action? onAnswerSent)
        {
            if (UserConsented == consent)
            {
                onAnswerSent?.Invoke();
                return;
            }

            if (consent == Consent.Ask)
            {
#if CLIENT
                GameMain.ExecuteAfterContentFinishedLoading(CreateConsentPrompt);
#endif
            }

            if (consent != Consent.No && consent != Consent.Yes)
            {
                UserConsented = consent;
                ShutDown();
                return;
            }
            if (consent == Consent.No)
            {
                UserConsented = consent;
                ShutDown();
            }

            TaskPool.Add(
                "GameAnalyticsConsent.SendAnswerToRemoteDatabase",
                SendAnswerToRemoteDatabase(consent),
                t =>
                {
                    onAnswerSent?.Invoke();
                    if (!t.TryGetResult(out bool success) || !success) { return; }

                    UserConsented = consent;
                    if (consent == Consent.Yes)
                    {
                        Init();
                    }
                });
        }

        /// <summary>
        /// Try to send the user's response to the remote consent server.
        /// Returns true upon success, false otherwise.
        /// </summary>
        private static async Task<bool> SendAnswerToRemoteDatabase(Consent consent)
        {
            AuthTicket authTicket;
            try
            {
                authTicket = await GetAuthTicket();
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Error in {nameof(GameAnalyticsManager)}.{nameof(SendAnswerToRemoteDatabase)}. Could not get an authentication ticket.", e);
                return false;
            }
            if (authTicket.Platform == Platform.None)
            {
                DebugConsole.AddWarning($"Error in {nameof(GameAnalyticsManager)}.{nameof(SendAnswerToRemoteDatabase)}. Not logged in to any platform.");
                return false;
            }
            if (string.IsNullOrEmpty(authTicket.Token))
            {
                DebugConsole.ThrowError($"Error in {nameof(GameAnalyticsManager)}.{nameof(SendAnswerToRemoteDatabase)}. {authTicket.Platform} authentication ticket was empty.");
                return false;
            }

            IRestResponse response;
            try
            {
                var client = new RestClient(consentServerUrl);

                var request = new RestRequest(consentServerFile, Method.GET);
                request.AddParameter("authticket", authTicket.Token);
                if (consent == Consent.Ask)
                {
                    request.AddParameter("action", "resetconsent");
                }
                else
                {
                    request.AddParameter("action", "setconsent");
                    request.AddParameter("consent", consent == Consent.Yes ? 1 : 0);
                }
                request.AddParameter("request_version", RemoteRequestVersion);
                request.AddParameter("platform", authTicket.Platform);

                response = await client.ExecuteAsync(request, Method.GET);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error while connecting to consent server", e);
                return false;
            }

            if (!CheckResponse(response)) { return false; }

            if (!string.IsNullOrWhiteSpace(response.Content))
            {
                DebugConsole.ThrowError($"Error in GameAnalyticsManager.SetContent. Consent server reported an error: {response.Content.Trim()}");
                return false;
            }
            return true;
        }

        public static void ResetConsent()
        {
            TaskPool.Add(
                "GameAnalyticsConsent.ResetConsentInternal",
                SendAnswerToRemoteDatabase(Consent.Ask),
                t =>
                {
                    if (!t.TryGetResult(out bool success) || !success) { return; }
                    DebugConsole.NewMessage("Reset GameAnalytics consent.");
                });
        }

        static partial void CreateConsentPrompt();

        public static void InitIfConsented()
        {
            #if DEBUG
            return;
            #endif
            
            if (!ConsentTextAvailable)
            {
                SetConsent(Consent.Unknown);
                return;
            }

            if (!SteamManager.IsInitialized && EosInterface.IdQueries.GetLoggedInPuids() is not { Length: > 0 })
            {
                DebugConsole.AddWarning("Error in GameAnalyticsManager.GetConsent: Could not get a Steam or EOS authentication ticket (not connected to Steam or EOS).");
                SetConsent(Consent.Error);
                return;
            }

            TaskPool.Add(
                "GameAnalyticsConsent.RequestAnswerFromRemoteDatabase",
                RequestAnswerFromRemoteDatabase(),
                t =>
                {
                    if (!t.TryGetResult(out Consent consent)) { return; }
                    SetConsentInternal(consent, onAnswerSent: null);
                });
        }

        private static async Task<Consent> RequestAnswerFromRemoteDatabase()
        {
            static void error(string reason, Exception? exception)
            {
                DebugConsole.ThrowError($"Error in {nameof(GameAnalyticsManager)}.{nameof(RequestAnswerFromRemoteDatabase)}: {reason}", exception);
                SetConsent(Consent.Error);
            }
            
            AuthTicket authTicket;
            try
            {
                authTicket = await GetAuthTicket();
            }
            catch (Exception e)
            {
                error("Could not get an authentication ticket.", e);
                return Consent.Error;
            }
            if (authTicket.Platform == Platform.None)
            {
                error($"Could not get an authentication ticket. Not logged in to any platform.", exception: null);
                return Consent.Error;
            }

            RestClient client;
            try
            {
                client = new RestClient(consentServerUrl);
            }
            catch (Exception e)
            {
                error("Error while connecting to consent server.", e);
                return Consent.Error;
            }

            var request = new RestRequest(consentServerFile, Method.GET);
            request.AddParameter("authticket", authTicket.Token);
            request.AddParameter("action", "getconsent");
            request.AddParameter("request_version", RemoteRequestVersion);
            request.AddParameter("platform", authTicket.Platform);

            IRestResponse response;
            try
            {
                response = await client.ExecuteAsync(request);
            }
            catch (Exception e)
            {
                error("Error executing the request to the consent server.", e.GetInnermost());
                return Consent.Error;
            }

            if (!CheckResponse(response))
            {
                return Consent.Error;
            }
            if (string.IsNullOrEmpty(response.Content))
            {
                return Consent.Ask;
            }
            return response.Content[0] == '1'
                ? Consent.Yes
                : Consent.No;
        }

        private static bool CheckResponse(IRestResponse response)
        {
            if (response.ErrorException != null)
            {
                DebugConsole.ThrowErrorLocalized(TextManager.GetWithVariable("MasterServerErrorException", "[error]", response.ErrorException.ToString()));
                return false;
            }

            if (response.StatusCode == HttpStatusCode.OK) { return true; }

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    DebugConsole.ThrowErrorLocalized(TextManager.GetWithVariable("MasterServerError404", "[masterserverurl]", consentServerUrl));
                    break;
                case HttpStatusCode.ServiceUnavailable:
                    DebugConsole.ThrowErrorLocalized(TextManager.Get("MasterServerErrorUnavailable"));
                    break;
                default:
                    DebugConsole.ThrowErrorLocalized(TextManager.GetWithVariables("MasterServerErrorDefault", 
                        ("[statuscode]", response.StatusCode.ToString()),
                        ("[statusdescription]", response.StatusDescription)));
                    break;
            }
            return false;
        }
    }
}