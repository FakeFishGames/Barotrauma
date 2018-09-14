
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace Barotrauma
{
    public class Sound
    {
        public static Vector3 CameraPos;

        private static List<Sound> loadedSounds = new List<Sound>();

        private static OggStream stream;

        private OggSound oggSound;

        private readonly string filePath;
        private readonly bool destroyOnGameEnd;

        private float baseVolume;
        private float range;

        private int alSourceId;
        
        public bool IsPlaying
        {
            get
            {
                return SoundManager.IsPlaying(alSourceId);
            }
        }

        private Sound(string file, bool destroyOnGameEnd)
        {
            filePath = file;

            foreach (Sound loadedSound in loadedSounds)
            {
                if (loadedSound.filePath == file) oggSound = loadedSound.oggSound;
            }

            if (oggSound == null && !SoundManager.Disabled)
            {
                try
                {
                    DebugConsole.Log("Loading sound " + file);
                    oggSound = OggSound.Load(file);                    
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to load sound "+file+"!", e);
                }
                ALHelper.Check(file);
            }

            baseVolume = 1.0f;
            range = 1000.0f;

            this.destroyOnGameEnd = destroyOnGameEnd;
                
            loadedSounds.Add(this);
        }
          
        public string FilePath
        {
            get { return filePath; }
        }

        public int AlBufferId
        {
             get { return oggSound==null ? -1 : oggSound.AlBufferId; }
        }

        public static void Init()
        {
            SoundManager.Init();
        }
        
        public static Sound Load(string file, bool destroyOnGameEnd = true)
        {
            if (!File.Exists(file))
            {
                DebugConsole.ThrowError("File \"" + file + "\" not found!");
                return null;
            }
            
            return new Sound(file, destroyOnGameEnd);
        }

        public static Sound Load(XElement element, bool destroyOnGameEnd = true)
        {
            string filePath = element.GetAttributeString("file", "");           

            var newSound = new Sound(filePath, destroyOnGameEnd);
            if (newSound != null)
            {
                newSound.baseVolume = element.GetAttributeFloat("volume", 1.0f);
                newSound.range = element.GetAttributeFloat("range", 1000.0f);
            }

            return newSound;
        }

        public int Play(float volume = 1.0f)
        {
            if (volume <= 0.0f) return -1;

            alSourceId = SoundManager.Play(this, volume);
            return alSourceId;
        }

        public int Play(Vector2 position)
        {
            return Play(baseVolume, range, position);
        }

        public int Play(float baseVolume, float range, Vector2 position)
        {          
            Vector2 relativePos = GetRelativePosition(position);
            float volume = GetVolume(relativePos, range, baseVolume);

            if (volume <= 0.0f) return -1;

            alSourceId = SoundManager.Play(this, relativePos, volume);

            return alSourceId;
        }

        public void UpdatePosition(Vector2 position)
        {
            int sourceIndex = -1;
            if (SoundManager.IsPlaying(this, out sourceIndex))
            {
                Vector2 relativePos = GetRelativePosition(position);
                float volume = GetVolume(relativePos, range, baseVolume);

                if (volume <= 0.0f)
                {
                    SoundManager.Stop(this);
                    return;
                }
                
                SoundManager.UpdateSoundPosition(sourceIndex, relativePos, volume);
            }
        }
        
        private float GetVolume(Vector2 relativePosition, float range, float baseVolume)
        {
            float volume = (range == 0.0f) ? 0.0f : MathHelper.Clamp(baseVolume * (range - (relativePosition.Length() * 100.0f)) / range, 0.0f, 1.0f);

            return volume;
        }

        private Vector2 GetRelativePosition(Vector2 position)
        {
            return new Vector2(position.X - CameraPos.X, position.Y - CameraPos.Y) / 100.0f;
        }

        public int Loop(int sourceIndex, float volume)
        {
            if (volume <= 0.0f)
            {
                if (sourceIndex > 0)
                {
                    SoundManager.Stop(sourceIndex);
                    sourceIndex = -1;
                }

                return sourceIndex;
            }

            int newIndex = SoundManager.Loop(this, sourceIndex, volume);

            return newIndex;
        }

        public int Loop(int sourceIndex, float baseVolume, Vector2 position, float range)
        {
            Vector2 relativePos = GetRelativePosition(position);
            float volume = GetVolume(relativePos, range, baseVolume);

            if (volume <= 0.0f)
            {
                if (sourceIndex > 0)
                {
                    SoundManager.Stop(sourceIndex);
                    sourceIndex = -1;
                }

                return sourceIndex;
            }

            alSourceId = SoundManager.Loop(this, sourceIndex, relativePos, volume);
            return alSourceId;
        }

        public static void OnGameEnd()
        {
            List<Sound> removableSounds = loadedSounds.FindAll(s => s.destroyOnGameEnd);

            foreach (Sound sound in removableSounds)
            {
                sound.Remove();
            }
        }
                        
        public void Remove()
        {
            //sound already removed?
            if (!loadedSounds.Contains(this)) return;

            loadedSounds.Remove(this);

            if (alSourceId > 0 &&
                (SoundManager.IsPlaying(alSourceId) || SoundManager.IsPaused(alSourceId)))
            {
                SoundManager.Stop(alSourceId);
                ALHelper.Check(filePath);
            }

            foreach (Sound s in loadedSounds)
            {
                if (s.oggSound == oggSound) return;
            }

            SoundManager.ClearAlSource(AlBufferId);
            ALHelper.Check(filePath);

            if (oggSound != null)
            {
                oggSound.Dispose();
                oggSound = null;
            }
        }


        public static void StartStream(string file, float volume = 1.0f)
        {
            if (SoundManager.Disabled) return;
            stream = SoundManager.StartStream(file, volume);
        }

        public static void StreamVolume(float volume = 1.0f)
        {
            if (SoundManager.Disabled || stream == null) return;
            stream.Volume = volume;
        }

        public static void StopStream()
        {
            if (stream != null) SoundManager.StopStream();
        }

        public static void Dispose()
        {
            SoundManager.Dispose();
        }

    }

}