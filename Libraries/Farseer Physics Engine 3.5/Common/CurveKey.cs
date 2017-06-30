#if !XNA && !WINDOWS_PHONE && !XBOX && !ANDROID

#region License

/*
MIT License
Copyright © 2006 The Mono.Xna Team

All rights reserved.

Authors:
Olivier Dufour (Duff)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

#endregion License

using System;

namespace Microsoft.Xna.Framework
{
    public class CurveKey : IEquatable<CurveKey>, IComparable<CurveKey>
    {
#region Private Fields

        private CurveContinuity continuity;
        private float position;
        private float tangentIn;
        private float tangentOut;
        private float value;

#endregion Private Fields

#region Properties

        public CurveContinuity Continuity
        {
            get { return continuity; }
            set { continuity = value; }
        }

        public float Position
        {
            get { return position; }
        }

        public float TangentIn
        {
            get { return tangentIn; }
            set { tangentIn = value; }
        }

        public float TangentOut
        {
            get { return tangentOut; }
            set { tangentOut = value; }
        }

        public float Value
        {
            get { return value; }
            set { this.value = value; }
        }

#endregion

#region Constructors

        public CurveKey(float position, float value)
            : this(position, value, 0, 0, CurveContinuity.Smooth)
        {
        }

        public CurveKey(float position, float value, float tangentIn, float tangentOut)
            : this(position, value, tangentIn, tangentOut, CurveContinuity.Smooth)
        {
        }

        public CurveKey(float position, float value, float tangentIn, float tangentOut, CurveContinuity continuity)
        {
            this.position = position;
            this.value = value;
            this.tangentIn = tangentIn;
            this.tangentOut = tangentOut;
            this.continuity = continuity;
        }

#endregion Constructors

#region Public Methods

#region IComparable<CurveKey> Members

        public int CompareTo(CurveKey other)
        {
            return position.CompareTo(other.position);
        }

#endregion

#region IEquatable<CurveKey> Members

        public bool Equals(CurveKey other)
        {
            return (this == other);
        }

#endregion

        public static bool operator !=(CurveKey a, CurveKey b)
        {
            return !(a == b);
        }

        public static bool operator ==(CurveKey a, CurveKey b)
        {
            if (Equals(a, null))
                return Equals(b, null);

            if (Equals(b, null))
                return Equals(a, null);

            return (a.position == b.position)
                   && (a.value == b.value)
                   && (a.tangentIn == b.tangentIn)
                   && (a.tangentOut == b.tangentOut)
                   && (a.continuity == b.continuity);
        }

        public CurveKey Clone()
        {
            return new CurveKey(position, value, tangentIn, tangentOut, continuity);
        }

        public override bool Equals(object obj)
        {
            return (obj is CurveKey) ? ((CurveKey) obj) == this : false;
        }

        public override int GetHashCode()
        {
            return position.GetHashCode() ^ value.GetHashCode() ^ tangentIn.GetHashCode() ^
                   tangentOut.GetHashCode() ^ continuity.GetHashCode();
        }

#endregion
    }
}

#endif