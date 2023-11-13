using System;
using Barotrauma.IO;
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
    partial class Video : IDisposable
    {
        private static Internal.EventCallback VideoFrameCallback;
        private static Internal.EventCallback VideoAudioCallback;

        private static Dictionary<IntPtr, Video> videos;

        public static void Init()
        {
            if (VideoFrameCallback == null)
            {
                VideoFrameCallback = VideoFrameUpdate;
            }

            if (VideoAudioCallback == null)
            {
                VideoAudioCallback = VideoAudioUpdate;
            }

            if (videos == null)
            {
                videos = new Dictionary<IntPtr, Video>();
            }
        }

        public static void Close()
        {
            if (videos != null)
            {
                List<Video> vids = videos.Values.ToList();
                foreach (Video v in vids)
                {
                    v.Dispose();
                }
            }
        }

        private IntPtr videoInternal;
        
        private Texture2D texture;
        private bool textureChanged;
        private Int32[] textureData;

        private object mutex;

        private VideoSound sound;
        
        public int Width { get; private set; }
        public int Height { get; private set; }

        public float AudioGain
        {
            get { return sound == null ? 0.0f : sound.BaseGain; }
            set { if (sound != null) { sound.BaseGain = value; } }
        }

        public bool LoadFailed { get; private set; }

        public static Video Load(GraphicsDevice graphicsDevice, SoundManager soundManager, string filename)
        {
            Video video = new Video(graphicsDevice, soundManager, filename);
            if (video.LoadFailed) { video = null; }
            return video;
        }

        private Video(GraphicsDevice graphicsDevice,SoundManager soundManager,string filename)
        {
            Init();

            videoInternal = Internal.loadVideo(filename);
            
            if (videoInternal == IntPtr.Zero) { LoadFailed = true; return; }

            mutex = new object();

            Width = Internal.getVideoWidth(videoInternal); Height = Internal.getVideoHeight(videoInternal);

            texture = new Texture2D(graphicsDevice, (int)Width, (int)Height);
            
            textureData = new Int32[Width * Height];
            for (int i = 0; i < Width * Height; i++)
            {
                textureData[i] = unchecked((int)0xff000000);
            }
            texture.SetData(textureData);

            videos.Add(videoInternal, this);

            IntPtr videoFrameCallbackPtr = Marshal.GetFunctionPointerForDelegate(VideoFrameCallback);
            Internal.setVideoFrameCallback(videoInternal, videoFrameCallbackPtr);
            IntPtr videoAudioCallbackPtr = Marshal.GetFunctionPointerForDelegate(VideoAudioCallback);
            Internal.setVideoAudioCallback(videoInternal, videoAudioCallbackPtr);

            sound = null;
            if (Internal.videoHasAudio(videoInternal)==1)
            {
                int sampleRate = Internal.getVideoAudioSampleRate(videoInternal);
                int channelCount = Internal.getVideoAudioChannelCount(videoInternal);
                sound = new VideoSound(soundManager, filename, sampleRate, channelCount, this);
            }

            textureChanged = false;

            Internal.playVideo(videoInternal);
        }

        public void Play()
        {
            if (LoadFailed) { return; }
            Internal.playVideo(videoInternal);
        }

        public void Dispose()
        {
            if (LoadFailed) { return; }

            Internal.deleteVideo(videoInternal);
            videos.Remove(videoInternal);
            
            sound?.Dispose();
            
            texture.Dispose();
        }

        public bool IsPlaying
        {
            get
            {
                if (LoadFailed) { return false; }
                return Internal.isVideoPlaying(videoInternal)==1;
            }
        }

        public Texture2D GetTexture()
        {
            if (LoadFailed) { return null; }
            lock (mutex)
            {
                if (textureChanged)
                {
                    texture.SetData(textureData);
                    textureChanged = false;
                }

                if (sound!=null && !sound.IsPlaying() && IsPlaying) { sound.Play(); }
            }

            return texture;
        }

        public void SetFrameData(IntPtr data)
        {
            lock (mutex)
            {
                Marshal.Copy(data, textureData, 0, (int)(Width * Height));
                textureChanged = true;
            }
        }

        static void VideoFrameUpdate(IntPtr videoInternal, IntPtr data, Int32 dataElemSize, Int32 dataLen)
        {
            Video video = videos[videoInternal];

            video.SetFrameData(data);
        }

        static void VideoAudioUpdate(IntPtr videoInternal, IntPtr data, Int32 dataElemSize, Int32 dataLen)
        {
            Video video = videos[videoInternal];

            if (video.sound != null && dataLen > 0)
            {
                //TODO: reduce garbage?
                short[] newBuf = new short[dataLen];
                Marshal.Copy(data, newBuf, 0, dataLen);
                video.sound.Enqueue(newBuf);
            }
        }
    }
}
