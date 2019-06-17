using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Sounds;

namespace Barotrauma.Media
{
    public class Video : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr LibVLCVideoLockCb(IntPtr opaque, IntPtr planes);
        private static LibVLCVideoLockCb VideoLockDelegate;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LibVLCVideoUnlockCb(IntPtr opaque, IntPtr picture, IntPtr planes);
        private static LibVLCVideoUnlockCb VideoUnlockDelegate;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LibVLCVideoDisplayCb(IntPtr opaque, IntPtr picture);
        private static LibVLCVideoDisplayCb VideoDisplayDelegate;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LibVLCAudioPlayCb(IntPtr data, IntPtr samples, uint count, long pts);
        private static LibVLCAudioPlayCb AudioPlayDelegate;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LibVLCAudioPauseCb(IntPtr data, long pts);
        private static LibVLCAudioPauseCb AudioPauseDelegate;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LibVLCAudioResumeCb(IntPtr data, long pts);
        private static LibVLCAudioResumeCb AudioResumeDelegate;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LibVLCAudioFlushCb(IntPtr data, long pts);
        private static LibVLCAudioFlushCb AudioFlushDelegate;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LibVLCAudioDrainCb(IntPtr data);
        private static LibVLCAudioDrainCb AudioDrainDelegate;


        private static IntPtr lib = IntPtr.Zero;

        private static Dictionary<int, Video> videos;
        private static int latestVideoId;
        public static void Init()
        {
            if (lib == IntPtr.Zero)
            {
                string[] parameters = {
                    //"--no-xlib"
                };

                lib = LibVlcWrapper.LibVlcMethods.libvlc_new(parameters.Length, parameters);

                videos = new Dictionary<int, Video>();
                latestVideoId = 0;

                VideoLockDelegate = VideoLockCallback;
                VideoUnlockDelegate = VideoUnlockCallback;
                VideoDisplayDelegate = VideoDisplayCallback;

                AudioPlayDelegate = AudioPlayCallback;
                AudioPauseDelegate = AudioPauseCallback;
                AudioResumeDelegate = AudioResumeCallback;
                AudioFlushDelegate = AudioFlushCallback;
                AudioDrainDelegate = AudioDrainCallback;
            }
        }

        public static void Close()
        {
            if (lib != IntPtr.Zero)
            {
                List<int> keys = videos.Keys.ToList();
                foreach (int key in keys)
                {
                    videos[key].Dispose();
                }

                LibVlcWrapper.LibVlcMethods.libvlc_free(lib);

                lib = IntPtr.Zero;
            }
        }

        private int videoId;
        private IntPtr unmanagedData;

        private IntPtr media;
        private IntPtr mediaPlayer;
        private Texture2D texture;
        private byte[] textureData;
        private object mutex;
        private VideoSound sound;

        /*private IntPtr videoLockDelegate;
        private IntPtr videoUnlockDelegate;
        private IntPtr videoDisplayDelegate;*/

        public uint Width { get; private set; }
        public uint Height { get; private set; }

        public Video(GraphicsDevice graphicsDevice,SoundManager soundManager,string filename,uint width,uint height)
        {
            Init();

            mutex = new object();
            sound = new VideoSound(soundManager, filename, 44100, this);
            videos.Add(latestVideoId, this);
            videoId = latestVideoId; latestVideoId++;

            /*using (var stream = File.OpenRead(filename))
            {*/
            byte[] filenameBytes = Encoding.UTF8.GetBytes(filename + "\0");
            byte[] filenameBytesNullTerminated = new byte[filenameBytes.Length + 1];
            filenameBytesNullTerminated[filenameBytes.Length] = 0;
            Array.Copy(filenameBytes, filenameBytesNullTerminated, filenameBytes.Length);
            media = LibVlcWrapper.LibVlcMethods.libvlc_media_new_path(lib, filenameBytesNullTerminated);
            LibVlcWrapper.LibVlcMethods.libvlc_media_parse(media);

            mediaPlayer = LibVlcWrapper.LibVlcMethods.libvlc_media_player_new(lib);
            LibVlcWrapper.LibVlcMethods.libvlc_media_player_set_media(mediaPlayer, media);
            //LibVlcWrapper.LibVlcMethods.libvlc_media_release(media);

            //LibVlcWrapper.LibVlcMethods.libvlc_video_get_size(mediaPlayer, 0, out width, out height);
            texture = new Texture2D(graphicsDevice, (int)width, (int)height);
            Width = width; Height = height;

            unmanagedData = Marshal.AllocHGlobal(sizeof(int)*2+(int)(width*height*3));
            int[] arr = { videoId, 1 };
            Marshal.Copy(arr, 0, unmanagedData, 2);
            textureData = new byte[width * height * 3];
            for (int i = 0; i < width*height*3; i++)
            {
                textureData[i] = 0;
            }

            IntPtr videoLockDelegatePtr = Marshal.GetFunctionPointerForDelegate(VideoLockDelegate);
            IntPtr videoUnlockDelegatePtr = Marshal.GetFunctionPointerForDelegate(VideoUnlockDelegate);
            IntPtr videoDisplayDelegatePtr = Marshal.GetFunctionPointerForDelegate(VideoDisplayDelegate);
            LibVlcWrapper.LibVlcMethods.libvlc_video_set_callbacks(mediaPlayer, videoLockDelegatePtr, videoUnlockDelegatePtr, videoDisplayDelegatePtr, unmanagedData);
            LibVlcWrapper.LibVlcMethods.libvlc_video_set_format(mediaPlayer, Encoding.UTF8.GetBytes("RV24\0"), (int)width, (int)height, (int)width * 3);

            IntPtr audioPlayDelegatePtr = Marshal.GetFunctionPointerForDelegate(AudioPlayDelegate);
            IntPtr audioPauseDelegatePtr = Marshal.GetFunctionPointerForDelegate(AudioPauseDelegate);
            IntPtr audioResumeDelegatePtr = Marshal.GetFunctionPointerForDelegate(AudioResumeDelegate);
            IntPtr audioFlushDelegatePtr = Marshal.GetFunctionPointerForDelegate(AudioFlushDelegate);
            IntPtr audioDrainDelegatePtr = Marshal.GetFunctionPointerForDelegate(AudioDrainDelegate);

            LibVlcWrapper.LibVlcMethods.libvlc_audio_set_callbacks(mediaPlayer, audioPlayDelegatePtr, audioPauseDelegatePtr, audioResumeDelegatePtr, audioFlushDelegatePtr, audioDrainDelegatePtr, unmanagedData);
            LibVlcWrapper.LibVlcMethods.libvlc_audio_set_format(mediaPlayer, Encoding.UTF8.GetBytes("S16N\0"), 44100, 2);

            LibVlcWrapper.LibVlcMethods.libvlc_audio_set_delay(mediaPlayer, 0);

            LibVlcWrapper.LibVlcMethods.libvlc_media_player_play(mediaPlayer);

            //}
        }

