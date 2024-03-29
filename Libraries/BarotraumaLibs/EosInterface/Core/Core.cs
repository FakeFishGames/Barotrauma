using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Barotrauma;

public static partial class EosInterface
{
    public static class Core
    {
        internal static Implementation? LoadedImplementation { get; private set; } = null;
        private static AssemblyLoadContext? assemblyLoadContext = null;

        private static bool hasShutDown = false;
        private static bool failedToInitialize = false;

        private static string GetAssemblyPath(string assemblyName)
            => Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                $"{assemblyName}.dll");

        private static bool resolvingDependency;

        private static Assembly? ResolveDependency(AssemblyLoadContext context, AssemblyName dependencyName)
        {
            if (resolvingDependency)
            {
                return null;
            }

            resolvingDependency = true;
            Assembly dependency =
                context.LoadFromAssemblyPath(
                    GetAssemblyPath(dependencyName.Name ?? throw new Exception("Dependency name was null")));
            resolvingDependency = false;
            return dependency;
        }

        public enum InitError
        {
            PlatformInterfaceNotCreated,
            AlreadyInitialized,
            UnknownOsPlatform,
            ImplementationDllLoadFailed,
            ImplementationDllHasNoValidClasses,
            ImplementationFailedToInstantiate,
            NativeDllLoadFailed,
            CannotRestartAfterShutdown,
            UnhandledErrorCondition
        }

        public enum Status
        {
            NotInitialized,
            InitializationError,
            ShutDown,
            InitializedButOffline,
            Online
        }

        public static bool IsInitialized
            => LoadedImplementation != null && LoadedImplementation.IsInitialized();

        public static Status CurrentStatus
        {
            get
            {
                if (hasShutDown)
                {
                    return Status.ShutDown;
                }

                if (failedToInitialize)
                {
                    return Status.InitializationError;
                }

                if (LoadedImplementation is { CurrentStatus: var status })
                {
                    return status;
                }

                return Status.NotInitialized;
            }
        }

        public static Result<Unit, InitError> Init(ApplicationCredentials applicationCredentials, bool enableOverlay)
        {
            var (success, failure) = Result<Unit, InitError>.GetFactoryMethods();
            if (LoadedImplementation != null)
            {
                return !LoadedImplementation.IsInitialized()
                    ? LoadedImplementation.Init(applicationCredentials, enableOverlay)
                    : failure(InitError.AlreadyInitialized);
            }

            if (hasShutDown)
            {
                return failure(InitError.CannotRestartAfterShutdown);
            }

            string platformSuffix;
            string nativeDllName;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                platformSuffix = "Win64";
                nativeDllName = "./EOSSDK-Win64-Shipping.dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                platformSuffix = "MacOS";
                nativeDllName = "./libEOSSDK-Mac-Shipping.dylib";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                platformSuffix = "Linux";
                nativeDllName = "./libEOSSDK-Linux-Shipping.so";
            }
            else
            {
                failedToInitialize = true;
                return failure(InitError.UnknownOsPlatform);
            }

            if (!NativeLibrary.TryLoad(nativeDllName, out var nativeLib))
            {
                failedToInitialize = true;
                return failure(InitError.NativeDllLoadFailed);
            }

            NativeLibrary.Free(nativeLib);

            string assemblyName = $"EosInterface.Implementation.{platformSuffix}";

            assemblyLoadContext = new AssemblyLoadContext(assemblyName, isCollectible: true);
            assemblyLoadContext.Resolving += ResolveDependency;

            Assembly implementationAssembly;
            try
            {
                implementationAssembly = assemblyLoadContext.LoadFromAssemblyPath(GetAssemblyPath(assemblyName));
            }
            catch
            {
                failedToInitialize = true;
                return failure(InitError.ImplementationDllLoadFailed);
            }

            var implementationTypes =
                implementationAssembly.DefinedTypes
                    .Where(t => t.IsSubclassOf(typeof(Implementation)))
                    .Where(t => t is { IsAbstract: false, IsGenericType: false })
                    .ToArray();
            if (!implementationTypes.Any())
            {
                failedToInitialize = true;
                return failure(InitError.ImplementationDllHasNoValidClasses);
            }

            Implementation implementationInstance;
            try
            {
                var implementationInstanceNullable =
                    (Implementation?)Activator.CreateInstance(implementationTypes.First());
                if (implementationInstanceNullable is null)
                {
                    failedToInitialize = true;
                    return failure(InitError.ImplementationFailedToInstantiate);
                }

                implementationInstance = implementationInstanceNullable;
            }
            catch
            {
                failedToInitialize = true;
                return failure(InitError.ImplementationFailedToInstantiate);
            }

            LoadedImplementation = implementationInstance;

            var initResult = implementationInstance.Init(applicationCredentials, enableOverlay);
            if (initResult.IsFailure)
            {
                failedToInitialize = true;
            }

            return initResult;
        }

        public enum WillRestartThroughLauncher
        {
            No,
            Yes
        }

        public enum CheckForLauncherAndRestartError
        {
            EosNotInitialized,
            UnexpectedError,
            UnhandledErrorCondition
        }

        public static Result<WillRestartThroughLauncher, CheckForLauncherAndRestartError> CheckForLauncherAndRestart()
            => LoadedImplementation.IsInitialized()
                ? LoadedImplementation.CheckForLauncherAndRestart()
                : Result.Failure(CheckForLauncherAndRestartError.EosNotInitialized);

        public static void Update()
        {
            if (LoadedImplementation.IsInitialized())
            {
                LoadedImplementation.Update();
            }
        }

        public static void CleanupAndQuit()
        {
            var loadedImplementation = LoadedImplementation;
            if (!loadedImplementation.IsInitialized())
            {
                return;
            }

            TaskPool.Add(
                "CleanupAndQuit",
                loadedImplementation.CloseAllOwnedSessions(),
                _ => QuitNow());
        }

        private static void QuitNow()
        {
            hasShutDown = CurrentStatus != Status.NotInitialized;
            LoadedImplementation?.Quit();
            LoadedImplementation = null;
            assemblyLoadContext?.Unload();
            assemblyLoadContext = null;
        }
    }

    internal abstract partial class Implementation
    {
        public abstract Core.Status CurrentStatus { get; }
        public abstract string NativeLibraryName { get; }

        public abstract Result<Unit, Core.InitError> Init(ApplicationCredentials applicationCredentials,
            bool enableOverlay);

        public abstract Result<Core.WillRestartThroughLauncher, Core.CheckForLauncherAndRestartError>
            CheckForLauncherAndRestart();

        public abstract void Update();
        public abstract void Quit();
    }
}