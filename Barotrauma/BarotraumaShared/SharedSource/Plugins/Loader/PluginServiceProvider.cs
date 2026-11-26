#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;

namespace Barotrauma.Plugins
{
    /// <summary>
    /// Service provider for plugins.
    /// This is the main way for plugins to interact with the game.
    /// This class implements the <a href="https://en.wikipedia.org/wiki/Dependency_injection">Dependency injection</a> technique.
    /// </summary>
    /// <remarks>
    /// Provided services:<br/>
    /// - <see cref="IDebugConsole"/><br/>
    /// - <see cref="IContentFileRegistrar"/><br/>
    /// - <see cref="ISimpleHookService"/><br/>
    /// - <see cref="IItemComponentRegistrar"/><br/>
    /// - <see cref="IEventActionRegistrar"/><br/>
    /// - <see cref="IGameNetwork"/><br/>
    /// - <see cref="IHarmonyProvider"/><br/>
    /// - <see cref="ISettingsService"/><br/>
    /// - <see cref="IStatusEffectService"/><br/>
    /// - <see cref="IAIObjectiveRegistrar"/><br/>
    /// </remarks>
    /// <example>
    /// <code>
    /// private static readonly IExampleService ExampleService = PluginServiceProvider.GetService&lt;IExampleService&gt;();
    /// </code>
    /// </example>
    public class PluginServiceProvider
    {
        private static readonly PluginServiceProvider Services =
            new PluginServiceBuilder()
                .AddTransient<IDebugConsole>(CreateTransient<ImplDebugConsole>)
                .AddTransient<IContentFileRegistrar>(CreateTransient<ImplContentFileRegistrar>)
                .AddTransient<ISimpleHookService>(CreateTransient<ImplSimpleHookService>)
                .AddTransient<IItemComponentRegistrar>(CreateTransient<ImplItemComponentRegistrar>)
                .AddTransient<IEventActionRegistrar>(CreateTransient<ImplEventActionRegistrar>)
                .AddTransient<IGameNetwork>(CreateTransient<ImplGameNetwork>)
                .AddTransient<IHarmonyProvider>(CreateTransient<ImplHarmonyProvider>)
                .AddTransient<IGameScreen>(CreateTransient<ImplGameScreen>)
                .AddTransient<ISettingsService>(CreateTransient<SettingsService>)
                .AddTransient<IStatusEffectService>(CreateTransient<ImplStatusEffectService>)
                .AddTransient<IAIObjectiveRegistrar>(CreateTransient<ImplAIObjectiveRegistrar>)
                .AddTransient<IContentPackageRetrievalService>(CreateTransient<ImplContentPackageRetrievalService>)
                .Build();

        private static IPluginService CreateTransient<TImplementation>(PluginData data, Assembly assembly)
            where TImplementation : IPluginService
        {
            if (PluginLoader.FindAlreadyExistingService(assembly, out TImplementation? existingService))
            {
                return existingService;
            }

            object? instance = Activator.CreateInstance(typeof(TImplementation), data, assembly);
            if (instance is not IPluginService service)
            {
                throw new InvalidOperationException($"Failed to create an instance of {typeof(TImplementation)}.");
            }

            return service;
        }

        private readonly ImmutableDictionary<Type, IPluginService> implementations;
        private readonly ImmutableDictionary<Type, Func<PluginData, Assembly, IPluginService>> factories;

        internal PluginServiceProvider(ImmutableDictionary<Type, IPluginService> implementations,
                                       ImmutableDictionary<Type, Func<PluginData, Assembly, IPluginService>> factories)
        {
            this.implementations = implementations;
            this.factories = factories;
        }

        private TService GetServiceInternal<TService>(PluginData data, Assembly callingAssembly) where TService : IPluginService
        {
            if (factories.TryGetValue(typeof(TService), out Func<PluginData, Assembly, IPluginService>? factory))
            {
                IPluginService serviceFactory = factory(data, callingAssembly);
                return (TService)serviceFactory;
            }

            if (implementations.TryGetValue(typeof(TService), out IPluginService? service))
            {
                return (TService)service;
            }

            throw new ArgumentException($"Service of type {typeof(TService)} not found.");
        }

        /// <summary>
        /// Gets a service that implements the requested interface.
        /// </summary>
        /// <typeparam name="TService">A plugin interface that implements <see cref="IPluginService"/>.</typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentException">
        /// The service of the requested type was not found.
        /// This shouldn't happen unless there is a bug in the base game,
        /// or you are accessing this method via  means that bypass the compile-time type checks,
        /// so catching this exception is not necessary.
        /// </exception>
        /// <remarks>
        /// The result of this method should be stored in a static field,
        /// as it is not recommended to call this method frequently.
        /// </remarks>
        /// <example>
        /// <code>
        /// private static readonly IExampleService ExampleService = PluginServiceProvider.GetService&lt;IExampleService&gt;();
        /// </code>
        /// </example>
        public static TService GetService<TService>() where TService : IPluginService
        {
            Assembly callingAssembly = Assembly.GetCallingAssembly();
            PluginData data = PluginLoader.TryFindPluginDataForAssembly(callingAssembly) ?? throw new InvalidOperationException("Failed to find plugin data for the calling assembly.");
            return Services.GetServiceInternal<TService>(data, callingAssembly);
        }
    }

    /// <summary>
    /// Builder for <see cref="PluginServiceProvider"/>.
    /// </summary>
    internal class PluginServiceBuilder
    {
        private readonly Dictionary<Type, IPluginService> implementations = new();
        private readonly Dictionary<Type, Func<PluginData, Assembly, IPluginService>> factories = new();

        public PluginServiceBuilder AddSingleton<TService, TImplementation>()
            where TService : IPluginService
            where TImplementation : TService
        {
            implementations.Add(typeof(TService), Activator.CreateInstance<TImplementation>());
            return this;
        }

        internal PluginServiceBuilder AddTransient<TService>(Func<PluginData, Assembly, IPluginService> factory)
        {
            factories.Add(typeof(TService), factory);
            return this;
        }

        public PluginServiceProvider Build()
            => new PluginServiceProvider(implementations.ToImmutableDictionary(),
                                         factories.ToImmutableDictionary());
    }
}