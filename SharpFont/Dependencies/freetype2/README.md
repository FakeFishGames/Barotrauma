FreeType2
=========

This directory contains all of the FreeType2 builds, a copy of the FreeType
Project License, and a patch file for when compiling new versions of freetype
for 64-bit Windows.

**WARNING**: These are not standard builds of FreeType on Windows (for 64-bit)
and will not work as a drop-in replacement for someone else's build. This is
why the package is distributed on NuGet as *SharpFont.Dependencies* instead of
*freetype2.redist* or something similar.

##Compiling FreeType on Windows

The copies of `freetype6.dll` that the Examples project uses by default are
chosen based on what works on my machine, and I will probably update it as
soon as a new version of FreeType is released. This means that it may not work
on older versions of Windows. If this is the case, you can either modify
the project file to point to another included version of freetype or you can
compile FreeType yourself from source.

**Note**: Any 32-bit copy of `freetype6.dll` works as a drop-in replacement,
including [this copy][1] from the GnuWin32 project. Older versions such as
that one may crash with a `EntryPointException` when using newer APIs. **This
is not true of 64-bit copies currently**

Thanks to [this StackOverflow answer][2] for the directions:

 1. Download the latest [FreeType source code][3].
 2. Open `builds\win32\vc2010\freetype.sln` (or whatever version of Visual
 Studio you have) in Visual Studio.
 3. Change the compile configuration from Debug to Release.
 4. Open the project properties window through Project -> Properties.
 5. In the `General` selection, change the `Target Name` to `freetype6` and
 the `Configuration Type` to `Dynamic Library (.dll)`.
 6. **If compiling for 64-bit** 
   - Apply a patch to the source code (see [Known Issues](#known-issues)).
   - Open up Configuration Manager (the last option in  the dropdown menu when
   changing your compile configuration) and change `Platform` to `x64`.
 7. Open up `ftoption.h` (in the project's `Header Files` section) and add the
 following three lines near the `DLL export compilation` section:

```C
#define FT_EXPORT(x) __declspec(dllexport) x
#define FT_EXPORT_DEF(x) __declspec(dllexport) x
#define FT_BASE(x) __declspec(dllexport) x
```

Finally, complile the project (`F6` or Build -> Build Solution).
`freetype6.dll` will be output to `objs\win32\vc2010`. If this is a build that
isn't included in [Dependencies][4], consider forking and submitting a pull
request with your new build.

## Windows x64

A patch file, [win64.patch][5], is included in this directory that will force
several types to be 64 bits wide. On Linux and OS X, the native `long` type is
64 bits wide on x64, but on Windows, it's still 32 bits wide for backwards
compatibility of applications assuming `sizeof(long) == sizeof(int)`. A list
of the affected types:

  - `FT_Long`
  - `FT_ULong`
  - `FT_Fixed`
  - `FT_F26Dot6`
  - `FT_Pos`
  
To apply the patch file, you need a copy of the `patch` utility built for
Windows. If using msysgit, it is already installed and is available from Git
Bash. Otherwise, a build is available from [GnuWin32][6]. Utilities such as
TortoiseDiff can provide a graphical way to do this same task.

After downloading and decompressing FreeType2, copy `win64.patch` into the
root folder (i.e. the folder containing `builds/`, `/include`, `/src`, etc.)
and run the following command:

```
patch -p0 < win64.patch
```

[1]: http://gnuwin32.sourceforge.net/packages/freetype.htm
[2]: http://stackoverflow.com/a/7387618/1122135
[3]: http://sourceforge.net/projects/freetype/files/freetype2/
[4]: https://github.com/Robmaister/SharpFont.Dependencies
[5]: https://github.com/Robmaister/SharpFont.Dependencies/blob/master/freetype2/win64.patch
[6]: http://gnuwin32.sourceforge.net/packages/patch.htm
