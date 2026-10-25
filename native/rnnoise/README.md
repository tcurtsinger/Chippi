# RNNoise

Vendored from Hopper's MSVC-patched classic RNNoise source (Hopper commit
b348aa1). The Phase 3 source comparison identifies upstream base
9acc1e5a633e0961a5895a73204df24744f199b6 (xiph/rnnoise).
The source files are copied unchanged from Hopper, including its VLA-to-heap
patches in pitch.c and celt_lpc.c, M_PI fallback and training-only changes.
This is the classic model and 480-sample, 48 kHz mono float API; samples use
the signed-16-bit amplitude scale. COPYING is the upstream BSD license.

Run native\rnnoise\build.cmd x64 or arm64 from an x64 Visual Studio build
machine. The latter requires the ARM64 C++ tools. Both use /MT and produce
build/artifacts/rnnoise/<architecture>/. No compiler installation is automatic.

The RNNoise native workflow cross-compiles both architectures, then loads each
DLL on its native Windows runner. smoke.c checks all seven exports and processes
in-memory silence and synthetic PCM. It never opens an audio device.
This establishes load/API compatibility, not microphone quality or performance.

Prebuilt DLLs for the Sound module will live under runtimes/win-*/native/.