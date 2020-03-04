using System;
using System.Net;
using System.IO;
using System.Text;
using GameAnalyticsSDK.Net.Logging;
using GameAnalyticsSDK.Net.Utilities;
using GameAnalyticsSDK.Net.State;
using GameAnalyticsSDK.Net.Validators;
using System.Collections.Generic;
using GameAnalyticsSDK.Net.Tasks;
#if !WINDOWS_UWP && !WINDOWS_WSA
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
#endif
#if WINDOWS_UWP || WINDOWS_WSA
using System.Threading.Tasks;
using Windows.Networking.Connectivity;
using GameAnalyticsSDK.Net.Device;
#endif

namespace GameAnalyticsSDK.Net.Http
{
    internal class GAHTTPApi
    {
        #region Fields and properties

        private static readonly GAHTTPApi _instance = new GAHTTPApi();

        // base url settings
        private static string protocol = "https";
        private static string hostName = "api.gameanalytics.com";
        private static string version = "v2";
        private static string baseUrl = getBaseUrl();
        private static string initializeUrlPath = "init";
        private static string eventsUrlPath = "events";
        private bool useGzip;

        private static string getBaseUrl()
        {
            return protocol + "://" + hostName + "/" + version;
        }

        public static GAHTTPApi Instance
        {
            get
            {
                return _instance;
            }
        }

        #endregion // Fields and properties

        // Constructor - setup the basic information for HTTP
        private GAHTTPApi()
        {
            this.useGzip = true;
#if DEBUG
            this.useGzip = false;
#endif
#if !WINDOWS_UWP && !WINDOWS_WSA
            ServicePointManager.ServerCertificateValidationCallback = MyRemoteCertificateValidationCallback;
#endif
#if WINDOWS_UWP || WINDOWS_WSA
            NetworkInformation.NetworkStatusChanged += NetworkInformationOnNetworkStatusChanged;
            CheckInternetAccess();
#endif
        }

#if WINDOWS_UWP || WINDOWS_WSA
        private static void NetworkInformationOnNetworkStatusChanged(object sender)
        {
            CheckInternetAccess();
        }

        private static void CheckInternetAccess()
        {
            var connectionProfile = NetworkInformation.GetInternetConnectionProfile();
            bool hasInternetAccess = (connectionProfile != null && connectionProfile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess);

            if (hasInternetAccess)
            {
                if (connectionProfile.IsWlanConnectionProfile)
                {
                    GADevice.ConnectionType = "wifi";
                }
                else if (connectionProfile.IsWwanConnectionProfile)
                {
                    GADevice.ConnectionType = "wwan";
                }
                else
                {
                    GADevice.ConnectionType = "lan";
                }
            }
            else
            {
                GADevice.ConnectionType = "offline";
            }
        }
#endif

#if !WINDOWS_UWP && !WINDOWS_WSA
        private bool MyRemoteCertificateValidationCallback(System.Object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            bool isOk = true;
            // If there are errors in the certificate chain, look at each error to determine the cause.
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                for (int i = 0; i < chain.ChainStatus.Length; i++)
                {
                    if (chain.ChainStatus[i].Status != X509ChainStatusFlags.RevocationStatusUnknown)
                    {
                        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                        chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0);
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
                        bool chainIsValid = chain.Build((X509Certificate2)certificate);
                        if (!chainIsValid)
                        {
                            isOk = false;
                        }
                    }
                }
            }
            return isOk;
        }
#endif

#region Public methods

#if WINDOWS_UWP || WINDOWS_WSA
        public async Task<KeyValuePair<EGAHTTPApiResponse, JSONObject>> RequestInitReturningDict()
#else
        public KeyValuePair<EGAHTTPApiResponse, JSONObject> RequestInitReturningDict()
