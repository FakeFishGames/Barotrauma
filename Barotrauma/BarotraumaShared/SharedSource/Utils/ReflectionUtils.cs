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

        public static Option<T1> ParseDerived<T1>(string str)
        {
            static Option<T1> none() => Option<T1>.None();
            
            var derivedTypes = GetDerivedNonAbstract<T1>();

            Option<T1> parseOfType(Type t)
            {
                //every T1 type is expected to have a method with the following signature:
                //  public static Option<T> Parse(string str)
                var parseFunc = t.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static);
                if (parseFunc is null) { return none(); }
                
                var parameters = parseFunc.GetParameters();
                if (parameters.Length != 1) { return none(); }
                
                var returnType = parseFunc.ReturnType;
                if (!returnType.IsConstructedGenericType) { return none(); }
                if (returnType.GetGenericTypeDefinition() != typeof(Option<>)) { return none(); }
                if (returnType.GenericTypeArguments[0] != t) { return none(); }

                //some hacky business to convert from Option<T2> to Option<T1> when we only know T2 at runtime
                static Option<T1> convert<T2>(Option<T2> option) where T2 : T1
                    => option.Select(v => (T1)v);
                Func<Option<T1>, Option<T1>> f = convert;
                var constructedConverter = f.Method.GetGenericMethodDefinition().MakeGenericMethod(typeof(T1), t);

                return constructedConverter.Invoke(null, new object?[] { parseFunc.Invoke(null, new object[] { str }) })
                    as Option<T1> ?? none();
            }

            return derivedTypes.Select(parseOfType).FirstOrDefault(t => t.IsSome()) ?? none();
        }
    }
}