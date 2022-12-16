#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

namespace Barotrauma
{
    public abstract class Result<T, TError>
        where T: notnull
        where TError: notnull
    {
        public abstract bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;

        public static Success<T, TError> Success(T value)
            => new Success<T, TError>(value);
        
        public static Failure<T, TError> Failure(TError error)
            => new Failure<T, TError>(error);

        public abstract bool TryUnwrapSuccess([MaybeNullWhen(returnValue: false)] out T value);
        public abstract bool TryUnwrapFailure([MaybeNullWhen(returnValue: false)] out TError value);
        
        public abstract override string? ToString();

        public static (Func<T, Result<T, TError>> Success, Func<TError, Result<T, TError>> Failure) GetFactoryMethods()
            => (Success, Failure);
    }

    public sealed class Success<T, TError> : Result<T, TError>
        where T: notnull
        where TError: notnull
    {
        public readonly T Value;
        public override bool IsSuccess => true;

        public override bool TryUnwrapSuccess([MaybeNullWhen(returnValue: false)] out T value)
        {
            value = Value;
            return true;
        }

        public override bool TryUnwrapFailure([MaybeNullWhen(returnValue: false)] out TError value)
        {
            value = default;
            return false;
        }

        public override string ToString()
            => $"Success<{typeof(T).NameWithGenerics()}, {typeof(TError).NameWithGenerics()}>({Value})";

        public Success(T value)
        {
            Value = value;
        }
    }

    public sealed class Failure<T, TError> : Result<T, TError>
        where T: notnull
        where TError: notnull
    {
        public readonly TError Error;

        public override bool IsSuccess => false;
        
        public override bool TryUnwrapSuccess([MaybeNullWhen(returnValue: false)] out T value)
        {
            value = default;
            return false;
        }

        public override bool TryUnwrapFailure([MaybeNullWhen(returnValue: false)] out TError value)
        {
            value = Error;
            return true;
        }
        
        public override string ToString()
            => $"Failure<{typeof(T).NameWithGenerics()}, {typeof(TError).NameWithGenerics()}>({Error})";

        public Failure(TError error)
        {
            Error = error;
        }
    }
}
