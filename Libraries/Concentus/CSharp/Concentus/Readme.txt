Hi there, welcome to the Concentus package. You're about to ask me for sample code, so I'll get straight to it:

If you're already using something like P/Opus then your code probably looks like this:

	[DllImport(OPUS_TARGET_DLL, CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr opus_encoder_create(int Fs, int channels, int application, out IntPtr error);

	[DllImport(OPUS_TARGET_DLL, CallingConvention = CallingConvention.Cdecl)]
	private static extern int opus_encode(IntPtr st, byte[] pcm, int frame_size, IntPtr data, int max_data_bytes);

	// Initialize
	IntPtr error;
	IntPtr _encoder = opus_encoder_create(48000, 1, OPUS_APPLICATION_AUDIO, out error);
	opus_encoder_ctl(_encoder, OPUS_SET_BITRATE_REQUEST, 12000);

	// Encoding loop
	byte[] inputAudioSamplesInterleaved; // 16-bit pcm data interleaved into a byte array
	byte[] outputBuffer[1000];
	int frameSize = 960;

	unsafe
	{
		fixed (byte* benc = outputBuffer)
		{
			IntPtr encodedPtr = new IntPtr(benc);
			int thisPacketSizeOrSometimesAnErrorCode = opus_encode(_encoder, inputAudioSamplesInterleaved, frameSize, encodedPtr, outputBuffer.Length);
		}
	}

Here is what you can replace it with:

	// Initialize
	OpusEncoder encoder = OpusEncoder.Create(48000, 1, OpusApplication.OPUS_APPLICATION_AUDIO);
	encoder.Bitrate = 12000;

	// Encoding loop
	short[] inputAudioSamples
	byte[] outputBuffer[1000];
	int frameSize = 960;

	int thisPacketSize = encoder.Encode(inputAudioSamples, 0, frameSize, outputBuffer, 0, outputBuffer.Length); // this throws OpusException on a failure, rather than returning a negative number

And here is the decoder path:

	OpusDecoder decoder = OpusDecoder.Create(48000, 1);

	// Decoding loop
	byte[] compressedPacket;
	int frameSize = 960; // must be same as framesize used in input, you can use OpusPacketInfo.GetNumSamples() to determine this dynamically
	short[] outputBuffer = new short[frameSize];

	int thisFrameSize = _decoder.Decode(compressedPacket, 0, compressedPacket.Length, outputBuffer, 0, frameSize, false);
