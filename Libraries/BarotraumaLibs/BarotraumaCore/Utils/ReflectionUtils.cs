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

        public static IEnumerable<Type> GetDerivedNonAbstract<T>()
        {
            Type t = typeof(T);
            Assembly assembly = typeof(T).Assembly;
            if (!cachedNonAbstractTypes.ContainsKey(assembly))
            {
                cachedNonAbstractTypes[assembly] = assembly.GetTypes()
                    .Where(t => !t.IsAbstract).ToImmutableArray();

                cachedDerivedNonAbstract[assembly] = new Dictionary<Type, ImmutableArray<Type>>();
            }
            if (cachedDerivedNonAbstract[assembly].TryGetValue(t, out var cachedArray))
            {
                return cachedArray;
            }
            var newArray = cachedNonAbstractTypes[assembly].Where(t2 => t2.IsSubclassOf(t)).ToImmutableArray();
            cachedDerivedNonAbstract[assembly].Add(t, newArray);
            return newArray;
        }

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

        /// <summary>
        /// Gets a type by its name, with backwards compatibility for types that have been renamed.
        /// <see cref="TypePreviouslyKnownAs"/>
        /// </summary>
        public static Type? GetTypeWithBackwardsCompatibility(string nameSpace, string typeName, bool throwOnError, bool ignoreCase)
        {
            if (Assembly.GetEntryAssembly() is not { } entryAssembly) { return null; }
            var types = entryAssembly
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
        /// The names of generic types include the arity at the end (fancy way of saying the number of parameters, e.g. GUISelectionCarousel<T> would be GUISelectionCarousel`1)
        /// This method strips that part out.
        /// </summary>
        public static Identifier GetTypeNameWithoutGenericArity(Type type)
        {
            string name = type.Name;
            int index = name.IndexOf('`');
            return (index == -1 ? name : name.Substring(0, index)).ToIdentifier();
        }
    }
}