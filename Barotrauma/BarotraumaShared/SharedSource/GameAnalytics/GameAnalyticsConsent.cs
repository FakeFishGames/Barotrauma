#nullable enable
using Barotrauma.Steam;
using RestSharp;
using System;
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
        private const string RemoteRequestVersion = "2";

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

        private static bool consentTextAvailable
            => TextManager.ContainsTag("statisticsconsentheader")
                && TextManager.ContainsTag("statisticsconsenttext");
 
        private const string consentServerUrl = "https://barotraumagame.com/baromaster/";
        private const string consentServerFile = "consentserver.php";

        private static async Task<string> GetAuthTicket()
        {
            var ticketOption = await SteamManager.GetAuthTicketForGameAnalyticsConsent();
            if (!ticketOption.TryUnwrap(out var authTicket) || authTicket.Data is null) { return ""; }
            //convert byte array to hex
            return BitConverter.ToString(authTicket.Data).Replace("-", "");
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
                CreateConsentPrompt();
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
            string authTicketStr;
            try
            {
                authTicketStr = await GetAuthTicket();
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in GameAnalyticsManager.SetConsent. Could not get a Steam authentication ticket.", e);
                return false;
            }

            if (string.IsNullOrEmpty(authTicketStr))
            {
                DebugConsole.ThrowError("Error in GameAnalyticsManager.SetContent. Steam authentication ticket was empty.");
                return false;
            }

            IRestResponse response;
            try
            {
                var client = new RestClient(consentServerUrl);

                var request = new RestRequest(consentServerFile, Method.GET);
                request.AddParameter("authticket", authTicketStr);
                request.AddParameter("action", "setconsent");
                request.AddParameter("consent", consent == Consent.Yes ? 1 : 0);
                request.AddParameter("request_version", RemoteRequestVersion);

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

        static partial void CreateConsentPrompt();

        public static void InitIfConsented()
        {
            #if DEBUG
            return;
            #endif
            
            if (!consentTextAvailable)
            {
                SetConsent(Consent.Unknown);
                return;
            }

            if (!SteamManager.IsInitialized)
            {
                DebugConsole.AddWarning("Error in GameAnalyticsManager.GetConsent: Could not get a Steam authentication ticket (not connected to Steam).");
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
            static void error(string reason, Exception exception)
            {
                DebugConsole.ThrowError($"Error in GameAnalyticsManager.GetConsent: {reason}", exception);
                SetConsent(Consent.Error);
            }
            
            string authTicketStr;
            try
            {
                authTicketStr = await GetAuthTicket();
            }
            catch (Exception e)
            {
                error("Could not get a Steam authentication ticket.", e);
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
            request.AddParameter("authticket", authTicketStr);
            request.AddParameter("action", "getconsent");
            request.AddParameter("request_version", RemoteRequestVersion);

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