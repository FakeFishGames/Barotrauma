using System;
using System.Collections.Generic;
using GameAnalyticsSDK.Net.Threading;
using GameAnalyticsSDK.Net.Logging;
using GameAnalyticsSDK.Net.State;
using GameAnalyticsSDK.Net.Validators;
using GameAnalyticsSDK.Net.Device;
using GameAnalyticsSDK.Net.Events;
using GameAnalyticsSDK.Net.Store;
#if WINDOWS_UWP || WINDOWS_WSA
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using System.Threading.Tasks;
#else
using System.Threading;
#endif

namespace GameAnalyticsSDK.Net
{
    public static class GameAnalytics
    {
        private static bool _endThread;

        static GameAnalytics()
        {
            _endThread = false;
            GADevice.Touch();
        }

#if !UNITY && !MONO
        public static event Action<string, EGALoggerMessageType> OnMessageLogged;

        internal static void MessageLogged(string message, EGALoggerMessageType type)
        {
            OnMessageLogged?.Invoke(message, type);
        }
#endif


        #region CONFIGURE

        public static void ConfigureAvailableCustomDimensions01(params string[] customDimensions)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureAvailableCustomDimensions01", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Available custom dimensions must be set before SDK is initialized");
                    return;
                }
                GAState.AvailableCustomDimensions01 = customDimensions;
            });
        }

        public static void ConfigureAvailableCustomDimensions02(params string[] customDimensions)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureAvailableCustomDimensions02", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Available custom dimensions must be set before SDK is initialized");
                    return;
                }
                GAState.AvailableCustomDimensions02 = customDimensions;
            });
        }

        public static void ConfigureAvailableCustomDimensions03(params string[] customDimensions)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureAvailableCustomDimensions03", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Available custom dimensions must be set before SDK is initialized");
                    return;
                }
                GAState.AvailableCustomDimensions03 = customDimensions;
            });
        }

        public static void ConfigureAvailableResourceCurrencies(params string[] resourceCurrencies)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureAvailableResourceCurrencies", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Available resource currencies must be set before SDK is initialized");
                    return;
                }
                GAState.AvailableResourceCurrencies = resourceCurrencies;
            });
        }

        public static void ConfigureAvailableResourceItemTypes(params string[] resourceItemTypes)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureAvailableResourceItemTypes", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Available resource item types must be set before SDK is initialized");
                    return;
                }
                GAState.AvailableResourceItemTypes = resourceItemTypes;
            });
        }

        public static void ConfigureBuild(string build)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureBuild", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Build version must be set before SDK is initialized.");
                    return;
                }
                if (!GAValidator.ValidateBuild(build))
                {
                    GALogger.I("Validation fail - configure build: Cannot be null, empty or above 32 length. String: " + build);
                    return;
                }
                GAState.Build = build;
            });
        }

        public static void ConfigureSdkGameEngineVersion(string sdkGameEngineVersion)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureSdkGameEngineVersion", () =>
            {
                if (IsSdkReady(true, false))
                {
                    return;
                }
                if (!GAValidator.ValidateSdkWrapperVersion(sdkGameEngineVersion))
                {
                    GALogger.I("Validation fail - configure sdk version: Sdk version not supported. String: " + sdkGameEngineVersion);
                    return;
                }
                GADevice.SdkGameEngineVersion = sdkGameEngineVersion;
            });
        }

        public static void ConfigureGameEngineVersion(string gameEngineVersion)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureGameEngineVersion", () =>
            {
                if (IsSdkReady(true, false))
                {
                    return;
                }
                if (!GAValidator.ValidateEngineVersion(gameEngineVersion))
                {
                    GALogger.I("Validation fail - configure sdk version: Sdk version not supported. String: " + gameEngineVersion);
                    return;
                }
                GADevice.GameEngineVersion = gameEngineVersion;
            });
        }

        public static void ConfigureUserId(string uId)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureUserId", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("A custom user id must be set before SDK is initialized.");
                    return;
                }
                if (!GAValidator.ValidateUserId(uId))
                {
                    GALogger.I("Validation fail - configure user_id: Cannot be null, empty or above 64 length. Will use default user_id method. Used string: " + uId);
                    return;
                }

                GAState.UserId = uId;
            });
        }

        #endregion // CONFIGURE

        #region INITIALIZE

        public static void Initialize(string gameKey, string gameSecret)
        {
            if(_endThread)
            {
                return;
            }

#if WINDOWS_UWP || WINDOWS_WSA
            CoreApplication.Suspending += OnSuspending;
            CoreApplication.Resuming += OnResuming;
#endif
            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("initialize", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("SDK already initialized. Can only be called once.");
                    return;
                }
                if (!GAValidator.ValidateKeys(gameKey, gameSecret))
                {
                    GALogger.W("SDK failed initialize. Game key or secret key is invalid. Can only contain characters A-z 0-9, gameKey is 32 length, gameSecret is 40 length. Failed keys - gameKey: " + gameKey + ", secretKey: " + gameSecret);
                    return;
                }

                GAState.SetKeys(gameKey, gameSecret);

                if (!GAStore.EnsureDatabase(false, gameKey))
                {
                    GALogger.W("Could not ensure/validate local event database: " + GADevice.WritablePath);
                }

                GAState.InternalInitialize();
            });
        }

