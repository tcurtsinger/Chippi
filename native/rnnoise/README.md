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

Prebuilt DLLs for the Sound module live under runtimes/win-*/native/.
hashes.csv records their byte sizes and SHA-256 hashes. These exact files came
from workflow run https://github.com/tcurtsinger/Chippi/actions/runs/40208306781.
CI used MSVC 14.51.36260 (Visual Studio 2026) with /MT for both DLLs; the local x64 check used MSVC 14.44. The workflow also verifies hashes and
test-loads the committed DLLs on native x64 and ARM64 runners. Rebuilding can
change binary hashes (including linker timestamps); a refresh must commit new
DLLs and hashes together. S3 will wire these files into the Sound module.

CI runner references:
- https://docs.github.com/en/actions/reference/runners/github-hosted-runners
- https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md