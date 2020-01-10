using System;
#if UNITY
using UnityEngine;
using GameAnalyticsSDK.Net.Store;
#endif
using System.IO;
using System.Text.RegularExpressions;
using GameAnalyticsSDK.Net.Logging;
#if WINDOWS_UWP || WINDOWS_WSA
using Windows.System.Profile;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage;
using Windows.System.UserProfile;
using Windows.Foundation.Metadata;
using Windows.Storage.Streams;
#endif

namespace GameAnalyticsSDK.Net.Device
{
	internal static class GADevice
	{
#if WINDOWS_UWP
        private const string _sdkWrapperVersion = "uwp 2.1.7";
#elif WINDOWS_WSA
        private const string _sdkWrapperVersion = "wsa 2.1.7";
#else
        private const string _sdkWrapperVersion = "mono 2.1.7";
#endif
#if UNITY
		private static readonly string _buildPlatform = UnityRuntimePlatformToString(Application.platform);
		private static readonly string _deviceModel = SystemInfo.deviceType.ToString().ToLowerInvariant();
		private static string _writablepath = GAStore.InMemory ? "" : GetPersistentPath();
#else
        private static readonly string _buildPlatform = RuntimePlatformToString();
#if WINDOWS_UWP || WINDOWS_WSA
        private static readonly string _deviceModel = GetDeviceModel();
        private static readonly string _advertisingId = AdvertisingManager.AdvertisingId;
        private static string _deviceId = GetDeviceId();
#else
        private static readonly string _deviceModel = "unknown";
#endif
        private static string _writablepath = GetPersistentPath();
#endif
        private static readonly string _osVersion = GetOSVersionString();
#if WINDOWS_UWP || WINDOWS_WSA
        private static readonly string _deviceManufacturer = GetDeviceManufacturer();
#else
        private static readonly string _deviceManufacturer = "unknown";
#endif

        public static void Touch()
		{
		}

		public static string SdkGameEngineVersion
		{
			private get;
			set;
		}

		public static string GameEngineVersion
		{
			get;
			set;
		}

		public static string ConnectionType
		{
			get;
			set;
		}

		public static string RelevantSdkVersion
		{
			get
			{
				if(!string.IsNullOrEmpty(SdkGameEngineVersion))
				{
					return SdkGameEngineVersion;
				}
				return _sdkWrapperVersion;
			}
		}

		public static string BuildPlatform
		{
			get
			{
				return _buildPlatform;
			}
		}

		public static string OSVersion
		{
			get
			{
				return _osVersion;
			}
		}

		public static string DeviceModel
		{
			get
			{
				return _deviceModel;
			}
		}

		public static string DeviceManufacturer
		{
			get
			{
				return _deviceManufacturer;
			}
		}

		public static string WritablePath
		{
			get
			{
				return _writablepath;
			}
		}

#if WINDOWS_UWP
        public static string AdvertisingId
        {
            get
            {
                return _advertisingId;
            }
        }

        public static string DeviceId
        {
            get
            {
                return _deviceId;
            }
        }
#endif

#if UNITY
		public static void UpdateConnectionType()
		{
			switch(Application.internetReachability)
			{
				case NetworkReachability.ReachableViaCarrierDataNetwork:
					{
						ConnectionType = "wwan";
					}
					break;

				case NetworkReachability.ReachableViaLocalAreaNetwork:
					{
						ConnectionType = "lan";
					}
					break;

				default:
					{
						ConnectionType = "offline";
					}
					break;
			}
		}