#if WINDOWS_UWP || WINDOWS_WSA
        private static async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            await WaitOnSuspend();
            deferral.Complete();
        }

        private static async Task WaitOnSuspend()
        {
            if (!GAState.UseManualSessionHandling)
            {
                OnSuspend();

                while (!GAThreading.IsThreadFinished())
                {
                    await Task.Delay(100);
                }
            }
            else
            {
                GALogger.I("OnSuspending: Not calling GameAnalytics.OnStop() as using manual session handling");
            }
        }

        private static void OnResuming(object sender, object e)
        {
            GAThreading.PerformTaskOnGAThread("onResuming", () =>
            {
                if(!GAState.UseManualSessionHandling)
                {
                    OnResume();
                }
                else
                {
                    GALogger.I("OnResuming: Not calling GameAnalytics.OnResume() as using manual session handling");
                }
            });
        }
#endif

        #endregion // INITIALIZE

        #region ADD EVENTS

        public static void AddBusinessEvent(string currency, int amount, string itemType, string itemId, string cartType/*, IDictionary<string, object> fields = null*/)
        {
            if(_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addBusinessEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add business event"))
                {
                    return;
                }
                // Send to events
                GAEvents.AddBusinessEvent(currency, amount, itemType, itemId, cartType, null);
            });
        }

        public static void AddResourceEvent(EGAResourceFlowType flowType, string currency, float amount, string itemType, string itemId/*, IDictionary<string, object> fields = null*/)
        {
            if(_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addResourceEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add resource event"))
                {
                    return;
                }

                GAEvents.AddResourceEvent(flowType, currency, amount, itemType, itemId, null);
            });
        }

        public static void AddProgressionEvent(EGAProgressionStatus progressionStatus, string progression01/*, IDictionary<string, object> fields = null*/)
        {
            AddProgressionEvent(progressionStatus, progression01, "", ""/*, fields*/);
        }

        public static void AddProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, double score/*, IDictionary<string, object> fields = null*/)
        {
            AddProgressionEvent(progressionStatus, progression01, "", "", score/*, fields*/);
        }

        public static void AddProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, string progression02/*, IDictionary<string, object> fields = null*/)
        {
            AddProgressionEvent(progressionStatus, progression01, progression02, ""/*, fields*/);
        }

        public static void AddProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, string progression02, double score/*, IDictionary<string, object> fields = null*/)
        {
            AddProgressionEvent(progressionStatus, progression01, progression02, "", score/*, fields*/);
        }

        public static void AddProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, string progression02, string progression03/*, IDictionary<string, object> fields = null*/)
        {
            if(_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addProgressionEvent", () =>
            {
                if(!IsSdkReady(true, true, "Could not add progression event"))
                {
                    return;
                }

                // Send to events
                // TODO(nikolaj): check if this cast from int to double is OK
                GAEvents.AddProgressionEvent(progressionStatus, progression01, progression02, progression03, 0, false, null);
            });
        }

        public static void AddProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, string progression02, string progression03, double score/*, IDictionary<string, object> fields = null*/)
        {
            if(_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addProgressionEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add progression event"))
                {
                    return;
                }

                // Send to events
                // TODO(nikolaj): check if this cast from int to double is OK
                GAEvents.AddProgressionEvent(progressionStatus, progression01, progression02, progression03, score, true, null);
            });
        }

        public static void AddDesignEvent(string eventId, IDictionary<string, object> fields = null)
        {
            if(_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addDesignEvent", () =>
            {
                if(!IsSdkReady(true, true, "Could not add design event"))
                {
                    return;
                }
                GAEvents.AddDesignEvent(eventId, 0, false, fields);
            });
        }

        public static void AddDesignEvent(string eventId, double value/*, IDictionary<string, object> fields = null*/)
        {
            if(_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addDesignEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add design event"))
                {
                    return;
                }
                GAEvents.AddDesignEvent(eventId, value, true, null);
            });
        }

        public static void AddErrorEvent(EGAErrorSeverity severity, string message/*, IDictionary<string, object> fields = null*/)
        {
            if(_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addErrorEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add error event"))
                {
                    return;
                }
                GAEvents.AddErrorEvent(severity, message, null);
            });
        }

        #endregion // ADD EVENTS

        #region SET STATE CHANGES WHILE RUNNING

        public static void SetEnabledInfoLog(bool flag)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setEnabledInfoLog", () =>
            {
                if (flag)
                {
                    GALogger.InfoLog = flag;
                    GALogger.I("Info logging enabled");
                }
                else
                {
                    GALogger.I("Info logging disabled");
                    GALogger.InfoLog = flag;
                }
            });
        }

        public static void SetEnabledVerboseLog(bool flag)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setEnabledVerboseLog", () =>
            {
                if (flag)
                {
                    GALogger.VerboseLog = flag;
                    GALogger.I("Verbose logging enabled");
                }
                else
                {
                    GALogger.I("Verbose logging disabled");
                    GALogger.VerboseLog = flag;
                }
            });
        }

        public static void SetEnabledManualSessionHandling(bool flag)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setEnabledManualSessionHandling", () =>
            {
                GAState.SetManualSessionHandling(flag);
            });
        }

        public static void SetEnabledEventSubmission(bool flag)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setEnabledEventSubmission", () =>
            {
                if (flag)
                {
                    GAState.SetEnabledEventSubmission(flag);
                    GALogger.I("Event submission enabled");
                }
                else
                {
                    GALogger.I("Event submission disabled");
                    GAState.SetEnabledEventSubmission(flag);
                }
            });
        }

        public static void SetCustomDimension01(string dimension)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setCustomDimension01", () =>
            {
                if (!GAValidator.ValidateDimension01(dimension))
                {
                    GALogger.W("Could not set custom01 dimension value to '" + dimension + "'. Value not found in available custom01 dimension values");
                    return;
                }
                GAState.SetCustomDimension01(dimension);
            });
        }

        public static void SetCustomDimension02(string dimension)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setCustomDimension02", () =>
            {
                if (!GAValidator.ValidateDimension02(dimension))
                {
                    GALogger.W("Could not set custom02 dimension value to '" + dimension + "'. Value not found in available custom02 dimension values");
                    return;
                }
                GAState.SetCustomDimension02(dimension);
            });
        }

        public static void SetCustomDimension03(string dimension)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setCustomDimension03", () =>
            {
                if (!GAValidator.ValidateDimension03(dimension))
                {
                    GALogger.W("Could not set custom03 dimension value to '" + dimension + "'. Value not found in available custom03 dimension values");
                    return;
                }
                GAState.SetCustomDimension03(dimension);
            });
        }

        public static void SetFacebookId(string facebookId)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setFacebookId", () =>
            {
                if (GAValidator.ValidateFacebookId(facebookId))
                {
                    GAState.SetFacebookId(facebookId);
                }
            });
        }

        public static void SetGender(EGAGender gender)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setGender", () =>
            {
                if (GAValidator.ValidateGender(gender))
                {
                    GAState.SetGender(gender);
                }
            });
        }

        public static void SetBirthYear(int birthYear)
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setBirthYear", () =>
            {
                if (GAValidator.ValidateBirthyear(birthYear))
                {
                    GAState.SetBirthYear(birthYear);
                }
            });
        }

        #endregion // SET STATE CHANGES WHILE RUNNING

        public static void StartSession()
        {
            if(_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("startSession", () =>
            {
#if WINDOWS_UWP || WINDOWS_WSA
                if(GAState.UseManualSessionHandling)
#endif
                {
                    if(!GAState.Initialized)
                    {
                        return;
                    }

                    if(GAState.IsEnabled() && GAState.SessionIsStarted())
                    {
                        GAState.EndSessionAndStopQueue(false);
                    }

                    GAState.ResumeSessionAndStartQueue();
                }
            });
        }

        public static void EndSession()
        {
#if WINDOWS_UWP || WINDOWS_WSA
            if(GAState.UseManualSessionHandling)
#endif
            {
                OnSuspend();
            }
        }

        public static void OnResume()
        {
            if(_endThread)
            {
                return;
            }

            GALogger.D("OnResume() called");
            GAThreading.PerformTaskOnGAThread("onResume", () =>
            {
                GAState.ResumeSessionAndStartQueue();
            });
        }

        public static void OnSuspend()
        {
            if(_endThread)
            {
                return;
            }

            GALogger.D("OnSuspend() called");
            GAThreading.PerformTaskOnGAThread("onSuspend", () =>
            {
                try
                {
                    GAState.EndSessionAndStopQueue(false);
                }
                catch(Exception)
                {
                }
            });
        }

        public static void OnQuit()
        {
            if(_endThread)
            {
                return;
            }

            GALogger.D("OnQuit() called");
            GAThreading.PerformTaskOnGAThread("onQuit", () =>
            {
                try
                {
                    _endThread = true;
                    GAState.EndSessionAndStopQueue(true);
                }
                catch(Exception)
                {
                }
            });
        }

        #region COMMAND CENTER

        public static string GetCommandCenterValueAsString(string key, string defaultValue = null)
        {
            return GAState.GetConfigurationStringValue(key, defaultValue);
        }

        public static bool IsCommandCenterReady()
        {
            return GAState.IsCommandCenterReady();
        }

        public static void AddCommandCenterListener(ICommandCenterListener listener)
        {
            GAState.AddCommandCenterListener(listener);
        }

        public static void RemoveCommandCenterListener(ICommandCenterListener listener)
        {
            GAState.RemoveCommandCenterListener(listener);
        }

        public static string GetConfigurationsAsString()
        {
            return GAState.GetConfigurationsAsString();
        }

#endregion // COMMAND CENTER

#region PRIVATE HELPERS

        private static bool IsSdkReady(bool needsInitialized)
        {
            return IsSdkReady(needsInitialized, true);
        }

        private static bool IsSdkReady(bool needsInitialized, bool warn)
        {
            return IsSdkReady(needsInitialized, warn, "");
        }

        private static bool IsSdkReady(bool needsInitialized, bool warn, String message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                message = message + ": ";
            }

            // Make sure database is ready
            if (!GAStore.IsTableReady)
            {
                if (warn)
                {
                    GALogger.W(message + "Datastore not initialized");
                }
                return false;
            }
            // Is SDK initialized
            if (needsInitialized && !GAState.Initialized)
            {
                if (warn)
                {
                    GALogger.W(message + "SDK is not initialized");
                }
                return false;
            }
            // Is SDK enabled
            if (needsInitialized && !GAState.IsEnabled())
            {
                if (warn)
                {
                    GALogger.W(message + "SDK is disabled");
                }
                return false;
            }
            // Is session started
            if (needsInitialized && !GAState.SessionIsStarted())
            {
                if (warn)
                {
                    GALogger.W(message + "Session has not started yet");
                }
                return false;
            }
            return true;
        }

#endregion // PRIVATE HELPERS
    }
}
