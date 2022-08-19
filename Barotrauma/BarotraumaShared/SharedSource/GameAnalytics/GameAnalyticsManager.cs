#nullable enable
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace Barotrauma
{
    static partial class GameAnalyticsManager
    {
        public enum ErrorSeverity
        {
            Undefined = 0,
            Debug = 1,
            Info = 2,
            Warning = 3,
            Error = 4,
            Critical = 5
        }

        public enum ProgressionStatus
        {
            Undefined = 0,
            Start = 1,
            Complete = 2,
            Fail = 3
        }

        public enum CustomDimensions01
        {
            Vanilla,
            Modded
        }

        public enum CustomDimensions02
        {
            None,
            Difficulty0to10,
            Difficulty10to20,
            Difficulty20to30,
            Difficulty30to40,
            Difficulty40to50,
            Difficulty50to60,
            Difficulty60to70,
            Difficulty70to80,
            Difficulty80to90,
            Difficulty90to100,
        }

        public enum ResourceCurrency
        {
            Money
        }

        public enum ResourceFlowType
        {
            Undefined = 0,
            Source = 1,
            Sink = 2
        }

        public enum MoneySource
        {
            Unknown,
            MissionReward,
            Store,
            Event,
            Ability,
            Cheat
        }

        public enum MoneySink
        {
            Unknown,
            Store,
            Service,
            Crew,
            SubmarineUpgrade,
            SubmarineWeapon,
            SubmarinePurchase,
            SubmarineSwitch
        }

        private readonly static HashSet<string> sentEventIdentifiers = new HashSet<string>();

        private class Implementation : IDisposable
        {
            #region GameAnalytics methods
            private readonly Action<string, string> initialize;
            internal void Initialize(string gameKey, string secretKey)
                => initialize(gameKey, secretKey);

            private readonly Action<string> configureBuild;
            internal void ConfigureBuild(string config) => configureBuild(config);

            private readonly Action<ErrorSeverity, string> addErrorEvent;
            internal void AddErrorEvent(ErrorSeverity severity, string message)
                => addErrorEvent(severity, message);

            private readonly Action<string, IDictionary<string, object>?> addDesignEvent0;
            internal void AddDesignEvent(string message, IDictionary<string, object>? fields = null)
                => addDesignEvent0(message, fields);

            private readonly Action<string, double> addDesignEvent1;
            internal void AddDesignEvent(string message, double value)
                => addDesignEvent1(message, value);

            private readonly Action<ProgressionStatus, string> addProgressionEvent01;
            internal void AddProgressionEvent(ProgressionStatus status, string progression01)
                => addProgressionEvent01(status, progression01);

            private readonly Action<ProgressionStatus, string, double> addProgressionEvent01Score;
            internal void AddProgressionEvent(ProgressionStatus status, string progression01, double score)
                => addProgressionEvent01Score(status, progression01, score);

            private readonly Action<ProgressionStatus, string, string> addProgressionEvent02;
            internal void AddProgressionEvent(ProgressionStatus status, string progression01, string progression02)
                => addProgressionEvent02(status, progression01, progression02);

            private readonly Action<ProgressionStatus, string, string, string> addProgressionEvent03;
            internal void AddProgressionEvent(ProgressionStatus status, string progression01, string progression02, string progression03)
                => addProgressionEvent03(status, progression01, progression02, progression03);

            private readonly Action<ResourceFlowType, string, float, string, string> addResourceEvent;
            internal void AddResourceEvent(ResourceFlowType flowType, string currency, float amount, string itemType, string itemId)
                => addResourceEvent(flowType, currency, amount, itemType, itemId);

            private readonly Action<string> setCustomDimension01;
            internal void SetCustomDimension01(string dimension01)
                => setCustomDimension01(dimension01);

            private readonly Action<string[]> configureAvailableCustomDimensions01;
            internal void ConfigureAvailableCustomDimensions01(params CustomDimensions01[] customDimensions)
                => configureAvailableCustomDimensions01(customDimensions.Select(d => d.ToString()).ToArray());

            private readonly Action<string> setCustomDimension02;
            internal void SetCustomDimension02(string dimension02)
                => setCustomDimension02(dimension02);

            private readonly Action<string[]> configureAvailableCustomDimensions02;
            internal void ConfigureAvailableCustomDimensions02(params CustomDimensions02[] customDimensions)
                => configureAvailableCustomDimensions02(customDimensions.Select(d => d.ToString()).ToArray());

            private readonly Action<string[]> configureAvailableResourceCurrencies;
            internal void ConfigureAvailableResourceCurrencies(params ResourceCurrency[] customDimensions)
                => configureAvailableResourceCurrencies(customDimensions.Select(d => d.ToString()).ToArray());

            private readonly Action<string[]> configureAvailableResourceItemTypes;
            internal void ConfigureAvailableResourceItemTypes(params string[] resourceItemTypes)
                => configureAvailableResourceItemTypes(resourceItemTypes);

            private readonly Action<bool> setEnabledInfoLog;
            internal void SetEnabledInfoLog(bool enabled)
                => setEnabledInfoLog(enabled);

            private readonly Action<bool> setEnabledVerboseLog;
            internal void SetEnabledVerboseLog(bool enabled)
                => setEnabledVerboseLog(enabled);
            #endregion

            #region Data required to fetch methods via reflection
            private const string AssemblyName = "GameAnalytics.NetStandard";
            private const string Namespace = "GameAnalyticsSDK.Net";
            private const string MainClass = "GameAnalytics";
            private const string EnumPrefix = "EGA";
            #endregion

            #region Call implementations
            private readonly object?[] args1 = new object?[1];
            private readonly object?[] args2 = new object?[2];
            private readonly object?[] args3 = new object?[3];
            private readonly object?[] args4 = new object?[4];
            private readonly object?[] args5 = new object?[5];

            private Action Call(MethodInfo methodInfo)
                => () => methodInfo?.Invoke(null, null);

            private Action<T> Call<T>(MethodInfo methodInfo)
                => (T arg1) =>
                {
                    args1[0] = arg1;
                    methodInfo.Invoke(null, args1);
                };

            private Action<T1, T2> Call<T1, T2>(MethodInfo methodInfo)
                => (T1 arg1, T2 arg2) =>
                {
                    args2[0] = arg1;
                    args2[1] = arg2;
                    methodInfo.Invoke(null, args2);
                };

            private Action<T1, T2, T3> Call<T1, T2, T3>(MethodInfo methodInfo)
                => (T1 arg1, T2 arg2, T3 arg3) =>
                {
                    args3[0] = arg1;
                    args3[1] = arg2;
                    args3[2] = arg3;
                    methodInfo.Invoke(null, args3);
                };

            private Action<T1, T2, T3, T4> Call<T1, T2, T3, T4>(MethodInfo methodInfo)
                => (T1 arg1, T2 arg2, T3 arg3, T4 arg4) =>
                {
                    args4[0] = arg1;
                    args4[1] = arg2;
                    args4[2] = arg3;
                    args4[3] = arg4;
                    methodInfo.Invoke(null, args4);
                };

            private Action<T1, T2, T3, T4, T5> Call<T1, T2, T3, T4, T5>(MethodInfo methodInfo)
                => (T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) =>
                {
                    args5[0] = arg1;
                    args5[1] = arg2;
                    args5[2] = arg3;
                    args5[3] = arg4;
                    args5[4] = arg5;
                    methodInfo.Invoke(null, args5);
                };
            #endregion

            private AssemblyLoadContext? loadContext;
            private Assembly? assembly;

            private string GetAssemblyPath(string assemblyName)
                => Path.Combine(
                        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                        $"{assemblyName}.dll");

            private bool resolvingDependency;
            private Assembly? ResolveDependency(AssemblyLoadContext context, AssemblyName dependencyName)
            {
                if (resolvingDependency) { return null; }
                resolvingDependency = true;
                Assembly dep = context.LoadFromAssemblyPath(GetAssemblyPath(dependencyName.Name ?? throw new Exception("Dependency name was null")));
                resolvingDependency = false;
                return dep;
            }

            internal Implementation()
            {
                loadContext = new AssemblyLoadContext(AssemblyName, isCollectible: true);
                loadContext.Resolving += ResolveDependency;
                assembly = loadContext.LoadFromAssemblyPath(
                    GetAssemblyPath(AssemblyName));

                Type getType(string name)
                    => assembly.GetType($"{Namespace}.{name}")
                        ?? throw new Exception($"Could not find type\"{Namespace}.{name}\"");

                var mainClass = getType(MainClass);
                var errorSeverityEnumType = getType($"{EnumPrefix}{nameof(ErrorSeverity)}");
                var progressionStatusEnumType = getType($"{EnumPrefix}{nameof(ProgressionStatus)}");
                var resourceFlowTypeEnumType = getType($"{EnumPrefix}{nameof(ResourceFlowType)}");

                MethodInfo getMethod(string name, Type[] types)
                {
                    foreach (var me in mainClass.GetMethods())
                    {
                        var aksjdnakjsdnf = me;
                    }

                    return mainClass?.GetMethod(name, BindingFlags.Public | BindingFlags.Static, binder: null, types: types, modifiers: null)
                        ?? throw new Exception($"Could not find method \"{name}\" with types {string.Join(',', types.Select(t => t.Name))}");
                }

                initialize = Call<string, string>(getMethod(nameof(Initialize),
                    new Type[] { typeof(string), typeof(string) }));
                configureBuild = Call<string>(getMethod(nameof(ConfigureBuild),
                    new Type[] { typeof(string) }));
                addErrorEvent = Call<ErrorSeverity, string>(getMethod(nameof(AddErrorEvent),
                    new Type[] { errorSeverityEnumType, typeof(string) }));
                addDesignEvent0 = Call<string, IDictionary<string, object>?>(getMethod(nameof(AddDesignEvent),
                    new Type[] { typeof(string), typeof(IDictionary<string, object>) }));
                addDesignEvent1 = Call<string, double>(getMethod(nameof(AddDesignEvent),
                    new Type[] { typeof(string), typeof(double) }));
                addProgressionEvent01 = Call<ProgressionStatus, string>(getMethod(nameof(AddProgressionEvent),
                    new Type[] { progressionStatusEnumType, typeof(string) }));
                addProgressionEvent01Score = Call<ProgressionStatus, string, double>(getMethod(nameof(AddProgressionEvent),
                    new Type[] { progressionStatusEnumType, typeof(string), typeof(double) }));
                addProgressionEvent02 = Call<ProgressionStatus, string, string>(getMethod(nameof(AddProgressionEvent),
                    new Type[] { progressionStatusEnumType, typeof(string), typeof(string) }));
                addProgressionEvent03 = Call<ProgressionStatus, string, string, string>(getMethod(nameof(AddProgressionEvent),
                    new Type[] { progressionStatusEnumType, typeof(string), typeof(string), typeof(string) }));

                setCustomDimension01 = Call<string>(getMethod(nameof(SetCustomDimension01),
                    new Type[] { typeof(string) }));
                configureAvailableCustomDimensions01 = Call<string[]>(getMethod(nameof(ConfigureAvailableCustomDimensions01),
                    new Type[] { typeof(string[]) }));
                setCustomDimension02 = Call<string>(getMethod(nameof(SetCustomDimension02),
                    new Type[] { typeof(string) }));
                configureAvailableCustomDimensions02 = Call<string[]>(getMethod(nameof(ConfigureAvailableCustomDimensions02),
                    new Type[] { typeof(string[]) }));

                configureAvailableResourceCurrencies = Call<string[]>(getMethod(nameof(ConfigureAvailableResourceCurrencies),
                    new Type[] { typeof(string[]) }));
                configureAvailableResourceItemTypes = Call<string[]>(getMethod(nameof(ConfigureAvailableResourceItemTypes),
                    new Type[] { typeof(string[]) }));
                addResourceEvent = Call<ResourceFlowType, string, float, string, string>(getMethod(nameof(AddResourceEvent),
                    new Type[] { resourceFlowTypeEnumType, typeof(string), typeof(float), typeof(string), typeof(string) }));
                setEnabledInfoLog = Call<bool>(getMethod(nameof(SetEnabledInfoLog),
                    new Type[] { typeof(bool) }));
                setEnabledVerboseLog = Call<bool>(getMethod(nameof(SetEnabledVerboseLog),
                    new Type[] { typeof(bool) }));

                onQuit = Call(getMethod("OnQuit", Array.Empty<Type>()));
            }

            private readonly Action? onQuit;
            private void OnQuit()
            {
                try
                {                    
                    if (assembly != null) { onQuit?.Invoke(); }
                }
                catch (Exception e)
                {
                    e = e.GetInnermost();

                    DebugConsole.AddWarning($"Failed to call GameAnalytics.OnQuit: {e.Message} {e.StackTrace}");
                    //If this happens then GameAnalytics is just broken,
                    //let's just hope that it uninitialized correctly and
                    //allow the game to keep running
                }
            }

            public void Dispose()
            {
                if (loadContext is null) { return; }

                OnQuit();
                loadContext?.Unload();
                loadContext = null;
                assembly = null;
            }
        }
        private static Implementation? loadedImplementation;

        private static void ValidateEventID(string eventID)
        {
#if DEBUG
            string[] parts = eventID.Split(':');
            if (parts.Length > 5)
            {
                DebugConsole.ThrowError($"Invalid GameAnalytics event id \"{eventID}\". Only 5 id parts allowed separated by ':'");
            }
            if (parts.Any(p => p.Length > 32))
            {
                DebugConsole.ThrowError($"Invalid GameAnalytics event id \"{eventID}\". Each id part separated by ':' must be 32 characters or less.");
            }
#endif
        }

        public static void AddErrorEvent(ErrorSeverity errorSeverity, string message)
        {
            if (!SendUserStatistics) { return; }
            loadedImplementation?.AddErrorEvent(errorSeverity, message);
        }

        /// <summary>
        /// Adds an error event to GameAnalytics if an event with the same identifier has not been added yet.
        /// </summary>
        public static void AddErrorEventOnce(string identifier, ErrorSeverity errorSeverity, string message)
        {
            if (!SendUserStatistics) { return; }
            if (sentEventIdentifiers.Contains(identifier)) { return; }

            if (GameMain.VanillaContent == null || ContentPackageManager.EnabledPackages.All.Any(p => p.HasMultiplayerSyncedContent && p != GameMain.VanillaContent))
            {
                message = "[MODDED] " + message;
            }

            loadedImplementation?.AddErrorEvent(errorSeverity, message);
            sentEventIdentifiers.Add(identifier);
        }

        public static void AddDesignEvent(string eventID)
        {
            if (!SendUserStatistics) { return; }
            ValidateEventID(eventID);
            loadedImplementation?.AddDesignEvent(eventID);
        }

        public static void AddDesignEvent(string eventID, double value)
        {
            if (!SendUserStatistics) { return; }
            ValidateEventID(eventID);
            loadedImplementation?.AddDesignEvent(eventID, value);
        }

        public static void AddProgressionEvent(ProgressionStatus progressionStatus, string progression01)
        {
            if (!SendUserStatistics) { return; }
            loadedImplementation?.AddProgressionEvent(progressionStatus, progression01);
        }

        public static void AddProgressionEvent(ProgressionStatus progressionStatus, string progression01, double score)
        {
            if (!SendUserStatistics) { return; }
            loadedImplementation?.AddProgressionEvent(progressionStatus, progression01, score);
        }

        public static void AddProgressionEvent(ProgressionStatus progressionStatus, string progression01, string progression02)
        {
            if (!SendUserStatistics) { return; }
            loadedImplementation?.AddProgressionEvent(progressionStatus, progression01, progression02);
        }

        public static void AddProgressionEvent(ProgressionStatus progressionStatus, string progression01, string progression02, string progression03)
        {
            if (!SendUserStatistics) { return; }
            loadedImplementation?.AddProgressionEvent(progressionStatus, progression01, progression02, progression03);
        }

        public static void SetCustomDimension01(CustomDimensions01 dimension)
        {
            if (!SendUserStatistics) { return; }
            loadedImplementation?.SetCustomDimension01(dimension.ToString());
        }

        public static void SetCurrentLevel(LevelData levelData)
        {
            if (!SendUserStatistics) { return; }

            CustomDimensions02 customDimension = CustomDimensions02.None;
            if (levelData != null)
            {
                float levelDifficulty = levelData.Difficulty;
                customDimension = (CustomDimensions02)MathHelper.Clamp((int)(levelDifficulty / 10) + 1, 0, Enum.GetValues(typeof(CustomDimensions02)).Length - 1);
            }

            loadedImplementation?.SetCustomDimension02(customDimension.ToString());
        }

        public static void AddMoneyGainedEvent(int amount, MoneySource moneySource, string eventId)
        {
            AddResourceEvent(ResourceFlowType.Source, ResourceCurrency.Money, amount, moneySource.ToString(), eventId);
        }

        public static void AddMoneySpentEvent(int amount, MoneySink moneySink, string eventId)
        {
            AddResourceEvent(ResourceFlowType.Sink, ResourceCurrency.Money, amount, moneySink.ToString(), eventId);
        }

        private static void AddResourceEvent(ResourceFlowType flowType, ResourceCurrency currency, float amount, string eventType, string eventId)
        {
            if (!SendUserStatistics) { return; }
            loadedImplementation?.AddResourceEvent(flowType, currency.ToString(), amount, eventType, eventId);
        }

        private static void Init()
        {
            ShutDown();
            try
            {
                loadedImplementation = new Implementation();
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Initializing GameAnalytics failed. Disabling user statistics...", e);
                SetConsent(Consent.Error);
                return;
            }
#if DEBUG
            try
            {
                loadedImplementation?.SetEnabledInfoLog(true);
                loadedImplementation?.SetEnabledVerboseLog(true);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Initializing GameAnalytics failed. Disabling user statistics...", e);
                SetConsent(Consent.Error);
                return;
            }
#endif

            string exePath = Assembly.GetEntryAssembly()!.Location;
            string? exeName = string.Empty;
#if SERVER
            exeName = "s";
#endif
            Md5Hash? exeHash = null;
            try
            {
                exeHash = Md5Hash.CalculateForFile(exePath, Md5Hash.StringHashOptions.BytePerfect);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error while calculating MD5 hash for the executable \"" + exePath + "\"", e);
            }
            try
            {
                string buildConfiguration = "Release";
#if DEBUG
                buildConfiguration = "Debug";
#elif UNSTABLE
                buildConfiguration = "Unstable";
#endif
                loadedImplementation?.ConfigureBuild(GameMain.Version.ToString()
                    + exeName + ":"
                    + AssemblyInfo.GitRevision + ":"
                    + buildConfiguration);
                loadedImplementation?.ConfigureAvailableCustomDimensions01(Enum.GetValues(typeof(CustomDimensions01)).Cast<CustomDimensions01>().ToArray());
                loadedImplementation?.ConfigureAvailableCustomDimensions02(Enum.GetValues(typeof(CustomDimensions02)).Cast<CustomDimensions02>().ToArray());
                loadedImplementation?.ConfigureAvailableResourceCurrencies(Enum.GetValues(typeof(ResourceCurrency)).Cast<ResourceCurrency>().ToArray());
                loadedImplementation?.ConfigureAvailableResourceItemTypes(
                    Enum.GetValues(typeof(MoneySink)).Cast<MoneySink>().Select(s => s.ToString()).Union(Enum.GetValues(typeof(MoneySource)).Cast<MoneySource>().Select(s => s.ToString())).ToArray());

                InitKeys();

                loadedImplementation?.AddDesignEvent("Executable:"
                    + GameMain.Version.ToString()
                    + exeName + ":"
                    + (exeHash?.ShortRepresentation ?? "Unknown") + ":"
                    + AssemblyInfo.GitRevision + ":"
                    + buildConfiguration);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Initializing GameAnalytics failed. Disabling user statistics...", e);
                SetConsent(Consent.Error);
                return;
            }

            var allPackages = ContentPackageManager.EnabledPackages.All.ToList();
            if (allPackages?.Count > 0)
            {
                List<string> packageNames = new List<string>();
                foreach (ContentPackage cp in allPackages)
                {
                    string sanitizedName = cp.Name.Replace(":", "").Replace(" ", "");
                    sanitizedName = sanitizedName.Substring(0, Math.Min(32, sanitizedName.Length));
                    packageNames.Add(sanitizedName);
                    loadedImplementation?.AddDesignEvent("ContentPackage:" + sanitizedName);
                }
                packageNames.Sort();
                loadedImplementation?.AddDesignEvent("AllContentPackages:" + string.Join(" ", packageNames));
            }
            loadedImplementation?.AddDesignEvent("Language:" + GameSettings.CurrentConfig.Language);

        }

        static partial void InitKeys();

        public static void ShutDown()
        {
            loadedImplementation?.Dispose();
            loadedImplementation = null;
        }
    }
}