#endif
        {
            JSONObject json;
            EGAHTTPApiResponse result = EGAHTTPApiResponse.NoResponse;
            string gameKey = GAState.GameKey;

            // Generate URL
            string url = baseUrl + "/" + gameKey + "/" + initializeUrlPath;
            url = "https://rubick.gameanalytics.com/v2/command_center?game_key=" + gameKey + "&interval_seconds=1000000";
            //url = "https://requestb.in/1fvbe2g1";

            GALogger.D("Sending 'init' URL: " + url);

        JSONObject initAnnotations = GAState.GetInitAnnotations();

            // make JSON string from data
            string JSONstring = initAnnotations.ToString();

            if (string.IsNullOrEmpty(JSONstring))
            {
                result = EGAHTTPApiResponse.JsonEncodeFailed;
                json = null;
                return new KeyValuePair<EGAHTTPApiResponse, JSONObject>(result, json);
            }

            string body = "";
            HttpStatusCode responseCode = (HttpStatusCode)0;
            string responseDescription = "";
            string authorization = "";
            try
            {
                byte[] payloadData = CreatePayloadData(JSONstring, false);
                HttpWebRequest request = CreateRequest(url, payloadData, false);
                authorization = request.Headers[HttpRequestHeader.Authorization];
#if WINDOWS_UWP || WINDOWS_WSA
                using (Stream dataStream = await request.GetRequestStreamAsync())
#else
                using(Stream dataStream = request.GetRequestStream())
#endif
                {
                    dataStream.Write(payloadData, 0, payloadData.Length);
                }

#if WINDOWS_UWP || WINDOWS_WSA
                using (HttpWebResponse response = await request.GetResponseAsync() as HttpWebResponse)
#else
                using(HttpWebResponse response = request.GetResponse() as HttpWebResponse)
#endif
                {
                    using (Stream dataStream = response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(dataStream))
                        {
                            string responseString = reader.ReadToEnd();

                            responseCode = response.StatusCode;
                            responseDescription = response.StatusDescription;

                            // print result
                            body = responseString;
                        }
                    }
                }
            }
            catch (WebException e)
            {
                if(e.Response != null)
                {
                    using (HttpWebResponse response = (HttpWebResponse)e.Response)
                    {
                        using (Stream streamResponse = response.GetResponseStream())
                        {
                            using (StreamReader streamRead = new StreamReader(streamResponse))
                            {
                                string responseString = streamRead.ReadToEnd();

                                responseCode = response.StatusCode;
                                responseDescription = response.StatusDescription;

                                body = responseString;
                            }
                        }

                    }

                }
            }
            catch(Exception e)
            {
                GALogger.E(e.ToString());
            }

            // process the response
            GALogger.D("init request content : " + body);

            JSONNode requestJsonDict = JSON.Parse(body);
            EGAHTTPApiResponse requestResponseEnum = ProcessRequestResponse(responseCode, responseDescription, body, "Init");

            // if not 200 result
            if (requestResponseEnum != EGAHTTPApiResponse.Ok && requestResponseEnum != EGAHTTPApiResponse.BadRequest)
            {
                GALogger.D("Failed Init Call. URL: " + url + ", Authorization: " + authorization + ", JSONString: " + JSONstring);
                result = requestResponseEnum;
                json = null;
                return new KeyValuePair<EGAHTTPApiResponse, JSONObject>(result, json);
            }

            if (requestJsonDict == null)
            {
                GALogger.D("Failed Init Call. Json decoding failed");
                result = EGAHTTPApiResponse.JsonDecodeFailed;
                json = null;
                return new KeyValuePair<EGAHTTPApiResponse, JSONObject>(result, json);
            }

            // print reason if bad request
            if (requestResponseEnum == EGAHTTPApiResponse.BadRequest)
            {
                GALogger.D("Failed Init Call. Bad request. Response: " + requestJsonDict.ToString());
                // return bad request result
                result = requestResponseEnum;
                json = null;
                return new KeyValuePair<EGAHTTPApiResponse, JSONObject>(result, json);
            }

            // validate Init call values
            JSONObject validatedInitValues = GAValidator.ValidateAndCleanInitRequestResponse(requestJsonDict);

            if (validatedInitValues == null)
            {
                result = EGAHTTPApiResponse.BadResponse;
                json = null;
                return new KeyValuePair<EGAHTTPApiResponse, JSONObject>(result, json);
            }

            // all ok
            result = EGAHTTPApiResponse.Ok;
            json = validatedInitValues;
            return new KeyValuePair<EGAHTTPApiResponse, JSONObject>(result, json);
        }

#if WINDOWS_UWP || WINDOWS_WSA
        public async Task<KeyValuePair<EGAHTTPApiResponse, JSONNode>> SendEventsInArray(List<JSONNode> eventArray)
