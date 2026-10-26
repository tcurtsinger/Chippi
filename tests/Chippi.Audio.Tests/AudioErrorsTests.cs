// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Audio.Tests;

/// <summary>Karl's copy review: failures showed diagnostic sentences in Sound pages and notices.</summary>
public sealed class AudioErrorsTests
{
    [Fact]
    public void Own_fragments_show_as_they_are() =>
        Assert.Equal("Device in use", AudioErrors.Reason(new InvalidOperationException("Device in use")));

    [Theory]
    [InlineData("The transport mixes 8 channels; only mono and stereo are supported.")]
    [InlineData("rnnoise_create returned null.")]
    [InlineData("Exception has been thrown by the target of an invocation and the inner exception explains it")]
    public void Diagnostic_text_never_shows(string message) =>
        Assert.Equal("Audio error", AudioErrors.Reason(new InvalidOperationException(message)));

    [Fact]
    public void System_failures_map_to_fragments()
    {
        Assert.Equal("Device removed", AudioErrors.Reason(new COMException("x", unchecked((int)0x88890004))));
        Assert.Equal("Device in use", AudioErrors.Reason(new COMException("x", unchecked((int)0x8889000A))));
        Assert.Equal("Device error", AudioErrors.Reason(new COMException("Catastrophic failure (0x8000FFFF)", unchecked((int)0x8000FFFF))));
        Assert.Equal("Noise suppression unavailable", AudioErrors.Reason(new DllNotFoundException("Unable to load DLL 'rnnoise'")));
    }

    [Fact]
    public void The_pipelines_own_failures_are_fragments()
    {
        Assert.Equal("Unsupported channel count (8)", AudioErrors.Reason(new NotSupportedException("Unsupported channel count (8)")));
        Assert.Equal("Noise suppression failed", AudioErrors.Reason(new InvalidOperationException("Noise suppression failed")));
    }
}
