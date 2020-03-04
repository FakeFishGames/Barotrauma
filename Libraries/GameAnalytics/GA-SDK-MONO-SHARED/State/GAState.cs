using System;
using System.Collections.Generic;
using GameAnalyticsSDK.Net.Device;
using GameAnalyticsSDK.Net.Logging;
using GameAnalyticsSDK.Net.Store;
using GameAnalyticsSDK.Net.Events;
using GameAnalyticsSDK.Net.Utilities;
using GameAnalyticsSDK.Net.Http;
using GameAnalyticsSDK.Net.Validators;
using GameAnalyticsSDK.Net.Threading;

namespace GameAnalyticsSDK.Net.State
{
    internal class GAState
    {
        #region Fields and properties

        private const String CategorySdkError = "sdk_error";
        private const int MaxCustomFieldsCount = 50;
        private const int MaxCustomFieldsKeyLength = 64;
        private const int MaxCustomFieldsValueStringLength = 256;

        private static readonly GAState _instance = new GAState();
        private static GAState Instance
        {
            get
            {
                return _instance;
            }
        }

        private string _userId;
        public static string UserId
        {
            private get { return Instance._userId; }
            set
            {
                Instance._userId = value == null ? "" : value;
                CacheIdentifier();
            }
        }

        private string _identifier;
        public static string Identifier
        {
            get { return Instance._identifier; }
            private set { Instance._identifier = value; }
        }

        private bool _initialized;
        public static bool Initialized
        {
            get { return Instance._initialized; }
            private set { Instance._initialized = value; }
        }

        private long _sessionStart;
        public static long SessionStart
        {
            get { return Instance._sessionStart; }
            private set { Instance._sessionStart = value; }
        }

        private int _sessionNum;
        public static int SessionNum
        {
            get { return Instance._sessionNum; }
            private set { Instance._sessionNum = value; }
        }

        private int _transactionNum;
        public static int TransactionNum
        {
            get { return Instance._transactionNum; }
            private set { Instance._transactionNum = value; }
        }

        private string _sessionId;
        public static string SessionId
        {
            get { return Instance._sessionId; }
            private set { Instance._sessionId = value; }
        }

        private string _currentCustomDimension01;
        public static string CurrentCustomDimension01
        {
            get { return Instance._currentCustomDimension01; }
            private set { Instance._currentCustomDimension01 = value; }
        }

        private string _currentCustomDimension02;
        public static string CurrentCustomDimension02
        {
            get { return Instance._currentCustomDimension02; }
            private set { Instance._currentCustomDimension02 = value; }
        }

        private string _currentCustomDimension03;
        public static string CurrentCustomDimension03
        {
            get { return Instance._currentCustomDimension03; }
            private set { Instance._currentCustomDimension03 = value; }
        }

        private string _gameKey;
        public static string GameKey
        {
            get { return Instance._gameKey; }
            private set { Instance._gameKey = value; }
        }

        private string _gameSecret;
        public static string GameSecret
        {
            get { return Instance._gameSecret; }
            private set { Instance._gameSecret = value; }
        }

        private string[] _availableCustomDimensions01 = new string[0];
        public static string[] AvailableCustomDimensions01
        {
            get { return Instance._availableCustomDimensions01; }
            set
            {
                // Validate
                if (!GAValidator.ValidateCustomDimensions(value))
                {
                    return;
                }
                Instance._availableCustomDimensions01 = value;

                // validate current dimension values
                ValidateAndFixCurrentDimensions();

                GALogger.I("Set available custom01 dimension values: (" + GAUtilities.JoinStringArray(value, ", ") + ")");
            }
        }

        private string[] _availableCustomDimensions02 = new string[0];
        public static string[] AvailableCustomDimensions02
        {
            get { return Instance._availableCustomDimensions02; }
            set
            {
                // Validate
                if (!GAValidator.ValidateCustomDimensions(value))
                {
                    return;
                }
                Instance._availableCustomDimensions02 = value;

                // validate current dimension values
                ValidateAndFixCurrentDimensions();

                GALogger.I("Set available custom02 dimension values: (" + GAUtilities.JoinStringArray(value, ", ") + ")");
            }
        }

        private string[] _availableCustomDimensions03 = new string[0];
        public static string[] AvailableCustomDimensions03
        {
            get { return Instance._availableCustomDimensions03; }
            set
            {
                // Validate
                if (!GAValidator.ValidateCustomDimensions(value))
                {
                    return;
                }
                Instance._availableCustomDimensions03 = value;

                // validate current dimension values
                ValidateAndFixCurrentDimensions();

                GALogger.I("Set available custom03 dimension values: (" + GAUtilities.JoinStringArray(value, ", ") + ")");
            }
        }

        private string[] _availableResourceCurrencies = new string[0];
        public static string[] AvailableResourceCurrencies
        {
            get { return Instance._availableResourceCurrencies; }
            set
            {
                // Validate
                if (!GAValidator.ValidateResourceCurrencies(value))
                {
                    return;
                }
                Instance._availableResourceCurrencies = value;

                GALogger.I("Set available resource currencies: (" + GAUtilities.JoinStringArray(value, ", ") + ")");
            }
        }

