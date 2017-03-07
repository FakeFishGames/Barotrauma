XBUILD              := xbuild
XBUILDFLAGS_RELEASE := /p:Configuration=Release
XBUILDFLAGS_DEBUG   := /p:Configuration=Debug

SOLUTION            := Source/SharpFont.sln

release:
	$(XBUILD) $(XBUILDFLAGS_RELEASE) $(SOLUTION)
debug:
	$(XBUILD) $(XBUILDFLAGS_DEBUG) $(SOLUTION)
clean:
	$(XBUILD) $(XBUILDFLAGS_DEBUG) $(SOLUTION) /t:Clean
	$(XBUILD) $(XBUILDFLAGS_RELEASE) $(SOLUTION) /t:Clean

.SUFFIXES:
.PHONY: release debug clean
