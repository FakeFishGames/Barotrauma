using System;
using GameAnalyticsSDK.Net.Http;
using System.Collections.Generic;
using GameAnalyticsSDK.Net.Utilities;
using System.Net;
using System.IO;
using GameAnalyticsSDK.Net.Logging;
using Foundation.Tasks;
#if WINDOWS_UWP || WINDOWS_WSA
using System.Threading.Tasks;
#endif

namespace GameAnalyticsSDK.Net.Tasks
{
	internal class SdkErrorTask
	{
		protected EGASdkErrorType type;
		protected byte[] payloadData;
		protected string hashHmac;
		protected string body = "";
		private const int MaxCount = 10;
		private static Dictionary<EGASdkErrorType, int> countMap = new Dictionary<EGASdkErrorType, int>();

		public SdkErrorTask(EGASdkErrorType type, byte[] payloadData, string secretKey)
		{
			this.type = type;
			this.payloadData = payloadData;
			this.hashHmac = GAUtilities.HmacWithKey(secretKey, payloadData);
		}

		public void Execute(string url)
		{
            AsyncTask.Run(() =>
			{
#if WINDOWS_UWP || WINDOWS_WSA
                DoInBackground(url).Wait();
#else
                DoInBackground(url);
#endif
            });
        }

#if WINDOWS_UWP || WINDOWS_WSA
        protected async Task DoInBackground(string url)
#else
        protected void DoInBackground(string url)
#endif
        {
			if(!countMap.ContainsKey(this.type))
			{
				countMap.Add(this.type, 0);
			}

			if(countMap[this.type] >= MaxCount)
			{
				return;
			}

			HttpStatusCode responseCode = (HttpStatusCode)0;
			string responseDescription = "";

			try
			{
				HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
				request.Method = "POST";
#if WINDOWS_UWP || WINDOWS_WSA
                request.Headers[HttpRequestHeader.ContentLength] = this.payloadData.Length.ToString();
#else
                request.ContentLength = payloadData.Length;
#endif
                request.Headers[HttpRequestHeader.Authorization] = this.hashHmac;
				request.ContentType = "application/json";

#if WINDOWS_UWP || WINDOWS_WSA
                using (Stream dataStream = await request.GetRequestStreamAsync())
#else
                using (Stream dataStream = request.GetRequestStream())
#endif
                {
                    dataStream.Write(this.payloadData, 0, payloadData.Length);
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
			GALogger.D("sdk error request content : " + body);

			OnPostExecute(responseCode, responseDescription);
		}

		protected void OnPostExecute(HttpStatusCode responseCode, string responseDescription)
		{

			if(string.IsNullOrEmpty(this.body))
			{
				GALogger.D("sdk error failed. Might be no connection. Description: " + responseDescription + ", Status code: " + responseCode);
				return;
			}

			if(responseCode != HttpStatusCode.OK)
			{
				GALogger.W("sdk error failed. response code not 200. status code: " + responseCode + ", description: " + responseDescription + ", body: " + this.body);
				return;
			}
			else
			{
				countMap[this.type] = countMap[this.type] + 1;
			}
		}
	}
}