        private string[] _availableResourceItemTypes = new string[0];
        public static string[] AvailableResourceItemTypes
        {
            get { return Instance._availableResourceItemTypes; }
            set
            {
                // Validate
                if (!GAValidator.ValidateResourceItemTypes(value))
                {
                    return;
                }
                Instance._availableResourceItemTypes = value;

                GALogger.I("Set available resource item types: (" + GAUtilities.JoinStringArray(value, ", ") + ")");
            }
        }

        private string _build;
        public static string Build
        {
            get { return Instance._build; }
            set { Instance._build = value; }
        }

        private bool _useManualSessionHandling;
        public static bool UseManualSessionHandling
        {
            get { return Instance._useManualSessionHandling; }
            private set { Instance._useManualSessionHandling = value; }
        }

        private bool _isEventSubmissionEnabled = true;
        public static bool IsEventSubmissionEnabled
        {
            get { return Instance._isEventSubmissionEnabled; }
            private set { Instance._isEventSubmissionEnabled = value; }
        }

        private bool Enabled
        {
            get;
            set;
        }
        private string FacebookId
        {
            get;
            set;
        }
        private string Gender
        {
            get;
            set;
        }
        private int BirthYear
        {
            get;
            set;
        }
        private JSONNode SdkConfigCached
        {
            get;
            set;
        }
        private bool InitAuthorized
        {
            get;
            set;
        }
        private long ClientServerTimeOffset
        {
            get;
            set;
        }
        private long SuspendBlockId
        {
            get;
            set;
        }

        private string _defaultUserId;
        private string DefaultUserId
        {
            get { return Instance._defaultUserId; }
            set
            {
                Instance._defaultUserId = value == null ? "" : value;
                CacheIdentifier();
            }
        }

        private static JSONNode SdkConfig
        {
            get
            {
                if (Instance.sdkConfig.AsObject != null && Instance.sdkConfig.Count != 0)
                {
                    return Instance.sdkConfig;
                }

                if (Instance.sdkConfigCached.AsObject != null && Instance.sdkConfigCached.Count != 0)
                {
                    return Instance.sdkConfigCached;
                }

                return Instance.sdkConfigDefault;
            }
        }

        private Dictionary<string, int> progressionTries = new Dictionary<string, int>();
        private JSONNode sdkConfigDefault = new JSONObject();
        private JSONNode sdkConfig = new JSONObject();
        private JSONNode sdkConfigCached = new JSONObject();
        private JSONNode configurations = new JSONObject();
        private bool commandCenterIsReady;
        private readonly List<ICommandCenterListener> commandCenterListeners = new List<ICommandCenterListener>();
        private readonly object configurationsLock = new object();

        public const string InMemoryPrefix = "in_memory_";
        private const string DefaultUserIdKey = "default_user_id";
        public const string SessionNumKey = "session_num";
        public const string TransactionNumKey = "transaction_num";
        private const string FacebookIdKey = "facebook_id";
        private const string GenderKey = "gender";
        private const string BirthYearKey = "birth_year";
        private const string Dimension01Key = "dimension01";
        private const string Dimension02Key = "dimension02";
        private const string Dimension03Key = "dimension03";
        private const string SdkConfigCachedKey = "sdk_config_cached";

        #endregion // Fields and properties

        private GAState()
        {
            Enabled = false;
        }

        ~GAState()
        {
            EndSessionAndStopQueue(false);
        }

        #region Public methods

        public static bool IsEnabled()
        {
            return Instance.Enabled;
        }

        public static void SetCustomDimension01(string dimension)
        {
            CurrentCustomDimension01 = dimension;
            if(GAStore.IsTableReady)
            {
                GAStore.SetState(Dimension01Key, dimension);
            }
            GALogger.I("Set custom01 dimension value: " + dimension);
        }

        public static void SetCustomDimension02(string dimension)
        {
            CurrentCustomDimension02 = dimension;
            if(GAStore.IsTableReady)
            {
                GAStore.SetState(Dimension02Key, dimension);
            }
            GALogger.I("Set custom02 dimension value: " + dimension);
        }

        public static void SetCustomDimension03(string dimension)
        {
            CurrentCustomDimension03 = dimension;
            if(GAStore.IsTableReady)
            {
                GAStore.SetState(Dimension03Key, dimension);
            }
            GALogger.I("Set custom03 dimension value: " + dimension);
        }

        public static void SetFacebookId(string facebookId)
        {
            Instance.FacebookId = facebookId;
            if(GAStore.IsTableReady)
            {
                GAStore.SetState(FacebookIdKey, facebookId);
            }
            GALogger.I("Set facebook id: " + facebookId);
        }

        public static void SetGender(EGAGender gender)
        {
            Instance.Gender = gender.ToString().ToLowerInvariant();
            if(GAStore.IsTableReady)
            {
                GAStore.SetState(GenderKey, Instance.Gender);
            }
            GALogger.I("Set gender: " + gender);
        }

        public static void SetBirthYear(int birthYear)
        {
            Instance.BirthYear = birthYear;
            if(GAStore.IsTableReady)
            {
                GAStore.SetState(BirthYearKey, birthYear.ToString());
            }
            GALogger.I("Set birth year: " + birthYear);
        }

