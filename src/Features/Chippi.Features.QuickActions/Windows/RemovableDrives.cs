// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Ioctl;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>
/// The drives Windows can eject, as the taskbar's Safely Remove Hardware lists them: removable volumes and disks on USB,
/// SD or FireWire. A page read only lists the lettered volumes, which asks no disk anything; each volume's device, name
/// and size are read once, when it first appears. Flash media in a reader can change under the same volume, so those
/// (and a reader with no card) are read again at most every 30 seconds.
/// </summary>
internal sealed class RemovableDrives
{
    /// <summary>The device to eject for a volume, and its name for a volume without a label.</summary>
    private sealed record Device(uint Node, string Name);

    /// <summary>A volume as last read: the device that ejects it (null for an internal disk), and the drive (null while it has no media).</summary>
    private sealed record Volume(Device? Device, RemovableDrive? Drive, long ReadAt);

    private static readonly string[] DriverSuffixes = [" SCSI Disk Device", " USB Device", " Disk Device"];
    private static readonly TimeSpan MediaCheck = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(string Root, string Volume), Volume> _volumes = new();

    public IReadOnlyList<RemovableDrive> Read()
    {
        var drives = new List<RemovableDrive>();
        var seen = new HashSet<(string, string)>();
        foreach (var (key, type) in Candidates())
        {
            seen.Add(key);
            var volume = _volumes.GetOrAdd(key, k => new Volume(Resolve(k.Root, type), null, 0));
            if (volume.Device is not { } device)
                continue;
            if (volume.ReadAt == 0
                || ((volume.Drive is null || type == DriveType.Removable) && Stopwatch.GetElapsedTime(volume.ReadAt) > MediaCheck))
                _volumes[key] = volume = volume with { Drive = Describe(key.Root, device), ReadAt = Stopwatch.GetTimestamp() };
            if (volume.Drive is { } drive)
                drives.Add(drive);
        }
        foreach (var key in _volumes.Keys)
        {
            if (!seen.Contains(key))
                _volumes.TryRemove(key, out _);
        }
        return drives;
    }

    /// <summary>"Samsung T7 (E:)" and its capacity, or null when the drive has no media (a card reader with no card).</summary>
    private static RemovableDrive? Describe(string root, Device device)
    {
        try
        {
            var info = new DriveInfo(root);
            if (!info.IsReady)
                return null;
            string label = info.VolumeLabel;
            return new RemovableDrive(root, $"{(label.Length > 0 ? label : device.Name)} ({root[..2]})", info.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // pulled out while being read
        }
    }

    /// <summary>Ejects the drive's device, as Safely Remove Hardware does; throws when Windows says no (files open on it).</summary>
    public static void Eject(RemovableDrive drive)
    {
        var type = new DriveInfo(drive.Id).DriveType;
        var device = Resolve(drive.Id, type) ?? throw new InvalidOperationException($"Eject failed: {drive.Name} is gone or can't be ejected");
        var veto = PNP_VETO_TYPE.PNP_VetoTypeUnknown;
        Span<char> vetoName = stackalloc char[260];
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(500);
            var result = PInvoke.CM_Request_Device_Eject(device.Node, out veto, vetoName, 0);
            if (result == CONFIGRET.CR_SUCCESS && veto == PNP_VETO_TYPE.PNP_VetoTypeUnknown)
                return;
            if (result != CONFIGRET.CR_SUCCESS && veto == PNP_VETO_TYPE.PNP_VetoTypeUnknown)
                throw new InvalidOperationException($"Eject failed: Windows answered {result}");
        }
        throw new InvalidOperationException($"Eject failed: {drive.Name} is in use ({veto})");
    }

