using System.Diagnostics.CodeAnalysis;

namespace Barotrauma;

/// <summary>
/// Discriminated union of three types.
/// Essentially the same thing as Either&lt;T1, T2&gt;, except for three types instead of two types.
/// </summary>
public readonly struct OneOf<T1, T2, T3>
    where T1 : notnull
    where T2 : notnull
    where T3 : notnull
{
    private readonly Option<T1> value1;
    private readonly Option<T2> value2;
    private readonly Option<T3> value3;

    private OneOf(Option<T1> value1, Option<T2> value2, Option<T3> value3)
    {
        this.value1 = value1;
        this.value2 = value2;
        this.value3 = value3;
    }

    public static implicit operator OneOf<T1, T2, T3>(T1 value1)
        => new OneOf<T1, T2, T3>(value1: Option.Some(value1), value2: Option.None, value3: Option.None);
    public static implicit operator OneOf<T1, T2, T3>(T2 value2)
        => new OneOf<T1, T2, T3>(value1: Option.None, value2: Option.Some(value2), value3: Option.None);
    public static implicit operator OneOf<T1, T2, T3>(T3 value3)
        => new OneOf<T1, T2, T3>(value1: Option.None, value2: Option.None, value3: Option.Some(value3));

    public bool TryGet([NotNullWhen(returnValue: true)] out T1? t1)
        => value1.TryUnwrap(out t1);
    public bool TryGet([NotNullWhen(returnValue: true)] out T2? t2)
        => value2.TryUnwrap(out t2);
    public bool TryGet([NotNullWhen(returnValue: true)] out T3? t3)
        => value3.TryUnwrap(out t3);

    private static string ObjectToStringWithType<T>(T obj)
        => $"{obj}: {typeof(T).Name}";

    public override string ToString()
        => $"OneOf<{typeof(T1).Name}, {typeof(T2).Name}, {typeof(T3).Name}>("
           + value1.Select(ObjectToStringWithType)
               .Fallback(value2.Select(ObjectToStringWithType))
               .Fallback(value3.Select(ObjectToStringWithType))
               .Fallback("None")
           + ")";
}