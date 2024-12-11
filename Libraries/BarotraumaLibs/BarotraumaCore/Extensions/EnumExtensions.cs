using System;
using System.Collections.Generic;

// NOTE: We should use struct in addition to Enum in all the type constraints (at the end of the method signatures), as it
// tells the compiler that we're only ever using value types, which enums always are anyway.
// This avoids a lot of allocations caused by the compiler preparing for anything, which in turn happens because despite
// how it works in practice, Enum is counted as a reference type in C#... for historical reasons.

// We use the (int)(object) cast because generic types can't be cast directly to int, so we box into object and unbox into int instead.
// It avoids some memory allocations that Convert.ToInt32() seems to do.
// NOTE: This should work fine as long as the enum values stay within - to + 2^31, so let's not use uint or long for them.

namespace Barotrauma.Extensions
{
    public static class EnumExtensions
    {
        /// <summary>
        /// Enum.HasFlag() checks if all flags matches. This method checks if any of them matches. It also avoids boxing allocations that the built-in version might still sometimes cause.
        /// E.g. when myEnum = SomeEnum.First | SomeEnum.Second, myEnum.HasFlag(SomeEnum.First | SomeEnum.Third) returns false, because not all of the flags match, but HasAnyFlag(SomeEnum.First | SomeEnum.Third) returns true, because some of the flags match.
        /// </summary>
        public static bool HasAnyFlag<T>(this T type, T value) where T : struct, Enum
        {
            int typeValue = (int)(object)type;
            int flagValue = (int)(object)value;
            return (typeValue & flagValue) != 0;
        }

        /// <summary>
        /// Adds a flag value to an enum.
        /// Note that enums are value types, so you need to use the value returned from this method.
        /// </summary>
        public static T AddFlag<T>(this T @enum, T flag) where T : struct, Enum
        {
            int enumValue = (int)(object)@enum;
            int flagValue = (int)(object)flag;
            return (T)(object)(enumValue | flagValue);
        }

        /// <summary>
        /// Removes a flag value from an enum.
        /// Note that enums are value types, so you need to use the value returned from this method.
        /// </summary>
        public static T RemoveFlag<T>(this T @enum, T flag) where T : struct, Enum
        {
            int enumValue = (int)(object)@enum;
            int flagValue = (int)(object)flag;
            return (T)(object)(enumValue & ~flagValue);
        }

        public static IEnumerable<T> GetIndividualFlags<T>(T flagsEnum) where T : struct, Enum
        {
            foreach (T value in Enum.GetValues(typeof(T)))
            {
                if (flagsEnum.HasAnyFlag(value)) { yield return value; }
            }
        }
    }
}