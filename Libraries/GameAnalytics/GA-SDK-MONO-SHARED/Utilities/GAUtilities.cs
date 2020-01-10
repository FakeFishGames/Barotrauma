using System;
using System.Text.RegularExpressions;
using System.Text;
using System.Collections.Generic;
#if UNITY
using GameAnalyticsSDK.Net.Utilities.Zip.GZip;
#else
using System.IO.Compression;
#endif
#if WINDOWS_UWP || WINDOWS_WSA
using Windows.Security.Cryptography.Core;
using Windows.Security.Cryptography;
using System.Runtime.InteropServices.WindowsRuntime;
#else
using System.Security.Cryptography;
#endif
using System.IO;

namespace GameAnalyticsSDK.Net.Utilities
{
	internal static class GAUtilities
	{
		private static readonly DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

		public static byte[] GzipCompress(string data)
		{
			if (string.IsNullOrEmpty(data))
			{
				return new byte[0];
			}

			byte[] result;

#if !UNITY
            using (MemoryStream msi = new MemoryStream(Encoding.UTF8.GetBytes(data)))
            {
                using (MemoryStream mso = new MemoryStream())
                {
                    using (GZipStream gs = new GZipStream(mso, CompressionMode.Compress))
                    {
                        msi.CopyTo(gs);
                    }

                    result = mso.ToArray();
                }
            }
#else
            using (MemoryStream outStream = new MemoryStream())
			{
				using (GZipOutputStream tinyStream = new GZipOutputStream(outStream))
				using (MemoryStream mStream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
				{
					mStream.CopyTo(tinyStream);
				}

				result = outStream.ToArray();
			}
#endif

                return result;
		}

		public static string HmacWithKey(string key, byte[] data)
		{
            byte[] keyByte = Encoding.UTF8.GetBytes(key);

#if WINDOWS_UWP || WINDOWS_WSA
            var hmacsha256 = MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha256);
#else
            using (var hmacsha256 = new HMACSHA256(keyByte))
#endif
            {
#if WINDOWS_UWP || WINDOWS_WSA
                var input = data.AsBuffer();
                var signatureKey = hmacsha256.CreateKey(keyByte.AsBuffer());
                var cypherMac = CryptographicEngine.Sign(signatureKey, input);
                return CryptographicBuffer.EncodeToBase64String(cypherMac);
#else
                byte[] hashmessage = hmacsha256.ComputeHash(data);
                return Convert.ToBase64String(hashmessage);
#endif
            }
		}

		public static bool StringMatch(string s, string pattern)
		{
			if(s == null || pattern == null)
			{
				return false;
			}

			return Regex.IsMatch(s, pattern);
		}

		public static string JoinStringArray(string[] v, string delimiter)
		{
			StringBuilder sbStr = new StringBuilder();
			for (int i = 0, il = v.Length; i < il; i++)
			{
				if (i > 0)
				{
					sbStr.Append(delimiter);
				}
				sbStr.Append(v[i]);
			}
			return sbStr.ToString();
		}

		public static bool StringArrayContainsString(string[] array, string search)
		{
			if (array.Length == 0)
			{
				return false;
			}

			foreach(string s in array)
			{
				if(s.Equals(search))
				{
					return true;
				}
			}
			return false;
		}

		public static long TimeIntervalSince1970()
		{
			TimeSpan interval = DateTime.Now.ToUniversalTime() - origin;
			return (long)interval.TotalSeconds;
		}

		public static string ArrayOfObjectsToJsonString(List<JSONNode> arr)
		{
			JSONArray json_array = new JSONArray();
			foreach (JSONNode x in arr)
			{
				json_array.Add(x);
			}
			return json_array.ToString();
		}

		public static void CopyTo(this Stream input, Stream output)
		{
			byte[] buffer = new byte[16 * 1024]; // Fairly arbitrary size
			int bytesRead;

			while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
			{
				output.Write(buffer, 0, bytesRead);
			}
		}
	}
}

