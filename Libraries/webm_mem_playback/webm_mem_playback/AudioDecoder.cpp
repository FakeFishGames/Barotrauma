#include "AudioDecoder.h"

OpusAudioDecoder::OpusAudioDecoder(int smpRate, int channels) {
	int errorCode = OPUS_OK;
	sampleRate = smpRate; channelCount = channels;
	decoder = opus_decoder_create(sampleRate, channelCount, &errorCode);
}

OpusAudioDecoder::~OpusAudioDecoder() {
	opus_decoder_destroy(decoder);
}

int OpusAudioDecoder::decode(uint8_t* compressedBuf, int compressedSize, int16_t* outBuf, int outSize) {
	return opus_decode(decoder, compressedBuf, compressedSize, outBuf, outSize / channelCount, 0) * channelCount;
}
