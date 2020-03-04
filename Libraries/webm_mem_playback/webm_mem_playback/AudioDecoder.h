#ifndef AUDIODECODER_H_INCLUDED
#define AUDIODECODER_H_INCLUDED

//opus
#include <opus.h>

//stl
#include <cinttypes>

class AudioDecoder {
	public:
		virtual ~AudioDecoder() {}
		virtual int decode(uint8_t* compressedBuf, int compressedSize, int16_t* outBuf, int outSize) =0;
};

class OpusAudioDecoder : public AudioDecoder {
	public:
		OpusAudioDecoder(int smpRate, int channels);
		virtual ~OpusAudioDecoder();
		int decode(uint8_t* compressedBuf, int compressedSize, int16_t* outBuf, int outSize) override;
	private:
		OpusDecoder* decoder;
		int channelCount;
		int sampleRate;
};

/* TODO: implement vorbis decoder */

#endif
