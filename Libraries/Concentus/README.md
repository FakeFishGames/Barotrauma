# Concentus: Opus for Everyone

This project is an effort to port the Opus reference library to work natively in other languages, and to gather together any such ports that may exist. With this code, developers should be left with no excuse to use an inferior codec, regardless of their language or runtime environment.

[NuGet Package](https://www.nuget.org/packages/Concentus)     

[Related OggOpus Library](https://github.com/lostromb/concentus.oggfile)

## Project Status

This repo contains completely functional Opus implementations in portable C# and Java. They are based on libopus master 1.1.2 configured with FIXED_POINT and with an extra switch to enable/disable the floating-point analysis functions. Both the encoder and decoder paths have been thoroughly tested to be bit-exact with their equivalent C functions in all common use cases. I have also included a port of the libspeexdsp resampler for general use.

Performance-wise, the current build runs about 40-50% as fast as its equivalent libopus build, mostly due to the lack of stack arrays and vectorized intrinsics in managed languages. I do not believe performance will get much better than this; if you need blazing-fast performance then I encourage you to try the P/Opus or JNI library. The API surface is finalized and existing code should not change, but I may add helper classes in the future.

No other ports beyond C# / Java are planned at this time, but pull requests are welcome from any contributors.

## Performance

For those interested in the expected real-world performance of the library, I ran some quick C# benchmarks on a Raspberry Pi 1 (700mhz ARM) at various modes:  

0.82x realtime - 48Khz Voice, Stereo, 32Kbps  (SILK), Compexity 0   
0.98x realtime - 48Khz Music, Stereo, 128Kbps (CELT), Compexity 10   
1.55x realtime - 16Khz Voice, Mono  , 32Kbps  (SILK), Compexity 0   
1.70x realtime - 48Khz Music, Stereo, 96Kbps  (CELT), Compexity 0   
3.59x realtime - 16Khz Music, Mono  , 96Kbps  (CELT), Compexity 0   