		private static string GetOSVersionString()
		{
			string osVersion = SystemInfo.operatingSystem;

			GALogger.D("GetOSVersionString: " + osVersion);

			// Capture and process OS version information
			// For Windows
			Match regexResult = Regex.Match(osVersion, @"Windows.*?\((\d{0,5}\.\d{0,5}\.(\d{0,5}))\)");
			if(regexResult.Success)
			{
				string versionNumberString = regexResult.Groups[1].Value;
				string buildNumberString = regexResult.Groups[2].Value;
				// Fix a bug in older versions of Unity where Windows 10 isn't recognised properly
				int buildNumber = 0;
				Int32.TryParse(buildNumberString, out buildNumber);
				if(buildNumber > 10000)
				{
					versionNumberString = "10.0." + buildNumberString;
				}
				return "windows " + versionNumberString;
			}
			// For OS X
			regexResult = Regex.Match(osVersion, @"Mac OS X (\d{0,5}\.\d{0,5}\.\d{0,5})");
			if(regexResult.Success)
			{
				return "mac_osx " + regexResult.Captures[0].Value.Replace("Mac OS X ", "");
			}
			regexResult = Regex.Match(osVersion, @"Mac OS X (\d{0,5}_\d{0,5}_\d{0,5})");
			if(regexResult.Success)
			{
				return "mac_osx " + regexResult.Captures[0].Value.Replace("Mac OS X ", "").Replace("_", ".");
			}
			// Not supporting other OS yet. The default version string won't be accepted by GameAnalytics
			return UnityRuntimePlatformToString(Application.platform) + " 0.0.0";
		}

        private static string GetPersistentPath()
        {
            string result = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + Path.DirectorySeparatorChar + "GameAnalytics" + Path.DirectorySeparatorChar + System.AppDomain.CurrentDomain.FriendlyName;

            if (!Directory.Exists(result))
            {
                Directory.CreateDirectory(result);
            }

            return result;
        }

        private static string UnityRuntimePlatformToString(RuntimePlatform platform)
        {
            switch(platform)
            {
                case RuntimePlatform.LinuxPlayer:
                    {
                        return "linux";
                    }

                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXDashboardPlayer:
                    {
                        return "mac_osx";
                    }

                case RuntimePlatform.PS3:
                    {
                        return "ps3";
                    }

                case RuntimePlatform.PS4:
                    {
                        return "ps4";
                    }

                case RuntimePlatform.PSP2:
                    {
                        return "vita";
                    }

                case RuntimePlatform.WindowsPlayer:
                    {
                        return "windows";
                    }

#if UNITY_5
				case RuntimePlatform.PSM:
					{
						return "psm";
					}

				case RuntimePlatform.WiiU:
					{
						return "wiiu";
					}

				case RuntimePlatform.WebGLPlayer:
					{
						return "webgl";
					}

				case RuntimePlatform.WSAPlayerARM:
				case RuntimePlatform.WSAPlayerX64:
				case RuntimePlatform.WSAPlayerX86:
					{
						switch(SystemInfo.deviceType)
						{
							case DeviceType.Desktop:
								{
									return "uwp_desktop";
								}

							case DeviceType.Handheld:
								{
									return "uwp_mobile";
								}

							case DeviceType.Console:
								{
									return "uwp_console";
								}

							default:
								{
									return "uwp_desktop";
								}
						}
					}
#endif

                case RuntimePlatform.WP8Player:
                    {
                        return "windows_phone";
                    }

                case RuntimePlatform.XBOX360:
                    {
                        return "xbox360";
                    }

                case RuntimePlatform.XboxOne:
                    {
                        return "xboxone";
                    }

                case RuntimePlatform.TizenPlayer:
                    {
                        return "tizen";
                    }

                case RuntimePlatform.SamsungTVPlayer:
                    {
                        return "samsung_tv";
                    }

                default:
                    {
                        return "unknown";
                    }
            }
        }
#else
        public static void UpdateConnectionType()
        {
            ConnectionType = "lan";
        }

