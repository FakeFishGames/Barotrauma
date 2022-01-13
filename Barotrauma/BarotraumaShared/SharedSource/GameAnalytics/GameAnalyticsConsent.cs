using System;
using Barotrauma.Steam;
using RestSharp;
using System.Net;
using System.Threading.Tasks;

namespace Barotrauma
{
    public static partial class GameAnalyticsManager
    {
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

        private readonly static string consentServerUrl = "https://barotraumagame.com/baromaster/";
        private readonly static string consentServerFile = "consentserver.php";

        private static string GetAuthTicket()
        {
            Steamworks.AuthTicket authTicket = SteamManager.GetAuthSessionTicket();
            //convert byte array to hex
            return BitConverter.ToString(authTicket.Data).Replace("-", "");
        }

        /// <summary>
        /// Sets the consent status. This method cannot be called to
        /// set the status to Consent.Yes; only a positive response from
        /// the database or the user accepting via the privacy policy
        /// prompt should enable it.
        /// </summary>
        public static void SetConsent(Consent consent)
        {
            if (consent == Consent.Yes)
            {
                throw new Exception(
                    "Cannot call SetConsent with value Consent.Yes, must only be set to this value via consent prompt");
            }
            SetConsentInternal(consent);
        }
        
        /// <summary>
        /// Implementation of the bulk of SetConsent.
        /// DO NOT CALL THIS UNLESS NEEDED.
        /// </summary>
        private static void SetConsentInternal(Consent consent)
        {
            if (UserConsented == consent) { return; }

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

            string authTicketStr;
            try
            {
                authTicketStr = GetAuthTicket();
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in GameAnalyticsManager.SetConsent. Could not get a Steam authentication ticket.", e);
                return;
            }

            RestClient client = null;
            try
            {
                client = new RestClient(consentServerUrl);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error while connecting to consent server", e);
            }
            if (client == null) { return; }

            var request = new RestRequest(consentServerFile, Method.GET);
            request.AddParameter("authticket", authTicketStr);
            request.AddParameter("action", "setconsent");
            request.AddParameter("consent", consent == Consent.Yes ? 1 : 0);

            var response = client.Execute(request, Method.GET);
            if (CheckResponse(response))
            {
                UserConsented = consent;
                if (consent == Consent.Yes)
                {
                    Init();
                }
            }
        }

        static partial void CreateConsentPrompt();

        public static void InitIfConsented()
        {
            if (!consentTextAvailable)
            {
                SetConsent(Consent.Unknown);
                return;
            }

            static void error(string reason, Exception exception)
            {
                DebugConsole.ThrowError($"Error in GameAnalyticsManager.GetConsent: {reason}", exception);
                SetConsent(Consent.Error);
            }

            if (!SteamManager.IsInitialized)
            {
                DebugConsole.AddWarning("Error in GameAnalyticsManager.GetConsent: Could not get a Steam authentication ticket (not connected to Steam).");
                SetConsent(Consent.Error);
                return;
            }

            string authTicketStr;
            try
            {
                authTicketStr = GetAuthTicket();
            }
            catch (Exception e)
            {
                error("Could not get a Steam authentication ticket.", e);
                return;
            }

            RestClient client;
            try
            {
                client = new RestClient(consentServerUrl);
            }
            catch (Exception e)
            {
                error("Error while connecting to consent server.", e);
                return;
            }

            var request = new RestRequest(consentServerFile, Method.GET);
            request.AddParameter("authticket", authTicketStr);
            request.AddParameter("action", "getconsent");

            TaskPool.Add($"{nameof(GameAnalyticsManager)}.{nameof(InitIfConsented)}", client.ExecuteAsync(request), (t) =>
            {
                if (t.Exception != null)
                {
                    error("Error executing the request to the consent server.", t.Exception.InnerException);
                    return;
                }

                if (!t.TryGetResult(out IRestResponse response)) { return; }
                if (!CheckResponse(response))
                {
                    SetConsent(Consent.Error);
                }
                else if (string.IsNullOrEmpty(response.Content))
                {
                    SetConsent(Consent.Ask);
                }
                else
                {
                    SetConsentInternal(response.Content[0] == '1'
                        ? Consent.Yes
                        : Consent.No);
                }
            });
        }

        private static bool CheckResponse(IRestResponse response)
        {
            if (response.ErrorException != null)
            {
                DebugConsole.ThrowError(TextManager.GetWithVariable("MasterServerErrorException", "[error]", response.ErrorException.ToString()));
                return false;
            }
            else if (response.StatusCode != HttpStatusCode.OK)
            {
                switch (response.StatusCode)
                {
                    case HttpStatusCode.NotFound:
                        DebugConsole.ThrowError(TextManager.GetWithVariable("MasterServerError404", "[masterserverurl]", consentServerUrl));
                        break;
                    case HttpStatusCode.ServiceUnavailable:
                        DebugConsole.ThrowError(TextManager.Get("MasterServerErrorUnavailable"));
                        break;
                    default:
                        DebugConsole.ThrowError(TextManager.GetWithVariables("MasterServerErrorDefault", new string[2] { "[statuscode]", "[statusdescription]" },
                            new string[2] { response.StatusCode.ToString(), response.StatusDescription }));
                        break;
                }
            }
            return response.StatusCode == HttpStatusCode.OK;
        }
    }
}