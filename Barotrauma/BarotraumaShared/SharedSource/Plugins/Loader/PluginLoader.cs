#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma.Plugins
{
    public enum PluginLoadResult
    {
        /// <summary>
        /// The plugin was loaded successfully.
        /// </summary>
        Success,

        /// <summary>
        /// A signature was included with the assembly but did not match the assembly.
        /// See <see cref="AssemblyVerificationResult.InvalidSignature"/>.
        /// </summary>
        InvalidSignature,

        /// <summary>
        /// There was an error loading the .dll file.
        /// Errors could be caused by the file not existing, the .dll being invalid or the file being blocked.
        /// </summary>
        AssemblyFailedToLoad,

        /// <summary>
        /// The plugin's specified game version is less than <see cref="PluginLoader.MinSupportedGameVersion"/>
        /// and would not work with the game anymore.
        /// </summary>
        OutdatedPlugin,

        /// <summary>
        /// The plugin is missing metadata used to identify it.
        /// The PluginToolbox project in the example repository can be used to generate this metadata.
        /// </summary>
        MissingMetadata,

        /// <summary>
        /// Plugin threw an exception during initialization.
        /// </summary>
        PluginThrewException
    }

    public enum AssemblyVerificationResult
    {
        /// <summary>
        /// The assembly was not signed.
        /// This is not an error, mods should not be signed.
        /// </summary>
        MissingSignature,

        /// <summary>
        /// The assembly was signed but the signature did not match the assembly.
        /// This is an error and the assembly will not be loaded.
        /// </summary>
        InvalidSignature,

        /// <summary>
        /// The assembly was signed and verified successfully.
        /// Warnings can be relaxed since we know the assembly is from us.
        /// </summary>
        Success
    }

    public readonly record struct PluginMetadata(
        string Name,
        Version GameVersion,
        Version Version,
        uint IdentifierHash,
        Option<string> RepositoryUrl,
        bool IsTrusted,
        string? Authors);

    internal static class PluginLoader
    {
        public static readonly Version MinSupportedGameVersion = new(0, 0, 0, 0);

        internal readonly record struct LoadedPlugin(
            PluginLoadContext PluginLoadContext,
            PluginMetadata Metadata,
            Assembly Assembly,
            PluginInfoFile Info,
            PluginData Data,
            List<IBarotraumaPlugin> PluginMains)
        {
            public void Unload()
            {
                try
                {
                    PluginMains.ForEach(static x => x.Dispose());
                }
                catch (Exception e)
                {
                    OnPluginThrowUnhandledException(e, Assembly);
                }

                PluginMains.Clear();
                Data.Dispose();
                PluginLoadContext.Unload();
            }
        }

        internal class UnloadedPlugin
        {
            public readonly WeakReference<PluginLoadContext> PluginLoadContext;
            public readonly PluginInfoFile PluginInfoFile;
            public readonly DateTime UnloadTime;

            public bool IsUnloading => PluginLoadContext.IsAlive();

            public UnloadedPlugin(WeakReference<PluginLoadContext> pluginLoadContext, PluginInfoFile pluginInfoFile)
            {
                PluginLoadContext = pluginLoadContext;
                PluginInfoFile = pluginInfoFile;
                UnloadTime = DateTime.Now;
            }
        }

        internal static IEnumerable<LoadedPlugin> LoadedPlugins => loadedPlugins;
        private static readonly List<LoadedPlugin> loadedPlugins = [];

        internal static IEnumerable<UnloadedPlugin> UnloadedPlugins => unloadedPlugins;
        private static readonly List<UnloadedPlugin> unloadedPlugins = [];
        
        private static readonly Dictionary<LoadedPlugin, List<ITransientService>> TransientServices = [];

        /// <summary>
        /// Tries to find an already existing service in the specified assembly.
        /// </summary>
        internal static bool FindAlreadyExistingService<T>(Assembly assembly, [NotNullWhen(true)] out T? existingService) where T: IPluginService
        {
            foreach (var (plugin, services) in TransientServices)
            {
                if (plugin.Assembly == assembly)
                {
                    foreach (ITransientService transientService in services)
                    {
                        if (transientService is T tService)
                        {
                            existingService = tService;
                            return true;
                        }
                    }
                }
            }

            existingService = default;
            return false;
        }

        /// <summary>
        /// Registers a transient service to be disposed when the plugin is unloaded.
        /// </summary>
        /// <param name="assembly">Which plugin the service belongs to.</param>
        /// <param name="service">The service to dispose later.</param>
        internal static void AddTransientService(Assembly assembly, ITransientService service)
        {
            if (!loadedPlugins.FirstOrNone(la => la.Assembly == assembly).TryUnwrap(out LoadedPlugin loadedPlugin))
            {
                DebugConsole.ThrowError($"Attempted to add a transient service to an assembly that is not loaded: {assembly.GetName().FullName}", pluginAssembly: assembly);
                return;
            }

            if (!TransientServices.TryGetValue(loadedPlugin, out List<ITransientService>? services))
            {
                services = [];
                TransientServices.Add(loadedPlugin, services);
            }

            services.Add(service);
        }

        // Some (not all) known 'vanilla' assemblies
        private static readonly Assembly[] NonModAssemblies =
        [
            // Barotrauma.dll
            typeof(GameMain).Assembly,
            // BarotraumaCore.dll
            typeof(Identifier).Assembly,
            // CoreLib.dll - .NET Core assembly
            typeof(object).Assembly
        ];

        /// <summary>
        /// Tries to find a plugin assembly that called this method.
        /// </summary>
        /// <returns>
        /// A plugin assembly or null if the method was called from a vanilla assembly.
        /// </returns>
        /// <remarks>
        /// This method is very slow and should not be used in performance-critical code.
        /// </remarks>
        internal static Assembly? FindCallingPluginAssembly()
        {
            StackTrace stackTrace = new StackTrace(skipFrames: 2);

            foreach (StackFrame frame in stackTrace.GetFrames())
            {
                Assembly? assembly = frame.GetMethod()?.Module.Assembly;
                if (assembly is null) { continue; }

                if (NonModAssemblies.Contains(assembly)) { continue; }

                foreach (LoadedPlugin plugin in loadedPlugins)
                {
                    if (plugin.Assembly == assembly)
                    {
                        return assembly;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to find the plugin that caused the exception by analyzing the stack trace of an exception.
        /// </summary>
        /// <param name="e">The exception to analyze.</param>
        /// <returns>The content package and metadata of the crashing plugin, if found. Otherwise, None.</returns>
        internal static Option<(ContentPackage ContentPackage, PluginMetadata Metadata)> TryFindCrashingPluginInfo(Exception e)
        {
            StackTrace stackTrace = new StackTrace(e, fNeedFileInfo: false);

            foreach (StackFrame frame in stackTrace.GetFrames().Reverse())
            {
                MethodBase? method = frame.GetMethod();
                if (method is null) { continue; }

                Assembly assembly = method.Module.Assembly;

                // Don't bother checking known non-mod assemblies
                if (NonModAssemblies.Contains(assembly)) { continue; }

                if (!loadedPlugins.FirstOrNone(la => la.PluginLoadContext.Assemblies.Contains(assembly)).TryUnwrap(out var plugin)) { continue; }

                return Option.Some((plugin.Info.ContentPackage!, plugin.Metadata));
            }

            return Option.None;
        }

        private static void OnPluginThrowUnhandledException(Exception e, Assembly assembly)
        {
            var contentPackage = TryFindContentPackageForAssembly(assembly);

            DebugConsole.ThrowError($"Plugin {assembly.GetName().Name} threw an unhandled exception.", e, pluginAssembly: assembly, contentPackage: contentPackage);
        }

        /// <summary>
        /// Unloads a plugin and disposes all services associated with it.
        /// </summary>
        /// <param name="pluginInfo">The plugin info associated with the assembly to unload.</param>
        /// <returns>
        /// WeakReference to the AssemblyLoadContext if the plugin was loaded in the first place.
        /// Used to track when the assembly has been cleanly unloaded and collected by the GC.
        /// </returns>
        /// <remarks>
        /// Unload order: 1) <see cref="IDisposable.Dispose"/> is called on the plugin's <see cref="IBarotraumaPlugin"/> instances,
        /// 2) the plugin's <see cref="PluginData"/> (hooks etc.) is disposed, 3) the plugin's load context is unloaded,
        /// 4) the plugin's transient services are disposed.
        /// <br/>
        /// This method absolutely cannot be inlined as it will cause tomfoolery with the weak reference
        /// and prevent the assembly from being unloaded.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void UnloadPlugin(PluginInfoFile pluginInfoFile)
        {
            foreach (LoadedPlugin loadedPlugin in loadedPlugins.ToArray())
            {
                if (loadedPlugin.Info == pluginInfoFile)
                {
                    loadedPlugin.Unload();
                    if (TransientServices.TryGetValue(loadedPlugin, out List<ITransientService>? services))
                    {
                        foreach (ITransientService service in services)
                        {
                            service.Dispose();
                        }
                    }

                    TransientServices.Remove(loadedPlugin);
                    loadedPlugins.Remove(loadedPlugin);

                    ReconstructPluginDataList();
                    PurgeCache();

                    WeakReference<PluginLoadContext> alcWeakRef = new(loadedPlugin.PluginLoadContext, trackResurrection: true);
                    var unloadedPlugin = new UnloadedPlugin(alcWeakRef, pluginInfoFile);
                    unloadedPlugins.Add(unloadedPlugin);
                    PluginUnloadVerifier.EnqueueUnloadVerification(unloadedPlugin);
                    return;
                }
            }

            DebugConsole.ThrowError($"Failed to unload plugin from {pluginInfoFile.ContentPackage.Name} because it was not loaded.");
        }

        private static void ReconstructPluginDataList()
            => PluginData.LoadedPluginData = [..loadedPlugins.Select(static lp => lp.Data)];

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static PluginLoadResult TryLoadPlugin(PluginInfoFile pluginInfoFile)
        {
            if (pluginInfoFile.VerificationResult is AssemblyVerificationResult.InvalidSignature)
            {
                return PluginLoadResult.InvalidSignature;
            }

            PluginLoadContext pluginLoadContext = new(pluginInfoFile, pluginInfoFile.ContentPackage.Name, isCollectible: true);

            Assembly mainPluginAssembly;

            try
            {
                mainPluginAssembly = pluginLoadContext.LoadFromAssemblyPath(pluginInfoFile.MainPluginInfo.AssemblyPath.FullPath);
            }
            catch (Exception)
            {
                return PluginLoadResult.AssemblyFailedToLoad;
            }

            Result<PluginMetadata, string> metadataResult = GetMetadata(pluginInfoFile.MainPluginInfo.ContentPackage, mainPluginAssembly, !pluginInfoFile.PromptWarning);

            if (!metadataResult.TryUnwrapSuccess(out PluginMetadata metadata))
            {
                string missingMetadata = metadataResult.TryUnwrapFailure(out string? missing)
                                             ? missing
                                             : throw new UnreachableCodeException();

                DebugConsole.ThrowError($"Plugin {mainPluginAssembly.GetName().Name} is missing metadata: {missingMetadata}");
                pluginLoadContext.Unload();
                return PluginLoadResult.MissingMetadata;
            }

            if (metadata.GameVersion < MinSupportedGameVersion)
            {
                DebugConsole.ThrowError($"Plugin {metadata.Name} is outdated and would not work with the current version of the game.");
                pluginLoadContext.Unload();
                return PluginLoadResult.OutdatedPlugin;
            }

            // Load extra assemblies
            foreach (var pluginInfo in pluginInfoFile.ExtraAssemblyInfos.Where(static p => p.ShoudLoad()))
            {
                try
                {
                    pluginLoadContext.LoadFromAssemblyPath(pluginInfo.AssemblyPath.FullPath);
                }
                catch (Exception)
                {
                    return PluginLoadResult.AssemblyFailedToLoad;
                }
            }

            List<IBarotraumaPlugin> pluginMains = [];
            foreach (Type type in pluginLoadContext.Assemblies.SelectMany(static a => a.GetTypes()))
            {
                if (typeof(IBarotraumaPlugin).IsAssignableFrom(type))
                {

                    IBarotraumaPlugin plugin = (IBarotraumaPlugin)Activator.CreateInstance(type)!;
                    pluginMains.Add(plugin);
                }
            }

            loadedPlugins.Add(new LoadedPlugin(
                                     PluginLoadContext: pluginLoadContext,
                                     Metadata: metadata,
                                     Assembly: mainPluginAssembly,
                                     Info: pluginInfoFile,
                                     Data: new PluginData(mainPluginAssembly, pluginInfoFile.ContentPackage),
                                     PluginMains: pluginMains));
            ReconstructPluginDataList();

            if (GCHelper.IsActive)
            {
                GCHelperAssemblyPatcher.Patch(mainPluginAssembly);
            }

            try
            {
                pluginMains.ForEach(static x => x.Init());
            }
            catch (Exception e)
            {
                OnPluginThrowUnhandledException(e, mainPluginAssembly);
                UnloadPlugin(pluginInfoFile);
                return PluginLoadResult.PluginThrewException;
            }

            return PluginLoadResult.Success;

            static Result<PluginMetadata, string> GetMetadata(ContentPackage cp, Assembly assembly, bool isTrusted)
            {
                AssemblyName asmName = assembly.GetName();

                string? name = asmName.Name;

                if (string.IsNullOrWhiteSpace(name)) { return Result.Failure("Name"); }

                Version? version = asmName.Version;

                if (version is null) { return Result.Failure("Version"); }

                AssemblyCompanyAttribute? companyAttribute = assembly.GetCustomAttribute<AssemblyCompanyAttribute>();
                if (companyAttribute is not { Company: { } author } || string.IsNullOrWhiteSpace(author))
                {
                    return Result.Failure("Authors");
                }

                IEnumerable<AssemblyMetadataAttribute> attrs = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();

                Option<string> repository = attrs.FirstOrDefault(static a => a.Key == "RepositoryUrl")?.Value is { } repo ? Option<string>.Some(repo) : Option.None;

                string? gameVersionStr = attrs.FirstOrDefault(static a => a.Key == "GameVersion")?.Value;

                if (gameVersionStr is null || !Version.TryParse(gameVersionStr, out Version? gameVersion))
                {
                    return Result.Failure("GameVersion");
                }

                uint identifierHash;
                using (MD5 md5 = MD5.Create())
                {
                    identifierHash = ToolBoxCore.IdentifierToUint32Hash(cp.Hash.StringRepresentation.ToIdentifier(), md5);
                }

                if (identifierHash == 0)
                {
                    throw new InvalidOperationException($"Failed to hash identifier \"{cp.Hash.StringRepresentation}\": hash was 0.");
                }

                return Result.Success(new PluginMetadata(
                                          Name: name,
                                          GameVersion: gameVersion,
                                          Version: version,
                                          IdentifierHash: identifierHash,
                                          RepositoryUrl: repository,
                                          IsTrusted: isTrusted,
                                          Authors: author));
            }
        }

        private static readonly string PublicKeyFile = Path.Combine("Data", "PublicKey.dat");

        public static AssemblyVerificationResult CheckAssembliesSignature(string directoryPath)
        {
            string signaturePath = Path.Combine(directoryPath, "assemblies.sig");

            if (!File.Exists(signaturePath))
            {
                return AssemblyVerificationResult.MissingSignature;
            }

            var files = Directory
                .EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal) // Keep order deterministic
                .ToList();

            if (files.Count == 0)
            {
                return AssemblyVerificationResult.InvalidSignature;
            }

            byte[] combinedHash;

            using (var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                foreach (var file in files)
                {
                    using var stream = File.OpenRead(file);
                    byte[] buffer = new byte[8192];
                    int read;

                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        incrementalHash.AppendData(buffer, 0, read);
                    }
                }

                combinedHash = incrementalHash.GetHashAndReset();
            }

            byte[] signature = File.ReadAllBytes(signaturePath);

            using RSA rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(File.ReadAllBytes(PublicKeyFile), out _);

            if (rsa.VerifyHash(combinedHash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                return AssemblyVerificationResult.Success;
            }

            return AssemblyVerificationResult.InvalidSignature;
        }

        /// <summary>
        /// Purges various caches where types from plugin assemblies may be stored.
        /// </summary>
        private static void PurgeCache()
        {
            SerializableProperty.PurgeCache();
            NetSerializableProperties.PurgeCache();
            ReflectionUtils.PurgeCache();
        }

        internal static Option<PluginMetadata> TryFindMetadataForAssembly(Assembly assembly)
        {
            foreach (LoadedPlugin loadedPlugin in loadedPlugins)
            {
                if (loadedPlugin.Assembly == assembly)
                {
                    return Option.Some(loadedPlugin.Metadata);
                }
            }

            return Option.None;
        }

        internal static Option<PluginData> GetPluginDataForHash(uint hash)
            => loadedPlugins
               .FirstOrNone(la => la.Metadata.IdentifierHash == hash)
               .Select(static lp => lp.Data);

        internal static ContentPackage? TryFindContentPackageForAssembly(Assembly assembly)
        {
            foreach (LoadedPlugin loadedPlugin in loadedPlugins)
            {
                if (loadedPlugin.PluginLoadContext.Assemblies.Contains(assembly))
                {
                    return loadedPlugin.Info.ContentPackage;
                }
            }

            return null;
        }

        internal static PluginData? TryFindPluginDataForAssembly(Assembly assembly)
        {
            foreach (LoadedPlugin loadedPlugin in loadedPlugins)
            {
                if (loadedPlugin.Assembly == assembly)
                {
                    return loadedPlugin.Data;
                }
            }

            return null;
        }

        public static void FinalizePluginLoad(PluginInfoFile file)
        {
            foreach (LoadedPlugin plugin in loadedPlugins)
            {
                if (plugin.Info != file) { continue; }

                try
                {
                    plugin.PluginMains.ForEach(static x => x.OnContentLoaded());
                }
                catch (Exception e)
                {
                    OnPluginThrowUnhandledException(e, plugin.Assembly);
                }
            }
        }

        public static bool IsPluginFileUnloading(PluginInfoFile pluginInfoFile)
        {
            // Compare main assembly path instead of the content package since the package reference could have been recreated
            return unloadedPlugins.Any(u => u.PluginInfoFile.MainPluginInfo.AssemblyPath == pluginInfoFile.MainPluginInfo.AssemblyPath && u.IsUnloading);
        }

        internal static void CheckUnloadedPlugins()
        {
            for (int i = unloadedPlugins.Count - 1; i >= 0; i--)
            {
                UnloadedPlugin unloadedPlugin = unloadedPlugins[i];
                TimeSpan timePassed = DateTime.Now - unloadedPlugin.UnloadTime;

                if (!unloadedPlugin.IsUnloading)
                {
                    var msg = $"Plugin was successfully unloaded after {Math.Round(timePassed.TotalSeconds, 2)} seconds";

                    msg = DebugConsole.AddContentPackageInfoToMessage(msg, unloadedPlugin.PluginInfoFile.ContentPackage);
                    DebugConsole.NewMessage(msg, Color.Green);
                    unloadedPlugins.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>
    /// Verifies that plugins are properly being garbage collected after unloading.
    /// </summary>
    internal static class PluginUnloadVerifier
    {
        private static Thread? verifyThread;
        private static readonly List<PluginLoader.UnloadedPlugin> UnloadedPlugins = [];

        public static void EnqueueUnloadVerification(PluginLoader.UnloadedPlugin unloadedPlugin)
        {
            lock (UnloadedPlugins)
            {
                UnloadedPlugins.Add(unloadedPlugin);
                TryStartVerifyThread();
            }
        }

        private static void TryStartVerifyThread()
        {
            if (verifyThread is { IsAlive: true }) { return; }

            verifyThread = new Thread(VerifyThread);
            verifyThread.Start();
        }

        private static void VerifyThread()
        {
            Thread.Sleep(millisecondsTimeout: 500);
            for (int i = 0; i < 10 && IsAnyAlive(); i++)
            {
                GC.Collect();
                Thread waitThread = new Thread(static () =>
                {
                    try
                    {
                        GC.WaitForPendingFinalizers();
                    }
                    catch (ThreadInterruptedException) { }
                });
                waitThread.Start();
                Stopwatch sw = Stopwatch.StartNew();
                while (waitThread.IsAlive)
                {
                    if (sw.ElapsedMilliseconds > 5000)
                    {
                        DebugConsole.ThrowError("Failed to wait for finalizers to finish after 5 seconds. Terminating the thread.");
                        waitThread.Interrupt();
                        waitThread.Join();
                        break;
                    }
                    Thread.Sleep(1);
                }
            }

            lock (UnloadedPlugins)
            {
                foreach (var unloadedPlugin in UnloadedPlugins)
                {
                    // If the plugin is still alive, something is blocking it from being collected
                    if (!unloadedPlugin.IsUnloading) { continue; }

                    string msg = $"Possible issue in the plugin: the plugin could not be fully unloaded and is still active in memory after cleanup attempts. " +
                         $"This usually means that some code from the plugin is still running or being strongly referenced. " +
                         $"Possible causes could be failing to unsubscribe from an event, or adding an element to a list that is not being cleared. " +
                         $"This can be a bug in either the mod or the vanilla game. In some cases the plugin might still unload after some time.";

#if DEBUG
                    DebugConsole.ThrowError(msg, contentPackage: unloadedPlugin.PluginInfoFile.ContentPackage);
#else
                    DebugConsole.AddWarning(msg, contentPackage: unloadedPlugin.PluginInfoFile.ContentPackage);
#endif
                }

                UnloadedPlugins.Clear();
            }

            verifyThread = null;
            return;

            static bool IsAnyAlive()
            {
                lock (UnloadedPlugins)
                {
                    foreach (var unloadedPlugin in UnloadedPlugins)
                    {
                        if (unloadedPlugin.IsUnloading) { return true; }
                    }

                    return false;
                }
            }
        }
    }
}