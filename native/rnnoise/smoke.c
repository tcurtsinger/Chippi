// SPDX-License-Identifier: GPL-3.0-or-later
#include <windows.h>
#include <math.h>
#include <stdio.h>

typedef void *(__cdecl *create_fn)(void *);
typedef void (__cdecl *destroy_fn)(void *);
typedef float (__cdecl *process_fn)(void *, float *, const float *);

int wmain(int argc, wchar_t **argv)
{
    const char *exports[] = { "rnnoise_get_size", "rnnoise_init", "rnnoise_create",
        "rnnoise_destroy", "rnnoise_process_frame", "rnnoise_model_from_file", "rnnoise_model_free" };
    HMODULE dll;
    create_fn create;
    destroy_fn destroy;
    process_fn process;
    void *state;
    float input[480] = { 0 }, output[480], probability;
    int i, frame;
    if (argc != 2) return 2;
    dll = LoadLibraryW(argv[1]);
    if (!dll) { fprintf(stderr, "LoadLibrary failed: %lu\n", GetLastError()); return 3; }
    for (i = 0; i < 7; ++i) {
        if (!GetProcAddress(dll, exports[i])) {
            fprintf(stderr, "Missing export: %s\n", exports[i]); FreeLibrary(dll); return 4;
        }
    }
#pragma warning(push)
#pragma warning(disable:4191)
    create = (create_fn)GetProcAddress(dll, "rnnoise_create");
    destroy = (destroy_fn)GetProcAddress(dll, "rnnoise_destroy");
    process = (process_fn)GetProcAddress(dll, "rnnoise_process_frame");
#pragma warning(pop)
    state = create(NULL);
    if (!state) { FreeLibrary(dll); return 5; }
    /* In-memory PCM only: silence followed by a low-level synthetic signal. */
    for (frame = 0; frame < 12; ++frame) {
        if (frame == 4) for (i = 0; i < 480; ++i) input[i] = (float)(1000 * sin(i * 0.1));
        probability = process(state, output, input);
        if (!isfinite(probability) || probability < 0 || probability > 1) {
            destroy(state); FreeLibrary(dll); return 6;
        }
        for (i = 0; i < 480; ++i) if (!isfinite(output[i])) {
            destroy(state); FreeLibrary(dll); return 7;
        }
    }
    destroy(state);
    FreeLibrary(dll);
    puts("PASS: seven exports, create/process/destroy, finite PCM; no audio devices opened");
    return 0;
}