        public static void IncrementSessionNum()
        {
            int sessionNumInt = SessionNum + 1;
            SessionNum = sessionNumInt;
        }

        public static void IncrementTransactionNum()
        {
            int transactionNumInt = TransactionNum + 1;
            TransactionNum = transactionNumInt;
        }

#pragma warning disable 0162
        public static void IncrementProgressionTries(string progression)
        {
            int tries = GetProgressionTries(progression) + 1;
            Instance.progressionTries[progression] = tries;

            if(GAStore.InMemory)
            {
                GALogger.D("Trying to IncrementProgressionTries with InMemory=true - cannot. Skipping.");
            }
            else
            {
                // Persist
                Dictionary<string, object> parms = new Dictionary<string, object>();
                parms.Add("$progression", progression);
                parms.Add("$tries", tries);
                GAStore.ExecuteQuerySync("INSERT OR REPLACE INTO ga_progression (progression, tries) VALUES($progression, $tries);", parms);
            }
        }
#pragma warning restore 0162

        public static int GetProgressionTries(string progression)
        {
            if(Instance.progressionTries.ContainsKey(progression))
            {
                return Instance.progressionTries[progression];
            }
            else
            {
                return 0;
            }
        }

#pragma warning disable 0162
        public static void ClearProgressionTries(string progression)
        {
            Dictionary<string, int> progressionTries = Instance.progressionTries;
            if(progressionTries.ContainsKey(progression))
            {
                progressionTries.Remove(progression);
            }

            if(GAStore.InMemory)
            {
                GALogger.D("Trying to ClearProgressionTries with InMemory=true - cannot. Skipping.");
            }
            else
            {
                // Delete
                Dictionary<string, object> parms = new Dictionary<string, object>();
                parms.Add("$progression", progression);
                GAStore.ExecuteQuerySync("DELETE FROM ga_progression WHERE progression = $progression;", parms);
            }
        }
#pragma warning restore 0162

        public static bool HasAvailableCustomDimensions01(string dimension1)
        {
            return GAUtilities.StringArrayContainsString(AvailableCustomDimensions01, dimension1);
        }

        public static bool HasAvailableCustomDimensions02(string dimension2)
        {
            return GAUtilities.StringArrayContainsString(AvailableCustomDimensions02, dimension2);
        }

        public static bool HasAvailableCustomDimensions03(string dimension3)
        {
            return GAUtilities.StringArrayContainsString(AvailableCustomDimensions03, dimension3);
        }

        public static bool HasAvailableResourceCurrency(string currency)
        {
            return GAUtilities.StringArrayContainsString(AvailableResourceCurrencies, currency);
        }

        public static bool HasAvailableResourceItemType(string itemType)
        {
            return GAUtilities.StringArrayContainsString(AvailableResourceItemTypes, itemType);
        }

        public static void SetKeys(string gameKey, string gameSecret)
        {
            GameKey = gameKey;
            GameSecret = gameSecret;
        }

        public static void SetManualSessionHandling(bool flag)
        {
            UseManualSessionHandling = flag;
            GALogger.I("Use manual session handling: " + flag);
        }

        public static void SetEnabledEventSubmission(bool flag)
        {
            IsEventSubmissionEnabled = flag;
        }

#if WINDOWS_UWP || WINDOWS_WSA
        public async static void InternalInitialize()
#else
        public static void InternalInitialize()
#endif
        {
            // Make sure database is ready
            if (!GAStore.IsTableReady)
            {
                return;
            }

            EnsurePersistedStates();
            GAStore.SetState(DefaultUserIdKey, Instance.DefaultUserId);

            Initialized = true;

#if WINDOWS_UWP || WINDOWS_WSA
            await StartNewSession();
#else
            StartNewSession();
#endif

            if (IsEnabled())
            {
                GAEvents.EnsureEventQueueIsRunning();
            }
        }

        public static void EndSessionAndStopQueue(bool endThread)
        {
            if(Initialized)
            {
                if (IsEnabled() && SessionIsStarted())
                {
                    GALogger.I("Ending session.");
                    GAEvents.StopEventQueue();
                    GAEvents.AddSessionEndEvent();
                    SessionStart = 0;
                }
            }

            if(endThread)
            {
                GAThreading.StopThread();
            }
        }

#if WINDOWS_UWP || WINDOWS_WSA
        public async static void ResumeSessionAndStartQueue()
#else
        public static void ResumeSessionAndStartQueue()
#endif
        {
            if(!Initialized)
            {
                return;
            }
            GALogger.I("Resuming session.");
            if(!SessionIsStarted())
            {
#if WINDOWS_UWP || WINDOWS_WSA
                await StartNewSession();
#else
                StartNewSession();
#endif
            }
        }

