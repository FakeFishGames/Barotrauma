using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using OpenTK.Audio.OpenAL;
using OpenTK.Audio;

using System;

namespace Subsurface.Sounds
{
    static class SoundManager
    {
        public const int DefaultSourceCount = 16;

        private static List<int> alSources = new List<int>();
        private static int[] alBuffers = new int[DefaultSourceCount];
        private static int lowpassFilterId;

        private static float overrideLowPassGain;

        public static float OverrideLowPassGain
        {
            get { return overrideLowPassGain; }
            set { overrideLowPassGain = MathHelper.Clamp(overrideLowPassGain, 0.0f, 1.0f); }
        }

        static AudioContext AC;

        public static OggStreamer oggStreamer;
        public static OggStream oggStream;

        public static void Init()
        {
            AC = new AudioContext();

            for (int i = 0 ; i < DefaultSourceCount; i++)
            {
                alSources.Add(OpenTK.Audio.OpenAL.AL.GenSource());
            }

            if (ALHelper.Efx.IsInitialized)
            {
                lowpassFilterId = ALHelper.Efx.GenFilter();
                //alFilters.Add(alFilterId);
                ALHelper.Efx.Filter(lowpassFilterId, OpenTK.Audio.OpenAL.EfxFilteri.FilterType, (int)OpenTK.Audio.OpenAL.EfxFilterType.Lowpass);
                                
                //LowPassHFGain = 1;
            }


        }

                
        //public SoundManager(int bufferCount = DefaultSourceCount)
        //{
        //    Stopwatch sw = new Stopwatch();
        //    sw.Start();

        //    alSourceId = AL.GenSource();
        //    //AL.Source(alSourceId, ALSourcei.Buffer, alBufferId);
            
        //    Volume = 1;

        //    if (ALHelper.Efx.IsInitialized)
        //    {
        //        alFilterId = ALHelper.Efx.GenFilter();
        //        ALHelper.Efx.Filter(alFilterId, EfxFilteri.FilterType, (int)EfxFilterType.Lowpass);
        //        ALHelper.Efx.Filter(alFilterId, EfxFilterf.LowpassGain, 1);
        //        LowPassHFGain = 1;
        //    }

        //    sw.Stop();
        //    System.Diagnostics.Debug.WriteLine("oggsource: "+sw.ElapsedMilliseconds);

        //}
        
       // public static int Play(Sound sound, float volume = 1.0f)
       // {
       //     return Play(sound, volume, new Vector2(0.0f, 0.0f));
       //}

        public static int Play(Sound sound, float volume = 1.0f)
        {
            return Play(sound, Vector2.Zero, volume, 0.0f);
            //for (int i = 2; i < DefaultSourceCount; i++)
            //{
            //    AL.SourceStop(alSources[i]);
            //    AL.Source(alSources[i], ALSourceb.Looping, false);
            //    System.Diagnostics.Debug.WriteLine(i + ": " + AL.GetSourceState(alSources[i]));
            //    System.Diagnostics.Debug.WriteLine(AL.GetSourceType(alSources[i]));
            //}

            //for (int i = 1; i < DefaultSourceCount; i++)
            //{
            //    //find a source that's free to use (not playing or paused)
            //    if (AL.GetSourceState(alSources[i]) == ALSourceState.Playing
            //        || AL.GetSourceState(alSources[i]) == ALSourceState.Paused) continue;

            //    //if (position!=Vector2.Zero)
            //    //    position /= 1000.0f;

            //    alBuffers[i]=sound.AlBufferId;
            //    AL.Source(alSources[i], ALSourceb.Looping, false);
            //    AL.Source(alSources[i], ALSource3f.Position, 0.0f, 0.0f, 0.0f);
            //    AL.Source(alSources[i], ALSourcei.Buffer, sound.AlBufferId);
            //    AL.Source(alSources[i], ALSourcef.Gain, volume);
            //    //AL.Source(alSources[i], ALSource3f.Position, position.X, position.Y, 0.0f);
            //    AL.SourcePlay(alSources[i]);

            //    //sound.sourceIndex = i;

            //    return i;
            //}

            //return -1;
        }

