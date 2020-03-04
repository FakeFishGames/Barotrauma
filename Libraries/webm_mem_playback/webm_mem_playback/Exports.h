#ifndef EXPORTS_H_INCLUDED
#define EXPORTS_H_INCLUDED

#ifdef _WIN32
#define EXPORT(x) extern "C" _declspec(dllexport) x  _cdecl
#elif defined __APPLE__
#define EXPORT(x) extern "C" x  __attribute__((visibility("default")))
#else
#define EXPORT(x) extern "C" x
#endif

#include <cinttypes>

class Video;
typedef void (*EventCallback)(Video* video, void* data, int32_t dataElemSize, int32_t dataLen);

EXPORT(Video*) loadVideo(const char* filename);
EXPORT(int32_t) getVideoWidth(Video* video);
EXPORT(int32_t) getVideoHeight(Video* video);
EXPORT(int32_t) videoHasAudio(Video* video);
EXPORT(int32_t) getVideoAudioSampleRate(Video* video);
EXPORT(int32_t) getVideoAudioChannelCount(Video* video);
EXPORT(void) deleteVideo(Video* video);
EXPORT(void) playVideo(Video* video);
EXPORT(void) stopVideo(Video* video);
EXPORT(int32_t) isVideoPlaying(Video* video);
EXPORT(void) setVideoFrameCallback(Video* video, EventCallback callback);
EXPORT(void) setVideoAudioCallback(Video* video, EventCallback callback);

#endif
