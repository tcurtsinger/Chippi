// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.KeepAwake;

public enum AwakeMode { Indefinitely, Duration, UntilTime }

public sealed record KeepAwakeSettings
{
    public AwakeMode Mode { get; init; }
    public int DurationMinutes { get; init; } = 60;
    public int UntilMinute { get; init; } = 18 * 60;
    public bool KeepDisplayOn { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || DurationMinutes is < 1 or > 1440 || UntilMinute is < 0 or >= 1440)
            throw new ArgumentException("1–1440 minutes");
    }

    public DateTimeOffset? Deadline(DateTimeOffset now)
    {
        Validate();
        if (Mode == AwakeMode.Indefinitely) return null;
        if (Mode == AwakeMode.Duration) return now.AddMinutes(DurationMinutes);
        var local = now.LocalDateTime.Date.AddMinutes(UntilMinute);
        if (local <= now.LocalDateTime) local = local.AddDays(1);
        if (TimeZoneInfo.Local.IsInvalidTime(local))
            throw new ArgumentException("Time doesn't exist on this date");
        return new DateTimeOffset(local);
    }
}