        public static int Play(Sound sound, Vector2 position, float volume = 1.0f, float lowPassGain = 0.0f)
        {
            //for (int i = 2; i < DefaultSourceCount; i++)
            //{
            //    AL.SourceStop(alSources[i]);
            //    AL.Source(alSources[i], ALSourceb.Looping, false);
            //    System.Diagnostics.Debug.WriteLine(i + ": " + AL.GetSourceState(alSources[i]));
            //    System.Diagnostics.Debug.WriteLine(AL.GetSourceType(alSources[i]));
            //}



            for (int i = 1; i < DefaultSourceCount; i++)
            {
                //find a source that's free to use (not playing or paused)
                if (OpenTK.Audio.OpenAL.AL.GetSourceState(alSources[i]) == OpenTK.Audio.OpenAL.ALSourceState.Playing
                    || OpenTK.Audio.OpenAL.AL.GetSourceState(alSources[i]) == OpenTK.Audio.OpenAL.ALSourceState.Paused) continue;

                //if (position!=Vector2.Zero)
                //    position /= 1000.0f;

                alBuffers[i] = sound.AlBufferId;
                OpenTK.Audio.OpenAL.AL.Source(alSources[i], OpenTK.Audio.OpenAL.ALSourceb.Looping, false);

                position /= 1000.0f;

                //System.Diagnostics.Debug.WriteLine("updatesoundpos: "+offset);
                OpenTK.Audio.OpenAL.AL.Source(alSources[i], OpenTK.Audio.OpenAL.ALSourcef.Gain, volume);
                OpenTK.Audio.OpenAL.AL.Source(alSources[i], OpenTK.Audio.OpenAL.ALSource3f.Position, position.X, position.Y, 0.0f);

                OpenTK.Audio.OpenAL.AL.Source(alSources[i], OpenTK.Audio.OpenAL.ALSourcei.Buffer, sound.AlBufferId);

                ALHelper.Efx.Filter(lowpassFilterId, OpenTK.Audio.OpenAL.EfxFilterf.LowpassGainHF, lowPassHfGain = Math.Min(lowPassGain, overrideLowPassGain));
                ALHelper.Efx.BindFilterToSource(alSources[i], lowpassFilterId);
                ALHelper.Check();

                //AL.Source(alSources[i], ALSource3f.Position, position.X, position.Y, 0.0f);
                OpenTK.Audio.OpenAL.AL.SourcePlay(alSources[i]);

                //sound.sourceIndex = i;

                return i;
            }

            return -1;
        }

        public static int Loop(Sound sound, int sourceIndex, float volume = 1.0f)
        {
            return Loop(sound,sourceIndex, Vector2.Zero, volume, 0.0f);
        }
        public static int Loop(Sound sound, int sourceIndex, Vector2 position, float volume = 1.0f, float lowPassGain = 0.0f)
        {
            if (sourceIndex<1)
            {
                sourceIndex = Play(sound, position, volume, lowPassGain);
                if (sourceIndex>0)
                {
                    AL.Source(alSources[sourceIndex], ALSourceb.Looping, true);
                    AL.Source(alSources[sourceIndex], ALSourcef.Gain, volume);
                }
                ALHelper.Check();
                return sourceIndex;
            }
            else
            {
                AL.Source(alSources[sourceIndex], ALSourceb.Looping, true);
                AL.Source(alSources[sourceIndex], ALSourcef.Gain, volume);
                ALHelper.Check();
                return sourceIndex;
            }
        }

        //public static int Loop(int sourceIndex, float volume = 1.0f)
        //{

        //    if (sourceIndex > 0 && alSources[sourceIndex]>0)
        //    {
        //        ALSourceState state = AL.GetSourceState(alSources[sourceIndex]);
        //        ALHelper.Check();
        //        if (state == ALSourceState.Playing) return sourceIndex;
        //    }

        //    int newSourceIndex = Play(sound, volume);
        //    AL.Source(alSources[sourceIndex], ALSourceb.Looping, true);
            
        //    return sourceIndex;
        //}

        public static void Pause(int sourceIndex)
        {
            if (AL.GetSourceState(alSources[sourceIndex]) != ALSourceState.Playing)
                return;

            AL.SourcePause(alSources[sourceIndex]);
            ALHelper.Check();
        }

        public static void Resume(int sourceIndex)
        {
            if (AL.GetSourceState(alSources[sourceIndex]) != ALSourceState.Paused)
                return;

            AL.SourcePlay(alSources[sourceIndex]);
            ALHelper.Check();
        }
        
        public static void Stop(int sourceIndex)
        {
            if (sourceIndex < 1) return;

            var state = AL.GetSourceState(alSources[sourceIndex]);
            if (state == ALSourceState.Playing || state == ALSourceState.Paused)
            {
                AL.SourceStop(alSources[sourceIndex]);
                AL.Source(alSources[sourceIndex], ALSourceb.Looping, false);
            }
        }

