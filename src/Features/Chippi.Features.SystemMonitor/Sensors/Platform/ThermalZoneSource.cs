// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>
/// ACPI thermal zones from "Thermal Zone Information" (Kelvin). Many desktop boards expose none; then
/// the list is empty and the card isn't shown.
/// </summary>
internal sealed class ThermalZoneSource : ITemperatureSource
{
    private const double KelvinOffset = 273.15;

    private PdhQuery? _query;
    private nint _temperature;
    private bool _none;

    public IReadOnlyList<ThermalZoneReading> Read()
    {
        if (_none)
            return [];
        if (_query is null)
        {
            var query = new PdhQuery();
            try
            {
                _temperature = query.Add(@"\Thermal Zone Information(*)\Temperature");
            }
            catch (PdhException ex) when (ex.Status is PdhQuery.NoObject or PdhQuery.NoCounter)
            {
                query.Dispose();
                _none = true;
                return [];
            }
            _query = query;
        }
        if (!_query.Collect())
            return [];
        // A zone reading 0 K reports nothing.
        return [.. _query.Values(_temperature)
            .Where(z => z.Value > 0)
            .Select(z => new ThermalZoneReading(z.Name, z.Value!.Value - KelvinOffset))];
    }

    public void Dispose() => _query?.Dispose();
}
