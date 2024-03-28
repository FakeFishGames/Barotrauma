using System;

namespace Barotrauma;

public static partial class EosInterface
{
    public sealed class EgsAuthContinuanceToken
    {
        // Got this number by checking a decoded continuance token, may be subject to change
        public static readonly TimeSpan Duration = TimeSpan.FromMinutes(30);

        public readonly DateTime ExpiryTime;
        public bool IsValid => value != IntPtr.Zero && DateTime.Now < ExpiryTime;

        private IntPtr value;

        public EgsAuthContinuanceToken(IntPtr value, DateTime expiryTime)
        {
            this.value = value;
            ExpiryTime = expiryTime;
        }

        public IntPtr Spend()
        {
            var retVal = IsValid ? value : IntPtr.Zero;
            value = IntPtr.Zero;
            return retVal;
        }

        public override string ToString()
            => $"{(IsValid ? "Valid" : "Invalid")} EGS ContinuanceToken"
               + (IsValid ? $" (expires on {ExpiryTime})" : "");
    }
}