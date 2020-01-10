CXX=g++
CXXFLAGS= -O3 -fPIC -Wall -I../libvpx_x64_linux -I../libwebm_x64_linux -I../opus/include


webm_mem_playback_x64: Exports.o AudioDecoder.o Video.o
	$(CXX) $(CXXFLAGS) -shared -L../opus_x64_linux/.libs/ -L../libvpx_x64_linux/ -L../libwebm_x64_linux/ -Wl,--whole-archive -lopus -lvpx -lwebm -lpthread -Wl,--no-whole-archive -o webm_mem_playback_x64.so Exports.o AudioDecoder.o Video.o

Exports.o: Exports.cpp Exports.h Video.h
	$(CXX) $(CXXFLAGS) -c Exports.cpp

Video.o: Video.cpp AudioDecoder.h Video.h
	$(CXX) $(CXXFLAGS) -c Video.cpp

AudioDecoder.o: AudioDecoder.cpp AudioDecoder.h
	$(CXX) $(CXXFLAGS) -c AudioDecoder.cpp