        public static JSONObject GetEventAnnotations()
        {
            JSONObject annotations = new JSONObject();

            // ---- REQUIRED ---- //

            // collector event API version
            annotations.Add("v", new JSONNumber(2));
            // User identifier
            annotations["user_id"] = Identifier;

            // Client Timestamp (the adjusted timestamp)
            annotations.Add("client_ts", new JSONNumber(GetClientTsAdjusted()));
            // SDK version
            annotations["sdk_version"] = GADevice.RelevantSdkVersion;
            // Operation system version
            annotations["os_version"] = GADevice.OSVersion;
            // Device make (hardcoded to apple)
            annotations["manufacturer"] = GADevice.DeviceManufacturer;
            // Device version
            annotations["device"] = GADevice.DeviceModel;
            // Platform (operating system)
            annotations["platform"] = GADevice.BuildPlatform;
            // Session identifier
            annotations["session_id"] = SessionId;
            // Session number
            annotations.Add(SessionNumKey, new JSONNumber(SessionNum));

            // type of connection the user is currently on (add if valid)
            string connection_type = GADevice.ConnectionType;
            if (GAValidator.ValidateConnectionType(connection_type))
            {
                annotations["connection_type"] = connection_type;
            }

            if (!string.IsNullOrEmpty(GADevice.GameEngineVersion))
            {
                annotations["engine_version"] = GADevice.GameEngineVersion;
            }

#if WINDOWS_UWP
            if (!string.IsNullOrEmpty(GADevice.AdvertisingId))
            {
                annotations["uwp_aid"] = GADevice.AdvertisingId;
            }
            else if (!string.IsNullOrEmpty(GADevice.DeviceId))
            {
                annotations["uwp_id"] = GADevice.DeviceId;
            }
#endif

            // ---- CONDITIONAL ---- //

            // App build version (use if not nil)
            if (!string.IsNullOrEmpty(Build))
            {
                annotations["build"] = Build;
            }

            // ---- OPTIONAL cross-session ---- //

            // facebook id (optional)
            if (!string.IsNullOrEmpty(Instance.FacebookId))
            {
                annotations[FacebookIdKey] = Instance.FacebookId;
            }
            // gender (optional)
            if (!string.IsNullOrEmpty(Instance.Gender))
            {
                annotations[GenderKey] = Instance.Gender;
            }
            // birth_year (optional)
            if (Instance.BirthYear != 0)
            {
                annotations.Add(BirthYearKey, new JSONNumber(Instance.BirthYear));
            }

            return annotations;
        }

        public static JSONObject GetSdkErrorEventAnnotations()
        {
            JSONObject annotations = new JSONObject();

            // ---- REQUIRED ---- //

            // collector event API version
            annotations.Add("v", new JSONNumber(2));

            // Category
            annotations["category"] = CategorySdkError;
            // SDK version
            annotations["sdk_version"] = GADevice.RelevantSdkVersion;
            // Operation system version
            annotations["os_version"] = GADevice.OSVersion;
            // Device make (hardcoded to apple)
            annotations["manufacturer"] = GADevice.DeviceManufacturer;
            // Device version
            annotations["device"] = GADevice.DeviceModel;
            // Platform (operating system)
            annotations["platform"] = GADevice.BuildPlatform;

            // type of connection the user is currently on (add if valid)
            string connection_type = GADevice.ConnectionType;
            if (GAValidator.ValidateConnectionType(connection_type))
            {
                annotations["connection_type"] = connection_type;
            }

            if (!string.IsNullOrEmpty(GADevice.GameEngineVersion))
            {
                annotations["engine_version"] = GADevice.GameEngineVersion;
            }

            return annotations;
        }

        public static JSONObject GetInitAnnotations()
        {
            JSONObject initAnnotations = new JSONObject();

            initAnnotations["user_id"] = GAState.Identifier;

            // SDK version
            initAnnotations["sdk_version"] = GADevice.RelevantSdkVersion;
            // Operation system version
            initAnnotations["os_version"] = GADevice.OSVersion;

            // Platform (operating system)
            initAnnotations["platform"] = GADevice.BuildPlatform;

            return initAnnotations;
        }

        public static long GetClientTsAdjusted()
        {
            long clientTs = GAUtilities.TimeIntervalSince1970();
            long clientTsAdjustedInteger = clientTs + Instance.ClientServerTimeOffset;

            if(GAValidator.ValidateClientTs(clientTsAdjustedInteger))
            {
                return clientTsAdjustedInteger;
            }
            else
            {
                return clientTs;
            }
        }

        public static bool SessionIsStarted()
        {
            return SessionStart != 0;
        }

