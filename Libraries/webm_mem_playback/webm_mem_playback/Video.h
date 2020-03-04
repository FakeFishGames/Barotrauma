#ifndef VIDEO_H_INCLUDED
#define VIDEO_H_INCLUDED

//std
#include <thread>
#include <mutex>
#include <atomic>
#include <string>
#include <vector>
#include <chrono>

//libvpx, libwebm
#include <common/file_util.h>
#include <common/hdr_util.h>
#include <mkvmuxer/mkvmuxer.h>
#include <mkvparser/mkvparser.h>
#include <mkvparser/mkvreader.h>
#include <vpx/vpx_decoder.h>
#include <tools_common.h>
#include <vpx/vp8dx.h>

//opus
#include <opus.h>

//local headers
#include "AudioDecoder.h"
#include "Exports.h"

class Video {
	public:
		Video(const char* fn);
		~Video();

		void play();
		void stop();
		void resume();
		void pause();
		long getPlaybackPos();
		void seek(long pos);

		int getWidth();
		int getHeight();
		//double getFramerate();
		bool hasAudio();
		int getAudioSampleRate();
		int getAudioChannelCount();
		const char* getVideoCodec();

		bool isPlaying();

		int getErrorCode();

		void setVideoCallback(EventCallback callback);
		void setAudioCallback(EventCallback callback);

		void updateVideo();
		void updateAudio();
	private:
		mkvparser::MkvReader* mkvReader;
		std::mutex mkvReaderMutex;
		long long ebmlHeaderPos;  mkvparser::EBMLHeader ebmlHeader;
		mkvparser::Segment* parserSegment;
		const mkvparser::SegmentInfo* parserSegmentInfo;

		std::vector<const mkvparser::Block*> videoBlocks;
		std::vector<long long> videoBlockTimes;
		std::vector<const mkvparser::Block*> audioBlocks;
		std::vector<long long> audioBlockTimes;

		int width; int height; /*double framerate;*/
		
		bool audioAvail; int audioSampleRate; int audioChannelCount;

		std::string filename;
		std::string videoCodecName;
		std::string audioCodecName;

		vpx_codec_ctx_t vpxCodec;
		vpx_codec_dec_cfg_t vpxCodecConfig;

		EventCallback videoCallback = nullptr;
		EventCallback audioCallback = nullptr;

		uint8_t* compressedFrameData;
		int compressedFrameCapacity;

		uint8_t* compressedAudioData;
		int compressedAudioCapacity;

		uint32_t* rawFrameData;

		int rawAudioDataSize;
		int16_t* rawAudioData;

		std::thread* videoThread;
		std::thread* audioThread;

		std::atomic<long long> videoPlaybackPos;
		std::atomic<long long> audioPlaybackPos;
		std::atomic<int> videoBlockIndex;
		std::atomic<int> audioBlockIndex;
		std::atomic<bool> stopRequested;

		AudioDecoder* audioDecoder;

		long long errorCode;
};


#endif // !VIDEO_H_INCLUDED
