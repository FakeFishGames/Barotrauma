#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Barotrauma
{
    public static class ReflectionUtils
    {
        /// <summary>
        /// A lock object to prevent multiple threads from accessing the caches at the same time
        /// </summary>
        private static readonly object mutex = new object();

        private static readonly Dictionary<Assembly, ImmutableArray<Type>> cachedNonAbstractTypes
            = new Dictionary<Assembly, ImmutableArray<Type>>();

        private static readonly Dictionary<Assembly, Dictionary<Type, ImmutableArray<Type>>> cachedDerivedNonAbstract
            = new Dictionary<Assembly, Dictionary<Type, ImmutableArray<Type>>>();

        public static T GetValueFromStaticProperty<T>(this PropertyInfo property)
        {
            if (property.GetMethod is not { IsStatic: true })
            {
                throw new ArgumentException($"Property {property} is not static");
            }

            var value = property.GetValue(obj: null);
            if (value is not T castValue)
            {
                throw new ArgumentException($"Property {property} is null or not of type {typeof(T)}");
            }

            return castValue;
        }

        public static IEnumerable<Type> GetDerivedNonAbstract(Type type)
        {
            Assembly assembly = type.Assembly;
            ImmutableArray<Type> newArray;
            lock (mutex)
            {
                if (!cachedNonAbstractTypes.ContainsKey(assembly))
                {
                    cachedNonAbstractTypes[assembly] = assembly.GetTypes()
                        .Where(t => !t.IsAbstract).ToImmutableArray();

                    cachedDerivedNonAbstract[assembly] = new Dictionary<Type, ImmutableArray<Type>>();
                }
                if (cachedDerivedNonAbstract[assembly].TryGetValue(type, out var cachedArray))
                {
                    return cachedArray;
                }
                newArray = cachedNonAbstractTypes[assembly].Where(t2 => t2.IsSubclassOf(type)).ToImmutableArray();
                cachedDerivedNonAbstract[assembly].Add(type, newArray);
            }
            return newArray;
        }

        public static IEnumerable<Type> GetDerivedNonAbstract<T>() => GetDerivedNonAbstract(typeof(T));

        public static Type? GetType(string nameWithNamespace)
        {
            if (Type.GetType(nameWithNamespace) is Type t) { return t; }

            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly?.GetType(nameWithNamespace) is Type t2) { return t2; }

            return null;
        }

        public static Option<TBase> ParseDerived<TBase, TInput>(TInput input)
            where TBase : notnull
            where TInput : notnull
        {
            static Option<TBase> none() => Option<TBase>.None();
            
            var derivedTypes = GetDerivedNonAbstract<TBase>();

            Option<TBase> parseOfType(Type t)
            {
                //every TBase type is expected to have a method with the following signature:
                //  public static Option<T> Parse(TInput str)
                var parseFunc = t.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static);
                if (parseFunc is null) { return none(); }
                
                var parameters = parseFunc.GetParameters();
                if (parameters.Length != 1) { return none(); }
                
                var returnType = parseFunc.ReturnType;
                if (!returnType.IsConstructedGenericType) { return none(); }
                if (returnType.GetGenericTypeDefinition() != typeof(Option<>)) { return none(); }
                if (returnType.GenericTypeArguments[0] != t) { return none(); }

                //some hacky business to convert from Option<T2> to Option<TBase> when we only know T2 at runtime
                static Option<TBase> convert<T2>(Option<T2> option) where T2 : TBase
                    => option.Select(v => (TBase)v);
                Func<Option<TBase>, Option<TBase>> f = convert;
                var genericArgs = f.Method.GetGenericArguments();
                genericArgs[^1] = t;
                var constructedConverter =
                    f.Method.GetGenericMethodDefinition().MakeGenericMethod(genericArgs);

                return constructedConverter.Invoke(null, new[] { parseFunc.Invoke(null, new object[] { input }) })
                    as Option<TBase>? ?? none();
            }

            return derivedTypes.Select(parseOfType).FirstOrDefault(t => t.IsSome());
        }

        public static string NameWithGenerics(this Type t)
        {
            if (!t.IsGenericType) { return t.Name; }

            string result = t.Name[..t.Name.IndexOf('`')];
            result += $"<{string.Join(", ", t.GetGenericArguments().Select(NameWithGenerics))}>";
            return result;
        }

        public static PropertyInfo? Find(this PropertyInfo[] propertyInfos, string name, bool ignoreCase)
        {
            foreach (PropertyInfo propertyInfo in propertyInfos)
            {
                if (string.Equals(propertyInfo.Name, name, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    return propertyInfo;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a type by its name, with backwards compatibility for types that have been renamed.
        /// <see cref="TypePreviouslyKnownAs"/>
        /// </summary>
        public static Type? GetTypeWithBackwardsCompatibility(Assembly assembly, string nameSpace, string typeName, bool throwOnError, bool ignoreCase)
        {
            var types = assembly
                .GetTypes()
                .Where(t => NameMatches(t.Namespace, nameSpace, ignoreCase));

            foreach (Type type in types)
            {
                if (NameMatches(type.Name, typeName, ignoreCase))
                {
                    return type;
                }

                if (type.GetCustomAttribute<TypePreviouslyKnownAs>() is { } knownAsAttribute)
                {
                    if (NameMatches(knownAsAttribute.PreviousName, typeName, ignoreCase))
                    {
                        return type;
                    }
                }
            }

            if (throwOnError)
            {
                throw new TypeLoadException($"Could not find the type {typeName} in namespace {nameSpace}");
            }

            return null;

            static bool NameMatches(string? name1, string? name2, bool ignoreCase)
                => string.Equals(name1, name2, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        /// <summary>
        /// The names of generic types include the arity at the end (fancy way of saying the number of parameters, e.g. GUISelectionCarousel&lt;T&gt; would be GUISelectionCarousel`1)
        /// This method strips that part out.
        /// </summary>
        public static Identifier GetTypeNameWithoutGenericArity(Type type)
        {
            string name = type.Name;
            int index = name.IndexOf('`');
            return (index == -1 ? name : name.Substring(0, index)).ToIdentifier();
        }

        private readonly record struct CachedMethod(MethodInfo Method, Type[] Types);

        private static readonly Dictionary<Type, Dictionary<string, Dictionary<Type, CachedMethod>>> CachedGenericMethods = new();

        public static object? InvokeGenericMethod(Type genericParam, Type module, string name, params (Type, object?)[] args)
        {
            Type[] types = new Type[args.Length];
            object?[] values = new object[args.Length];

            for (int i = 0; i < args.Length; i++)
            {
                types[i] = args[i].Item1;
                values[i] = args[i].Item2;
            }

            if (CachedGenericMethods.TryGetValue(module, out Dictionary<string, Dictionary<Type, CachedMethod>>? methods))
            {
                if (methods.TryGetValue(name, out Dictionary<Type, CachedMethod>? genericMethods))
                {
                    if (genericMethods.TryGetValue(genericParam, out CachedMethod cachedMethod))
                    {
                        if (cachedMethod.Types.Length != types.Length)
                        {
                            throw new ArgumentException($"Method {name} has {cachedMethod.Types.Length} parameters, but {types.Length} were provided");
                        }

                        if (!cachedMethod.Types.SequenceEqual(types))
                        {
                            throw new ArgumentException($"Method {name} has parameters of types {string.Join(", ", cachedMethod.Types.Select(t => t.Name))}, but {string.Join(", ", types.Select(t => t.Name))} were provided");
                        }

                        return cachedMethod.Method.Invoke(null, values);
                    }
                }
            }

            MethodInfo? method = module.GetMethod(name, BindingFlags.Public | BindingFlags.Static, types);
            if (method is null)
            {
                throw new ArgumentException($"Method {name} not found in {module}");
            }

            MethodInfo genericMethod = method.MakeGenericMethod(genericParam);
            if (!CachedGenericMethods.ContainsKey(module))
            {
                CachedGenericMethods[module] = new Dictionary<string, Dictionary<Type, CachedMethod>>();
            }

            if (!CachedGenericMethods[module].ContainsKey(name))
            {
                CachedGenericMethods[module][name] = new Dictionary<Type, CachedMethod>();
            }

            CachedGenericMethods[module][name][genericParam] = new CachedMethod(genericMethod, genericMethod.GetParameters().Select(static p => p.ParameterType).ToArray());
            return genericMethod.Invoke(null, values);
        }

        public static void PurgeCache()
        {
            lock (mutex)
            {
                cachedNonAbstractTypes.Clear();
                cachedDerivedNonAbstract.Clear();
                CachedGenericMethods.Clear();
            }
        }
    }
}