        public static JSONObject ValidateAndCleanCustomFields(IDictionary<string, object> fields)
        {
            JSONObject result = new JSONObject();

            if(fields != null)
            {
                int count = 0;

                foreach(KeyValuePair<string, object> entry in fields)
                {
                    if(entry.Key == null || entry.Value == null)
                    {
                        GALogger.W("ValidateAndCleanCustomFields: entry with key=" + entry.Key + ", value=" + entry.Value + " has been omitted because its key or value is null");
                    }
                    else if(count < MaxCustomFieldsCount)
                    {
                        if(GAUtilities.StringMatch(entry.Key, "^[a-zA-Z0-9_]{1," + MaxCustomFieldsKeyLength + "}$"))
                        {
                            if(entry.Value is string || entry.Value is char)
                            {
                                string value = Convert.ToString(entry.Value);

                                if(value.Length <= MaxCustomFieldsValueStringLength && value.Length > 0)
                                {
                                    result[entry.Key] = value;
                                    ++count;
                                }
                                else
                                {
                                    GALogger.W("ValidateAndCleanCustomFields: entry with key=" + entry.Key + ", value=" + entry.Value + " has been omitted because its value is an empty string or exceeds the max number of characters (" + MaxCustomFieldsValueStringLength + ")");
                                }
                            }
                            else if (entry.Value is double)
                            {
                                result[entry.Key] = new JSONNumber((double)entry.Value);
                                ++count;
                            }
                            else if (entry.Value is float)
                            {
                                result[entry.Key] = new JSONNumber((float)entry.Value);
                                ++count;
                            }
                            else if (entry.Value is long || entry.Value is ulong)
                            {
                                result[entry.Key] = new JSONNumber(Convert.ToInt64(entry.Value));
                                ++count;
                            }
                            else if (entry.Value is int ||
                                entry.Value is byte ||
                                entry.Value is sbyte ||
                                entry.Value is byte ||
                                entry.Value is uint ||
                                entry.Value is short ||
                                entry.Value is ushort)
                            {
                                result[entry.Key] = new JSONNumber(Convert.ToInt32(entry.Value));
                                ++count;
                            }
                            else
                            {
                                GALogger.W("ValidateAndCleanCustomFields: entry with key=" + entry.Key + ", value=" + entry.Value + " has been omitted because its value is not a string or number");
                            }
                        }
                        else
                        {
                            GALogger.W("ValidateAndCleanCustomFields: entry with key=" + entry.Key + ", value=" + entry.Value + " has been omitted because its key illegal characters, an empty or exceeds the max number of characters (" + MaxCustomFieldsKeyLength + ")");
                        }
                    }
                    else
                    {
                        GALogger.W("ValidateAndCleanCustomFields: entry with key=" + entry.Key + ", value=" + entry.Value + " has been omitted because it exceeds the max number of custom fields (" + MaxCustomFieldsCount + ")");
                    }
                }
            }

            return result;
        }

        public static string GetConfigurationStringValue(string key, string defaultValue)
        {
            lock (Instance.configurationsLock)
            {
                return !Instance.configurations[key].IsNull ? Instance.configurations[key].Value : defaultValue;
            }
        }

        public static bool IsCommandCenterReady()
        {
            return Instance.commandCenterIsReady;
        }

        public static void AddCommandCenterListener(ICommandCenterListener listener)
        {
            if(!Instance.commandCenterListeners.Contains(listener))
            {
                Instance.commandCenterListeners.Add(listener);
            }
        }

        public static void RemoveCommandCenterListener(ICommandCenterListener listener)
        {
            if(Instance.commandCenterListeners.Contains(listener))
            {
                Instance.commandCenterListeners.Remove(listener);
            }
        }

        public static string GetConfigurationsAsString()
        {
            return Instance.configurations.ToString();
        }

#endregion // Public methods

#region Private methods

