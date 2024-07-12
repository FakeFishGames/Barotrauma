#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

namespace Barotrauma
{
    public abstract class Result<TSuccess, TFailure>
        where TSuccess: notnull
        where TFailure: notnull
    {
        public abstract bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;

        public static Success<TSuccess, TFailure> Success(TSuccess value)
            => new Success<TSuccess, TFailure>(value);

        public static Failure<TSuccess, TFailure> Failure(TFailure error)
            => new Failure<TSuccess, TFailure>(error);

        public abstract bool TryUnwrapSuccess([NotNullWhen(returnValue: true)] out TSuccess? value);
        public abstract bool TryUnwrapFailure([NotNullWhen(returnValue: true)] out TFailure? value);

        public abstract override string ToString();

        public static (Func<TSuccess, Result<TSuccess, TFailure>> Success, Func<TFailure, Result<TSuccess, TFailure>> Failure) GetFactoryMethods()
            => (Success, Failure);

        public static implicit operator Result<TSuccess, TFailure>(Result.UnspecifiedSuccess<TSuccess> unspecifiedSuccess)
            => Success(unspecifiedSuccess.Value);

        public static implicit operator Result<TSuccess, TFailure>(Result.UnspecifiedFailure<TFailure> unspecifiedFailure)
            => Failure(unspecifiedFailure.Value);

        public void Match(Action<TSuccess> success, Action<TFailure> failure)
        {
            if (TryUnwrapSuccess(out var successValue)) { success(successValue); }
            if (TryUnwrapFailure(out var failureValue)) { failure(failureValue); }
        }
    }

    public sealed class Success<TSuccess, TFailure> : Result<TSuccess, TFailure>
        where TSuccess: notnull
        where TFailure: notnull
    {
        public readonly TSuccess Value;
        public override bool IsSuccess => true;

        public override bool TryUnwrapSuccess([MaybeNullWhen(returnValue: false)] out TSuccess value)
        {
            value = Value;
            return true;
        }

        public override bool TryUnwrapFailure([MaybeNullWhen(returnValue: false)] out TFailure value)
        {
            value = default;
            return false;
        }

        public override string ToString()
            => $"Success<{typeof(TSuccess).NameWithGenerics()}, {typeof(TFailure).NameWithGenerics()}>({Value})";

        public Success(TSuccess value)
        {
            Value = value;
        }
    }

    public sealed class Failure<TSuccess, TFailure> : Result<TSuccess, TFailure>
        where TSuccess: notnull
        where TFailure: notnull
    {
        public readonly TFailure Error;

        public override bool IsSuccess => false;
        
        public override bool TryUnwrapSuccess([MaybeNullWhen(returnValue: false)] out TSuccess value)
        {
            value = default;
            return false;
        }

        public override bool TryUnwrapFailure([MaybeNullWhen(returnValue: false)] out TFailure value)
        {
            value = Error;
            return true;
        }
        
        public override string ToString()
            => $"Failure<{typeof(TSuccess).NameWithGenerics()}, {typeof(TFailure).NameWithGenerics()}>({Error})";

        public Failure(TFailure error)
        {
            Error = error;
        }
    }

    public static class Result
    {
        public readonly ref struct UnspecifiedSuccess<TSuccess>
            where TSuccess : notnull
        {
            internal readonly TSuccess Value;
            internal UnspecifiedSuccess(TSuccess value) { Value = value; }
        }
        
        public readonly ref struct UnspecifiedFailure<TFailure>
            where TFailure : notnull
        {
            internal readonly TFailure Value;
            internal UnspecifiedFailure(TFailure value) { Value = value; }
        }

        public static UnspecifiedSuccess<TSuccess> Success<TSuccess>(TSuccess value) where TSuccess : notnull
            => new UnspecifiedSuccess<TSuccess>(value);

        public static UnspecifiedFailure<TFailure> Failure<TFailure>(TFailure value) where TFailure : notnull
            => new UnspecifiedFailure<TFailure>(value);
    }
}