		private static string GetOSVersionString()
		{
#if WINDOWS_UWP
            string deviceFamilyVersion = AnalyticsInfo.VersionInfo.DeviceFamilyVersion;
            ulong version = ulong.Parse(deviceFamilyVersion);
            ulong major = (version & 0xFFFF000000000000L) >> 48;
            ulong minor = (version & 0x0000FFFF00000000L) >> 32;
            ulong build = (version & 0x00000000FFFF0000L) >> 16;
            return BuildPlatform + string.Format(" {0}.{1}.{2}", major, minor, build);
#elif WINDOWS_WSA
            // Always 8.1 on Universal Windows 8.1
            return BuildPlatform + " 8";
#else
            Version v = Environment.OSVersion.Version;
			return BuildPlatform + string.Format(" {0}.{1}.{2}", v.Major, v.Minor, v.Build);
#endif
		}

#if WINDOWS_UWP || WINDOWS_WSA
        private static string GetDeviceId()
        {
            string result = "";

#if WINDOWS_UWP
            if(ApiInformation.IsTypePresent("Windows.System.Profile.HardwareIdentification"))
#endif
            {
                var token = HardwareIdentification.GetPackageSpecificToken(null);
                var hardwareId = token.Id;
                var dataReader = DataReader.FromBuffer(hardwareId);

                byte[] bytes = new byte[hardwareId.Length];
                dataReader.ReadBytes(bytes);

                result = BitConverter.ToString(bytes).Replace("-", "");
            }

            return result;
        }

        private static string GetDeviceManufacturer()
        {
            EasClientDeviceInformation eas = new EasClientDeviceInformation();
            return eas.SystemManufacturer;
        }

        private static string GetDeviceModel()
        {
            EasClientDeviceInformation eas = new EasClientDeviceInformation();
            return eas.SystemProductName;
        }

#if WINDOWS_UWP
        private static string RuntimePlatformToString()
        {
            switch(AnalyticsInfo.VersionInfo.DeviceFamily)
            {
                case "Windows.Mobile":
                    {
                        return "uwp_mobile";
                    }

                case "Windows.Desktop":
                    {
                        return "uwp_desktop";
                    }

                case "Windows.Universal":
                    {
                        return "uwp_iot";
                    }

                case "Windows.Xbox":
                    {
                        return "uwp_console";
                    }

                case "Windows.Team":
                    {
                        return "uwp_surfacehub";
                    }

                case "Windows.Holographic":
                    {
                        return "uwp_holographic";
                    }

                default:
                    {
                        return AnalyticsInfo.VersionInfo.DeviceFamily;
                    }
            }
        }
#else
        private static string RuntimePlatformToString()
        {
            EasClientDeviceInformation eas = new EasClientDeviceInformation();
            switch(eas.OperatingSystem.ToUpperInvariant())
            {
                case "WINDOWSPHONE":
                    {
                        return "windows_phone";
                    }

                case "WINDOWS":
                    {
                        return "windows";
                    }

                default:
                    {
                        return eas.OperatingSystem;
                    }
            }
        }
#endif
#else
        private static string RuntimePlatformToString()
		{
			switch(Environment.OSVersion.Platform)
			{
				case PlatformID.Unix:
					{
						// Well, there are chances MacOSX is reported as Unix instead of MacOSX.
						// Instead of platform check, we'll do a feature checks (Mac specific root folders)
						if(Directory.Exists("/Applications") && Directory.Exists("/System") && Directory.Exists("/Users") && Directory.Exists("/Volumes"))
						{
							return "mac_osx";
						}
						else
						{
							return "linux";
						}
					}

				case PlatformID.MacOSX:
					{
						return "mac_osx";
					}

				case PlatformID.Win32NT:
				case PlatformID.Win32S:
				case PlatformID.Win32Windows:
				case PlatformID.WinCE:
					{
						return "windows";
					}

				case PlatformID.Xbox:
					{
						return "xbox360";
					}

				default:
					{
						return "unknown";
					}
			}
		}
#endif

        private static string GetPersistentPath()
		{
#if WINDOWS_UWP || WINDOWS_WSA
            System.Threading.Tasks.Task<StorageFolder> gaFolderTask = System.Threading.Tasks.Task.Run<StorageFolder>(async() => await ApplicationData.Current.LocalFolder.CreateFolderAsync("GameAnalytics", CreationCollisionOption.OpenIfExists));
            return gaFolderTask.GetAwaiter().GetResult().Path;
#else
            string result = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + Path.DirectorySeparatorChar + "GameAnalytics" + Path.DirectorySeparatorChar + System.AppDomain.CurrentDomain.FriendlyName;

            if(!Directory.Exists(result))
            {
                Directory.CreateDirectory(result);
            }

            return result;
#endif
        }

#endif
    }
}
