using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.HardwareAdapter.ZlgUsbCanFd;

public sealed record ZlgUsbCanFdOptions(
    int DeviceIndex,
    string BusMode,
    int DataBaudRate,
    string CanFdStandard,
    bool EnableInternalTermination,
    int TransmitTimeoutMilliseconds);

/// <summary>
/// 周立功 USBCANFD-200U 的原始 CAN/CAN FD 传输实现。
/// 该类只负责厂家 SDK 的设备、通道和报文收发，不解释电机 DBC/字节协议。
/// </summary>
public sealed class ZlgUsbCanFdTransport : ICanTransport
{
    private readonly ZlgUsbCanFdOptions _options;
    private readonly ConcurrentDictionary<int, IntPtr> _channels = new();
    private readonly ConcurrentDictionary<int, int> _channelBaudRates = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IntPtr _deviceHandle;
    private CancellationTokenSource? _receiveCancellation;
    private Task? _receiveTask;
    private bool _disposed;

    public ZlgUsbCanFdTransport(ZlgUsbCanFdOptions options) => _options = options;

    public bool IsConnected { get; private set; }
    public string DeviceSummary { get; private set; } = CanHardwareBaseline.DisplayName;
    public string? LastError { get; private set; }
    public IReadOnlyCollection<int> ConnectedChannels => _channels.Keys.Order().ToArray();

    public event EventHandler<CanFrame>? FrameReceived;
    public event EventHandler? ConnectionStateChanged;

    public async Task ConnectAsync(int channel, int baudRate, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateOptions(channel, baudRate);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channels.TryGetValue(channel, out _))
            {
                if (_channelBaudRates[channel] != baudRate)
                    throw new InvalidOperationException($"CAN{channel} 已按 {_channelBaudRates[channel]} bps 初始化，不能在运行中改为 {baudRate} bps。");
                return;
            }

            EnsureNativeLibraryPresent();
            OpenDeviceIfRequired();
            ConfigureAndStartChannel(channel, baudRate);
            IsConnected = true;
            LastError = null;
            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
            StartReceiveLoopIfRequired();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            LastError = BuildNativeFailureMessage(ex);
            await CloseDeviceWhenNoChannelAsync().ConfigureAwait(false);
            throw new InvalidOperationException(LastError, ex);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            await CloseDeviceWhenNoChannelAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SendAsync(CanFrame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected || !_channels.TryGetValue(frame.Channel, out var channelHandle))
            throw new InvalidOperationException($"USBCANFD-200U 的 CAN{frame.Channel} 尚未连接。");

        var id = frame.Id & ZlgCanNative.CanIdMask;
        if (frame.IsExtended) id |= ZlgCanNative.ExtendedFrameFlag;

        if (frame.IsFd || frame.Data.Length > 8)
        {
            if (!string.Equals(_options.BusMode, "CAN FD", StringComparison.Ordinal))
                throw new InvalidOperationException("当前通道配置为 CAN 2.0，不能发送 CAN FD 报文。");
            if (frame.Data.Length > 64) throw new ArgumentOutOfRangeException(nameof(frame), "CAN FD 数据长度不能超过 64 字节。");

            var nativeFrame = ZcanCanFdFrame.Create();
            nativeFrame.CanId = id;
            nativeFrame.Length = checked((byte)frame.Data.Length);
            nativeFrame.Flags = frame.BitRateSwitch ? ZlgCanNative.CanFdBitRateSwitch : (byte)0;
            Array.Copy(frame.Data, nativeFrame.Data, frame.Data.Length);
            var transmit = new ZcanTransmitFdData { Frame = nativeFrame, TransmitType = 0 };
            if (ZlgCanNative.ZCAN_TransmitFD(channelHandle, ref transmit, 1) != 1)
                throw new IOException($"USBCANFD-200U CAN{frame.Channel} 发送 CAN FD 报文失败。");
        }
        else
        {
            if (frame.Data.Length > 8) throw new ArgumentOutOfRangeException(nameof(frame), "经典 CAN 数据长度不能超过 8 字节。");
            var nativeFrame = ZcanCanFrame.Create();
            nativeFrame.CanId = id;
            nativeFrame.DataLength = checked((byte)frame.Data.Length);
            Array.Copy(frame.Data, nativeFrame.Data, frame.Data.Length);
            var transmit = new ZcanTransmitData { Frame = nativeFrame, TransmitType = 0 };
            if (ZlgCanNative.ZCAN_Transmit(channelHandle, ref transmit, 1) != 1)
                throw new IOException($"USBCANFD-200U CAN{frame.Channel} 发送经典 CAN 报文失败。");
        }