        private static void CacheIdentifier()
        {
            if(!string.IsNullOrEmpty(GAState.UserId))
            {
                GAState.Identifier = GAState.UserId;
            }
#if WINDOWS_UWP
            else if (!string.IsNullOrEmpty(GADevice.AdvertisingId))
            {
                GAState.Identifier = GADevice.AdvertisingId;
            }
            else if (!string.IsNullOrEmpty(GADevice.DeviceId))
            {
                GAState.Identifier = GADevice.DeviceId;
            }
#endif
            else if(!string.IsNullOrEmpty(Instance.DefaultUserId))
            {
                GAState.Identifier = Instance.DefaultUserId;
            }

            GALogger.D("identifier, {clean:" + GAState.Identifier + "}");
        }

#pragma warning disable 0162
        private static void EnsurePersistedStates()
        {
            if(GAStore.InMemory)
            {
#if UNITY
                GALogger.D("retrieving persisted states from local PlayerPrefs");

                GAState instance = GAState.Instance;

                instance.DefaultUserId = UnityEngine.PlayerPrefs.GetString(InMemoryPrefix + SessionNumKey, Guid.NewGuid().ToString());
                {
                    int tmp;
                    int.TryParse(UnityEngine.PlayerPrefs.GetString(InMemoryPrefix + SessionNumKey, "0"), out tmp);
                    SessionNum = tmp;
                }
                {
                    int tmp;
                    int.TryParse(UnityEngine.PlayerPrefs.GetString(InMemoryPrefix + TransactionNumKey, "0"), out tmp);
                    TransactionNum = tmp;
                }
                if(!string.IsNullOrEmpty(instance.FacebookId))
                {
                    UnityEngine.PlayerPrefs.SetString(InMemoryPrefix + FacebookIdKey, instance.FacebookId);
                }
                else
                {
                    instance.FacebookId = UnityEngine.PlayerPrefs.GetString(InMemoryPrefix + FacebookIdKey, "");
                }
                if(!string.IsNullOrEmpty(instance.Gender))
                {
                    UnityEngine.PlayerPrefs.SetString(InMemoryPrefix + GenderKey, instance.Gender);
                }
                else
                {
                    instance.Gender = UnityEngine.PlayerPrefs.GetString(InMemoryPrefix + GenderKey, "");
                }
                if(instance.BirthYear != 0)
                {
                    UnityEngine.PlayerPrefs.SetString(InMemoryPrefix + BirthYearKey, instance.BirthYear.ToString());
                }
                else
                {
                    int tmp;
                    int.TryParse(UnityEngine.PlayerPrefs.GetString(InMemoryPrefix + BirthYearKey, "0"), out tmp);
                    instance.BirthYear = tmp;
                }
                if(!string.IsNullOrEmpty(CurrentCustomDimension01))
                {
                    UnityEngine.PlayerPrefs.SetString(InMemoryPrefix + Dimension01Key, CurrentCustomDimension01);
                }
                else
                {
                    CurrentCustomDimension01 = UnityEngine.PlayerPrefs.GetString(InMemoryPrefix + Dimension01Key, "");
                }
                if(!string.IsNullOrEmpty(CurrentCustomDimension02))
                {
                    UnityEngine.PlayerPrefs.SetString(InMemoryPrefix + Dimension02Key, CurrentCustomDimension02);
                }
                else
                {
                    CurrentCustomDimension02 = UnityEngine.PlayerPrefs.GetString(InMemoryPrefix + Dimension02Key, "");
                }
                if(!string.IsNullOrEmpty(CurrentCustomDimension03))
                {
                    UnityEngine.PlayerPrefs.SetString(InMemoryPrefix + Dimension03Key, CurrentCustomDimension03);
                }
                else
                {
                    CurrentCustomDimension03 = UnityEngine.PlayerPrefs.GetString(InMemoryPrefix + Dimension03Key, "");
                }

                string sdkConfigCachedString = UnityEngine.PlayerPrefs.GetString(InMemoryPrefix + SdkConfigCachedKey, "");
                if(!string.IsNullOrEmpty(sdkConfigCachedString))
                {
                    // decode JSON
                    JSONNode sdkConfigCached = null;
                    try
                    {
                        sdkConfigCached = JSONNode.LoadFromBinaryBase64(sdkConfigCachedString);
                    }
                    catch(Exception)
                    {
                        //GALogger.E("EnsurePersistedStates: Error decoding json, " + e);
                    }

                    if(sdkConfigCached != null && sdkConfigCached.Count != 0)
                    {
                        instance.SdkConfigCached = sdkConfigCached;
                    }
                }
#else
                GALogger.W("EnsurePersistedStates: No implementation yet for InMemory=true");
#endif
            }
            else
            {
                // get and extract stored states
                JSONObject state_dict = new JSONObject();
                JSONArray results_ga_state = GAStore.ExecuteQuerySync("SELECT * FROM ga_state;");

                if (results_ga_state != null && results_ga_state.Count != 0)
                {
                    for (int i = 0; i < results_ga_state.Count; ++i)
                    {
                        JSONNode result = results_ga_state[i];
                        state_dict.Add(result["key"], result["value"]);
                    }
                }

                // insert into GAState instance
                GAState instance = GAState.Instance;

                instance.DefaultUserId = state_dict[DefaultUserIdKey] != null ? state_dict[DefaultUserIdKey].Value : Guid.NewGuid().ToString();

                SessionNum = state_dict[SessionNumKey] != null ? state_dict[SessionNumKey].AsInt : 0;

                TransactionNum = state_dict[TransactionNumKey] != null ? state_dict[TransactionNumKey].AsInt : 0;

                // restore cross session user values
                if(!string.IsNullOrEmpty(instance.FacebookId))
                {
                    GAStore.SetState(FacebookIdKey, instance.FacebookId);
                }
                else
                {
                    instance.FacebookId = state_dict[FacebookIdKey] != null ? state_dict[FacebookIdKey].Value : "";
                    if(!string.IsNullOrEmpty(instance.FacebookId))
                    {
                        GALogger.D("facebookid found in DB: " + instance.FacebookId);
                    }
                }

                if(!string.IsNullOrEmpty(instance.Gender))
                {
                    GAStore.SetState(GenderKey, instance.Gender);
                }
                else
                {
                    instance.Gender = state_dict[GenderKey] != null ? state_dict[GenderKey].Value : "";
                    if(!string.IsNullOrEmpty(instance.Gender))
                    {
                        GALogger.D("gender found in DB: " + instance.Gender);
                    }
                }

                if(instance.BirthYear != 0)
                {
                    GAStore.SetState(BirthYearKey, instance.BirthYear.ToString());
                }
                else
                {
                    instance.BirthYear = state_dict[BirthYearKey] != null ? state_dict[BirthYearKey].AsInt : 0;
                    if(instance.BirthYear != 0)
                    {
                        GALogger.D("birthYear found in DB: " + instance.BirthYear);
                    }
                }

                // restore dimension settings
                if(!string.IsNullOrEmpty(CurrentCustomDimension01))
                {
                    GAStore.SetState(Dimension01Key, CurrentCustomDimension01);
                }
                else
                {
                    CurrentCustomDimension01 = state_dict[Dimension01Key] != null ? state_dict[Dimension01Key].Value : "";
                    if(!string.IsNullOrEmpty(CurrentCustomDimension01))
                    {
                        GALogger.D("Dimension01 found in cache: " + CurrentCustomDimension01);
                    }
                }

                if(!string.IsNullOrEmpty(CurrentCustomDimension02))
                {
                    GAStore.SetState(Dimension02Key, CurrentCustomDimension02);
                }
                else
                {
                    CurrentCustomDimension02 = state_dict[Dimension02Key] != null ? state_dict[Dimension02Key].Value : "";
                    if(!string.IsNullOrEmpty(CurrentCustomDimension02))
                    {
                        GALogger.D("Dimension02 found in cache: " + CurrentCustomDimension02);
                    }
                }

                if(!string.IsNullOrEmpty(CurrentCustomDimension03))
                {
                    GAStore.SetState(Dimension03Key, CurrentCustomDimension03);
                }
                else
                {
                    CurrentCustomDimension03 = state_dict[Dimension03Key] != null ? state_dict[Dimension03Key].Value : "";
                    if(!string.IsNullOrEmpty(CurrentCustomDimension03))
                    {
                        GALogger.D("Dimension03 found in cache: " + CurrentCustomDimension03);
                    }
                }

                // get cached init call values
                string sdkConfigCachedString = state_dict[SdkConfigCachedKey] != null ? state_dict[SdkConfigCachedKey].Value : "";
                if (!string.IsNullOrEmpty(sdkConfigCachedString))
                {
                    // decode JSON
                    JSONNode sdkConfigCached = null;

                    try
                    {
                        sdkConfigCached = JSONNode.LoadFromBinaryBase64(sdkConfigCachedString);
                    }
                    catch(Exception)
                    {
                        //GALogger.E("EnsurePersistedStates: Error decoding json, " + e);
                    }

                    if (sdkConfigCached != null && sdkConfigCached.Count != 0)
                    {
                        instance.SdkConfigCached = sdkConfigCached;
                    }
                }

                JSONArray results_ga_progression = GAStore.ExecuteQuerySync("SELECT * FROM ga_progression;");

                if (results_ga_progression != null && results_ga_progression.Count != 0)
                {
                    for (int i = 0; i < results_ga_progression.Count; ++i)
                    {
                        JSONNode result = results_ga_progression[i];
                        if (result != null && result.Count != 0)
                        {
                            instance.progressionTries[result["progression"].Value] = result["tries"].AsInt;
                        }
                    }
                }
            }
        }
#pragma warning restore 0162

#if WINDOWS_UWP || WINDOWS_WSA
        private async static System.Threading.Tasks.Task StartNewSession()
#else
        private static void StartNewSession()
#endif
        {
            GALogger.I("Starting a new session.");

            // make sure the current custom dimensions are valid
            ValidateAndFixCurrentDimensions();

            // call the init call
#if WINDOWS_UWP || WINDOWS_WSA
            KeyValuePair<EGAHTTPApiResponse, JSONObject> initResponse = await GAHTTPApi.Instance.RequestInitReturningDict();
#else
            KeyValuePair<EGAHTTPApiResponse, JSONObject> initResponse = GAHTTPApi.Instance.RequestInitReturningDict();
#endif

            StartNewSession(initResponse.Key, initResponse.Value);
        }

