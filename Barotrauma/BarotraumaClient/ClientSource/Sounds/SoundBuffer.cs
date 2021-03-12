using Barotrauma.Extensions;
using OpenAL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma.Sounds
{
    public class SoundBuffers : IDisposable
    {
        private static HashSet<uint> bufferPool = new HashSet<uint>();
#if OSX
        public const int MaxBuffers = 400; //TODO: check that this value works for macOS
#else
        public const int MaxBuffers = 32000;
#endif
        public static int BuffersGenerated { get; private set; } = 0;
        private Sound sound;

        public uint AlBuffer { get; private set; } = 0;
        public uint AlMuffledBuffer { get; private set; } = 0;
        
        public SoundBuffers(Sound sound) { this.sound = sound; }
        public void Dispose()
        {
            if (AlBuffer != 0) { bufferPool.Add(AlBuffer); }
            if (AlMuffledBuffer != 0) { bufferPool.Add(AlMuffledBuffer); }
            AlBuffer = 0;
            AlMuffledBuffer = 0;
        }

        public static void ClearPool()
        {
            bufferPool.ForEach(b => Al.DeleteBuffer(b));
            bufferPool.Clear();
        }

        public bool RequestAlBuffers()
        {
            if (AlBuffer != 0) { return false; }
            int alError = 0;
            while (bufferPool.Count < 2 && BuffersGenerated < MaxBuffers)
            {
                Al.GenBuffer(out uint newBuffer);
                alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    DebugConsole.AddWarning($"Error when generating sound buffer: {Al.GetErrorString(alError)}. {BuffersGenerated} buffer(s) were generated. No more sound buffers will be generated.");
                    BuffersGenerated = MaxBuffers;
                }
                else if (!Al.IsBuffer(newBuffer))
                {
                    DebugConsole.AddWarning($"Error when generating sound buffer: result is not a valid buffer. {BuffersGenerated} buffer(s) were generated. No more sound buffers will be generated.");
                    BuffersGenerated = MaxBuffers;
                }
                else
                {
                    bufferPool.Add(newBuffer);
                    BuffersGenerated++;
                    if (BuffersGenerated >= MaxBuffers)
                    {
                        DebugConsole.AddWarning($"{BuffersGenerated} buffer(s) were generated. No more sound buffers will be generated.");
                    }
                }
            }

            if (bufferPool.Count >= 2)
            {
                AlBuffer = bufferPool.First();
                bufferPool.Remove(AlBuffer);
                AlMuffledBuffer = bufferPool.First();
                bufferPool.Remove(AlMuffledBuffer);
                return true;
            }

            //can't generate any more OpenAL buffers! we'll have to steal a buffer from someone...
            foreach (var otherSound in sound.Owner.LoadedSounds)
            {
                if (otherSound == sound) { continue; }
                if (otherSound.IsPlaying()) { continue; }
                if (otherSound.Buffers == null) { continue; }
                if (otherSound.Buffers.AlBuffer == 0) { continue; }
                AlBuffer = otherSound.Buffers.AlBuffer;
                AlMuffledBuffer = otherSound.Buffers.AlMuffledBuffer;
                otherSound.Buffers.AlBuffer = 0;
                otherSound.Buffers.AlMuffledBuffer = 0;
                return true;
            }

            return false;
        }
    }
}