        return Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_receiveCancellation is not null)
            {
                await _receiveCancellation.CancelAsync().ConfigureAwait(false);
                if (_receiveTask is not null)
                {
                    try { await _receiveTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                _receiveCancellation.Dispose();
                _receiveCancellation = null;
                _receiveTask = null;
            }

            foreach (var handle in _channels.Values)
            {
                if (handle == IntPtr.Zero) continue;
                ZlgCanNative.ZCAN_ClearBuffer(handle);
                ZlgCanNative.ZCAN_ResetCAN(handle);
            }
            _channels.Clear();
            _channelBaudRates.Clear();

            if (_deviceHandle != IntPtr.Zero)
            {
                ZlgCanNative.ZCAN_CloseDevice(_deviceHandle);
                _deviceHandle = IntPtr.Zero;
            }
            IsConnected = false;
            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await DisconnectAsync().ConfigureAwait(false);
        _gate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ValidateOptions(int channel, int baudRate)
    {
        ValidateInteropLayout();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("周立功 Windows SDK 只能在 Windows 上使用。");
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("本项目仅集成周立功 x64 SDK，请以 x64 进程运行。");
        if (channel is < 0 or >= CanHardwareBaseline.ChannelCount) throw new ArgumentOutOfRangeException(nameof(channel), "USBCANFD-200U 只有 CAN0、CAN1 两个通道。");
        if (!CanHardwareBaseline.SupportedArbitrationBaudRates.Contains(baudRate)) throw new ArgumentOutOfRangeException(nameof(baudRate), "仲裁域波特率不受当前官方标准参数支持。");
        if (_options.DeviceIndex is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(_options.DeviceIndex));
        if (_options.BusMode is not ("CAN 2.0" or "CAN FD")) throw new ArgumentException("总线模式只能是 CAN 2.0 或 CAN FD。");
        if (!CanHardwareBaseline.SupportedDataBaudRates.Contains(_options.DataBaudRate))
            throw new ArgumentOutOfRangeException(nameof(_options.DataBaudRate), "数据域波特率不受当前官方标准参数支持。");
        if (_options.CanFdStandard is not ("ISO" or "Non-ISO"))
            throw new ArgumentException("CAN FD 标准只能是 ISO 或 Non-ISO。", nameof(_options.CanFdStandard));
        if (_options.TransmitTimeoutMilliseconds is < 1 or > 4000) throw new ArgumentOutOfRangeException(nameof(_options.TransmitTimeoutMilliseconds));
    }

    /// <summary>
    /// 在调用厂家 DLL 前核对托管结构与周立功 zlgcan.h 的 x64 内存布局，避免字段偏移错误造成越界或错误报文。
    /// </summary>
    public static void ValidateInteropLayout()
    {
        var expectedSizes = new (Type Type, int Size)[]
        {
            (typeof(ZcanDeviceInfo), 80),
            (typeof(ZcanChannelInitConfig), 32),
            (typeof(ZcanCanFrame), 16),
            (typeof(ZcanCanFdFrame), 72),
            (typeof(ZcanTransmitData), 20),
            (typeof(ZcanReceiveData), 24),
            (typeof(ZcanTransmitFdData), 76),
            (typeof(ZcanReceiveFdData), 80)
        };

        foreach (var (type, expected) in expectedSizes)
        {
            var actual = Marshal.SizeOf(type);
            if (actual != expected)
                throw new PlatformNotSupportedException($"ZLGCAN 结构体 {type.Name} 大小异常：期望 {expected} 字节，实际 {actual} 字节。");
        }
    }

    private static void EnsureNativeLibraryPresent()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ZlgCanNative.LibraryName);
        if (!File.Exists(path))
            throw new DllNotFoundException($"未找到 {path}。请从周立功官方 ZLGCAN x64 接口库复制 zlgcan.dll 和 kerneldlls 目录。");
    }

    private void OpenDeviceIfRequired()
    {
        if (_deviceHandle != IntPtr.Zero) return;
        _deviceHandle = ZlgCanNative.ZCAN_OpenDevice(
            ZlgCanNative.DeviceTypeUsbCanFd200U,
            checked((uint)_options.DeviceIndex),
            0);
        if (_deviceHandle == IntPtr.Zero)
            throw new IOException($"无法打开 USBCANFD-200U（USB 设备索引 {_options.DeviceIndex}）。请检查 USB 线、PWR/SYS 指示灯、驱动和是否被 ZCANPRO 占用。");

        var info = ZcanDeviceInfo.Create();
        if (ZlgCanNative.ZCAN_GetDeviceInf(_deviceHandle, ref info) == ZlgCanNative.StatusOk)
        {
            var serial = DecodeCString(info.SerialNumber);
            DeviceSummary = $"{CanHardwareBaseline.Model} · SN {(string.IsNullOrWhiteSpace(serial) ? "未知" : serial)} · {info.CanChannelCount} 路 CAN";
        }
    }

    private void ConfigureAndStartChannel(int channel, int baudRate)
    {
        SetValue(channel, "protocol", string.Equals(_options.BusMode, "CAN FD", StringComparison.Ordinal) ? "1" : "0");
        if (string.Equals(_options.BusMode, "CAN FD", StringComparison.Ordinal))
        {
            SetValue(channel, "canfd_standard", string.Equals(_options.CanFdStandard, "Non-ISO", StringComparison.Ordinal) ? "1" : "0");
            SetValue(channel, "canfd_dbit_baud_rate", _options.DataBaudRate.ToString());
        }
        SetValue(channel, "canfd_abit_baud_rate", baudRate.ToString());

        var config = new ZcanChannelInitConfig
        {
            CanType = ZlgCanNative.CanControllerTypeCanFd,
            CanFd = new ZcanCanFdInitConfig
            {
                AcceptanceCode = 0,
                AcceptanceMask = 0,
                Filter = 0,
                Mode = 0
            }
        };
        var channelHandle = ZlgCanNative.ZCAN_InitCAN(_deviceHandle, checked((uint)channel), ref config);
        if (channelHandle == IntPtr.Zero) throw new IOException($"USBCANFD-200U CAN{channel} 初始化失败。");

        try
        {
            SetValue(channel, "initenal_resistance", _options.EnableInternalTermination ? "1" : "0");
            SetValue(channel, "tx_timeout", _options.TransmitTimeoutMilliseconds.ToString());
            if (ZlgCanNative.ZCAN_StartCAN(channelHandle) != ZlgCanNative.StatusOk)
                throw new IOException($"USBCANFD-200U CAN{channel} 启动失败。");
            _channels[channel] = channelHandle;
            _channelBaudRates[channel] = baudRate;
        }
        catch
        {
            ZlgCanNative.ZCAN_ResetCAN(channelHandle);
            throw;
        }
    }

    private void SetValue(int channel, string key, string value)
    {
        var path = $"{channel}/{key}";
        if (ZlgCanNative.ZCAN_SetValue(_deviceHandle, path, value) != ZlgCanNative.StatusOk)
            throw new IOException($"USBCANFD-200U 属性设置失败：{path}={value}。");
    }

    private void StartReceiveLoopIfRequired()
    {
        if (_receiveTask is not null) return;
        _receiveCancellation = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCancellation.Token));
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var lastOnlineCheck = DateTime.UtcNow;
        while (!cancellationToken.IsCancellationRequested)
        {
            var receivedAny = false;
            try
            {
                foreach (var pair in _channels.ToArray())
                {
                    receivedAny |= ReceiveClassic(pair.Key, pair.Value);
                    receivedAny |= ReceiveCanFd(pair.Key, pair.Value);
                }

                if (DateTime.UtcNow - lastOnlineCheck >= TimeSpan.FromSeconds(1))
                {
                    lastOnlineCheck = DateTime.UtcNow;
                    if (_deviceHandle != IntPtr.Zero && ZlgCanNative.ZCAN_IsDeviceOnLine(_deviceHandle) != ZlgCanNative.StatusOnline)
                    {
                        IsConnected = false;
                        LastError = "USBCANFD-200U USB 连接已断开。";
                        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                LastError = $"USBCANFD-200U 接收线程异常：{ex.Message}";
                ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!receivedAny) await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool ReceiveClassic(int channel, IntPtr handle)
    {
        var receive = ZcanReceiveData.Create();
        if (ZlgCanNative.ZCAN_Receive(handle, ref receive, 1, 0) != 1) return false;
        var length = Math.Min(receive.Frame.DataLength, (byte)8);
        var data = receive.Frame.Data.Take(length).ToArray();
        RaiseFrame(receive.Frame.CanId, data, channel, isFd: false, bitRateSwitch: false,
            receive.TimestampMicroseconds);
        return true;
    }

    private bool ReceiveCanFd(int channel, IntPtr handle)
    {
        var receive = ZcanReceiveFdData.Create();
        if (ZlgCanNative.ZCAN_ReceiveFD(handle, ref receive, 1, 0) != 1) return false;
        var length = Math.Min(receive.Frame.Length, (byte)64);
        var data = receive.Frame.Data.Take(length).ToArray();
        RaiseFrame(receive.Frame.CanId, data, channel, isFd: true,
            bitRateSwitch: (receive.Frame.Flags & ZlgCanNative.CanFdBitRateSwitch) != 0,
            receive.TimestampMicroseconds);
        return true;
    }

    private void RaiseFrame(
        uint nativeId,
        byte[] data,
        int channel,
        bool isFd,
        bool bitRateSwitch,
        ulong hardwareTimestampMicroseconds)
    {
        var extended = (nativeId & ZlgCanNative.ExtendedFrameFlag) != 0;
        var id = nativeId & ZlgCanNative.CanIdMask;
        FrameReceived?.Invoke(this, new CanFrame(
            id,
            data,
            DateTime.UtcNow,
            extended,
            channel,
            isFd,
            bitRateSwitch,
            hardwareTimestampMicroseconds,
            nativeId));
    }

    private async Task CloseDeviceWhenNoChannelAsync()
    {
        if (!_channels.IsEmpty || _deviceHandle == IntPtr.Zero) return;
        await Task.Yield();
        ZlgCanNative.ZCAN_CloseDevice(_deviceHandle);
        _deviceHandle = IntPtr.Zero;
        IsConnected = false;
    }

    private static string DecodeCString(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0) length = bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, length).Trim();
    }

    private static string BuildNativeFailureMessage(Exception exception) => exception switch
    {
        BadImageFormatException => "zlgcan.dll 位数不匹配。本项目必须使用周立功官方 x64 接口库并以 x64 运行。",
        EntryPointNotFoundException => "zlgcan.dll 版本不兼容，缺少当前适配器需要的 ZLGCAN API。",
        _ => "无法加载周立功 zlgcan.dll 或其 kerneldlls 依赖，请安装官方驱动并保持 SDK 目录结构完整。"
    };
}
