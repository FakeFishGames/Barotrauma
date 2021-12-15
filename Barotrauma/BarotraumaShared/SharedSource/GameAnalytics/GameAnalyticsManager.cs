#nullable enable
using Barotrauma.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace Barotrauma
{
    public static partial class GameAnalyticsManager
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

            private readonly Action<string> setCustomDimension01;
            internal void SetCustomDimension01(string dimension01)
                => setCustomDimension01(dimension01);

            private readonly Action<string[]> configureAvailableCustomDimensions01;
            internal void ConfigureAvailableCustomDimensions01(params string[] customDimensions)
                => configureAvailableCustomDimensions01(customDimensions);

            private readonly Action<bool> setEnabledInfoLog;
            internal void SetEnabledInfoLog(bool enabled)
                => setEnabledInfoLog(enabled);
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
            #endregion

            private AssemblyLoadContext? loadContext;
            private Assembly? assembly;

            private string GetAssemblyPath(string assemblyName)
                => Path.Combine(
                        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
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

                MethodInfo getMethod(string name, Type[] types)
                {
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
                setEnabledInfoLog = Call<bool>(getMethod(nameof(SetEnabledInfoLog),
                    new Type[] { typeof(bool) }));

                onQuit = Call(getMethod("OnQuit", Array.Empty<Type>()));
            }

            private readonly Action? onQuit;
            private void OnQuit()
            {
                if (assembly != null) { onQuit?.Invoke(); }
            }

            public void Dispose()
            {
                if (loadContext is null) { return; }
                OnQuit();
                loadContext?.Unload();
                loadContext = null;
                assembly = null;
            }

            ~Implementation()
            {
                OnQuit();
            }
        }
        private static Implementation? loadedImplementation;

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

            if (GameMain.Config.AllEnabledPackages != null)
            {
                if (GameMain.VanillaContent == null || GameMain.Config.AllEnabledPackages.Any(p => p.HasMultiplayerIncompatibleContent && p != GameMain.VanillaContent))
                {
                    message = "[MODDED] " + message;
                }
            }

            loadedImplementation?.AddErrorEvent(errorSeverity, message);
            sentEventIdentifiers.Add(identifier);
        }

        public static void AddDesignEvent(string eventID)
        {
            if (!SendUserStatistics) { return; }
            loadedImplementation?.AddDesignEvent(eventID);
        }

        public static void AddDesignEvent(string eventID, double value)
        {
            if (!SendUserStatistics) { return; }
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

        public static void SetCustomDimension01(string dimension)
        {
            if (!SendUserStatistics) { return; }
            loadedImplementation?.SetCustomDimension01(dimension);
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
                using (var stream = File.OpenRead(exePath))
                {
                    exeHash = new Md5Hash(stream);
                }
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
                loadedImplementation?.ConfigureAvailableCustomDimensions01("singleplayer", "multiplayer", "editor");

                InitKeys();

                loadedImplementation?.AddDesignEvent("Executable:"
                    + GameMain.Version.ToString()
                    + exeName + ":"
                    + ((exeHash?.ShortHash == null) ? "Unknown" : exeHash.ShortHash) + ":"
                    + AssemblyInfo.GitBranch + ":"
                    + AssemblyInfo.GitRevision + ":"
                    + buildConfiguration);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Initializing GameAnalytics failed. Disabling user statistics...", e);
                SetConsent(Consent.Error);
                return;
            }

            var allPackages = GameMain.Config?.AllEnabledPackages.ToList();
            if (allPackages?.Count > 0)
            {
                StringBuilder sb = new StringBuilder("ContentPackage: ");
                int i = 0;
                foreach (ContentPackage cp in allPackages)
                {
                    string trimmedName = cp.Name.Replace(":", "").Replace(" ", "");
                    sb.Append(trimmedName.Substring(0, Math.Min(32, trimmedName.Length)));
                    if (i < allPackages.Count - 1) { sb.Append(" "); }
                }
                loadedImplementation?.AddDesignEvent(sb.ToString());
            }
        }

        static partial void InitKeys();

        public static void ShutDown()
        {
            loadedImplementation?.Dispose();
            loadedImplementation = null;
        }
    }
}
