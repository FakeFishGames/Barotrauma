using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Barotrauma
{
    /// <summary>
    /// A generic wrapper around System.Buffers.ArrayPool to provide a consistent 
    /// interface for renting and returning arrays, reducing GC pressure.
    /// </summary>
    public static class ArrayPoolBase<T>
    {
        // Using the shared pool which is thread-safe and optimized for general use.
        private static readonly ArrayPool<T> Pool = ArrayPool<T>.Shared;

        /// <summary>
        /// Rents a buffer of at least the specified size. 
        /// Note: The buffer is not guaranteed to be empty.
        /// </summary>
        public static T[] Rent(int size)
        {
            return Pool.Rent(size);
        }

        /// <summary>
        /// Rents a buffer and ensures it is cleared before use.
        /// Useful if the logic depends on default-initialized values.
        /// </summary>
        public static T[] RentZeroed(int size)
        {
            T[] buffer = Pool.Rent(size);
            Array.Clear(buffer, 0, size);
            return buffer;
        }

        /// <summary>
        /// Returns the buffer to the pool for future reuse.
        /// Clears the array to prevent memory leaks (sensitive data) 
        /// and avoid accidental cross-contamination.
        /// </summary>
        public static void Return(T[] buffer)
        {
            Pool.Return(buffer, clearArray: true);
        }

        /// <summary>
        /// Rents a buffer and executes an action. 
        /// The buffer is automatically returned to the pool after the action completes.
        /// This is the safest way to prevent memory leaks.
        /// </summary>
        public static void RentImmediate(int size, Action<T[]> action)
        {
            T[] buffer = Pool.Rent(size);
            try
            {
                action(buffer);
            }
            finally
            {
                // Ensure the buffer is returned even if the action throws an exception.
                Pool.Return(buffer, clearArray: true);
            }
        }

        /// <summary>
        /// Rents a buffer and executes a function that returns a result.
        /// The buffer is returned to the pool immediately after the function finishes.
        /// </summary>
        public static TResult RentImmediate<TResult>(int size, Func<T[], TResult> func)
        {
            T[] buffer = Pool.Rent(size);
            try
            {
                return func(buffer);
            }
            finally
            {
                Pool.Return(buffer, clearArray: true);
            }
        }
    }

    /// <summary>
    /// Type-specific alias for managing float arrays efficiently.
    /// </summary>
    public static class FloatArrayPool
    {
        /// <summary>
        /// Rents a buffer of at least the specified size. 
        /// Note: The buffer is not guaranteed to be empty.
        /// </summary>
        public static float[] Rent(int size) => ArrayPoolBase<float>.Rent(size);
        /// <summary>
        /// Rents a buffer and ensures it is cleared before use.
        /// Useful if the logic depends on default-initialized values.
        /// </summary>
        public static float[] RentZeroed(int size) => ArrayPoolBase<float>.RentZeroed(size);
        /// <summary>
        /// Returns the buffer to the pool for future reuse.
        /// Clears the array to prevent memory leaks (sensitive data) 
        /// and avoid accidental cross-contamination.
        /// </summary>
        public static void Return(float[] buffer) => ArrayPoolBase<float>.Return(buffer);
        /// <summary>
        /// Rents a buffer and executes an action. 
        /// The buffer is automatically returned to the pool after the action completes.
        /// This is the safest way to prevent memory leaks.
        /// </summary>
        public static void RentImmediate(int size, Action<float[]> action) => ArrayPoolBase<float>.RentImmediate(size, action);
        /// <summary>
        /// Rents a buffer and executes an action. 
        /// The buffer is automatically returned to the pool after the action completes.
        /// This is the safest way to prevent memory leaks.
        /// </summary>
        public static TResult RentImmediate<TResult>(int size, Func<float[], TResult> func) => ArrayPoolBase<float>.RentImmediate(size, func);
    }

    /// <summary>
    /// Type-specific alias for managing byte arrays efficiently.
    /// Frequently used for network packets and file streams.
    /// </summary>
    public static class ByteArrayPool
    {
        /// <summary>
        /// Rents a buffer of at least the specified size. 
        /// Note: The buffer is not guaranteed to be empty.
        /// </summary>
        public static byte[] Rent(int size) => ArrayPoolBase<byte>.Rent(size);
        /// <summary>
        /// Rents a buffer and ensures it is cleared before use.
        /// Useful if the logic depends on default-initialized values.
        /// </summary>
        public static byte[] RentZeroed(int size) => ArrayPoolBase<byte>.RentZeroed(size);
        /// <summary>
        /// Returns the buffer to the pool for future reuse.
        /// Clears the array to prevent memory leaks (sensitive data) 
        /// and avoid accidental cross-contamination.
        /// </summary>
        public static void Return(byte[] buffer) => ArrayPoolBase<byte>.Return(buffer);
        /// <summary>
        /// Rents a buffer and executes an action. 
        /// The buffer is automatically returned to the pool after the action completes.
        /// This is the safest way to prevent memory leaks.
        /// </summary>
        public static void RentImmediate(int size, Action<byte[]> action) => ArrayPoolBase<byte>.RentImmediate(size, action);
        /// <summary>
        /// Rents a buffer and executes an action. 
        /// The buffer is automatically returned to the pool after the action completes.
        /// This is the safest way to prevent memory leaks.
        /// </summary>
        public static TResult RentImmediate<TResult>(int size, Func<byte[], TResult> func) => ArrayPoolBase<byte>.RentImmediate(size, func);
    }
    public class PooledBuffer : IDisposable
    {
        public byte[] Array { get; private set; }
        public int Length { get; private set; }
        private bool _disposed;

        public PooledBuffer(int size)
        {
            Array = ByteArrayPool.Rent(size);
            Length = size;
            _disposed = false;
        }
        public PooledBuffer(byte[] data)
        {
            Array = ByteArrayPool.Rent(data.Length);
            Length = data.Length;
            _disposed = false;

            data.AsSpan().CopyTo(Array);
        }
        public PooledBuffer(ImmutableArray<byte> data)
        {
            Array = ByteArrayPool.Rent(data.Length);
            Length = data.Length;
            _disposed = false;
            data.AsSpan().CopyTo(Array);
        }
        public PooledBuffer(Span<byte> data)
        {
            Array = ByteArrayPool.Rent(data.Length);
            Length = data.Length;
            _disposed = false;
            data.CopyTo(Array);
        }
        public PooledBuffer(IntPtr data, int size, int startIndex = 0) : this(size)
        {
            if (data != IntPtr.Zero && size > 0)
            {
                Marshal.Copy(data, Array, startIndex, size);
            }
        }
        /// <summary>
        /// Creates a deep copy of this buffer in a new PooledBuffer instance.
        /// </summary>
        public PooledBuffer Clone()
        {
            var clone = new PooledBuffer(Length);

            AsSpan().CopyTo(clone.AsSpan());

            return clone;
        }
        public Span<byte> AsSpan() => Array.AsSpan(0, Length);
        public Span<byte> AsSpan(int start) => Array.AsSpan(start, Length - start);
        public Span<byte> AsSpan(int start, int count)
        {
            if (start < 0 || count < 0 || (start + count) > Length)
            {
                throw new ArgumentOutOfRangeException("The specified start or length is out of bounds for this buffer.");
            }
            return Array.AsSpan(start, count);
        }
        public Span<byte> AsSpanRange(int start, int end)
        {
            int count = end - start;
            return AsSpan(start, count);
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this); // Tell GC we already cleaned up, no need to run finalizer
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (Array != null)
                {
                    ByteArrayPool.Return(Array);
                    Array = null;
                }
                _disposed = true;
            }
        }

        // The Finalizer (Destructor)
        ~PooledBuffer()
        {
            Dispose(false);
        }
        public void Resize(int newSize){
            if (newSize <= Array.Length)
            {
                // If the new size fits in the existing buffer, 
                // we just update our internal length tracker as ArrayPool uses multiples of 2.
                Length = newSize;
                return;
            }

        
            byte[] newArray = ByteArrayPool.Rent(newSize);
            Array.AsSpan(0, Length).CopyTo(newArray);

            ByteArrayPool.Return(Array);

            Array = newArray;
            Length = newSize;
        }
        public byte this[int index]
        {
            get => Array[index];
            set => Array[index] = value;
        }

        public static implicit operator byte[](PooledBuffer buffer) => buffer.Array;
    }
}