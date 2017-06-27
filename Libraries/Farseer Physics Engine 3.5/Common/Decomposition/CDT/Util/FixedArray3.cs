/* Poly2Tri
 * Copyright (c) 2009-2010, Poly2Tri Contributors
 * http://code.google.com/p/poly2tri/
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without modification,
 * are permitted provided that the following conditions are met:
 *
 * * Redistributions of source code must retain the above copyright notice,
 *   this list of conditions and the following disclaimer.
 * * Redistributions in binary form must reproduce the above copyright notice,
 *   this list of conditions and the following disclaimer in the documentation
 *   and/or other materials provided with the distribution.
 * * Neither the name of Poly2Tri nor the names of its contributors may be
 *   used to endorse or promote products derived from this software without specific
 *   prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 * "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 * LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
 * A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
 * EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
 * PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
 * LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Collections.Generic;

namespace FarseerPhysics.Common.Decomposition.CDT.Util
{
    internal struct FixedArray3<T> : IEnumerable<T> where T : class
    {
        public T _0, _1, _2;

        public T this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0:
                        return _0;
                    case 1:
                        return _1;
                    case 2:
                        return _2;
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
            set
            {
                switch (index)
                {
                    case 0:
                        _0 = value;
                        break;
                    case 1:
                        _1 = value;
                        break;
                    case 2:
                        _2 = value;
                        break;
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
        }

        #region IEnumerable<T> Members

        public IEnumerator<T> GetEnumerator()
        {
            return Enumerate().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        public bool Contains(T value)
        {
            for (int i = 0; i < 3; ++i) if (this[i] == value) return true;
            return false;
        }

        public int IndexOf(T value)
        {
            for (int i = 0; i < 3; ++i) if (this[i] == value) return i;
            return -1;
        }

        public void Clear()
        {
            _0 = _1 = _2 = null;
        }

        public void Clear(T value)
        {
            for (int i = 0; i < 3; ++i) if (this[i] == value) this[i] = null;
        }

        private IEnumerable<T> Enumerate()
        {
            for (int i = 0; i < 3; ++i) yield return this[i];
        }
    }
}