    /// <summary>The lettered volumes that could be ejectable, keyed by their root and volume name (which changes with the media).</summary>
    private static IEnumerable<((string Root, string Volume) Key, DriveType Type)> Candidates()
    {
        string system = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
        Span<char> volume = stackalloc char[64];
        var found = new List<((string, string), DriveType)>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Removable or DriveType.Fixed or DriveType.CDRom)
                || string.Equals(drive.Name, system, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!PInvoke.GetVolumeNameForVolumeMountPoint(drive.Name, volume))
                continue; // a SUBST or network letter
            found.Add(((drive.Name, volume.ToString().TrimEnd('\0')), drive.DriveType));
        }
        return found;
    }

    /// <summary>
    /// The device that ejects a volume: the disk, or the nearest parent Windows marks removable (the USB device a USB disk
    /// sits on), as Safely Remove Hardware picks it. Null for an internal disk.
    /// </summary>
    private static Device? Resolve(string root, DriveType type)
    {
        using var volume = Open($@"\\.\{root[..2]}");
        if (volume.IsInvalid || DeviceNumber(volume) is not { } number)
            return null;
        if (type != DriveType.Removable
            && BusType(volume) is not (STORAGE_BUS_TYPE.BusTypeUsb or STORAGE_BUS_TYPE.BusTypeSd or STORAGE_BUS_TYPE.BusTypeMmc or STORAGE_BUS_TYPE.BusType1394))
            return null;
        uint disk = DiskNode(number);
        if (disk == 0)
            return null;
        uint node = RemovableNode(disk);
        if (node == 0 && type != DriveType.Removable)
            return null;
        return new Device(node != 0 ? node : disk, FriendlyName(disk) ?? (type == DriveType.CDRom ? "CD Drive" : "USB Drive"));
    }

    private static SafeFileHandle Open(string path) => PInvoke.CreateFile(path, 0, FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
        null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, 0, null);

    private static unsafe STORAGE_DEVICE_NUMBER? DeviceNumber(SafeFileHandle device)
    {
        STORAGE_DEVICE_NUMBER number;
        return PInvoke.DeviceIoControl(device, PInvoke.IOCTL_STORAGE_GET_DEVICE_NUMBER, default,
            new Span<byte>(&number, sizeof(STORAGE_DEVICE_NUMBER)), out _, null) ? number : null;
    }

    private static unsafe STORAGE_BUS_TYPE? BusType(SafeFileHandle device)
    {
        var query = new STORAGE_PROPERTY_QUERY
        {
            PropertyId = STORAGE_PROPERTY_ID.StorageDeviceProperty,
            QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery,
        };
        Span<byte> output = stackalloc byte[1024];
        if (!PInvoke.DeviceIoControl(device, PInvoke.IOCTL_STORAGE_QUERY_PROPERTY, new ReadOnlySpan<byte>(&query, sizeof(STORAGE_PROPERTY_QUERY)),
                output, out uint returned, null)
            || returned < (int)Marshal.OffsetOf<STORAGE_DEVICE_DESCRIPTOR>(nameof(STORAGE_DEVICE_DESCRIPTOR.RawPropertiesLength)))
            return null;
        return MemoryMarshal.Read<STORAGE_DEVICE_DESCRIPTOR>(output).BusType;
    }

    /// <summary>The disk (or CD drive) with this number, as a device node; 0 when it isn't found.</summary>
    private static uint DiskNode(STORAGE_DEVICE_NUMBER number)
    {
        var kind = number.DeviceType == (uint)FILE_DEVICE_TYPE.FILE_DEVICE_CD_ROM ? PInvoke.GUID_DEVINTERFACE_CDROM : PInvoke.GUID_DEVINTERFACE_DISK;
        foreach (string path in Interfaces(kind))
        {
            using (var handle = Open(path))
            {
                if (handle.IsInvalid || DeviceNumber(handle) is not { } candidate
                    || candidate.DeviceNumber != number.DeviceNumber || candidate.DeviceType != number.DeviceType)
                    continue;
            }
            return InstanceId(path) is { } id ? Locate(id) : 0;
        }
        return 0;
    }

    private static unsafe List<string> Interfaces(Guid kind)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (PInvoke.CM_Get_Device_Interface_List_Size(out uint length, kind, default, CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS)
                return [];
            var buffer = new char[length];
            CONFIGRET result;
            fixed (char* list = buffer)
                result = PInvoke.CM_Get_Device_Interface_List(kind, default, list, length, CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
            if (result == CONFIGRET.CR_BUFFER_SMALL)
                continue; // a device arrived between the two calls
            if (result != CONFIGRET.CR_SUCCESS)
                return [];
            return [.. new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)];
        }
        return [];
    }

    private static string? InstanceId(string interfacePath)
    {
        Span<byte> buffer = stackalloc byte[1024];
        uint size = (uint)buffer.Length;
        if (PInvoke.CM_Get_Device_Interface_Property(interfacePath, PInvoke.DEVPKEY_Device_InstanceId, out _, buffer, ref size, 0) != CONFIGRET.CR_SUCCESS)
            return null;
        return MemoryMarshal.Cast<byte, char>(buffer[..(int)size]).ToString().TrimEnd('\0');
    }

    private static unsafe uint Locate(string instanceId)
    {
        fixed (char* id = instanceId)
            return PInvoke.CM_Locate_DevNode(out uint node, id, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_NORMAL) == CONFIGRET.CR_SUCCESS ? node : 0;
    }

    /// <summary>The disk, its parent or grandparent, whichever Windows marks removable first; 0 when none is.</summary>
    private static uint RemovableNode(uint disk)
    {
        uint node = disk;
        for (int depth = 0; depth < 3; depth++)
        {
            if (PInvoke.CM_Get_DevNode_Status(out var status, out _, node, 0) == CONFIGRET.CR_SUCCESS
                && (status & CM_DEVNODE_STATUS_FLAGS.DN_REMOVABLE) != 0)
                return node;
            if (PInvoke.CM_Get_Parent(out uint parent, node, 0) != CONFIGRET.CR_SUCCESS)
                return 0;
            node = parent;
        }
        return 0;
    }

    /// <summary>The disk's name without the driver's suffix: "Samsung PSSD T7", not "Samsung PSSD T7 SCSI Disk Device".</summary>
    private static string? FriendlyName(uint disk)
    {
        Span<byte> buffer = stackalloc byte[512];
        uint size = (uint)buffer.Length;
        if (PInvoke.CM_Get_DevNode_Registry_Property(disk, PInvoke.CM_DRP_FRIENDLYNAME, out _, buffer, ref size, 0) != CONFIGRET.CR_SUCCESS)
            return null;
        string name = MemoryMarshal.Cast<byte, char>(buffer[..(int)size]).ToString().TrimEnd('\0').Trim();
        foreach (string suffix in DriverSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^suffix.Length].Trim();
        }
        return name.Length > 0 ? name : null;
    }
}
