SharpFont [![NuGet Version](https://img.shields.io/nuget/vpre/SharpFont.svg)](https://www.nuget.org/packages/SharpFont) [![Gratipay Tips](https://img.shields.io/gratipay/Robmaister.svg)](https://gratipay.com/Robmaister)
=========
### Cross-platform FreeType bindings for .NET

SharpFont is a library that provides FreeType bindings for .NET. It's MIT
licensed to make sure licensing doesn't get in the way of using the library in
your own projects. Unlike [Tao.FreeType][1], SharpFont provides the full
public API and not just the basic methods needed to render simple text.
Everything from format-specific APIs to the caching subsystem are included.

SharpFont simplifies the FreeType API in a few ways:

 - The error codes that most FreeType methods return are converted to
   exceptions.
 - Since the return values are no longer error codes, methods with a single
   `out` parameter are returned instead.
 - Most methods are instance methods instead of static methods. This avoids
   unnecessary redundancy in method calls and creates an API with a .NET
   look-and-feel.

For example, a regular FreeType method looks like this:

```C
Face face;
int err = FT_New_Face(library, "./myfont.ttf", 0, &face);
```

The equivalent code in C# with SharpFont is:

```CSharp
Face face = new Face(library, "./myfont.ttf");
```

##Quick Start

###NuGet
SharpFont is available on [NuGet][2]. It can be installed by issuing the
following command in the package manager console:

```
PM> Install-Package SharpFont
```

###From Source
Clone the repository and compile the solution. Copy `SharpFont.dll` to your
project and include it as a reference. On Windows, you must include a compiled
copy of FreeType2 as `freetype6.dll` in the project's output directory. It is
possible to rename the file by changing the filename constant in
[FT.Internal.cs][3] and recompile. On Linux and OSX (and any other Mono
supported platform), you must also copy `SharpFont.dll.config` to the
project's output directory.

Two extensions for Visual Studio make it easy to follow the coding format in 
this project and prevent lots of spurious whitespace changes.
The [.editorconfig](http://editorconfig.org/) file in the project works with the
[EditorConfig](https://visualstudiogallery.msdn.microsoft.com/c8bccfe2-650c-4b42-bc5c-845e21f96328) 
extension and the [Format document on Save](https://visualstudiogallery.msdn.microsoft.com/3ea1c920-69c4-441f-9979-ccc2752dac56) 
extension makes it basically automatic.

####Mono
With the removal of the `WIN64` configurations, the included `Makefile` is
effectively redundant. However, you can still build SharpFont by calling
`make` while in the root directory of this project.

####FreeType
A large number of FreeType builds for Windows are now available in the
[SharpFont.Dependencies][4] repository.

##Known Issues

While SharpFont is fully compatible with and runs on 64-bit Windows, it relies
on a patch for FreeType to do this. This patch is already included in
[SharpFont.Dependencies/freetype2][5]. You do not need to worry about this as
a user of the library. If you are compiling FreeType from source, you can find
the patch and instructions at the same location.

##License

As metioned earlier, SharpFont is licensed under the MIT License. The terms of
the MIT license are included in both the [LICENSE][6] file and below:

```
Copyright (c) 2012-2016 Robert Rouhani <robert.rouhani@gmail.com>

SharpFont based on Tao.FreeType, Copyright (c) 2003-2007 Tao Framework Team

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The Windows binary of FreeType that is included in the Examples project and in
the NuGet package is redistributed under the FreeType License (FTL).

```
Portions of this software are copyright (c) 2016 The FreeType Project
(www.freetype.org). All rights reserved.
```


[1]: http://taoframework.svn.sourceforge.net/viewvc/taoframework/trunk/src/Tao.FreeType/
[2]: https://nuget.org/packages/SharpFont/
[3]: SharpFont/FT.Internal.cs
[4]: https://github.com/Robmaister/SharpFont.Dependencies
[5]: https://github.com/Robmaister/SharpFont.Dependencies/tree/master/freetype2
[6]: LICENSE
