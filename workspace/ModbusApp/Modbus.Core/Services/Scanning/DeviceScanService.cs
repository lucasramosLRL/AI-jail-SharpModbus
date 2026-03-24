using System.Net;
using System.Runtime.CompilerServices;
using Modbus.Core.Domain.Entities;
using Modbus.Core.Domain.Enums;
using Modbus.Core.Domain.ValueObjects;

namespace Modbus.Core.Services.Scanning;

public class DeviceScanService : IDeviceScanService
{
    private readonly IModbusServiceFactory _factory;

    public DeviceScanService(IModbusServiceFactory factory)
    {
        _factory = factory;
    }

    public async IAsyncEnumerable<DeviceScanResult> ScanRtuAsync(
        RtuConfig rtuConfig,
        byte startAddress,
        byte endAddress,
        IProgress<ScanProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int total = endAddress - startAddress + 1;
        int found = 0;

        var tempDevice = new ModbusDevice
        {
            Name = "scan-probe",
            SlaveId = startAddress,
            TransportType = TransportType.Rtu,
            Rtu = rtuConfig
        };

        using var service = _factory.Create(tempDevice);

        await service.ConnectAsync(cancellationToken);

        for (byte addr = startAddress; addr <= endAddress; addr++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int current = addr - startAddress + 1;
            progress?.Report(new ScanProgress
            {
                Current = current,
                Total = total,
                Found = found,
                CurrentLabel = $"Address {addr}"
            });

            DeviceScanResult? result = null;
            try
            {
                using var perAddrCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                perAddrCts.CancelAfter(TimeSpan.FromSeconds(2));

                var slaveIdData = await service.ReportSlaveIdAsync(addr, perAddrCts.Token);
                var serialNumber = await TryReadSerialNumberAsync(service, addr, cancellationToken);
                result = BuildResult(addr, slaveIdData.RawData, serialNumber, null, rtuConfig);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // per-address timeout — device not present, continue
            }
            catch (Exception)
            {
                // any other error (Modbus exception, framing error) — skip address
            }

            if (result != null)
            {
                found++;
                yield return result;
            }
        }
    }

    public async IAsyncEnumerable<DeviceScanResult> ScanTcpAsync(
        string startIp,
        string endIp,
        int port,
        IProgress<ScanProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ips = EnumerateIpRange(startIp, endIp).ToList();
        int total = ips.Count;
        int found = 0;

        for (int i = 0; i < ips.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string ip = ips[i];
            progress?.Report(new ScanProgress
            {
                Current = i + 1,
                Total = total,
                Found = found,
                CurrentLabel = ip
            });

            DeviceScanResult? result = null;
            var tcpConfig = new TcpConfig { IpAddress = ip, Port = port };
            var tempDevice = new ModbusDevice
            {
                Name = "scan-probe",
                SlaveId = 1,
                TransportType = TransportType.Tcp,
                Tcp = tcpConfig
            };

            using var service = _factory.Create(tempDevice);
            try
            {
                using var perIpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                perIpCts.CancelAfter(TimeSpan.FromMilliseconds(500));

                await service.ConnectAsync(perIpCts.Token);
                var slaveIdData = await service.ReportSlaveIdAsync(1, perIpCts.Token);
                var serialNumber = await TryReadSerialNumberAsync(service, 1, cancellationToken);
                result = BuildResult(1, slaveIdData.RawData, serialNumber, tcpConfig, null);
            }
            catch (Exception)
            {
                // not reachable or not a Modbus device — skip
            }

            if (result != null)
            {
                found++;
                yield return result;
            }
        }
    }

    private static async Task<uint?> TryReadSerialNumberAsync(
        IModbusService service, byte slaveId, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var words = await service.ReadInputRegistersAsync(slaveId, 0, 2, cts.Token);
            return (uint)(words[0] << 16) | words[1];
        }
        catch
        {
            return null;
        }
    }

    private static DeviceScanResult BuildResult(
        byte slaveId,
        byte[] rawData,
        uint? serialNumber,
        TcpConfig? tcp,
        RtuConfig? rtu)
    {
        byte? deviceCode = rawData.Length > 0 ? rawData[0] : null;
        byte? firmwareVersion = rawData.Length > 2 ? rawData[2] : null;
        string? modelName = deviceCode.HasValue ? DeviceCodeRegistry.GetModelName(deviceCode.Value) : null;

        string suggestedName = (modelName, serialNumber) switch
        {
            (not null, not null) => $"{modelName} #{serialNumber.Value:D8}",
            (not null, null)     => $"{modelName} (Slave {slaveId})",
            _                    => $"Device 0x{slaveId:X2}"
        };

        return new DeviceScanResult
        {
            SlaveId = slaveId,
            DeviceCode = deviceCode,
            ModelName = modelName,
            FirmwareVersion = firmwareVersion,
            SerialNumber = serialNumber,
            SuggestedName = suggestedName,
            Tcp = tcp,
            Rtu = rtu
        };
    }

    private static IEnumerable<string> EnumerateIpRange(string startIp, string endIp)
    {
        if (!IPAddress.TryParse(startIp, out var start) ||
            !IPAddress.TryParse(endIp, out var end))
            yield break;

        var startBytes = start.GetAddressBytes();
        var endBytes = end.GetAddressBytes();

        // Only handle /24 ranges (same first 3 octets)
        if (startBytes[0] != endBytes[0] ||
            startBytes[1] != endBytes[1] ||
            startBytes[2] != endBytes[2])
            yield break;

        byte startLast = startBytes[3];
        byte endLast = endBytes[3];

        if (startLast > endLast) yield break;

        for (byte b = startLast; b <= endLast; b++)
        {
            yield return $"{startBytes[0]}.{startBytes[1]}.{startBytes[2]}.{b}";
            if (b == 255) break; // avoid byte overflow
        }
    }
}
