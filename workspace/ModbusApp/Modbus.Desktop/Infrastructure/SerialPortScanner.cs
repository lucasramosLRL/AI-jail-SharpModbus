using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;

namespace Modbus.Desktop.Infrastructure;

internal static class SerialPortScanner
{
    // Linux paths to probe when GetPortNames() returns nothing
    private static readonly string[] LinuxPatterns =
    [
        "/dev/ttyS*",
        "/dev/ttyUSB*",
        "/dev/ttyACM*",
        "/dev/ttyAMA*",
        "/dev/rfcomm*"
    ];

    public static string[] GetPortNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return SerialPort.GetPortNames();

        // On Linux, GetPortNames() only returns ports that exist in /dev.
        // Enumerate explicitly so we can union with its results and cover all patterns.
        var ports = new SortedSet<string>(SerialPort.GetPortNames(), StringComparer.Ordinal);

        foreach (var pattern in LinuxPatterns)
        {
            var dir     = Path.GetDirectoryName(pattern)!;
            var search  = Path.GetFileName(pattern);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, search))
                ports.Add(file);
        }

        return [.. ports];
    }
}
