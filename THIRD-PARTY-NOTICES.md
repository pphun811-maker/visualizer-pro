# Third-party components

- .NET / WPF — Microsoft, MIT. https://github.com/dotnet/runtime and https://github.com/dotnet/wpf
- NAudio 2.2.1 — Mark Heath and contributors, MIT. https://github.com/naudio/NAudio
- SkiaSharp 3.119.4 — Microsoft and contributors, MIT; Skia includes BSD-style third-party licenses. https://github.com/mono/SkiaSharp
- OpenTK / GLWpfControl — MIT. https://github.com/opentk/opentk and https://github.com/opentk/GLWpfControl
- FFmpeg — LGPL build supplied as separate unmodified executables. License is included in `tools/ffmpeg/LICENSE.txt`; run `tools/ffmpeg/ffmpeg.exe -L` for license details and `-version` for the exact configuration.

FFmpeg binary archive: https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl.zip

Build recipes and dependencies: https://github.com/BtbN/FFmpeg-Builds

FFmpeg source for the bundled 2026-10-15 build (commit 3c92f003c7): https://github.com/FFmpeg/FFmpeg/tree/3c92f003c7

FFmpeg is invoked as a separate process. Visualizer Pro does not link FFmpeg libraries. It can use replacement executables in its `tools/ffmpeg` directory.

NuGet package license files, when supplied by their publishers, are collected in the release `licenses` directory.
