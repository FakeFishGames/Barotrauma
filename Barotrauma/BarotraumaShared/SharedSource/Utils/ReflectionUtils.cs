using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Barotrauma
{
    public static class ReflectionUtils
    {
        public static IEnumerable<Type> GetDerivedNonAbstract<T>()
        {
            return Assembly.GetEntryAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(T)) && !t.IsAbstract);
        }
    }
}