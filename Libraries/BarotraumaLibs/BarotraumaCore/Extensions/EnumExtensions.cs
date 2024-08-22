using System;

namespace Barotrauma.Extensions
{
    public static class EnumExtensions
    {
        /// <summary>
        /// Enum.HasFlag() checks if all flags matches. This method checks if any of them matches.
        /// E.g. when myEnum = SomeEnum.First | SomeEnum.Second, myEnum.HasFlag(SomeEnum.First | SomeEnum.Third) returns false, because not all of the flags match, but HasAnyFlag(SomeEnum.First | SomeEnum.Third) returns true, because some of the flags match.
        /// </summary>
        public static bool HasAnyFlag<T>(this T type, T value) where T : Enum
        {
            int typeValue = Convert.ToInt32(type);
            int flagValue = Convert.ToInt32(value);
            return (typeValue & flagValue) != 0;
        }

        /// <summary>
        /// Adds a flag value to an enum.
        /// Note that enums are value types, so you need to use the value returned from this method.
        /// </summary>
        public static T AddFlag<T>(this T @enum, T flag) where T : Enum
        {
            int enumValue = Convert.ToInt32(@enum);
            int flagValue = Convert.ToInt32(flag);
            return (T)(object)(enumValue | flagValue);
        }

        /// <summary>
        /// Removes a flag value from an enum.
        /// Note that enums are value types, so you need to use the value returned from this method.
        /// </summary>
        public static T RemoveFlag<T>(this T @enum, T flag) where T : Enum
        {
            int enumValue = Convert.ToInt32(@enum);
            int flagValue = Convert.ToInt32(flag);
            return (T)(object)(enumValue & ~flagValue);
        }
    }
}