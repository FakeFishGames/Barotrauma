using System;
#if WINDOWS_UWP || WINDOWS_WSA
using Windows.Foundation.Diagnostics;
using MetroLog;
using MetroLog.Targets;
#elif MONO
using NLog;
using NLog.Config;
using NLog.Targets;
using GameAnalyticsSDK.Net.Device;
using System.IO;
#endif

namespace GameAnalyticsSDK.Net.Logging
{
	internal class GALogger
	{
#region Fields and properties

		private static readonly GALogger _instance = new GALogger();
		private bool infoLogEnabled;
		private bool infoLogVerboseEnabled;
#pragma warning disable 0649
		private static bool debugEnabled;
#pragma warning restore 0649
		private const string Tag = "GameAnalytics";

#if WINDOWS_UWP || WINDOWS_WSA
        private IFileLoggingSession session;
        private ILoggingChannel logger;
        private ILogger log;
#elif MONO
		private static ILogger logger;
#elif !UNITY
        private ILogger logger;
#endif

        private static GALogger Instance
		{
			get 
			{
				return _instance;
			}
		}

		public static bool InfoLog 
		{
			set 
			{
				Instance.infoLogEnabled = value;
			}
		}

		public static bool VerboseLog
		{
			set
			{
				Instance.infoLogVerboseEnabled = value;
			}
		}

#endregion // Fields and properties

		private GALogger()
		{
#if DEBUG
            debugEnabled = true;
#endif
#if WINDOWS_UWP || WINDOWS_WSA
            session = new FileLoggingSession("ga-session");
#if WINDOWS_UWP
            var options = new LoggingChannelOptions();
            logger = new LoggingChannel("ga-channel", options);
#else
            logger = new LoggingChannel("ga-channel");
#endif
            session.AddLoggingChannel(logger);

            LogManagerFactory.DefaultConfiguration.AddTarget(LogLevel.Trace, LogLevel.Fatal, new StreamingFileTarget());
            log = LogManagerFactory.DefaultLogManager.GetLogger<GALogger>();
#elif MONO
			logger = LogManager.GetCurrentClassLogger();
			var config = new LoggingConfiguration();

			var consoleTarget = new ColoredConsoleTarget();
			config.AddTarget("console", consoleTarget);

			var fileTarget = new FileTarget();
			config.AddTarget("file", fileTarget);

			//consoleTarget.Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}";
			fileTarget.FileName = GADevice.WritablePath + Path.DirectorySeparatorChar + "ga_log.txt";
			fileTarget.Layout = "${message}";

			var rule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
			config.LoggingRules.Add(rule1);

			var rule2 = new LoggingRule("*", LogLevel.Debug, fileTarget);
			config.LoggingRules.Add(rule2);

			LogManager.Configuration = config;
#endif
        }

        #region Public methods

        public static void I(String format)
		{
			if(!Instance.infoLogEnabled)
			{
				return;
			}

			string message = "Info/" + Tag + ": " + format;
			Instance.SendNotificationMessage(message, EGALoggerMessageType.Info);
		}

		public static void W(String format)
		{
			string message = "Warning/" + Tag + ": " + format;
			Instance.SendNotificationMessage(message, EGALoggerMessageType.Warning);
		}

		public static void E(String format)
		{
			string message = "Error/" + Tag + ": " + format;
			Instance.SendNotificationMessage(message, EGALoggerMessageType.Error);
		}

		public static void II(String format)
		{
			if(!Instance.infoLogVerboseEnabled)
			{
				return;
			}

			string message = "Verbose/" + Tag + ": " + format;
			Instance.SendNotificationMessage(message, EGALoggerMessageType.Info);
		}

		public static void D(String format)
		{
			if(!debugEnabled)
			{
				return;
			}

			string message = "Debug/" + Tag + ": " + format;
			Instance.SendNotificationMessage(message, EGALoggerMessageType.Debug);
		}

#endregion // Public methods

#region Private methods

		private void SendNotificationMessage(string message, EGALoggerMessageType type)
		{
			switch(type)
			{
				case EGALoggerMessageType.Error:
					{
#if UNITY
                        UnityEngine.Debug.LogError(message);
#elif WINDOWS_UWP || WINDOWS_WSA
                        logger.LogMessage(message, LoggingLevel.Error);
                        log.Error(message);
                        GameAnalytics.MessageLogged(message, type);
#elif MONO
						logger.Error(message);
#else
                        logger.LogError(message);
                        GameAnalytics.MessageLogged(message, type);
#endif
                    }
                    break;

				case EGALoggerMessageType.Warning:
					{
#if UNITY
                        UnityEngine.Debug.LogWarning(message);
#elif WINDOWS_UWP || WINDOWS_WSA
                        logger.LogMessage(message, LoggingLevel.Warning);
                        log.Warn(message);
                        GameAnalytics.MessageLogged(message, type);
#elif MONO
						logger.Warn(message);
#else
                        logger.LogWarning(message);
                        GameAnalytics.MessageLogged(message, type);
#endif
                    }
                    break;

				case EGALoggerMessageType.Debug:
					{
#if UNITY
                        UnityEngine.Debug.Log(message);
#elif WINDOWS_UWP || WINDOWS_WSA
                        logger.LogMessage(message, LoggingLevel.Information);
                        log.Debug(message);
                        GameAnalytics.MessageLogged(message, type);
#elif MONO
						logger.Debug(message);
#else
                        logger.LogDebug(message);
                        GameAnalytics.MessageLogged(message, type);
#endif
                    }
                    break;

				case EGALoggerMessageType.Info:
					{
#if UNITY
                        UnityEngine.Debug.Log(message);
#elif WINDOWS_UWP || WINDOWS_WSA
                        logger.LogMessage(message, LoggingLevel.Information);
                        log.Info(message);
                        GameAnalytics.MessageLogged(message, type);
#elif MONO
						logger.Info(message);
#else
                        logger.LogInformation(message);
                        GameAnalytics.MessageLogged(message, type);
#endif
                    }
                    break;
			}
		}

#endregion // Private methods
	}
}