#else
        public KeyValuePair<EGAHTTPApiResponse, JSONNode> SendEventsInArray(List<JSONNode> eventArray)
#endif
        {
            JSONNode json;

            if (eventArray.Count == 0)
            {
                GALogger.D("sendEventsInArray called with missing eventArray");
            }

            EGAHTTPApiResponse result = EGAHTTPApiResponse.NoResponse;
            string gameKey = GAState.GameKey;

            // Generate URL
            string url = baseUrl + "/" + gameKey + "/" + eventsUrlPath;
            GALogger.D("Sending 'events' URL: " + url);

            // make JSON string from data
            string JSONstring = GAUtilities.ArrayOfObjectsToJsonString(eventArray);

            if (JSONstring.Length == 0)
            {
                GALogger.D("sendEventsInArray JSON encoding failed of eventArray");
                json = null;
                result = EGAHTTPApiResponse.JsonEncodeFailed;
                return new KeyValuePair<EGAHTTPApiResponse, JSONNode>(result, json);
            }

            string body = "";
            HttpStatusCode responseCode = (HttpStatusCode)0;
            string responseDescription = "";
            string authorization = "";
            try
            {
                byte[] payloadData = CreatePayloadData(JSONstring, useGzip);
                HttpWebRequest request = CreateRequest(url, payloadData, useGzip);
                authorization = request.Headers[HttpRequestHeader.Authorization];
#if WINDOWS_UWP || WINDOWS_WSA
                using (Stream dataStream = await request.GetRequestStreamAsync())
#else
                using(Stream dataStream = request.GetRequestStream())
#endif
                {
                    dataStream.Write(payloadData, 0, payloadData.Length);
                }

#if WINDOWS_UWP || WINDOWS_WSA
                using (HttpWebResponse response = await request.GetResponseAsync() as HttpWebResponse)
#else
                using(HttpWebResponse response = request.GetResponse() as HttpWebResponse)
#endif
                {
                    using (Stream dataStream = response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(dataStream))
                        {
                            string responseString = reader.ReadToEnd();

                            responseCode = response.StatusCode;
                            responseDescription = response.StatusDescription;

                            // print result
                            body = responseString;
                        }
                    }
                }
            }
            catch (WebException e)
            {
                if(e.Response != null)
                {
                    using (HttpWebResponse response = (HttpWebResponse)e.Response)
                    {
                        using (Stream streamResponse = response.GetResponseStream())
                        {
                            using (StreamReader streamRead = new StreamReader(streamResponse))
                            {
                                string responseString = streamRead.ReadToEnd();

                                responseCode = response.StatusCode;
                                responseDescription = response.StatusDescription;

                                body = responseString;
                            }
                        }
                    }
                }
            }
            catch(Exception e)
            {
                GALogger.E(e.ToString());
            }

            GALogger.D("events request content: " + body);

            EGAHTTPApiResponse requestResponseEnum = ProcessRequestResponse(responseCode, responseDescription, body, "Events");

            // if not 200 result
            if (requestResponseEnum != EGAHTTPApiResponse.Ok && requestResponseEnum != EGAHTTPApiResponse.BadRequest)
            {
                GALogger.D("Failed events Call. URL: " + url + ", Authorization: " + authorization + ", JSONString: " + JSONstring);
                json = null;
                result = requestResponseEnum;
                return new KeyValuePair<EGAHTTPApiResponse, JSONNode>(result, json);
            }

            // decode JSON
            JSONNode requestJsonDict = JSON.Parse(body);

            if (requestJsonDict == null)
            {
                json = null;
                result = EGAHTTPApiResponse.JsonDecodeFailed;
                return new KeyValuePair<EGAHTTPApiResponse, JSONNode>(result, json);
            }

            // print reason if bad request
            if (requestResponseEnum == EGAHTTPApiResponse.BadRequest)
            {
                GALogger.D("Failed Events Call. Bad request. Response: " + requestJsonDict.ToString());
            }

            // return response
            json = requestJsonDict;
            result = requestResponseEnum;
            return new KeyValuePair<EGAHTTPApiResponse, JSONNode>(result, json);
        }

        public void SendSdkErrorEvent(EGASdkErrorType type)
        {
            if(!GAState.IsEventSubmissionEnabled)
            {
                return;
            }
            
            string gameKey = GAState.GameKey;
            string secretKey = GAState.GameSecret;

            // Validate
            if (!GAValidator.ValidateSdkErrorEvent(gameKey, secretKey, type))
            {
                return;
            }

            // Generate URL
            string url = baseUrl + "/" + gameKey + "/" + eventsUrlPath;
            GALogger.D("Sending 'events' URL: " + url);

            string payloadJSONString = "";

            JSONObject json = GAState.GetSdkErrorEventAnnotations();

            string typeString = SdkErrorTypeToString(type);
            json.Add("type", typeString);

            List<JSONNode> eventArray = new List<JSONNode>();
            eventArray.Add(json);
            payloadJSONString = GAUtilities.ArrayOfObjectsToJsonString(eventArray);

            if(string.IsNullOrEmpty(payloadJSONString))
            {
                GALogger.W("sendSdkErrorEvent: JSON encoding failed.");
                return;
            }

            GALogger.D("sendSdkErrorEvent json: " + payloadJSONString);
            byte[] payloadData = Encoding.UTF8.GetBytes(payloadJSONString);
            SdkErrorTask sdkErrorTask = new SdkErrorTask(type, payloadData, secretKey);
            sdkErrorTask.Execute(url);
        }

