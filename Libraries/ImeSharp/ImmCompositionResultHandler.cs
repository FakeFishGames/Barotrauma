using System;
using System.Text;
using System.Runtime.InteropServices;
using ImeSharp.Native;

namespace ImeSharp
{
    internal abstract class ImmCompositionResultHandler
    {
        protected IntPtr _imeContext;

        public int Flag { get; private set; }

        internal ImmCompositionResultHandler(IntPtr imeContext, int flag)
        {
            this.Flag = flag;
            _imeContext = imeContext;
        }

        internal virtual void Update() { }

        internal bool Update(int lParam)
        {
            if ((lParam & Flag) == Flag)
            {
                Update();
                return true;
            }
            return false;
        }
    }

    internal class ImmCompositionStringHandler : ImmCompositionResultHandler
    {
        internal const int BufferSize = 1024;
        private byte[] _byteBuffer = new byte[BufferSize];
        private int _byteCount;

        private char[] _charBuffer = new char[BufferSize / 2];
        private int _charCount;

        public char[] Values { get { return _charBuffer; } }
        public int Count { get { return _charCount; } }

        public char this[int index]
        {
            get
            {
                if (index >= _charCount || index < 0)
                    throw new ArgumentOutOfRangeException("index");

                return _charBuffer[index];
            }
        }

        internal ImmCompositionStringHandler(IntPtr imeContext, int flag) : base(imeContext, flag)
        {
        }

        public override string ToString()
        {
            if (_charCount <= 0)
                return string.Empty;

            return new string(_charBuffer, 0, _charCount);
        }

        internal void Clear()
        {
            Array.Clear(_byteBuffer, 0, _byteCount);
            _byteCount = 0;

            Array.Clear(_charBuffer, 0, _charCount);
            _charCount = 0;
        }

        internal override void Update()
        {
            _byteCount = NativeMethods.ImmGetCompositionString(_imeContext, Flag, IntPtr.Zero, 0);
            IntPtr pointer = Marshal.AllocHGlobal(_byteCount);

            try
            {
                Array.Clear(_byteBuffer, 0, _byteCount);

                if (_byteCount > 0)
                {
                    NativeMethods.ImmGetCompositionString(_imeContext, Flag, pointer, _byteCount);

                    Marshal.Copy(pointer, _byteBuffer, 0, _byteCount);

                    Array.Clear(_charBuffer, 0, _charCount);
                    _charCount = Encoding.Unicode.GetChars(_byteBuffer, 0, _byteCount, _charBuffer, 0);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }

    internal class ImmCompositionIntHandler : ImmCompositionResultHandler
    {
        public int Value { get; private set; }

        internal ImmCompositionIntHandler(IntPtr imeContext, int flag) : base(imeContext, flag) { }

        public override string ToString()
        {
            return Value.ToString();
        }

        internal override void Update()
        {
            Value = NativeMethods.ImmGetCompositionString(_imeContext, Flag, IntPtr.Zero, 0);
        }
    }
}
