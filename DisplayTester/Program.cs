using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

class Program
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;

        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;

        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;

        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;

        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    const int ENUM_CURRENT_SETTINGS = -1;
    const int DISPLAY_DEVICE_ACTIVE = 0x00000001;

    static void Main()
    {
        var wmiFriendlyNames = GetWmiMonitorNames();

        uint devNum = 0;
        DISPLAY_DEVICE display = new DISPLAY_DEVICE();
        display.cb = Marshal.SizeOf(display);

        Console.WriteLine("monitors:");

        while (EnumDisplayDevices(null, devNum, ref display, 0))
        {
            string displayName = display.DeviceName;

            DISPLAY_DEVICE monitor = new DISPLAY_DEVICE();
            monitor.cb = Marshal.SizeOf(monitor);
            string monitorDeviceId = "";
            string friendlyName = "";

            // Try to get monitor info for this display
            if (EnumDisplayDevices(displayName, 0, ref monitor, 0))
            {
                monitorDeviceId = monitor.DeviceID; // MONITOR\...
                friendlyName = MatchWmiName(wmiFriendlyNames, monitorDeviceId);
            }

            bool isActive = (display.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0;

            uint maxWidth = 0, maxHeight = 0, bestFreq = 0;
            bool gotMode = false;
            DEVMODE mode = new DEVMODE();
            mode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));

            if (isActive)
            {
                gotMode = EnumDisplaySettings(displayName, ENUM_CURRENT_SETTINGS, ref mode);
                if (gotMode)
                {
                    maxWidth = mode.dmPelsWidth;
                    maxHeight = mode.dmPelsHeight;
                    bestFreq = mode.dmDisplayFrequency;
                }
            }
            else
            {
                int iMode = 0;
                while (EnumDisplaySettings(displayName, iMode, ref mode))
                {
                    uint area = mode.dmPelsWidth * mode.dmPelsHeight;
                    uint bestArea = maxWidth * maxHeight;

                    if (area > bestArea || (area == bestArea && mode.dmDisplayFrequency > bestFreq))
                    {
                        maxWidth = mode.dmPelsWidth;
                        maxHeight = mode.dmPelsHeight;
                        bestFreq = mode.dmDisplayFrequency;
                        gotMode = true;
                    }

                    iMode++;
                }
            }

            Console.WriteLine($"  - name: \"{display.DeviceName}\"");
            Console.WriteLine($"    description: \"{display.DeviceString}\"");
            Console.WriteLine($"    device_id: \"{display.DeviceID}\"");
            Console.WriteLine($"    monitor_id: \"{monitorDeviceId}\"");
            Console.WriteLine($"    friendly_name: \"{friendlyName}\"");
            Console.WriteLine($"    active: {isActive.ToString().ToLower()}");

            if (gotMode)
            {
                Console.WriteLine($"    resolution:");
                Console.WriteLine($"      width: {maxWidth}");
                Console.WriteLine($"      height: {maxHeight}");
                Console.WriteLine($"      frequency: {bestFreq}");
            }
            else
            {
                Console.WriteLine("    resolution: null");
            }

            Console.WriteLine();
            devNum++;
            display.cb = Marshal.SizeOf(display);
        }
    }

    static Dictionary<string, string> GetWmiMonitorNames()
    {
        var map = new Dictionary<string, string>();
        var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorID");

        foreach (ManagementObject obj in searcher.Get())
        {
            string instance = (string)obj["InstanceName"];
            string name = DecodeEdidString((ushort[])obj["UserFriendlyName"]);
            map[instance] = name;
        }

        return map;
    }

    static string MatchWmiName(Dictionary<string, string> wmiMap, string monitorDeviceId)
    {
        if (string.IsNullOrWhiteSpace(monitorDeviceId))
            return "";

        // Example: MONITOR\VSCB931\7&73cdc58&0&UID20738
        var parts = monitorDeviceId.Split("\\");
        string idCore = "";
        if(parts.Length > 1)
        {
            idCore = parts[1];
        }
        else
        {
            return "";
        }
            foreach (var kvp in wmiMap)
            {
                var key = kvp.Key;
                if (key.ToLower().StartsWith($"DISPLAY\\{idCore}", StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

        return "";
    }

    static string DecodeEdidString(ushort[] chars)
    {
        if (chars == null) return "";
        var sb = new StringBuilder();
        foreach (var c in chars)
        {
            if (c == 0) break;
            sb.Append((char)c);
        }
        return sb.ToString().Trim();
    }
}