#endregion // Public methods

#region Private methods

        private byte[] CreatePayloadData(string payload, bool gzip)
        {
            byte[] payloadData;

            if(gzip)
            {
                payloadData = GAUtilities.GzipCompress(payload);
                GALogger.D("Gzip stats. Size: " + Encoding.UTF8.GetBytes(payload).Length + ", Compressed: " + payloadData.Length + ", Content: " + payload);
            }
            else
            {
                payloadData = Encoding.UTF8.GetBytes(payload);
            }

            return payloadData;
        }

        private static string SdkErrorTypeToString(EGASdkErrorType value)
        {
            switch(value)
            {
                case EGASdkErrorType.Rejected:
                    {
                        return "rejected";
                    }

                default:
                    {
                        return "";
                    }
            }
        }

        private HttpWebRequest CreateRequest(string url, byte[] payloadData, bool gzip)
        {
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            request.Method = "POST";
#if WINDOWS_UWP
            request.Headers[HttpRequestHeader.ContentLength] = payloadData.Length.ToString();
#elif WINDOWS_WSA
            //request.Headers[HttpRequestHeader.ContentLength] = payloadData.Length.ToString();
            // Bug setting Content Length on WSA
#else
            request.ContentLength = payloadData.Length;
#endif

            if (gzip)
            {
                request.Headers[HttpRequestHeader.ContentEncoding] = "gzip";
            }

            // create authorization hash
            String key = GAState.GameSecret;

            request.Headers[HttpRequestHeader.Authorization] = GAUtilities.HmacWithKey(key, payloadData);
            request.ContentType = "application/json";

            return request;
        }

        private EGAHTTPApiResponse ProcessRequestResponse(HttpStatusCode responseCode, string responseMessage, string body, string requestId)
        {
            // if no result - often no connection
            if(string.IsNullOrEmpty(body))
            {
                GALogger.D(requestId + " request. failed. Might be no connection. Description: " + responseMessage + ", Status code: " + responseCode);
                return EGAHTTPApiResponse.NoResponse;
            }

            // ok
            if (responseCode == HttpStatusCode.OK)
            {
                return EGAHTTPApiResponse.Ok;
            }

            // 401 can return 0 status
            if (responseCode == (HttpStatusCode)0 || responseCode == HttpStatusCode.Unauthorized)
            {
                GALogger.D(requestId + " request. 401 - Unauthorized.");
                return EGAHTTPApiResponse.Unauthorized;
            }

            if (responseCode == HttpStatusCode.BadRequest)
            {
                GALogger.D(requestId + " request. 400 - Bad Request.");
                return EGAHTTPApiResponse.BadRequest;
            }

            if (responseCode == HttpStatusCode.InternalServerError)
            {
                GALogger.D(requestId + " request. 500 - Internal Server Error.");
                return EGAHTTPApiResponse.InternalServerError;
            }

            return EGAHTTPApiResponse.UnknownResponseCode;
        }

#endregion // Private methods
        }
    }
