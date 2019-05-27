/***

MIT License

Copyright (c) 2018 Nathan Glover

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

****************

Further modified for use in Barotrauma.
Original source code at https://github.com/NathanielGlover/OpenAL.NETCore/

***/

using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;

namespace OpenAL
{
    public class Alc
    {
#if OSX
        public const string OpenAlDll = "/System/Library/Frameworks/OpenAL.framework/OpenAL";
#elif LINUX
        public const string OpenAlDll = "libopenal.so.1";
#elif WINDOWS
#if X86
        public const string OpenAlDll = "soft_oal_x86.dll";
#elif X64
        public const string OpenAlDll = "soft_oal_x64.dll";
#endif
#endif

        #region Enum

        public const int False = 0;
        public const int True = 1;
        public const int Frequency = 0x1007;
        public const int Refresh = 0x1008;
        public const int Sync = 0x1009;
        public const int MonoSources = 0x1010;
        public const int StereoSources = 0x1011;
        public const int NoError = False;
        public const int InvalidDevice = 0xA001;
        public const int InvalidContext = 0xA002;
        public const int InvalidEnum = 0xA003;
        public const int InvalidValue = 0xA004;
        public const int OutOfMemory = 0xA005;
        public const int DefaultDeviceSpecifier = 0x1004;
        public const int DeviceSpecifier = 0x1005;
        public const int Extensions = 0x1006;
        public const int MajorVersion = 0x1000;
        public const int MinorVersion = 0x1001;
        public const int AttributesSize = 0x1002;
        public const int AllAttributes = 0x1003;
        public const int DefaultAllDevicesSpecifier = 0x1012;
        public const int AllDevicesSpecifier = 0x1013;
        public const int CaptureDeviceSpecifier = 0x310;
        public const int CaptureDefaultDeviceSpecifier = 0x311;
        public const int EnumCaptureSamples = 0x312;

        #endregion

        #region Context Management Functions

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcCreateContext")]
        private static extern IntPtr _CreateContext(IntPtr device, IntPtr attrlist);

        public static IntPtr CreateContext(IntPtr device, int[] attrList)
        {
            GCHandle handle = GCHandle.Alloc(attrList, GCHandleType.Pinned);
            IntPtr retVal = _CreateContext(device, handle.AddrOfPinnedObject());
            handle.Free();
            return retVal;
        }

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcMakeContextCurrent")]
        public static extern bool MakeContextCurrent(IntPtr context);

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcProcessContext")]
        public static extern void ProcessContext(IntPtr context);

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcSuspendContext")]
        public static extern void SuspendContext(IntPtr context);

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcDestroyContext")]
        public static extern void DestroyContext(IntPtr context);

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcGetCurrentContext")]
        public static extern IntPtr GetCurrentContext();

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcGetContextsDevice")]
        public static extern IntPtr GetContextsDevice(IntPtr context);

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcOpenDevice")]
        public static extern IntPtr OpenDevice(string deviceName);

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcCloseDevice")]
        public static extern bool CloseDevice(IntPtr device);

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcGetError")]
        public static extern int GetError(IntPtr device);

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcIsExtensionPresent")]
        public static extern bool IsExtensionPresent(IntPtr device, string extname);

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcGetProcAddress")]
        public static extern IntPtr GetProcAddress(IntPtr device, string funcname);

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcGetEnumValue")]
        public static extern int GetEnumValue(IntPtr device, string enumname);

        #endregion

        #region Query Functions

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcGetString")]
        private static extern IntPtr _GetString(IntPtr device, int param);

        public static string GetString(IntPtr device, int param)
        {
            IntPtr strPtr = _GetString(device, param);
            int strLen = 0;
            while (Marshal.ReadByte(strPtr,strLen)!='\0') { strLen++; }
            byte[] bytes = new byte[strLen];
            Marshal.Copy(strPtr, bytes, 0, strLen);
            return Encoding.UTF8.GetString(bytes);
        }

        public static IList<string> GetStringList(IntPtr device, int param)
        {
            List<string> retVal = new List<string>();
            IntPtr strPtr = _GetString(device, param);
            int strStart = 0;
            int strEnd = 0;
            byte currChar = Marshal.ReadByte(strPtr, strEnd);
            if (currChar == '\0') { return retVal; }
            byte prevChar = 255;
            while (true) {
                strEnd++;
                prevChar = currChar;
                currChar = Marshal.ReadByte(strPtr, strEnd);

                if (currChar == '\0')
                {
                    if (prevChar == '\0')
                    {
                        break;
                    }
                    byte[] bytes = new byte[strEnd-strStart];
                    Marshal.Copy(strPtr+strStart, bytes, 0, strEnd - strStart);
                    retVal.Add(Encoding.UTF8.GetString(bytes));
                    strStart = strEnd+1;
                }
            }
            return retVal;
        }
        
        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcGetIntegerv")]
        public static extern void GetIntegerv(IntPtr device, int param, int size, IntPtr data);

        public static void GetInteger(IntPtr device, int param, out int data)
        {
            int[] dataArr = new int[1];
            GCHandle handle = GCHandle.Alloc(dataArr,GCHandleType.Pinned);
            GetIntegerv(device, param, 1, handle.AddrOfPinnedObject());
            handle.Free();
            data = dataArr[0];
        }

        #endregion

        #region Capture Functions

        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcCaptureOpenDevice")]
        private static extern IntPtr CaptureOpenDevice(IntPtr devicename, uint frequency, int format, int buffersize);

        public static IntPtr CaptureOpenDevice(string devicename, uint frequency, int format, int buffersize)
        {
            byte[] devicenameBytes = Encoding.UTF8.GetBytes(devicename);
            GCHandle devicenameHandle = GCHandle.Alloc(devicenameBytes, GCHandleType.Pinned);
            IntPtr retVal = CaptureOpenDevice(devicenameHandle.AddrOfPinnedObject(), frequency, format, buffersize);
            devicenameHandle.Free();
            return retVal;
        }
        
        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcCaptureCloseDevice")]
        public static extern bool CaptureCloseDevice(IntPtr device);
        
        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcCaptureStart")]
        public static extern void CaptureStart(IntPtr device);
        
        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcCaptureStop")]
        public static extern void CaptureStop(IntPtr device);
        
        [DllImport(OpenAlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "alcCaptureSamples")]
        public static extern void CaptureSamples(IntPtr device, IntPtr buffer, int samples);

        #endregion
    }
}
