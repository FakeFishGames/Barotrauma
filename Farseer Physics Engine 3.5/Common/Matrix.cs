#if !XNA && !WINDOWS_PHONE && !XBOX && !ANDROID

#region License

/*
MIT License
Copyright © 2006 The Mono.Xna Team

All rights reserved.

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
using System.Runtime.InteropServices;

namespace Microsoft.Xna.Framework
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Matrix : IEquatable<Matrix>
    {
#region Public Fields

        public float M11;
        public float M12;
        public float M13;
        public float M14;
        public float M21;
        public float M22;
        public float M23;
        public float M24;
        public float M31;
        public float M32;
        public float M33;
        public float M34;
        public float M41;
        public float M42;
        public float M43;
        public float M44;

#endregion Public Fields

#region Static Properties

        private static Matrix identity = new Matrix(1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f);

        public static Matrix Identity
        {
            get { return identity; }
        }

#endregion Static Properties

#region Public Properties

        public Vector3 Backward
        {
            get { return new Vector3(M31, M32, M33); }
            set
            {
                M31 = value.X;
                M32 = value.Y;
                M33 = value.Z;
            }
        }

        public Vector3 Down
        {
            get { return new Vector3(-M21, -M22, -M23); }
            set
            {
                M21 = -value.X;
                M22 = -value.Y;
                M23 = -value.Z;
            }
        }

        public Vector3 Forward
        {
            get { return new Vector3(-M31, -M32, -M33); }
            set
            {
                M31 = -value.X;
                M32 = -value.Y;
                M33 = -value.Z;
            }
        }

        public Vector3 Left
        {
            get { return new Vector3(-M11, -M12, -M13); }
            set
            {
                M11 = -value.X;
                M12 = -value.Y;
                M13 = -value.Z;
            }
        }

        public Vector3 Right
        {
            get { return new Vector3(M11, M12, M13); }
            set
            {
                M11 = value.X;
                M12 = value.Y;
                M13 = value.Z;
            }
        }

        public Vector3 Translation
        {
            get { return new Vector3(M41, M42, M43); }
            set
            {
                M41 = value.X;
                M42 = value.Y;
                M43 = value.Z;
            }
        }

        public Vector3 Up
        {
            get { return new Vector3(M21, M22, M23); }
            set
            {
                M21 = value.X;
                M22 = value.Y;
                M23 = value.Z;
            }
        }

#endregion Public Properties

#region Constructors

        /// <summary>
        /// Constructor for 4x4 Matrix
        /// </summary>
        /// <param name="m11">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m12">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m13">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m14">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m21">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m22">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m23">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m24">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m31">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m32">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m33">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m34">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m41">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m42">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m43">
        /// A <see cref="System.Single"/>
        /// </param>
        /// <param name="m44">
        /// A <see cref="System.Single"/>
        /// </param>
        public Matrix(float m11, float m12, float m13, float m14, float m21, float m22, float m23, float m24,
                      float m31, float m32, float m33, float m34, float m41, float m42, float m43, float m44)
        {
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M14 = m14;
            M21 = m21;
            M22 = m22;
            M23 = m23;
            M24 = m24;
            M31 = m31;
            M32 = m32;
            M33 = m33;
            M34 = m34;
            M41 = m41;
            M42 = m42;
            M43 = m43;
            M44 = m44;
        }

#endregion Constructors

#region Public Static Methods

        public static Matrix CreateWorld(Vector3 position, Vector3 forward, Vector3 up)
        {
            Matrix ret;
            CreateWorld(ref position, ref forward, ref up, out ret);
            return ret;
        }

        public static void CreateWorld(ref Vector3 position, ref Vector3 forward, ref Vector3 up, out Matrix result)
        {
            Vector3 x, y, z;
            Vector3.Normalize(ref forward, out z);
            Vector3.Cross(ref forward, ref up, out x);
            Vector3.Cross(ref x, ref forward, out y);
            x.Normalize();
            y.Normalize();

            result = new Matrix();
            result.Right = x;
            result.Up = y;
            result.Forward = z;
            result.Translation = position;
            result.M44 = 1f;
        }

        /// <summary>
        /// Adds second matrix to the first.
        /// </summary>
        /// <param name="matrix1">
        /// A <see cref="Matrix"/>
        /// </param>
        /// <param name="matrix2">
        /// A <see cref="Matrix"/>
        /// </param>
        /// <returns>
        /// A <see cref="Matrix"/>
        /// </returns>
        public static Matrix Add(Matrix matrix1, Matrix matrix2)
        {
            matrix1.M11 += matrix2.M11;
            matrix1.M12 += matrix2.M12;
            matrix1.M13 += matrix2.M13;
            matrix1.M14 += matrix2.M14;
            matrix1.M21 += matrix2.M21;
            matrix1.M22 += matrix2.M22;
            matrix1.M23 += matrix2.M23;
            matrix1.M24 += matrix2.M24;
            matrix1.M31 += matrix2.M31;
            matrix1.M32 += matrix2.M32;
            matrix1.M33 += matrix2.M33;
            matrix1.M34 += matrix2.M34;
            matrix1.M41 += matrix2.M41;
            matrix1.M42 += matrix2.M42;
            matrix1.M43 += matrix2.M43;
            matrix1.M44 += matrix2.M44;
            return matrix1;
        }


        /// <summary>
        /// Adds two Matrix and save to the result Matrix
        /// </summary>
        /// <param name="matrix1">
        /// A <see cref="Matrix"/>
        /// </param>
        /// <param name="matrix2">
        /// A <see cref="Matrix"/>
        /// </param>
        /// <param name="result">
        /// A <see cref="Matrix"/>
        /// </param>
        public static void Add(ref Matrix matrix1, ref Matrix matrix2, out Matrix result)
        {
            result.M11 = matrix1.M11 + matrix2.M11;
            result.M12 = matrix1.M12 + matrix2.M12;
            result.M13 = matrix1.M13 + matrix2.M13;
            result.M14 = matrix1.M14 + matrix2.M14;
            result.M21 = matrix1.M21 + matrix2.M21;
            result.M22 = matrix1.M22 + matrix2.M22;
            result.M23 = matrix1.M23 + matrix2.M23;
            result.M24 = matrix1.M24 + matrix2.M24;
            result.M31 = matrix1.M31 + matrix2.M31;
            result.M32 = matrix1.M32 + matrix2.M32;
            result.M33 = matrix1.M33 + matrix2.M33;
            result.M34 = matrix1.M34 + matrix2.M34;
            result.M41 = matrix1.M41 + matrix2.M41;
            result.M42 = matrix1.M42 + matrix2.M42;
            result.M43 = matrix1.M43 + matrix2.M43;
            result.M44 = matrix1.M44 + matrix2.M44;
        }


        public static Matrix CreateBillboard(Vector3 objectPosition, Vector3 cameraPosition,
                                             Vector3 cameraUpVector, Nullable<Vector3> cameraForwardVector)
        {
            Matrix ret;
            CreateBillboard(ref objectPosition, ref cameraPosition, ref cameraUpVector, cameraForwardVector, out ret);
            return ret;
        }

        public static void CreateBillboard(ref Vector3 objectPosition, ref Vector3 cameraPosition,
                                           ref Vector3 cameraUpVector, Vector3? cameraForwardVector, out Matrix result)
        {
            Vector3 translation = objectPosition - cameraPosition;
            Vector3 backwards, right, up;
            Vector3.Normalize(ref translation, out backwards);
            Vector3.Normalize(ref cameraUpVector, out up);
            Vector3.Cross(ref backwards, ref up, out right);
            Vector3.Cross(ref backwards, ref right, out up);
            result = Identity;
            result.Backward = backwards;
            result.Right = right;
            result.Up = up;
            result.Translation = translation;
        }

        public static Matrix CreateConstrainedBillboard(Vector3 objectPosition, Vector3 cameraPosition,
                                                        Vector3 rotateAxis, Nullable<Vector3> cameraForwardVector,
                                                        Nullable<Vector3> objectForwardVector)
        {
            throw new NotImplementedException();
        }


        public static void CreateConstrainedBillboard(ref Vector3 objectPosition, ref Vector3 cameraPosition,
                                                      ref Vector3 rotateAxis, Vector3? cameraForwardVector,
                                                      Vector3? objectForwardVector, out Matrix result)
        {
            throw new NotImplementedException();
        }


        public static Matrix CreateFromAxisAngle(Vector3 axis, float angle)
        {
            throw new NotImplementedException();
        }


        public static void CreateFromAxisAngle(ref Vector3 axis, float angle, out Matrix result)
        {
            throw new NotImplementedException();
        }

        public static Matrix CreateLookAt(Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUpVector)
        {
            Matrix ret;
            CreateLookAt(ref cameraPosition, ref cameraTarget, ref cameraUpVector, out ret);
            return ret;
        }


        public static void CreateLookAt(ref Vector3 cameraPosition, ref Vector3 cameraTarget, ref Vector3 cameraUpVector,
                                        out Matrix result)
        {
            // http://msdn.microsoft.com/en-us/library/bb205343(v=VS.85).aspx

            Vector3 vz = Vector3.Normalize(cameraPosition - cameraTarget);
            Vector3 vx = Vector3.Normalize(Vector3.Cross(cameraUpVector, vz));
            Vector3 vy = Vector3.Cross(vz, vx);
            result = Identity;
            result.M11 = vx.X;
            result.M12 = vy.X;
            result.M13 = vz.X;
            result.M21 = vx.Y;
            result.M22 = vy.Y;
            result.M23 = vz.Y;
            result.M31 = vx.Z;
            result.M32 = vy.Z;
            result.M33 = vz.Z;
            result.M41 = -Vector3.Dot(vx, cameraPosition);
            result.M42 = -Vector3.Dot(vy, cameraPosition);
            result.M43 = -Vector3.Dot(vz, cameraPosition);
        }

        public static Matrix CreateOrthographic(float width, float height, float zNearPlane, float zFarPlane)
        {
            Matrix ret;
            CreateOrthographic(width, height, zNearPlane, zFarPlane, out ret);
            return ret;
        }


        public static void CreateOrthographic(float width, float height, float zNearPlane, float zFarPlane,
                                              out Matrix result)
        {
            result.M11 = 2/width;
            result.M12 = 0;
            result.M13 = 0;
            result.M14 = 0;
            result.M21 = 0;
            result.M22 = 2/height;
            result.M23 = 0;
            result.M24 = 0;
            result.M31 = 0;
            result.M32 = 0;
            result.M33 = 1/(zNearPlane - zFarPlane);
            result.M34 = 0;
            result.M41 = 0;
            result.M42 = 0;
            result.M43 = zNearPlane/(zNearPlane - zFarPlane);
            result.M44 = 1;
        }


        public static Matrix CreateOrthographicOffCenter(float left, float right, float bottom, float top,
                                                         float zNearPlane, float zFarPlane)
        {
            Matrix ret;
            CreateOrthographicOffCenter(left, right, bottom, top, zNearPlane, zFarPlane, out ret);
            return ret;
        }


        public static void CreateOrthographicOffCenter(float left, float right, float bottom, float top,
                                                       float zNearPlane, float zFarPlane, out Matrix result)
        {
            result.M11 = 2/(right - left);
            result.M12 = 0;
            result.M13 = 0;
            result.M14 = 0;
            result.M21 = 0;
            result.M22 = 2/(top - bottom);
            result.M23 = 0;
            result.M24 = 0;
            result.M31 = 0;
            result.M32 = 0;
            result.M33 = 1/(zNearPlane - zFarPlane);
            result.M34 = 0;
            result.M41 = (left + right)/(left - right);
            result.M42 = (bottom + top)/(bottom - top);
            result.M43 = zNearPlane/(zNearPlane - zFarPlane);
            result.M44 = 1;
        }


        public static Matrix CreatePerspective(float width, float height, float zNearPlane, float zFarPlane)
        {
            throw new NotImplementedException();
        }


        public static void CreatePerspective(float width, float height, float zNearPlane, float zFarPlane,
                                             out Matrix result)
        {
            throw new NotImplementedException();
        }


        public static Matrix CreatePerspectiveFieldOfView(float fieldOfView, float aspectRatio, float nearPlaneDistance,
                                                          float farPlaneDistance)
        {
            Matrix ret;
            CreatePerspectiveFieldOfView(fieldOfView, aspectRatio, nearPlaneDistance, farPlaneDistance, out ret);
            return ret;
        }


        public static void CreatePerspectiveFieldOfView(float fieldOfView, float aspectRatio, float nearPlaneDistance,
                                                        float farPlaneDistance, out Matrix result)
        {
            // http://msdn.microsoft.com/en-us/library/bb205351(v=VS.85).aspx
            // http://msdn.microsoft.com/en-us/library/bb195665.aspx

            result = new Matrix(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

            if (fieldOfView < 0 || fieldOfView > 3.14159262f)
                throw new ArgumentOutOfRangeException("fieldOfView",
                                                      "fieldOfView takes a value between 0 and Pi (180 degrees) in radians.");

            if (nearPlaneDistance <= 0.0f)
                throw new ArgumentOutOfRangeException("nearPlaneDistance",
                                                      "You should specify positive value for nearPlaneDistance.");

            if (farPlaneDistance <= 0.0f)
                throw new ArgumentOutOfRangeException("farPlaneDistance",
                                                      "You should specify positive value for farPlaneDistance.");

            if (farPlaneDistance <= nearPlaneDistance)
                throw new ArgumentOutOfRangeException("nearPlaneDistance",
                                                      "Near plane distance is larger than Far plane distance. Near plane distance must be smaller than Far plane distance.");

            float yscale = (float) 1/(float) Math.Tan(fieldOfView/2);
            float xscale = yscale/aspectRatio;

            result.M11 = xscale;
            result.M22 = yscale;
            result.M33 = farPlaneDistance/(nearPlaneDistance - farPlaneDistance);
            result.M34 = -1;
            result.M43 = nearPlaneDistance*farPlaneDistance/(nearPlaneDistance - farPlaneDistance);
        }


        public static Matrix CreatePerspectiveOffCenter(float left, float right, float bottom, float top,
                                                        float zNearPlane, float zFarPlane)
        {
            throw new NotImplementedException();
        }


        public static void CreatePerspectiveOffCenter(float left, float right, float bottom, float top,
                                                      float nearPlaneDistance, float farPlaneDistance, out Matrix result)
        {
            throw new NotImplementedException();
        }


        public static Matrix CreateRotationX(float radians)
        {
            Matrix returnMatrix = Identity;

            returnMatrix.M22 = (float) Math.Cos(radians);
            returnMatrix.M23 = (float) Math.Sin(radians);
            returnMatrix.M32 = -returnMatrix.M23;
            returnMatrix.M33 = returnMatrix.M22;

            return returnMatrix;
        }


        public static void CreateRotationX(float radians, out Matrix result)
        {
            result = Identity;

            result.M22 = (float) Math.Cos(radians);
            result.M23 = (float) Math.Sin(radians);
            result.M32 = -result.M23;
            result.M33 = result.M22;
        }


        public static Matrix CreateRotationY(float radians)
        {
            Matrix returnMatrix = Identity;

            returnMatrix.M11 = (float) Math.Cos(radians);
            returnMatrix.M13 = (float) Math.Sin(radians);
            returnMatrix.M31 = -returnMatrix.M13;
            returnMatrix.M33 = returnMatrix.M11;

            return returnMatrix;
        }


        public static void CreateRotationY(float radians, out Matrix result)
        {
            result = Identity;

            result.M11 = (float) Math.Cos(radians);
            result.M13 = (float) Math.Sin(radians);
            result.M31 = -result.M13;
            result.M33 = result.M11;
        }


        public static Matrix CreateRotationZ(float radians)
        {
            Matrix returnMatrix = Identity;

            returnMatrix.M11 = (float) Math.Cos(radians);
            returnMatrix.M12 = (float) Math.Sin(radians);
            returnMatrix.M21 = -returnMatrix.M12;
            returnMatrix.M22 = returnMatrix.M11;

            return returnMatrix;
        }


        public static void CreateRotationZ(float radians, out Matrix result)
        {
            result = Identity;

            result.M11 = (float) Math.Cos(radians);
            result.M12 = (float) Math.Sin(radians);
            result.M21 = -result.M12;
            result.M22 = result.M11;
        }


        public static Matrix CreateScale(float scale)
        {
            Matrix returnMatrix = Identity;

            returnMatrix.M11 = scale;
            returnMatrix.M22 = scale;
            returnMatrix.M33 = scale;

            return returnMatrix;
        }


        public static void CreateScale(float scale, out Matrix result)
        {
            result = Identity;

            result.M11 = scale;
            result.M22 = scale;
            result.M33 = scale;
        }


        public static Matrix CreateScale(float xScale, float yScale, float zScale)
        {
            Matrix returnMatrix = Identity;

            returnMatrix.M11 = xScale;
            returnMatrix.M22 = yScale;
            returnMatrix.M33 = zScale;

            return returnMatrix;
        }


        public static void CreateScale(float xScale, float yScale, float zScale, out Matrix result)
        {
            result = Identity;

            result.M11 = xScale;
            result.M22 = yScale;
            result.M33 = zScale;
        }


        public static Matrix CreateScale(Vector3 scales)
        {
            Matrix returnMatrix = Identity;

            returnMatrix.M11 = scales.X;
            returnMatrix.M22 = scales.Y;
            returnMatrix.M33 = scales.Z;

            return returnMatrix;
        }


        public static void CreateScale(ref Vector3 scales, out Matrix result)
        {
            result = Identity;

            result.M11 = scales.X;
            result.M22 = scales.Y;
            result.M33 = scales.Z;
        }


        public static Matrix CreateTranslation(float xPosition, float yPosition, float zPosition)
        {
            Matrix returnMatrix = Identity;

            returnMatrix.M41 = xPosition;
            returnMatrix.M42 = yPosition;
            returnMatrix.M43 = zPosition;

            return returnMatrix;
        }


        public static void CreateTranslation(float xPosition, float yPosition, float zPosition, out Matrix result)
        {
            result = Identity;

            result.M41 = xPosition;
            result.M42 = yPosition;
            result.M43 = zPosition;
        }


        public static Matrix CreateTranslation(Vector3 position)
        {
            Matrix returnMatrix = Identity;

            returnMatrix.M41 = position.X;
            returnMatrix.M42 = position.Y;
            returnMatrix.M43 = position.Z;

            return returnMatrix;
        }


        public static void CreateTranslation(ref Vector3 position, out Matrix result)
        {
            result = Identity;

            result.M41 = position.X;
            result.M42 = position.Y;
            result.M43 = position.Z;
        }

        public static Matrix Divide(Matrix matrix1, Matrix matrix2)
        {
            Matrix ret;
            Divide(ref matrix1, ref matrix2, out ret);
            return ret;
        }


        public static void Divide(ref Matrix matrix1, ref Matrix matrix2, out Matrix result)
        {
            Matrix inverse = Invert(matrix2);
            Multiply(ref matrix1, ref inverse, out result);
        }


        public static Matrix Divide(Matrix matrix1, float divider)
        {
            Matrix ret;
            Divide(ref matrix1, divider, out ret);
            return ret;
        }


        public static void Divide(ref Matrix matrix1, float divider, out Matrix result)
        {
            float inverseDivider = 1f/divider;
            Multiply(ref matrix1, inverseDivider, out result);
        }

        public static Matrix Invert(Matrix matrix)
        {
            Invert(ref matrix, out matrix);
            return matrix;
        }


        public static void Invert(ref Matrix matrix, out Matrix result)
        {
            //
            // Use Laplace expansion theorem to calculate the inverse of a 4x4 matrix
            // 
            // 1. Calculate the 2x2 determinants needed and the 4x4 determinant based on the 2x2 determinants 
            // 2. Create the adjugate matrix, which satisfies: A * adj(A) = det(A) * I
            // 3. Divide adjugate matrix with the determinant to find the inverse

            float det1 = matrix.M11*matrix.M22 - matrix.M12*matrix.M21;
            float det2 = matrix.M11*matrix.M23 - matrix.M13*matrix.M21;
            float det3 = matrix.M11*matrix.M24 - matrix.M14*matrix.M21;
            float det4 = matrix.M12*matrix.M23 - matrix.M13*matrix.M22;
            float det5 = matrix.M12*matrix.M24 - matrix.M14*matrix.M22;
            float det6 = matrix.M13*matrix.M24 - matrix.M14*matrix.M23;
            float det7 = matrix.M31*matrix.M42 - matrix.M32*matrix.M41;
            float det8 = matrix.M31*matrix.M43 - matrix.M33*matrix.M41;
            float det9 = matrix.M31*matrix.M44 - matrix.M34*matrix.M41;
            float det10 = matrix.M32*matrix.M43 - matrix.M33*matrix.M42;
            float det11 = matrix.M32*matrix.M44 - matrix.M34*matrix.M42;
            float det12 = matrix.M33*matrix.M44 - matrix.M34*matrix.M43;

            float detMatrix = (float) (det1*det12 - det2*det11 + det3*det10 + det4*det9 - det5*det8 + det6*det7);

            float invDetMatrix = 1f/detMatrix;

            Matrix ret; // Allow for matrix and result to point to the same structure

            ret.M11 = (matrix.M22*det12 - matrix.M23*det11 + matrix.M24*det10)*invDetMatrix;
            ret.M12 = (-matrix.M12*det12 + matrix.M13*det11 - matrix.M14*det10)*invDetMatrix;
            ret.M13 = (matrix.M42*det6 - matrix.M43*det5 + matrix.M44*det4)*invDetMatrix;
            ret.M14 = (-matrix.M32*det6 + matrix.M33*det5 - matrix.M34*det4)*invDetMatrix;
            ret.M21 = (-matrix.M21*det12 + matrix.M23*det9 - matrix.M24*det8)*invDetMatrix;
            ret.M22 = (matrix.M11*det12 - matrix.M13*det9 + matrix.M14*det8)*invDetMatrix;
            ret.M23 = (-matrix.M41*det6 + matrix.M43*det3 - matrix.M44*det2)*invDetMatrix;
            ret.M24 = (matrix.M31*det6 - matrix.M33*det3 + matrix.M34*det2)*invDetMatrix;
            ret.M31 = (matrix.M21*det11 - matrix.M22*det9 + matrix.M24*det7)*invDetMatrix;
            ret.M32 = (-matrix.M11*det11 + matrix.M12*det9 - matrix.M14*det7)*invDetMatrix;
            ret.M33 = (matrix.M41*det5 - matrix.M42*det3 + matrix.M44*det1)*invDetMatrix;
            ret.M34 = (-matrix.M31*det5 + matrix.M32*det3 - matrix.M34*det1)*invDetMatrix;
            ret.M41 = (-matrix.M21*det10 + matrix.M22*det8 - matrix.M23*det7)*invDetMatrix;
            ret.M42 = (matrix.M11*det10 - matrix.M12*det8 + matrix.M13*det7)*invDetMatrix;
            ret.M43 = (-matrix.M41*det4 + matrix.M42*det2 - matrix.M43*det1)*invDetMatrix;
            ret.M44 = (matrix.M31*det4 - matrix.M32*det2 + matrix.M33*det1)*invDetMatrix;

            result = ret;
        }


        public static Matrix Lerp(Matrix matrix1, Matrix matrix2, float amount)
        {
            throw new NotImplementedException();
        }


        public static void Lerp(ref Matrix matrix1, ref Matrix matrix2, float amount, out Matrix result)
        {
            throw new NotImplementedException();
        }

        public static Matrix Multiply(Matrix matrix1, Matrix matrix2)
        {
            Matrix ret;
            Multiply(ref matrix1, ref matrix2, out ret);
            return ret;
        }


        public static void Multiply(ref Matrix matrix1, ref Matrix matrix2, out Matrix result)
        {
            result.M11 = matrix1.M11*matrix2.M11 + matrix1.M12*matrix2.M21 + matrix1.M13*matrix2.M31 +
                         matrix1.M14*matrix2.M41;
            result.M12 = matrix1.M11*matrix2.M12 + matrix1.M12*matrix2.M22 + matrix1.M13*matrix2.M32 +
                         matrix1.M14*matrix2.M42;
            result.M13 = matrix1.M11*matrix2.M13 + matrix1.M12*matrix2.M23 + matrix1.M13*matrix2.M33 +
                         matrix1.M14*matrix2.M43;
            result.M14 = matrix1.M11*matrix2.M14 + matrix1.M12*matrix2.M24 + matrix1.M13*matrix2.M34 +
                         matrix1.M14*matrix2.M44;

            result.M21 = matrix1.M21*matrix2.M11 + matrix1.M22*matrix2.M21 + matrix1.M23*matrix2.M31 +
                         matrix1.M24*matrix2.M41;
            result.M22 = matrix1.M21*matrix2.M12 + matrix1.M22*matrix2.M22 + matrix1.M23*matrix2.M32 +
                         matrix1.M24*matrix2.M42;
            result.M23 = matrix1.M21*matrix2.M13 + matrix1.M22*matrix2.M23 + matrix1.M23*matrix2.M33 +
                         matrix1.M24*matrix2.M43;
            result.M24 = matrix1.M21*matrix2.M14 + matrix1.M22*matrix2.M24 + matrix1.M23*matrix2.M34 +
                         matrix1.M24*matrix2.M44;

            result.M31 = matrix1.M31*matrix2.M11 + matrix1.M32*matrix2.M21 + matrix1.M33*matrix2.M31 +
                         matrix1.M34*matrix2.M41;
            result.M32 = matrix1.M31*matrix2.M12 + matrix1.M32*matrix2.M22 + matrix1.M33*matrix2.M32 +
                         matrix1.M34*matrix2.M42;
            result.M33 = matrix1.M31*matrix2.M13 + matrix1.M32*matrix2.M23 + matrix1.M33*matrix2.M33 +
                         matrix1.M34*matrix2.M43;
            result.M34 = matrix1.M31*matrix2.M14 + matrix1.M32*matrix2.M24 + matrix1.M33*matrix2.M34 +
                         matrix1.M34*matrix2.M44;

            result.M41 = matrix1.M41*matrix2.M11 + matrix1.M42*matrix2.M21 + matrix1.M43*matrix2.M31 +
                         matrix1.M44*matrix2.M41;
            result.M42 = matrix1.M41*matrix2.M12 + matrix1.M42*matrix2.M22 + matrix1.M43*matrix2.M32 +
                         matrix1.M44*matrix2.M42;
            result.M43 = matrix1.M41*matrix2.M13 + matrix1.M42*matrix2.M23 + matrix1.M43*matrix2.M33 +
                         matrix1.M44*matrix2.M43;
            result.M44 = matrix1.M41*matrix2.M14 + matrix1.M42*matrix2.M24 + matrix1.M43*matrix2.M34 +
                         matrix1.M44*matrix2.M44;
        }


        public static Matrix Multiply(Matrix matrix1, float factor)
        {
            matrix1.M11 *= factor;
            matrix1.M12 *= factor;
            matrix1.M13 *= factor;
            matrix1.M14 *= factor;
            matrix1.M21 *= factor;
            matrix1.M22 *= factor;
            matrix1.M23 *= factor;
            matrix1.M24 *= factor;
            matrix1.M31 *= factor;
            matrix1.M32 *= factor;
            matrix1.M33 *= factor;
            matrix1.M34 *= factor;
            matrix1.M41 *= factor;
            matrix1.M42 *= factor;
            matrix1.M43 *= factor;
            matrix1.M44 *= factor;
            return matrix1;
        }


        public static void Multiply(ref Matrix matrix1, float factor, out Matrix result)
        {
            result.M11 = matrix1.M11*factor;
            result.M12 = matrix1.M12*factor;
            result.M13 = matrix1.M13*factor;
            result.M14 = matrix1.M14*factor;
            result.M21 = matrix1.M21*factor;
            result.M22 = matrix1.M22*factor;
            result.M23 = matrix1.M23*factor;
            result.M24 = matrix1.M24*factor;
            result.M31 = matrix1.M31*factor;
            result.M32 = matrix1.M32*factor;
            result.M33 = matrix1.M33*factor;
            result.M34 = matrix1.M34*factor;
            result.M41 = matrix1.M41*factor;
            result.M42 = matrix1.M42*factor;
            result.M43 = matrix1.M43*factor;
            result.M44 = matrix1.M44*factor;
        }


        public static Matrix Negate(Matrix matrix)
        {
            Multiply(ref matrix, -1.0f, out matrix);
            return matrix;
        }


        public static void Negate(ref Matrix matrix, out Matrix result)
        {
            Multiply(ref matrix, -1.0f, out result);
        }

        public static Matrix Subtract(Matrix matrix1, Matrix matrix2)
        {
            matrix1.M11 -= matrix2.M11;
            matrix1.M12 -= matrix2.M12;
            matrix1.M13 -= matrix2.M13;
            matrix1.M14 -= matrix2.M14;
            matrix1.M21 -= matrix2.M21;
            matrix1.M22 -= matrix2.M22;
            matrix1.M23 -= matrix2.M23;
            matrix1.M24 -= matrix2.M24;
            matrix1.M31 -= matrix2.M31;
            matrix1.M32 -= matrix2.M32;
            matrix1.M33 -= matrix2.M33;
            matrix1.M34 -= matrix2.M34;
            matrix1.M41 -= matrix2.M41;
            matrix1.M42 -= matrix2.M42;
            matrix1.M43 -= matrix2.M43;
            matrix1.M44 -= matrix2.M44;
            return matrix1;
        }

        public static void Subtract(ref Matrix matrix1, ref Matrix matrix2, out Matrix result)
        {
            result.M11 = matrix1.M11 - matrix2.M11;
            result.M12 = matrix1.M12 - matrix2.M12;
            result.M13 = matrix1.M13 - matrix2.M13;
            result.M14 = matrix1.M14 - matrix2.M14;
            result.M21 = matrix1.M21 - matrix2.M21;
            result.M22 = matrix1.M22 - matrix2.M22;
            result.M23 = matrix1.M23 - matrix2.M23;
            result.M24 = matrix1.M24 - matrix2.M24;
            result.M31 = matrix1.M31 - matrix2.M31;
            result.M32 = matrix1.M32 - matrix2.M32;
            result.M33 = matrix1.M33 - matrix2.M33;
            result.M34 = matrix1.M34 - matrix2.M34;
            result.M41 = matrix1.M41 - matrix2.M41;
            result.M42 = matrix1.M42 - matrix2.M42;
            result.M43 = matrix1.M43 - matrix2.M43;
            result.M44 = matrix1.M44 - matrix2.M44;
        }

        public static Matrix Transpose(Matrix matrix)
        {
            Matrix ret;
            Transpose(ref matrix, out ret);
            return ret;
        }


        public static void Transpose(ref Matrix matrix, out Matrix result)
        {
            result.M11 = matrix.M11;
            result.M12 = matrix.M21;
            result.M13 = matrix.M31;
            result.M14 = matrix.M41;

            result.M21 = matrix.M12;
            result.M22 = matrix.M22;
            result.M23 = matrix.M32;
            result.M24 = matrix.M42;

            result.M31 = matrix.M13;
            result.M32 = matrix.M23;
            result.M33 = matrix.M33;
            result.M34 = matrix.M43;

            result.M41 = matrix.M14;
            result.M42 = matrix.M24;
            result.M43 = matrix.M34;
            result.M44 = matrix.M44;
        }

#endregion Public Static Methods

#region Public Methods

        public float Determinant()
        {
            float minor1, minor2, minor3, minor4, minor5, minor6;

            minor1 = M31*M42 - M32*M41;
            minor2 = M31*M43 - M33*M41;
            minor3 = M31*M44 - M34*M41;
            minor4 = M32*M43 - M33*M42;
            minor5 = M32*M44 - M34*M42;
            minor6 = M33*M44 - M34*M43;

            return M11*(M22*minor6 - M23*minor5 + M24*minor4) -
                   M12*(M21*minor6 - M23*minor3 + M24*minor2) +
                   M13*(M21*minor5 - M22*minor3 + M24*minor1) -
                   M14*(M21*minor4 - M22*minor2 + M23*minor1);
        }

        public bool Equals(Matrix other)
        {
            return this == other;
        }

#endregion Public Methods

#region Operators

        public static Matrix operator +(Matrix matrix1, Matrix matrix2)
        {
            Add(ref matrix1, ref matrix2, out matrix1);
            return matrix1;
        }

        public static Matrix operator /(Matrix matrix1, Matrix matrix2)
        {
            Matrix ret;
            Divide(ref matrix1, ref matrix2, out ret);
            return ret;
        }

        public static Matrix operator /(Matrix matrix1, float divider)
        {
            Matrix ret;
            Divide(ref matrix1, divider, out ret);
            return ret;
        }

        public static bool operator ==(Matrix matrix1, Matrix matrix2)
        {
            return (matrix1.M11 == matrix2.M11) && (matrix1.M12 == matrix2.M12) &&
                   (matrix1.M13 == matrix2.M13) && (matrix1.M14 == matrix2.M14) &&
                   (matrix1.M21 == matrix2.M21) && (matrix1.M22 == matrix2.M22) &&
                   (matrix1.M23 == matrix2.M23) && (matrix1.M24 == matrix2.M24) &&
                   (matrix1.M31 == matrix2.M31) && (matrix1.M32 == matrix2.M32) &&
                   (matrix1.M33 == matrix2.M33) && (matrix1.M34 == matrix2.M34) &&
                   (matrix1.M41 == matrix2.M41) && (matrix1.M42 == matrix2.M42) &&
                   (matrix1.M43 == matrix2.M43) && (matrix1.M44 == matrix2.M44);
        }

        public static bool operator !=(Matrix matrix1, Matrix matrix2)
        {
            return !(matrix1 == matrix2);
        }

        public static Matrix operator *(Matrix matrix1, Matrix matrix2)
        {
            Matrix returnMatrix = new Matrix();
            Multiply(ref matrix1, ref matrix2, out returnMatrix);
            return returnMatrix;
        }

        public static Matrix operator *(Matrix matrix, float scaleFactor)
        {
            Multiply(ref matrix, scaleFactor, out matrix);
            return matrix;
        }

        public static Matrix operator *(float scaleFactor, Matrix matrix)
        {
            Matrix target;
            target.M11 = matrix.M11*scaleFactor;
            target.M12 = matrix.M12*scaleFactor;
            target.M13 = matrix.M13*scaleFactor;
            target.M14 = matrix.M14*scaleFactor;
            target.M21 = matrix.M21*scaleFactor;
            target.M22 = matrix.M22*scaleFactor;
            target.M23 = matrix.M23*scaleFactor;
            target.M24 = matrix.M24*scaleFactor;
            target.M31 = matrix.M31*scaleFactor;
            target.M32 = matrix.M32*scaleFactor;
            target.M33 = matrix.M33*scaleFactor;
            target.M34 = matrix.M34*scaleFactor;
            target.M41 = matrix.M41*scaleFactor;
            target.M42 = matrix.M42*scaleFactor;
            target.M43 = matrix.M43*scaleFactor;
            target.M44 = matrix.M44*scaleFactor;
            return target;
        }

        public static Matrix operator -(Matrix matrix1, Matrix matrix2)
        {
            Matrix returnMatrix = new Matrix();
            Subtract(ref matrix1, ref matrix2, out returnMatrix);
            return returnMatrix;
        }


        public static Matrix operator -(Matrix matrix1)
        {
            Negate(ref matrix1, out matrix1);
            return matrix1;
        }

#endregion

#region Object Overrides

        public override bool Equals(object obj)
        {
            return this == (Matrix) obj;
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return "{ {M11:" + M11 + " M12:" + M12 + " M13:" + M13 + " M14:" + M14 + "}" +
                   " {M21:" + M21 + " M22:" + M22 + " M23:" + M23 + " M24:" + M24 + "}" +
                   " {M31:" + M31 + " M32:" + M32 + " M33:" + M33 + " M34:" + M34 + "}" +
                   " {M41:" + M41 + " M42:" + M42 + " M43:" + M43 + " M44:" + M44 + "} }";
        }

#endregion
    }
}

#endif