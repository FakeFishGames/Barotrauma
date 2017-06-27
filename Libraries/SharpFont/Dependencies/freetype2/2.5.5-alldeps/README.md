# Notes about these builds

These builds are statically linked with Zlib 1.2.8, LibPNG 1.6.16, BZip2 1.0.6, and HarfBuzz 0.9.36 (From the ShiftMedia Project).

The Freetype sources were patched with the fttypes-h-win32.patch file in the ../2.5.4-alldeps/patches directory. This patch changes the fttypes.h file's usage of long to __int64 on Windows.