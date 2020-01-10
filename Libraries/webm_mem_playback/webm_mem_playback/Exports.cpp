#include "Exports.h"
#include "Video.h"

Video* loadVideo(const char* filename) {
	Video* retVal = new Video(filename);
	if (retVal->getErrorCode() != 0) {
		delete retVal; retVal = nullptr;
	}
	return retVal;
}

int32_t getVideoWidth(Video* video) {
	return video->getWidth();
}

int32_t getVideoHeight(Video* video) {
	return video->getHeight();
}

int32_t videoHasAudio(Video* video) {
	return video->hasAudio() ? 1 : 0;
}

int32_t getVideoAudioSampleRate(Video* video) {
	return video->getAudioSampleRate();
}

int32_t getVideoAudioChannelCount(Video* video) {
	return video->getAudioChannelCount();
}

void deleteVideo(Video* video) {
	delete video;
}

void playVideo(Video* video) {
	video->play();
}

void stopVideo(Video* video) {
	video->stop();
}

int32_t isVideoPlaying(Video* video) {
	return video->isPlaying() ? 1 : 0;
}

void setVideoFrameCallback(Video* video, EventCallback callback) {
	video->setVideoCallback(callback);
}

void setVideoAudioCallback(Video* video, EventCallback callback) {
	video->setAudioCallback(callback);
}
