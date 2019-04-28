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

    internal static class Arrays
    {
        internal static T[][] InitTwoDimensionalArray<T>(int x, int y)
        {
            T[][] returnVal = new T[x][];
            for (int c = 0; c < x; c++)
            {
                returnVal[c] = new T[y];
            }
            return returnVal;
        }

        internal static Pointer<Pointer<T>> InitTwoDimensionalArrayPointer<T>(int x, int y)
        {
            Pointer<Pointer<T>> returnVal = Pointer.Malloc<Pointer<T>>(x);
            for (int c = 0; c < x; c++)
            {
                returnVal[c] = Pointer.Malloc<T>(y);
            }
            return returnVal;
        }

        internal static T[][][] InitThreeDimensionalArray<T>(int x, int y, int z)
        {
            T[][][] returnVal = new T[x][][];
            for (int c = 0; c < x; c++)
            {
                returnVal[c] = new T[y][];
                for (int a = 0; a < y; a++)
                {
                    returnVal[c][a] = new T[z];
                }
            }
            return returnVal;
        }

        //FIXME: For the most part this method is used to zero-out arrays, which is usually already done by the runtime.

        internal static void MemSetByte(byte[] array, byte value)
        {
            for (int c = 0; c < array.Length; c++)
            {
                array[c] = value;
            }
        }

        internal static void MemSetInt(int[] array, int value, int length)
        {
            for (int c = 0; c < length; c++)
            {
                array[c] = value;
            }
        }

        internal static void MemSetShort(short[] array, short value, int length)
        {
            for (int c = 0; c < length; c++)
            {
                array[c] = value;
            }
        }

        internal static void MemSetFloat(float[] array, float value, int length)
        {
            for (int c = 0; c < length; c++)
            {
                array[c] = value;
            }
        }

        internal static void MemSetSbyte(sbyte[] array, sbyte value, int length)
        {
            for (int c = 0; c < length; c++)
            {
                array[c] = value;
            }
        }

        internal static void MemSetWithOffset<T>(T[] array, T value, int offset, int length)
        {
            for (int c = offset; c < offset + length; c++)
            {
                array[c] = value;
            }
        }

        internal static void MemMove<T>(T[] array, int src_idx, int dst_idx, int length)
        {
            if (src_idx == dst_idx || length == 0)
                return;

            // Do regions overlap?
            if (src_idx + length > dst_idx || dst_idx + length > src_idx)
            {
                // Take extra precautions
                if (dst_idx < src_idx)
                {
                    // Copy forwards
                    for (int c = 0; c < length; c++)
                    {
                        array[c + dst_idx] = array[c + src_idx];
                    }
                }
                else
                {
                    // Copy backwards
                    for (int c = length - 1; c >= 0; c--)
                    {
                        array[c + dst_idx] = array[c + src_idx];
                    }
                }
            }
            else
            {
                // Memory regions cannot overlap; just do a fast copy
                Array.Copy(array, src_idx, array, dst_idx, length);
            }
        }

        internal static void MemMoveInt(int[] array, int src_idx, int dst_idx, int length)
        {
            if (src_idx == dst_idx || length == 0)
                return;

            // Do regions overlap?
            if (src_idx + length > dst_idx || dst_idx + length > src_idx)
            {
                // Take extra precautions
                if (dst_idx < src_idx)
                {
                    // Copy forwards
                    for (int c = 0; c < length; c++)
                    {
                        array[c + dst_idx] = array[c + src_idx];
                    }
                }
                else
                {
                    // Copy backwards
                    for (int c = length - 1; c >= 0; c--)
                    {
                        array[c + dst_idx] = array[c + src_idx];
                    }
                }
            }
            else
            {
                // Memory regions cannot overlap; just do a fast copy
                Array.Copy(array, src_idx, array, dst_idx, length);
            }
        }

        internal static void MemMoveShort(short[] array, int src_idx, int dst_idx, int length)
        {
            if (src_idx == dst_idx || length == 0)
                return;

            // Do regions overlap?
            if (src_idx + length > dst_idx || dst_idx + length > src_idx)
            {
                // Take extra precautions
                if (dst_idx < src_idx)
                {
                    // Copy forwards
                    for (int c = 0; c < length; c++)
                    {
                        array[c + dst_idx] = array[c + src_idx];
                    }
                }
                else
                {
                    // Copy backwards
                    for (int c = length - 1; c >= 0; c--)
                    {
                        array[c + dst_idx] = array[c + src_idx];
                    }
                }
            }
            else
            {
                // Memory regions cannot overlap; just do a fast copy
                Array.Copy(array, src_idx, array, dst_idx, length);
            }
        }
    }
}
