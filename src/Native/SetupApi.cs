// Copyright (c) Benjamin Maurin (njz3) 2026

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SuperResScaler.Native;

/// <summary>
/// Creates and removes root-enumerated devices, as "devcon install" and "devcon remove" do.
/// Creating and removing need administrator rights; listing does not.
/// </summary>
internal static class SetupApi
{
    public static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");

    private const int DIGCF_PRESENT = 0x2;
    private const int DICD_GENERATE_ID = 0x1;
    private const int SPDRP_HARDWAREID = 0x1;
    private const int DIF_REMOVE = 0x05;
    private const int DIF_REGISTERDEVICE = 0x19;
    private const int INSTALLFLAG_FORCE = 0x1;
    private const int CM_GET_DEVICE_ID_BUFFER = 200;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    /// <summary>Instance ids of the devices of a class with this hardware id (present ones, or all known ones).</summary>
    public static List<string> FindDevices(Guid deviceClass, string hardwareId, bool presentOnly)
    {
        var result = new List<string>();
        ForEachDevice(deviceClass, hardwareId, presentOnly, (_, data) =>
        {
            var id = new StringBuilder(CM_GET_DEVICE_ID_BUFFER);
            if (CM_Get_Device_IDW(data.DevInst, id, id.Capacity, 0) == 0)
                result.Add(id.ToString());
        });
        return result;
    }

    /// <summary>
    /// The Device Manager problem code of the present devices with this hardware id (0 = working;
    /// 43 = stopped by Windows, e.g. after its user-mode driver crashed too often).
    /// </summary>
    public static List<int> FindProblems(Guid deviceClass, string hardwareId)
    {
        var result = new List<int>();
        ForEachDevice(deviceClass, hardwareId, presentOnly: true, (_, data) =>
        {
            if (CM_Get_DevNode_Status(out int status, out int problem, data.DevInst, 0) == 0)
                result.Add((status & DN_HAS_PROBLEM) != 0 ? problem : 0);
        });
        return result;
    }

    /// <summary>Creates a root device with this hardware id and installs the driver of <paramref name="infPath"/> on it.</summary>
    /// <returns>True when Windows asks for a restart.</returns>
    public static bool CreateRootDevice(Guid deviceClass, string className, string hardwareId, string infPath)
    {
        IntPtr set = SetupDiCreateDeviceInfoList(ref deviceClass, IntPtr.Zero);
        if (set == InvalidHandle)
            throw Win32("SetupDiCreateDeviceInfoList");
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            if (!SetupDiCreateDeviceInfoW(set, className, ref deviceClass, null, IntPtr.Zero, DICD_GENERATE_ID, ref data))
                throw Win32("SetupDiCreateDeviceInfo");
            byte[] ids = Encoding.Unicode.GetBytes(hardwareId + "\0\0");
            if (!SetupDiSetDeviceRegistryPropertyW(set, ref data, SPDRP_HARDWAREID, ids, ids.Length))
                throw Win32("SetupDiSetDeviceRegistryProperty");
            if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, set, ref data))
                throw Win32("DIF_REGISTERDEVICE");
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        if (!UpdateDriverForPlugAndPlayDevicesW(IntPtr.Zero, hardwareId, infPath, INSTALLFLAG_FORCE, out bool reboot))
            throw Win32("UpdateDriverForPlugAndPlayDevices");
        return reboot;
    }

    /// <summary>Removes every device of the class with this hardware id, present or not. Returns how many.</summary>
    public static int RemoveDevices(Guid deviceClass, string hardwareId)
    {
        int removed = 0;
        Exception? error = null;
        ForEachDevice(deviceClass, hardwareId, presentOnly: false, (set, data) =>
        {
            if (SetupDiCallClassInstaller(DIF_REMOVE, set, ref data))
                removed++;
            else
                error ??= Win32("DIF_REMOVE");
        });
        if (removed == 0 && error != null)
            throw error;
        return removed;
    }

    private delegate void DeviceAction(IntPtr set, SP_DEVINFO_DATA data);

    private static void ForEachDevice(Guid deviceClass, string hardwareId, bool presentOnly, DeviceAction action)
    {
        IntPtr set = SetupDiGetClassDevsW(ref deviceClass, null, IntPtr.Zero, presentOnly ? DIGCF_PRESENT : 0);
        if (set == InvalidHandle)
            throw Win32("SetupDiGetClassDevs");
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (int index = 0; SetupDiEnumDeviceInfo(set, index, ref data); index++)
            {
                if (ReadHardwareIds(set, ref data).Contains(hardwareId, StringComparer.OrdinalIgnoreCase))
                    action(set, data);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string[] ReadHardwareIds(IntPtr set, ref SP_DEVINFO_DATA data)
    {
        var buffer = new byte[1024];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, SPDRP_HARDWAREID, out _, buffer, buffer.Length, out int required))
        {
            if (required <= buffer.Length)
                return [];
            buffer = new byte[required];
            if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, SPDRP_HARDWAREID, out _, buffer, buffer.Length, out required))
                return [];
        }
        return Encoding.Unicode.GetString(buffer, 0, Math.Min(required, buffer.Length))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static Win32Exception Win32(string function)
    {
        int code = Marshal.GetLastWin32Error();
        return new Win32Exception(code, $"{function} : {new Win32Exception(code).Message} (0x{code:X8})");
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiCreateDeviceInfoW(IntPtr set, string name, ref Guid classGuid, string? description,
        IntPtr hwndParent, int flags, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiSetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, int property, byte[] buffer, int size);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, int property,
        out int registryType, byte[] buffer, int size, out int required);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(int function, IntPtr set, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("newdev.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UpdateDriverForPlugAndPlayDevicesW(IntPtr hwndParent, string hardwareId, string infPath, int flags, out bool reboot);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(int devInst, StringBuilder buffer, int length, int flags);

    private const int DN_HAS_PROBLEM = 0x00000400;

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out int status, out int problem, int devInst, int flags);
}
