
using System.Collections.Generic;
using System.IO;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Subsurface.Sounds;

namespace Subsurface
{
    public class Sound
    {
        public static Vector3 CameraPos;

        private static List<Sound> loadedSounds = new List<Sound>();

        private static OggStream stream;

        private OggSound oggSound;

        string filePath;

        private int alSourceId;

        private bool destroyOnGameEnd;
                
        
        //public float Volume
        //{
        //    set { SoundManager.Volume(sourceIndex, value); }
        //}

        private Sound(string file, bool destroyOnGameEnd)
        {
            filePath = file;

            foreach (Sound loadedSound in loadedSounds)
            {
                if (loadedSound.filePath == file) oggSound = loadedSound.oggSound;
            }

            if (oggSound == null)
            {
                oggSound = OggSound.Load(file);            
            }

            this.destroyOnGameEnd = destroyOnGameEnd;
                
            loadedSounds.Add(this);
        }

        public string FilePath
        {
            get { return filePath; }
        }

        public int AlBufferId
        {
             get { return oggSound.AlBufferId; }
        }

        public static void Init()
        {
            SoundManager.Init();
        }
        
        public static Sound Load(string file, bool destroyOnGameEnd = true)
        {
            if (!File.Exists(file))
            {
                DebugConsole.ThrowError("File ''" + file + "'' not found!");
                return null;
            }
            
            return new Sound(file, destroyOnGameEnd);
        }

        public int Play(float volume = 1.0f)
        {
            alSourceId = SoundManager.Play(this, volume);
            return alSourceId;
        }

        public int Play(float baseVolume, float range, Vector2 position)
        {          
            //position = new Vector2(position.X - CameraPos.X, position.Y - CameraPos.Y);
            
            //volume = (range == 0.0f) ? 0.0f : MathHelper.Clamp(volume * (range - position.Length())/range, 0.0f, 1.0f);

            Vector2 relativePos = GetRelativePosition(position);
            float volume = GetVolume(relativePos, range, baseVolume);

            alSourceId = SoundManager.Play(this, relativePos, volume, volume);

            return alSourceId;

            //if (newIndex == -1) return -1;

            //return UpdatePosition(newIndex, position, range, volume);
        }

        public int Play(float volume, float range, Body body)
        {
            //Vector2 bodyPosition = ConvertUnits.ToDisplayUnits(body.Position);
            //bodyPosition.Y = -bodyPosition.Y;


            alSourceId = Play(volume, range, ConvertUnits.ToDisplayUnits(body.Position));

            return alSourceId;
        }

        private float GetVolume(Vector2 relativePosition, float range, float baseVolume)
        {
            float volume = (range == 0.0f) ? 0.0f : MathHelper.Clamp(baseVolume * (range - relativePosition.Length()) / range, 0.0f, 1.0f);

            return volume;
        }

        private Vector2 GetRelativePosition(Vector2 position)
        {
            return new Vector2(position.X - CameraPos.X, position.Y - CameraPos.Y);
        }

        //public static int UpdatePosition(int sourceIndex, Vector2 position, float range, float baseVolume = 1.0f)
        //{
        //    position = new Vector2(position.X - CameraPos.X, position.Y - CameraPos.Y);
        //    float volume = (range == 0.0f) ? 0.0f : MathHelper.Clamp(baseVolume * (range - position.Length())/range, 0.0f, 1.0f);

        //    if (volume <= 0.0f)
        //    {
        //        if (sourceIndex > 0)
        //        {
        //            SoundManager.Stop(sourceIndex);
        //            sourceIndex = -1;
        //        }

        //        return sourceIndex;
        //    }

        //    SoundManager.UpdateSoundPosition(sourceIndex, position, volume, volume);

        //    return sourceIndex;
        //}

        //public int UpdatePosition(int sourceIndex, Body body, float range, float baseVolume = 1.0f)
        //{
        //    Vector2 bodyPosition = ConvertUnits.ToDisplayUnits(body.Position);
        //    bodyPosition.Y = -bodyPosition.Y;
        //    return UpdatePosition(sourceIndex, bodyPosition, range, baseVolume);
        //}

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

            return SoundManager.Loop(this, sourceIndex, position, volume, volume);

        }

        public bool IsPlaying
        {
            get
            {
                return SoundManager.IsPlaying(alSourceId);
            }
        }

        //public int Loop(float volume = 1.0f)
        //{
        //    return SoundManager.Loop(this, volume);
        //}

        //public void Pause()
        //{
        //    SoundManager.Pause(this);
        //}

        //public void Resume()
        //{
        //    SoundManager.Resume(this);
        //}

        //public void Stop()
        //{
        //    SoundManager.Stop(this);
        //}

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

            System.Diagnostics.Debug.WriteLine("Removing sound " + filePath + " (buffer id" + AlBufferId + ")");

            if (alSourceId>0 && SoundManager.IsPlaying(alSourceId))
            {
                SoundManager.Stop(alSourceId);
            }

            foreach (Sound s in loadedSounds)
            {
                if (s.oggSound == oggSound) return;
            }

            SoundManager.ClearAlSource(AlBufferId);
            oggSound.Dispose();
        }


        public static void StartStream(string file, float volume = 1.0f)
        {
            stream = SoundManager.StartStream(file, volume);
        }

        public static void StreamVolume(float volume = 1.0f)
        {
            stream.Volume = volume;
        }

        public static void StopStream()
        {
            if (stream!=null) SoundManager.StopStream();
        }

        public static void Dispose()
        {
            SoundManager.Dispose();
        }

    }

}