        public static void StartNewSession(EGAHTTPApiResponse initResponse, JSONObject initResponseDict)
        {
            // init is ok
            if(initResponse == EGAHTTPApiResponse.Ok && initResponseDict != null)
            {
                // set the time offset - how many seconds the local time is different from servertime
                long timeOffsetSeconds = 0;
                if(initResponseDict["server_ts"] != null)
                {
                    long serverTs = initResponseDict["server_ts"].AsLong;
                    timeOffsetSeconds = CalculateServerTimeOffset(serverTs);
                }
                initResponseDict.Add("time_offset", new JSONNumber(timeOffsetSeconds));

                // insert new config in sql lite cross session storage
                GAStore.SetState(SdkConfigCachedKey, initResponseDict.SaveToBinaryBase64());

                // set new config and cache in memory
                Instance.sdkConfigCached = initResponseDict;
                Instance.sdkConfig = initResponseDict;

                Instance.InitAuthorized = true;
            }
            else if(initResponse == EGAHTTPApiResponse.Unauthorized)
            {
                GALogger.W("Initialize SDK failed - Unauthorized");
                Instance.InitAuthorized = false;
            }
            else
            {
                // log the status if no connection
                if(initResponse == EGAHTTPApiResponse.NoResponse || initResponse == EGAHTTPApiResponse.RequestTimeout)
                {
                    GALogger.I("Init call (session start) failed - no response. Could be offline or timeout.");
                }
                else if(initResponse == EGAHTTPApiResponse.BadResponse || initResponse == EGAHTTPApiResponse.JsonEncodeFailed || initResponse == EGAHTTPApiResponse.JsonDecodeFailed)
                {
                    GALogger.I("Init call (session start) failed - bad response. Could be bad response from proxy or GA servers.");
                }
                else if(initResponse == EGAHTTPApiResponse.BadRequest || initResponse == EGAHTTPApiResponse.UnknownResponseCode)
                {
                    GALogger.I("Init call (session start) failed - bad request or unknown response.");
                }

                // init call failed (perhaps offline)
                if(Instance.sdkConfig == null)
                {
                    if(Instance.sdkConfigCached != null)
                    {
                        GALogger.I("Init call (session start) failed - using cached init values.");
                        // set last cross session stored config init values
                        Instance.sdkConfig = Instance.sdkConfigCached;
                    }
                    else
                    {
                        GALogger.I("Init call (session start) failed - using default init values.");
                        // set default init values
                        Instance.sdkConfig = Instance.sdkConfigDefault;
                    }
                }
                else
                {
                    GALogger.I("Init call (session start) failed - using cached init values.");
                }
                Instance.InitAuthorized = true;
            }

            {
                JSONNode currentSdkConfig = SdkConfig;

                if (currentSdkConfig["enabled"].IsBoolean && !currentSdkConfig["enabled"].AsBool)
                {
                    Instance.Enabled = false;
                }
                else if (!Instance.InitAuthorized)
                {
                    Instance.Enabled = false;
                }
                else
                {
                    Instance.Enabled = true;
                }
            }

            // set offset in state (memory) from current config (config could be from cache etc.)
            Instance.ClientServerTimeOffset = SdkConfig["time_offset"] != null ? SdkConfig["time_offset"].AsLong : 0;

            // populate configurations
            PopulateConfigurations(SdkConfig);

            // if SDK is disabled in config
            if(!IsEnabled())
            {
                GALogger.W("Could not start session: SDK is disabled.");
                // stop event queue
                // + make sure it's able to restart if another session detects it's enabled again
                GAEvents.StopEventQueue();
                return;
            }
            else
            {
                GAEvents.EnsureEventQueueIsRunning();
            }

            // generate the new session
            string newSessionId = Guid.NewGuid().ToString();
            string newSessionIdLowercase = newSessionId.ToLowerInvariant();

            // Set session id
            SessionId = newSessionIdLowercase;

            // Set session start
            SessionStart = GetClientTsAdjusted();

            // Add session start event
            GAEvents.AddSessionStartEvent();
        }

