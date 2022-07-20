using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Barotrauma
{
    public static class ReflectionUtils
    {
        private static Type[] cachedNonAbstractTypes;
        public static IEnumerable<Type> GetDerivedNonAbstract<T>()
        {
            if (cachedNonAbstractTypes == null)
            {
                cachedNonAbstractTypes = Assembly.GetEntryAssembly().GetTypes().Where(t => !t.IsAbstract).ToArray();
            }
            return cachedNonAbstractTypes.Where(t => t.IsSubclassOf(typeof(T)));
        }
    }
}