        public void Dispose()
        {
            LibVlcWrapper.LibVlcMethods.libvlc_media_player_stop(mediaPlayer);

            Monitor.Enter(mutex);
            //just waiting for callbacks to be done
            Monitor.Exit(mutex);

            Marshal.FreeHGlobal(unmanagedData);

            sound.Dispose();

            LibVlcWrapper.LibVlcMethods.libvlc_media_release(media);
            LibVlcWrapper.LibVlcMethods.libvlc_media_player_release(mediaPlayer);
            
            texture.Dispose();

            videos.Remove(videoId);
        }

        public bool IsPlaying
        {
            get
            {
                return LibVlcWrapper.LibVlcMethods.libvlc_media_player_is_playing(mediaPlayer)!=0;
            }
        }

        public Texture2D GetTexture()
        {
            Monitor.Enter(mutex);

            int[] arr = { -1 };
            IntPtr changedPtr = (IntPtr)(unmanagedData.ToInt64() + sizeof(int));
            Marshal.Copy(changedPtr, arr, 0, 1);

            if (arr[0]!=0)
            {
                IntPtr colorLocation = (IntPtr)(unmanagedData.ToInt64() + sizeof(int) * 2);
                Marshal.Copy(colorLocation, textureData, 0, (int)(Width * Height * 3));
                Color[] colors = new Color[Width * Height];
                for (int y = 0; y < Height;y++)
                {
                    for (int x = 0; x < Width;x++)
                    {
                        colors[x + y * Width] = new Color(textureData[3 * (x + y * Width) + 0],
                                                          textureData[3 * (x + y * Width) + 1],
                                                          textureData[3 * (x + y * Width) + 2],
                                                          (byte)255);
                    }
                }
                texture.SetData(colors);
                arr[0] = 0;
                Marshal.Copy(arr, 0, changedPtr, 1);
            }

            if (!sound.IsPlaying()) videos[videoId].sound.Play();

            Monitor.Exit(mutex);

            return texture;
        }

        static IntPtr VideoLockCallback(IntPtr opaque, IntPtr planes)
        {
            int[] arr = { -1 };
            Marshal.Copy(opaque, arr, 0, 1);
            int mutexIndex = arr[0];

            Monitor.Enter(videos[mutexIndex].mutex);

            IntPtr unmanagedData = (IntPtr)(opaque.ToInt64() + sizeof(int) * 2);
            IntPtr[] ptrPtr = { unmanagedData };
            Marshal.Copy(ptrPtr, 0, planes, 1);

            return IntPtr.Zero;
        }

        static void VideoUnlockCallback(IntPtr opaque, IntPtr picture, IntPtr planes)
        {
            int[] arr = { -1 };
            Marshal.Copy(opaque, arr, 0, 1);
            int mutexIndex = arr[0];
            arr[0] = 1;
            IntPtr changedPtr = (IntPtr)(opaque.ToInt64() + sizeof(int));
            Marshal.Copy(arr, 0, changedPtr, 1);

            Monitor.Exit(videos[mutexIndex].mutex);
        }

        static void VideoDisplayCallback(IntPtr opaque,IntPtr picture)
        {
            //Assert(picture == IntPtr.Zero);
        }

        static void AudioPlayCallback(IntPtr data, IntPtr samples, uint count, long pts)
        {
            short[] buf = new short[count*2];
            Marshal.Copy(samples, buf, 0, (int)count*2);
            int[] arr = { -1 };
            Marshal.Copy(data, arr, 0, 1);
            int mutexIndex = arr[0];

            Monitor.Enter(videos[mutexIndex].mutex);

            videos[mutexIndex].sound.Enqueue(buf);

            Monitor.Exit(videos[mutexIndex].mutex);
        }

        static void AudioPauseCallback(IntPtr data, long pts)
        {

        }

        static void AudioResumeCallback(IntPtr data, long pts)
        {

        }

        static void AudioFlushCallback(IntPtr data, long pts)
        {

        }

        static void AudioDrainCallback(IntPtr data)
        {

        }
    }
}
