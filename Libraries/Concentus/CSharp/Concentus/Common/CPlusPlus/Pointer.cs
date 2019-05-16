/* Copyright (c) 2016 Logan Stromberg

   Redistribution and use in source and binary forms, with or without
   modification, are permitted provided that the following conditions
   are met:

   - Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.

   - Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.

   - Neither the name of Internet Society, IETF or IETF Trust, nor the
   names of specific contributors, may be used to endorse or promote
   products derived from this software without specific prior written
   permission.

   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
   ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
   LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
   A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER
   OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
   EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
   PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
   PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
   LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
   NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
   SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

namespace Concentus.Common.CPlusPlus
{
    using System;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Text;

    /// <summary>
    /// This simulates a C++ style pointer as far as can be implemented in C#. It represents a handle
    /// to an array of objects, along with a base offset that represents the address.
    /// When you are programming in debug mode, this class also enforces memory boundaries,
    /// tracks uninitialized values, and also records all statistics of accesses to its base array.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Pointer<T>
    {
        private const bool CHECK_UNINIT_MEM = false;

#if DEBUG && !NET35
        private class Statistics
        {
            public Statistics(int baseOffset)
            {
                this.baseOffset = baseOffset;
            }

            public int baseOffset;
            public int minReadIndex = int.MaxValue;
            public int maxReadIndex = int.MinValue;
            public int minWriteIndex = int.MaxValue;
            public int maxWriteIndex = int.MinValue;

            public Tuple<int, int> ReadRange
            {
                get
                {
                    if (minReadIndex == int.MaxValue || maxReadIndex == int.MinValue)
                        return null;
                    return new Tuple<int, int>(minReadIndex - baseOffset, maxReadIndex - baseOffset);
                }
            }

            public Tuple<int, int> WriteRange
            {
                get
                {
                    if (minWriteIndex == int.MaxValue || maxWriteIndex == int.MinValue)
                        return null;
                    return new Tuple<int, int>(minWriteIndex - baseOffset, maxWriteIndex - baseOffset);
                }
            }
        }
        private bool[] _initialized;
        private Statistics _statistics;
        private int _length;
#endif

        private T[] _array;
        private int _offset;

        public Pointer(int capacity)
        {
            _array = new T[capacity];
            _offset = 0;
#if DEBUG && !NET35
            _length = capacity;
            _statistics = new Statistics(0);
            _initialized = new bool[capacity];
            for (int c = 0; c < capacity; c++)
            {
                _initialized[c] = false;
            }
#endif
        }

        public Pointer(T[] buffer)
        {
            _array = buffer;
            _offset = 0;
#if DEBUG && !NET35
            _length = buffer.Length;
            _statistics = new Statistics(0);
            _initialized = new bool[buffer.Length];
            for (int c = 0; c < buffer.Length; c++)
            {
                _initialized[c] = true;
            }
#endif
        }

        public Pointer(T[] buffer, int absoluteOffset)
        {
            _array = buffer;
            _offset = absoluteOffset;
#if DEBUG && !NET35
            _length = buffer.Length - absoluteOffset;
            //Inlines.OpusAssert(_length >= 0, "Attempted to point past the end of an array");
            _statistics = new Statistics(absoluteOffset);
            _initialized = new bool[buffer.Length];
            for (int c = 0; c < buffer.Length; c++)
            {
                _initialized[c] = true;
            }
#endif
        }

#if DEBUG && !NET35
        private Pointer(T[] buffer, int absoluteOffset, Statistics statistics, bool[] initializedStatus)
        {
            _array = buffer;
            _offset = absoluteOffset;
            _length = buffer.Length - absoluteOffset;
            //Inlines.OpusAssert(_length >= 0, "Attempted to point past the end of an array");
            _statistics = statistics;
            _initialized = initializedStatus;
        }

        public Tuple<int, int> ReadRange
        {
            get
            {
                return _statistics.ReadRange;
            }
        }

        public Tuple<int, int> WriteRange
        {
            get
            {
                return _statistics.WriteRange;
            }
        }

        public int Length
        {
            get
            {
                return _length;
            }
        }
#endif

        public int Offset
        {
            get
            {
                return _offset;
            }
        }

        // This should only be temporary while I migrate from pointers to arrays
        public T[] Data
        {
            get
            {
                return _array;
            }
        }

        public T this[int index]
        {
            get
            {
#if DEBUG && !NET35
#pragma warning disable 162
                if (CHECK_UNINIT_MEM) Inlines.OpusAssert(_initialized[index + _offset], "Attempted to read from uninitialized memory!");
#pragma warning restore 162
                // Inlines.OpusAssert(index < _length, "Attempted to read past the end of an array!");
                _statistics.maxReadIndex = Math.Max(_statistics.maxReadIndex, index + _offset);
                _statistics.minReadIndex = Math.Min(_statistics.minReadIndex, index + _offset);
#endif
                return _array[index + _offset];
            }

            set
            {
#if DEBUG && !NET35
                // Inlines.OpusAssert(index < _length, "Attempted to write past the end of an array!");
                _statistics.maxWriteIndex = Math.Max(_statistics.maxWriteIndex, index + _offset);
                _statistics.minWriteIndex = Math.Min(_statistics.minWriteIndex, index + _offset);
                _initialized[index + _offset] = true;
#endif
                _array[index + _offset] = value;
            }
        }

        public T this[uint index]
        {
            get
            {
                return this[(int)index];
            }

            set
            {
                this[(int)index] = value;
            }
        }

        /// <summary>
        /// Returns the value currently under the pointer, and returns a new pointer with +1 offset.
        /// This method is not very efficient because it creates new pointers; this is because we must preserve
        /// the pass-by-value nature of C++ pointers when they are used as arguments to functions
        /// </summary>
        /// <returns></returns>
        public Pointer<T> Iterate(out T returnVal)
        {
            returnVal = _array[_offset];
            return Point(1);
        }

#if DEBUG && !NET35
        public Pointer<T> Point(int relativeOffset)
        {
            if (relativeOffset == 0) return this;
            return new Pointer<T>(_array, _offset + relativeOffset, _statistics, _initialized);
        }

        public Pointer<T> Point(uint relativeOffset)
        {
            if (relativeOffset == 0) return this;
            return new Pointer<T>(_array, _offset + (int)relativeOffset, _statistics, _initialized);
        }
#else
        public Pointer<T> Point(int relativeOffset)
        {
            if (relativeOffset == 0) return this;
            return new Pointer<T>(_array, _offset + relativeOffset);
        }

        public Pointer<T> Point(uint relativeOffset)
        {
            if (relativeOffset == 0) return this;
            return new Pointer<T>(_array, _offset + (int)relativeOffset);
        }
#endif

        private static string invert_endianness(string hexstring)
        {
            StringBuilder b = new StringBuilder(hexstring.Length);
            for (int c = 0; c < hexstring.Length / 2; c++)
            {
                b.Append(hexstring.Substring(hexstring.Length - ((c + 1) * 2), 2));
            }
            return b.ToString();
        }

        private static void PrintMemCopy<E>(E[] source, int sourceOffset, int length)
        {
            if (typeof(E) == typeof(int) || typeof(E) == typeof(uint))
            {
                Debug.WriteLine(string.Format("memcpy of {0} bytes", length * 4));
                string buf = string.Empty;
                for (int c = 0; c < length; c++)
                {
                    buf += invert_endianness(string.Format("{0:x8}", source[c + sourceOffset]));
                }
                Debug.WriteLine(buf);
            }
            else if (typeof(E) == typeof(short) || typeof(E) == typeof(ushort))
            {
                Debug.WriteLine(string.Format("memcpy of {0} bytes", length * 2));
                string buf = string.Empty;
                for (int c = 0; c < length; c++)
                {
                    buf += invert_endianness(string.Format("{0:x4}", source[c + sourceOffset]));
                }
                Debug.WriteLine(buf);
            }
            else if (typeof(E) == typeof(byte) || typeof(E) == typeof(sbyte))
            {
                Debug.WriteLine(string.Format("memcpy of {0} bytes", length));
                string buf = string.Empty;
                for (int c = 0; c < length; c++)
                {
                    buf += invert_endianness(string.Format("{0:x2}", source[c + sourceOffset]));
                }
                Debug.WriteLine(buf);
            }
        }

        /// <summary>
        /// Copies the contents of this pointer, starting at its current address, into the space of another pointer.
        /// !!! IMPORTANT !!! REMEMBER THAT C++ memcpy is (DEST, SOURCE, LENGTH) !!!!
        /// IN C# IT IS (SOURCE, DEST, LENGTH). DON'T GET SCOOPED LIKE I DID
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="length"></param>
#if DEBUG
        public void MemCopyTo(Pointer<T> destination, int length, bool debug = false)
        {
            Inlines.OpusAssert(length >= 0, "Cannot memcopy() with a negative length!");
            if (debug)
                PrintMemCopy(_array, _offset, length);
            
            for (int c = 0; c < length; c++)
            {
                destination[c] = _array[c + _offset];
            }
        }
#else
        public void MemCopyTo(Pointer<T> destination, int length)
        {
            if (destination is Pointer<T>)
            {
                // Use the fast way if we have access to the base array
                Array.Copy(_array, _offset, ((Pointer<T>)destination)._array, destination.Offset, length);
            }
            else
            {
                // Otherwise do it the slow way
                for (int c = 0; c < length; c++)
                {
                    destination[c] = _array[c + _offset];
                }
            }
        }
#endif

        /// <summary>
        /// Copies the contents of this pointer, starting at its current address, into an array.
        /// !!! IMPORTANT !!! REMEMBER THAT C++ memcpy is (DEST, SOURCE, LENGTH) !!!!
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="length"></param>
#if DEBUG
        public void MemCopyTo(T[] destination, int destOffset, int length)
        {
            Inlines.OpusAssert(length >= 0, "Cannot memcopy() with a negative length!");
            //PrintMemCopy(_array, _offset, length);
            for (int c = 0; c < length; c++)
            {
                destination[c + destOffset] = _array[c + _offset];
            }
        }
#else
        public void MemCopyTo(T[] destination, int offset, int length)
        {
            // Use the fast way if we have access to the base array
            Array.Copy(_array, _offset, destination, offset, length);
        }
#endif

        /// <summary>
        /// Loads N values from a source array into this pointer's space
        /// </summary>
        /// <param name="length"></param>
#if DEBUG && !NET35
        public void MemCopyFrom(T[] source, int sourceOffset, int length)
        {
            Inlines.OpusAssert(length >= 0, "Cannot memcopy() with a negative length!");
            //PrintMemCopy(source, sourceOffset, length);
            for (int c = 0; c < length; c++)
            {
                _array[c + _offset] = source[c + sourceOffset];
                _initialized[c + _offset] = true;
            }
        }
#else
        public void MemCopyFrom(T[] source, int sourceOffset, int length)
        {
            Array.Copy(source, sourceOffset, _array, _offset, length);
        }
#endif

        /// <summary>
        /// Assigns a certain value to a range of spaces in this array
        /// </summary>
        /// <param name="value">The value to set</param>
        /// <param name="length">The number of values to write</param>
        public void MemSet(T value, int length)
        {
#if DEBUG
            Inlines.OpusAssert(length >= 0, "Cannot memset() with a negative length!");
#endif
            MemSet(value, (uint)length);
        }

        /// <summary>
        /// Assigns a certain value to a range of spaces in this array
        /// </summary>
        /// <param name="value">The value to set</param>
        /// <param name="length">The number of values to write</param>
        public void MemSet(T value, uint length)
        {
            for (int c = _offset; c < _offset + length; c++)
            {
                _array[c] = value;
#if DEBUG && !NET35
                _initialized[c] = true;
#endif
            }
        }

        public void MemMoveTo(Pointer<T> other, int length)
        {
            if (_array == other._array)
            {
                // Pointers refer to the same array, perform a move
                //if (debug)
                //    PrintMemCopy(_array, _offset, length);
                MemMove(other.Offset - Offset, length);
            }
            else
            {
                // Pointers refer to different arrays (if you end up here you probably wanted to just to MemCopy())
                // Debug.WriteLine("Unnecessary memmove detected");
                MemCopyTo(other, length);
            }
        }

        /// <summary>
        /// Moves regions of memory within the bounds of this pointer's array.
        /// Extra checks are done to ensure that the data is not corrupted if the copy
        /// regions overlap
        /// </summary>
        /// <param name="move_dist">The offset to send this pointer's data to</param>
        /// <param name="length">The number of values to copy</param>
#if DEBUG && !NET35
        public void MemMove(int move_dist, int length)
        {
            Inlines.OpusAssert(length >= 0, "Cannot memmove() with a negative length!");
            if (move_dist == 0 || length == 0)
                return;

            // Do regions overlap?
            if ((move_dist > 0 && move_dist < length) || (move_dist < 0 && 0 - move_dist > length))
            {
                // Take extra precautions
                if (move_dist < 0)
                {
                    // Copy forwards
                    for (int c = 0; c < length; c++)
                    {
                        _array[c + _offset + move_dist] = _array[c + _offset];
                        _initialized[c + _offset + move_dist] = true;
                    }
                }
                else
                {
                    // Copy backwards
                    for (int c = length - 1; c >= 0; c--)
                    {
                        _array[c + _offset + move_dist] = _array[c + _offset];
                        _initialized[c + _offset + move_dist] = true;
                    }
                }
            }
            else
            {
                for (int c = 0; c < length; c++)
                {
                    _array[c + _offset + move_dist] = _array[c + _offset];
                    _initialized[c + _offset + move_dist] = true;
                }
            }
        }
#else
        public void MemMove(int move_dist, int length)
        {
            Arrays.MemMove(_array, _offset, _offset + move_dist, length);
        }
#endif

        /*/// <summary>
        /// Simulates pointer zooming: newPtr = &amp;ptr[offset].
        /// Returns a pointer that is offset from this one within the same buffer.
        /// </summary>
        /// <param name="arg"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        internal static Pointer<T> operator +(Pointer<T> arg, int offset)
        {
            return new Pointer<T>(arg._array, arg._offset + offset);
        }*/

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }
            Pointer<T> other = (Pointer<T>)obj;
            return other._offset == _offset &&
                other._array == _array;
        }
        
        public override int GetHashCode()
        {
            return _array.GetHashCode() + _offset.GetHashCode();
        }
    }

    /// <summary>
    /// This is a helper class which contains static methods that involve pointers
    /// </summary>
    public static class Pointer
    {
        /// <summary>
        /// Allocates a new array and returns a pointer to it
        /// </summary>
        /// <typeparam name="E"></typeparam>
        /// <param name="capacity"></param>
        /// <returns></returns>
        public static Pointer<E> Malloc<E>(int capacity)
        {
            //this returns a pointer inside of a random field, to make sure offset indexing works properly
            //E[] field = new E[capacity * 2];
            //return new Pointer<E>(field, new Random().Next(0, capacity - 1));
            return new Pointer<E>(capacity);
        }

        /// <summary>
        /// Creates a pointer to an existing array
        /// </summary>
        /// <typeparam name="E"></typeparam>
        /// <param name="memory"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static Pointer<E> GetPointer<E>(this E[] memory, int offset = 0)
        {
            if (memory == null)
                return null;
            //if (Debugger.IsAttached && offset == memory.Length / 2)
            //{
            //    // This may be a partitioned array. Signal the debugger
            //    Debugger.Break();
            //}
            return new Pointer<E>(memory, offset);
        }
    }
}