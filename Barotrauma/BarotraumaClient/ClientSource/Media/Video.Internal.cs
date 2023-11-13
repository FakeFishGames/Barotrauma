using System;
using System.Runtime.InteropServices;

namespace Barotrauma.Media
{
    partial class Video : IDisposable
    {
        private static class Internal
        {
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            public delegate void EventCallback(IntPtr videoInternal, IntPtr data, Int32 dataElemSize, Int32 dataLen);

#if WINDOWS
            private const string DLL_NAME = "webm_mem_playback_x64.dll";
#elif LINUX
            private const string DLL_NAME = "webm_mem_playback_x64.so";
#elif OSX
            private const string DLL_NAME = "webm_mem_playback_x64.dylib";
#endif
            private const CallingConvention CALLING_CONVENTION = CallingConvention.Cdecl;

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern IntPtr loadVideo(string filename);

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern Int32 getVideoWidth(IntPtr videoInternal);

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern Int32 getVideoHeight(IntPtr videoInternal);

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern Int32 videoHasAudio(IntPtr videoInternal);

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern Int32 getVideoAudioSampleRate(IntPtr videoInternal);

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern Int32 getVideoAudioChannelCount(IntPtr videoInternal);

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern void deleteVideo(IntPtr videoInternal);

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern void playVideo(IntPtr videoInternal);

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern void stopVideo(IntPtr videoInternal);

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern Int32 isVideoPlaying(IntPtr videoInternal);

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern void setVideoFrameCallback(IntPtr videoInternal, IntPtr callback);

            [DllImport(DLL_NAME, CallingConvention = CALLING_CONVENTION, CharSet = CharSet.Ansi)]
            public static extern void setVideoAudioCallback(IntPtr videoInternal, IntPtr callback);
        }
    }
}