        public static bool IsPlaying(int sourceIndex)
        {
            if (sourceIndex < 1 || sourceIndex>alSources.Count-1) return false;
            var state = AL.GetSourceState(alSources[sourceIndex]);
            return (state == ALSourceState.Playing);
        }

        public static void Volume(int sourceIndex, float volume)
        {
            AL.Source(alSources[sourceIndex], ALSourcef.Gain, volume);
            ALHelper.Check();
        }

        //int alFilterId;

        static float lowPassHfGain;
        public static float LowPassHFGain
        {
            get { return lowPassHfGain; }
            set
            {
                if (ALHelper.Efx.IsInitialized)
                {
                    overrideLowPassGain = value;
                    for (int i = 0; i < DefaultSourceCount; i++)
                    {
                        //find a source that's free to use (not playing or paused)
                        if (OpenTK.Audio.OpenAL.AL.GetSourceState(alSources[i]) != OpenTK.Audio.OpenAL.ALSourceState.Playing
                            && OpenTK.Audio.OpenAL.AL.GetSourceState(alSources[i])!= OpenTK.Audio.OpenAL.ALSourceState.Paused) continue;

                        ALHelper.Efx.Filter(lowpassFilterId, OpenTK.Audio.OpenAL.EfxFilterf.LowpassGainHF, lowPassHfGain = value);                        
                        ALHelper.Efx.BindFilterToSource(alSources[i], lowpassFilterId);
                        ALHelper.Check();
                    }

                }
            }
        }

        //float volume;
        //public float Volume
        //{
        //    get { return volume; }
        //    set
        //    {
        //        AL.Source(alSourceId, ALSourcef.Gain, volume = value);
        //        ALHelper.Check();
        //    }
        //}

        public static void UpdateSoundPosition(int sourceIndex, Vector2 position, float baseVolume = 1.0f, float lowPassGain = 0.0f)
        {
            if (sourceIndex < 1) return;

            //Resume(sourceIndex);

            position/= 1000.0f;
            
            //System.Diagnostics.Debug.WriteLine("updatesoundpos: "+offset);
            OpenTK.Audio.OpenAL.AL.Source(alSources[sourceIndex], OpenTK.Audio.OpenAL.ALSourcef.Gain, baseVolume);
            OpenTK.Audio.OpenAL.AL.Source(alSources[sourceIndex], OpenTK.Audio.OpenAL.ALSource3f.Position, position.X, position.Y, 0.0f);

            ALHelper.Efx.Filter(lowpassFilterId, OpenTK.Audio.OpenAL.EfxFilterf.LowpassGainHF, lowPassHfGain = Math.Min(lowPassGain, overrideLowPassGain));
            ALHelper.Efx.BindFilterToSource(alSources[sourceIndex], lowpassFilterId);
            ALHelper.Check();
        }

        public static OggStream StartStream(string file, float volume = 1.0f)
        {
            if (oggStreamer == null)
                oggStreamer = new OggStreamer();

            oggStream = new OggStream(file);
            
            oggStreamer.AddStream(oggStream);

            oggStream.Volume = volume;

            oggStream.Play();

            return oggStream;
        }

        public static void StopStream()
        {
            if (oggStream!=null) oggStream.Stop();
        }

        public static void ClearAlSource(int bufferId)
        {
            for (int i = 1; i < DefaultSourceCount; i++)
            { 
                if (alBuffers[i] == bufferId)
                {
                    OpenTK.Audio.OpenAL.AL.Source(alSources[i], OpenTK.Audio.OpenAL.ALSourcei.Buffer, 0);
                }
            }


             
        }
        
        public static void Dispose()
        {
            if (ALHelper.Efx.IsInitialized)
                ALHelper.Efx.DeleteFilter(lowpassFilterId);

            for (int i = 0; i < DefaultSourceCount; i++)
            {
                var state = OpenTK.Audio.OpenAL.AL.GetSourceState(alSources[i]);
                if (state == OpenTK.Audio.OpenAL.ALSourceState.Playing || state == OpenTK.Audio.OpenAL.ALSourceState.Paused)
                    Stop(i);

                OpenTK.Audio.OpenAL.AL.DeleteSource(alSources[i]);
                
                ALHelper.Check();
            }

            if (oggStream!=null)
            {
                oggStream.Stop();
                oggStream.Dispose();
            }
            
            if (oggStreamer != null)
                oggStreamer.Dispose();
        }

    }
}
