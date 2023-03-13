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

        public static IEnumerable<Type> GetDerivedNonAbstract<T>()
        {
            Assembly assembly = typeof(T).Assembly;
            if (!cachedNonAbstractTypes.ContainsKey(assembly))
            {
                cachedNonAbstractTypes[assembly] = assembly.GetTypes()
                    .Where(t => !t.IsAbstract).ToImmutableArray();
            }
            return cachedNonAbstractTypes[assembly].Where(t => t.IsSubclassOf(typeof(T)));
        }

        public static Option<TBase> ParseDerived<TBase, TInput>(TInput input) where TInput : notnull where TBase : notnull
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

        public static string NameWithGenerics(this Type t)
        {
            if (!t.IsGenericType) { return t.Name; }
            
            string result = t.Name[..t.Name.IndexOf('`')];
            result += $"<{string.Join(", ", t.GetGenericArguments().Select(NameWithGenerics))}>";
            return result;
        }
    }
}