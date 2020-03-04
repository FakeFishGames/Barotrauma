#include "Video.h"

static const VpxInterface vpx_decoders[] = {
#if CONFIG_VP8_DECODER
  { "vp8", VP8_FOURCC, &vpx_codec_vp8_dx },
#endif

#if CONFIG_VP9_DECODER
  { "vp9", VP9_FOURCC, &vpx_codec_vp9_dx },
#endif
};

int get_vpx_decoder_count(void) {
	return sizeof(vpx_decoders) / sizeof(vpx_decoders[0]);
}

//https://en.wikipedia.org/wiki/YUV#Y%E2%80%B2UV444_to_RGB888_conversion
static void frameYUV420toRGBA(vpx_image_t* img, uint32_t* buf) {
	for (int y = 0; y < img->d_h; y++) {
		for (int x = 0; x < img->d_w; x++) {
			int c = img->planes[0][(y * img->stride[0]) + x] - 16;
			int d = img->planes[1][((y/2) * img->stride[1]) + (x/2)] - 128;
			int e = img->planes[2][((y/2) * img->stride[2]) + (x/2)] - 128;
			
			int r = (298 * c + 409 * e + 128) >> 8;
			if (r > 255) { r = 255; } if (r < 0) { r = 0; }
			int g = (298 * c - 100 * d - 208 * e + 128) >> 8;
			if (g > 255) { g = 255; } if (g < 0) { g = 0; }
			int b = (298 * c + 516 * d + 128) >> 8;
			if (b > 255) { b = 255; } if (b < 0) { b = 0; }

			buf[x + y * img->d_w] = (255 << 24) | (b << 16) | (g << 8) | r;
		}
	}
}

Video::Video(const char* fn) {
	mkvReader = nullptr;
	parserSegment = nullptr;
	parserSegmentInfo = nullptr;

	filename = fn;
	
	videoPlaybackPos = 0; audioPlaybackPos = 0;
	videoBlockIndex = 0; audioBlockIndex = 0;

	mkvReader = new mkvparser::MkvReader();

	errorCode = mkvReader->Open(fn);
	if (errorCode) { return; }

	errorCode = ebmlHeader.Parse(mkvReader, ebmlHeaderPos);
	if (errorCode) { return; }

	errorCode = mkvparser::Segment::CreateInstance(mkvReader, ebmlHeaderPos, parserSegment);
	if (errorCode) { return; }

	errorCode = parserSegment->Load();
	if (errorCode < 0) { return; }

	parserSegmentInfo = parserSegment->GetInfo();
	if (parserSegmentInfo == nullptr) { errorCode = -1; return; }

	const mkvparser::Tracks* const parserTracks = parserSegment->GetTracks();
	
	mkvparser::VideoTrack* videoTrack = nullptr;
	mkvparser::AudioTrack* audioTrack = nullptr;
	unsigned long trackCount = parserTracks->GetTracksCount();
	for (int i = 0; i < trackCount; i++) {
		const mkvparser::Track* track = parserTracks->GetTrackByIndex(i);

		if (track->GetType() == mkvparser::Track::Type::kVideo) {
			if (videoTrack == nullptr) { videoTrack = (mkvparser::VideoTrack*)track; }
		} else if (track->GetType() == mkvparser::Track::Type::kAudio) {
			if (audioTrack == nullptr) { audioTrack = (mkvparser::AudioTrack*)track; }
		}
	}

	if (videoTrack == nullptr) { errorCode = -1; return; }
	
	//framerate = videoTrack->GetFrameRate(); //TODO: reimplement?
	width = videoTrack->GetDisplayWidth();
	height = videoTrack->GetDisplayHeight();
	videoCodecName = videoTrack->GetCodecId();

	rawFrameData = new uint32_t[width*height];

	int videoCodecIndex = -1;
	if (videoCodecName == mkvmuxer::Tracks::kVp8CodecId) {
		videoCodecIndex = 0;
	} else if (videoCodecName == mkvmuxer::Tracks::kVp9CodecId) {
		videoCodecIndex = 1;
	}

	vpxCodecConfig.w = width;
	vpxCodecConfig.h = height;
	vpxCodecConfig.threads = 1;

	vpx_codec_dec_init(&vpxCodec, vpx_decoders[videoCodecIndex].codec_interface(), &vpxCodecConfig, 0);

	audioAvail = audioTrack != nullptr;

	audioSampleRate = 0; audioChannelCount = 0;
	audioDecoder = nullptr;
	rawAudioDataSize = 0;
	rawAudioData = nullptr;
	audioCodecName = "N/A";
	if (audioAvail) {
		audioCodecName = audioTrack->GetCodecId();
		if (audioCodecName == mkvmuxer::Tracks::kOpusCodecId) {
			audioDecoder = new OpusAudioDecoder((int)audioTrack->GetSamplingRate(), audioTrack->GetChannels());
		} else {
			audioCodecName = "Unsupported Codec "+audioCodecName;
			audioAvail = false;
		}
	}

	if (audioAvail) {
		rawAudioDataSize = audioTrack->GetSamplingRate()*audioTrack->GetChannels() * 2;
		rawAudioData = new int16_t[rawAudioDataSize];
		audioSampleRate = audioTrack->GetSamplingRate();
		audioChannelCount = audioTrack->GetChannels();
	}

	long long prevVideoTime = 0;
	long long prevAudioTime = 0;
	const mkvparser::Cluster* cluster = parserSegment->GetFirst();

	while (cluster != nullptr) {
		const mkvparser::BlockEntry* blockEntry;
		cluster->GetFirst(blockEntry);

		for (; blockEntry != nullptr; cluster->GetNext(blockEntry, blockEntry)) {
			const mkvparser::Block* block = blockEntry->GetBlock();
			mkvparser::Track::Type blockType = (mkvparser::Track::Type)parserTracks->GetTrackByNumber(block->GetTrackNumber())->GetType();
			
			if (block->GetFrameCount() <= 0) { continue; }
			if (blockType == mkvparser::Track::Type::kVideo) {
				videoBlocks.push_back(block);
				videoBlockTimes.push_back(prevVideoTime);
				prevVideoTime = block->GetTime(cluster);
			} else if (blockType == mkvparser::Track::Type::kAudio && audioAvail) {
				audioBlocks.push_back(block);
				audioBlockTimes.push_back(prevAudioTime);
				prevAudioTime = block->GetTime(cluster);
			}

			if (blockEntry->EOS()) { break; }
		}

		if (cluster->EOS()) { break; }
		cluster = parserSegment->GetNext(cluster);
	}

	compressedFrameData = nullptr;
	compressedFrameCapacity = 0;
	compressedAudioData = nullptr;
	compressedAudioCapacity = 0;
	stopRequested = true;
	videoThread = nullptr;
	audioThread = nullptr;
	stop();
}