        private static void ValidateAndFixCurrentDimensions()
        {
            // validate that there are no current dimension01 not in list
            if (!GAValidator.ValidateDimension01(CurrentCustomDimension01))
            {
                GALogger.D("Invalid dimension01 found in variable. Setting to nil. Invalid dimension: " + CurrentCustomDimension01);
                SetCustomDimension01("");
            }
            // validate that there are no current dimension02 not in list
            if (!GAValidator.ValidateDimension02(CurrentCustomDimension02))
            {
                GALogger.D("Invalid dimension02 found in variable. Setting to nil. Invalid dimension: " + CurrentCustomDimension02);
                SetCustomDimension02("");
            }
            // validate that there are no current dimension03 not in list
            if (!GAValidator.ValidateDimension03(CurrentCustomDimension03))
            {
                GALogger.D("Invalid dimension03 found in variable. Setting to nil. Invalid dimension: " + CurrentCustomDimension03);
                SetCustomDimension03("");
            }
        }

        private static long CalculateServerTimeOffset(long serverTs)
        {
            long clientTs = GAUtilities.TimeIntervalSince1970();
            return serverTs - clientTs;
        }

        private static void PopulateConfigurations(JSONNode sdkConfig)
        {
            lock(Instance.configurationsLock)
            {
                JSONArray configurations = sdkConfig["configurations"].AsArray;

                if(configurations != null)
                {
                    for(int i = 0; i < configurations.Count; ++i)
                    {
                        JSONNode configuration = configurations[i];

                        if(configuration != null)
                        {
                            string key = configuration["key"].Value;
                            object value = null;
                            if(configuration["value"].IsNumber)
                            {
                                value = configuration["value"].AsDouble;
                            }
                            else
                            {
                                value = configuration["value"].Value;
                            }
                            long start_ts = configuration["start"].IsNumber ? configuration["start"].AsLong : long.MinValue;
                            long end_ts = configuration["end"].IsNumber ? configuration["end"].AsLong : long.MaxValue;

                            long client_ts_adjusted = GetClientTsAdjusted();

                            GALogger.D("PopulateConfigurations: key=" + key + ", value=" + value + ", start_ts=" + start_ts + ", end_ts=" + ", client_ts_adjusted=" + client_ts_adjusted);

                            if(key != null && value != null && client_ts_adjusted > start_ts && client_ts_adjusted < end_ts)
                            {
                                JSONObject json = new JSONObject();
                                if(configuration["value"].IsNumber)
                                {
                                    Instance.configurations.Add(key, new JSONNumber(configuration["value"].AsDouble));
                                }
                                else
                                {
                                    Instance.configurations.Add(key, configuration["value"].Value);
                                }

                                GALogger.D("configuration added: " + configuration);
                            }
                        }
                    }
                }
                Instance.commandCenterIsReady = true;
                foreach(ICommandCenterListener listener in Instance.commandCenterListeners)
                {
                    listener.OnCommandCenterUpdated();
                }
            }
        }

        #endregion // Private methods
    }
}
