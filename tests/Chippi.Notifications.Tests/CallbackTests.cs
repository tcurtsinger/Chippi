// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Notifications;

public sealed class CallbackTests
{
    [Fact]
    public void Matching_app_id_delivers_encoded_arguments_and_another_identity_is_ignored()
    {
        string? actual = null;
        var callback = new ActivationCallback("Chippi.dev.fixture", value => actual = value);
        string payload = ToastActivation.Encode("downloads", ["--downloads-run", "42"]);
        callback.Activate("Chippi.dev.other", payload, 0, 0);
        Assert.Null(actual);
        callback.Activate("Chippi.dev.fixture", payload, 0, 0);
        Assert.Equal(payload, actual);
    }

    [Fact]
    public void Managed_handler_failure_never_escapes_the_com_boundary()
    {
        var callback = new ActivationCallback("test", _ => throw new InvalidOperationException("expected"));
        callback.Activate("test", "data", 0, 0);
    }

    [Fact]
    public void An_ordinary_launch_does_not_acquire_a_mutex_or_register_notifications()
    {
        var result = ToastStartup.Prepare(["--organize", @"C:\Downloads"]);
        Assert.False(result.Exit); Assert.Null(result.Owner); Assert.Null(result.Presenter);
        Assert.Equal(["--organize", @"C:\Downloads"], result.Arguments);
    }
}