static void initUpdateVideoThread(Video* video) {
	video->updateVideo();
}

static void initUpdateAudioThread(Video* video) {
	video->updateAudio();
}

void Video::updateVideo() {
	long long makeupTime = 0;
	for (; videoBlockIndex < videoBlocks.size(); videoBlockIndex++) {
		if (stopRequested) { break; }

		int i = videoBlockIndex;
		const mkvparser::Block* block = videoBlocks[i];
		for (int j = 0; j < block->GetFrameCount(); j++) {
            std::chrono::high_resolution_clock::time_point frameTargetEndTime = std::chrono::high_resolution_clock::now();
			long long frameDuration = 0;
			if (videoBlockIndex < videoBlocks.size() - 1) {
				//TODO: does it even make sense for a block to have more than one frame?
				frameDuration = (videoBlockTimes[i + 1] - videoBlockTimes[i]) / block->GetFrameCount();
			}
			frameTargetEndTime += std::chrono::nanoseconds(frameDuration-1);
			const mkvparser::Block::Frame& blockFrame = block->GetFrame(j);

			long long frameDataPos = blockFrame.pos;
			long long frameDataLen = blockFrame.len;

			if (frameDataLen <= 0) { break; }

			if (compressedFrameData == nullptr) {
				compressedFrameData = new uint8_t[frameDataLen*2];
				compressedFrameCapacity = frameDataLen * 2;
			} else if (compressedFrameCapacity < frameDataLen) {
				delete[] compressedFrameData;
				compressedFrameData = new uint8_t[frameDataLen*2];
				compressedFrameCapacity = frameDataLen * 2;
			}

			mkvReaderMutex.lock();
			mkvReader->Read(frameDataPos, frameDataLen, compressedFrameData);
			mkvReaderMutex.unlock();

			vpx_codec_decode(&vpxCodec, compressedFrameData, frameDataLen, this, 0);

			if (makeupTime>0) {
				makeupTime -= frameDuration;
				videoPlaybackPos += frameDuration;
				continue;
			}

			vpx_codec_iter_t iter = NULL;
			vpx_image_t* img = NULL;
			img = vpx_codec_get_frame(&vpxCodec, &iter);
			if (img == nullptr) { continue; }

			frameYUV420toRGBA(img, rawFrameData);

			if (videoCallback != nullptr) { videoCallback(this, rawFrameData, sizeof(uint32_t), width*height); }

			std::chrono::high_resolution_clock::time_point frameEndTime = std::chrono::high_resolution_clock::now();
			if ((frameEndTime - frameTargetEndTime).count() > 0) {
				makeupTime += (frameEndTime - frameTargetEndTime).count() * 2;
			} else {
				std::this_thread::sleep_until(frameTargetEndTime);
			}
			videoPlaybackPos += frameDuration;
		}
	}
	stopRequested = true;
}

