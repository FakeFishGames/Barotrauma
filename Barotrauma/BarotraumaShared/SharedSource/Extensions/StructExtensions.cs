namespace Barotrauma.Extensions
{
    public static class StructExtensions
    {
        public static bool TryGetValue<T>(this T? nullableStruct, out T nonNullable) where T : struct
        {
            if (nullableStruct.HasValue)
            {
                nonNullable = nullableStruct.Value;
                return true;
            }
            nonNullable = default(T);
            return false;
        }
    }
}