void Video::updateAudio() {
	for (; audioBlockIndex < audioBlocks.size(); audioBlockIndex++) {
		if (stopRequested) { break; }

		int i = audioBlockIndex;
		const mkvparser::Block* block = audioBlocks[i];
		for (int j = 0; j < block->GetFrameCount(); j++) {
			long long frameDuration = 0;
			if (audioBlockIndex < audioBlocks.size() - 1) {
				//TODO: does it even make sense for a block to have more than one frame?
				frameDuration = (audioBlockTimes[i + 1] - audioBlockTimes[i]) / block->GetFrameCount();
			}
			std::chrono::high_resolution_clock::time_point frameTargetEndTime = std::chrono::high_resolution_clock::now() + std::chrono::nanoseconds(frameDuration-1000000);
			const mkvparser::Block::Frame& blockFrame = block->GetFrame(j);

			long long frameDataPos = blockFrame.pos;
			long long frameDataLen = blockFrame.len;

			if (frameDataLen <= 0) { break; }

			if (compressedAudioData == nullptr) {
				compressedAudioData = new uint8_t[frameDataLen * 2];
				compressedAudioCapacity = frameDataLen * 2;
			}
			else if (compressedAudioCapacity < frameDataLen) {
				delete[] compressedAudioData;
				compressedAudioData = new uint8_t[frameDataLen * 2];
				compressedAudioCapacity = frameDataLen * 2;
			}

			mkvReaderMutex.lock();
			mkvReader->Read(frameDataPos, frameDataLen, compressedAudioData);
			mkvReaderMutex.unlock();

			int decodedSampleCount = audioDecoder->decode(compressedAudioData, frameDataLen, rawAudioData, rawAudioDataSize);

			if (audioCallback != nullptr) { audioCallback(this, rawAudioData, sizeof(int16_t), decodedSampleCount); }

			std::chrono::high_resolution_clock::time_point frameEndTime = std::chrono::high_resolution_clock::now();
			if ((frameEndTime - frameTargetEndTime).count() < 0) {
                //disabled this because timing issues in the audio are much more noticeable than the video
				//std::this_thread::sleep_until(frameTargetEndTime);
			}
			audioPlaybackPos += frameDuration;
		}
	}
}

Video::~Video() {
	stop();
	if (audioDecoder != nullptr) { delete audioDecoder; }
	vpx_codec_destroy(&vpxCodec);
	if (parserSegment != nullptr) {
		delete parserSegment;
	}
	if (mkvReader != nullptr) {
		mkvReader->Close(); delete mkvReader; mkvReader = nullptr;
	}
	if (rawFrameData != nullptr) {
		delete[] rawFrameData;
	}
	if (rawAudioData != nullptr) {
		delete[] rawAudioData;
	}
    if (compressedFrameData != nullptr) {
        delete[] compressedFrameData;
    }
    if (compressedAudioData != nullptr) {
        delete[] compressedAudioData;
    }
}

void Video::play() {
	stop(); resume();
}

void Video::stop() {
	pause();
	videoPlaybackPos = 0;
	audioPlaybackPos = 0;
	videoBlockIndex = 0;
	audioBlockIndex = 0;
}

void Video::pause() {
	stopRequested = true;
	if (videoThread != nullptr) {
		videoThread->join();
		delete videoThread;
		videoThread = nullptr;
	}
	if (audioThread != nullptr) {
		audioThread->join();
		delete audioThread;
		audioThread = nullptr;
	}
}

void Video::resume() {
	//TODO: perform video-audio synchronization here
	stopRequested = false;
	videoThread = new std::thread(initUpdateVideoThread, this);
	if (audioAvail) { audioThread = new std::thread(initUpdateAudioThread, this); }
}

long Video::getPlaybackPos() { return videoPlaybackPos; }
void Video::seek(long pos) { /* TODO: implement? */ }

int Video::getWidth() { return width; }
int Video::getHeight() { return height; }
//double Video::getFramerate() { return framerate; }
bool Video::hasAudio() { return audioAvail; }
int Video::getAudioSampleRate() { return audioSampleRate; }
int Video::getAudioChannelCount() { return audioChannelCount; }

const char* Video::getVideoCodec() { return videoCodecName.c_str(); }

bool Video::isPlaying() { return !stopRequested; }

int Video::getErrorCode() { return errorCode; }

void Video::setVideoCallback(EventCallback callback) { videoCallback = callback; }
void Video::setAudioCallback(EventCallback callback) { audioCallback = callback; }
