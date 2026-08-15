using System.Diagnostics;
using System.Text;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.HardwareAdapter.XinChaoRenDaPcie1604;

/// <summary>
/// 新超仁达 PCIE-1604 的低速耐久试验采集实现。厂家 SDK 的单点扫描在独立后台任务中执行，
/// UI/试验状态机只读取不可变快照，避免逐工位直接调用厂家 DLL。
/// </summary>
public sealed class Pcie1604Acquisition : IAnalogAcquisition
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _snapshotGate = new();
    private Pcie1604Native.BoardInfo _board;
    private AnalogAcquisitionConfiguration? _configuration;
    private Pcie1604SdkCompatibility? _compatibility;
    private CancellationTokenSource? _scanCancellation;
    private Task? _scanTask;
    private TaskCompletionSource<AnalogSnapshot>? _firstSnapshot;
    private AnalogSnapshot? _latest;
    private long _latestMonotonicTimestamp;
    private bool _boardOpened;
    private int _connected;
    private int _disposeStarted;
    private long _sequence;
    private string? _lastError;

    public bool IsConnected => Volatile.Read(ref _connected) != 0;
    public string DeviceSummary { get; private set; } = $"{AnalogHardwareBaseline.DisplayName} · {AnalogHardwareBaseline.TerminalModel}";
    public string? LastError => Volatile.Read(ref _lastError);

    public async Task ConnectAsync(AnalogAcquisitionConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposeStarted();
        ValidateConfiguration(configuration);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposeStarted();
            await DisconnectCoreAsync().ConfigureAwait(false);
            EnsureRuntimePrerequisites();
            var compatibility = Pcie1604SdkCompatibility.LoadAndValidate(AppContext.BaseDirectory);
            Pcie1604Native.ValidateManagedLayout(compatibility);

            _configuration = configuration;
            _compatibility = compatibility;
            _board = Pcie1604Native.BoardInfo.Create(configuration.BoardId);
            var openResult = Pcie1604Native.DeviceOpen(ref _board);
            if (openResult == -1)
                throw new IOException("PCIE-1604 打开失败：" + Pcie1604Native.DescribeError(openResult));
            if (_board.Handle == 0)
                throw new IOException("PCIE-1604 DeviceOpen 未返回有效板卡句柄；为避免访问错误设备，连接已终止。");
            _boardOpened = true;

            // DeviceOpen/Connect must not reset all board I/O implicitly. BoardReset may alter DO/DA state;
            // use it only in an explicit maintenance operation after the site safety design is confirmed.
            var parameters = BuildParameters(configuration, compatibility);
            EnsureSuccess(Pcie1604Native.AdSetParameters(ref _board, ref parameters), "设置 AD 参数", compatibility);
            EnsureSuccess(Pcie1604Native.AdClearFifo(ref _board), "清空 AD FIFO", compatibility);

            var dllVersion = ReadDllVersion(compatibility);
            DeviceSummary = $"{AnalogHardwareBaseline.DisplayName} · ID {configuration.BoardId} · {_board.BoardVersion} · DLL {dllVersion}".Trim(' ', '·');
            Volatile.Write(ref _lastError, null);
            lock (_snapshotGate)
            {
                _latest = null;
                _latestMonotonicTimestamp = 0;
            }
            _sequence = 0;
            _firstSnapshot = new TaskCompletionSource<AnalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            _scanCancellation = new CancellationTokenSource();
            Volatile.Write(ref _connected, 1);
            _scanTask = Task.Run(
                () => ScanLoopAsync(configuration, compatibility, _scanCancellation.Token),
                CancellationToken.None);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(configuration.ReadTimeoutMilliseconds);
            await _firstSnapshot.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            if (!IsConnected)
                throw new IOException("PCIE-1604 在首个有效快照产生后立即离线：" + (LastError ?? "未知错误"));
            ThrowIfDisposeStarted();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            Volatile.Write(ref _lastError, BuildNativeFailureMessage(ex));
            await DisconnectCoreAsync().ConfigureAwait(false);
            throw new InvalidOperationException(LastError, ex);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex.Message);
            await DisconnectCoreAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task<AnalogSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposeStarted();
        cancellationToken.ThrowIfCancellationRequested();
        var backgroundError = LastError;
        if (!string.IsNullOrWhiteSpace(backgroundError))
            throw new IOException("PCIE-1604 后台采集异常：" + backgroundError);
        var configuration = _configuration;
        if (!IsConnected || configuration is null)
            throw new InvalidOperationException("PCIE-1604 尚未连接。");

        AnalogSnapshot? snapshot;
        long monotonicTimestamp;
        lock (_snapshotGate)
        {
            snapshot = _latest;
            monotonicTimestamp = _latestMonotonicTimestamp;
        }
        if (snapshot is null)
            throw new IOException("PCIE-1604 尚未产生有效采样快照。");
        if (monotonicTimestamp == 0 ||
            Stopwatch.GetElapsedTime(monotonicTimestamp).TotalMilliseconds > configuration.ReadTimeoutMilliseconds)
            throw new TimeoutException($"PCIE-1604 采样已停滞，最后数据时间：{snapshot.Timestamp:HH:mm:ss.fff}。");
        return Task.FromResult(snapshot);
    }

    public async Task DisconnectAsync()
    {
        if (Volatile.Read(ref _disposeStarted) != 0) return;
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try { await DisconnectCoreAsync().ConfigureAwait(false); }
        finally { _lifecycleGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try { await DisconnectCoreAsync().ConfigureAwait(false); }
        finally
        {
            _lifecycleGate.Release();
        }
        GC.SuppressFinalize(this);
    }

    private async Task ScanLoopAsync(
        AnalogAcquisitionConfiguration configuration,
        Pcie1604SdkCompatibility compatibility,
        CancellationToken cancellationToken)
    {
        try
        {
            var firstChannel = configuration.Channels.Min(x => x.Channel);
            var lastPhysicalChannel = configuration.Channels.Max(x =>
                x.Differential ? x.Channel + 1 : x.Channel);
            var physicalChannelCount = lastPhysicalChannel - firstChannel + 1;
            var buffer = new byte[physicalChannelCount * 2];
            var histories = configuration.Channels.ToDictionary(
                x => x.Channel,
                _ => new Queue<double>(configuration.FilterWindow));
            var period = TimeSpan.FromSeconds(1.0 / configuration.SampleRateHz);

            while (!cancellationToken.IsCancellationRequested)
            {
                var started = Stopwatch.GetTimestamp();
                var voltages = await ReadOneScanAsync(
                    configuration,
                    compatibility,
                    firstChannel,
                    physicalChannelCount,
                    buffer,
                    histories,
                    cancellationToken).ConfigureAwait(false);

                EnsureSuccess(
                    Pcie1604Native.DigitalInputGetWord(ref _board, 0, out var digitalInputs),
                    "读取 DI",
                    compatibility);
                var snapshot = new AnalogSnapshot(DateTime.Now, voltages, digitalInputs, Interlocked.Increment(ref _sequence));
                lock (_snapshotGate)
                {
                    _latest = snapshot;
                    _latestMonotonicTimestamp = Stopwatch.GetTimestamp();
                }
                _firstSnapshot?.TrySetResult(snapshot);
                Volatile.Write(ref _lastError, null);

                var spent = Stopwatch.GetElapsedTime(started);
                var remaining = period - spent;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex.Message);
            Volatile.Write(ref _connected, 0);
            _firstSnapshot?.TrySetException(ex);
        }
    }

    private async Task<IReadOnlyDictionary<int, double>> ReadOneScanAsync(
        AnalogAcquisitionConfiguration configuration,
        Pcie1604SdkCompatibility compatibility,
        int firstChannel,
        int physicalChannelCount,
        byte[] buffer,
        Dictionary<int, Queue<double>> histories,
        CancellationToken cancellationToken)
    {
        EnsureSuccess(Pcie1604Native.AdSetWorkStatus(ref _board, 3), "启动 AD 单点扫描", compatibility);
        var timeout = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSuccess(
                Pcie1604Native.AdGetFifoStatus(ref _board, out var status),
                "读取 AD FIFO 状态",
                compatibility);
            if (status == 4) break;
            if (status == 3) throw new IOException("PCIE-1604 AD FIFO 已满，本轮数据完整性无法保证。");
            if (status > 4) throw new IOException($"PCIE-1604 返回未定义的 AD FIFO 状态：{status}。");
            if (timeout.ElapsedMilliseconds >= configuration.ReadTimeoutMilliseconds)
                throw new TimeoutException("PCIE-1604 等待单点采样完成超时。");
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }

        uint bufferSize = checked((uint)buffer.Length);
        EnsureSuccess(
            Pcie1604Native.AdGetSingleData(ref _board, buffer, ref bufferSize),
            "读取 AD 单点数据",
            compatibility);
        if (bufferSize != buffer.Length)
            throw new IOException($"PCIE-1604 返回数据长度异常：期望 {buffer.Length} 字节，实际 {bufferSize} 字节。");

        var values = new Dictionary<int, double>(configuration.Channels.Count);
        foreach (var request in configuration.Channels)
        {
            var index = request.Channel - firstChannel;
            var raw = compatibility.DecodeSingleSample(buffer[index * 2], buffer[index * 2 + 1]);
            var voltage = (double)raw;
            EnsureSuccess(
                Pcie1604Native.AdConvertToVoltage(
                    ref _board,
                    checked((byte)compatibility.GetRangeCode(request.Range)),
                    ref voltage),
                $"转换 AI{request.Channel} 电压",
                compatibility);

            var history = histories[request.Channel];
            history.Enqueue(voltage);
            while (history.Count > configuration.FilterWindow) history.Dequeue();
            values[request.Channel] = history.Average();
        }
        return values;
    }

    private static Pcie1604Native.AdParameters BuildParameters(
        AnalogAcquisitionConfiguration configuration,
        Pcie1604SdkCompatibility compatibility)
    {
        var parameters = Pcie1604Native.AdParameters.Create();
        var firstChannel = configuration.Channels.Min(x => x.Channel);
        var lastChannel = configuration.Channels.Max(x => x.Differential ? x.Channel + 1 : x.Channel);
        parameters.StartChannel = checked((byte)firstChannel);
        parameters.ChannelCount = checked((byte)(lastChannel - firstChannel + 1));
        parameters.SampleFrequency = compatibility.ToDriverFrequency(configuration.SampleRateHz, parameters.ChannelCount);
        parameters.Mode = 0x01; // 软件触发、内部时钟。

        foreach (var request in configuration.Channels)
        {
            var rangeCode = checked((uint)compatibility.GetRangeCode(request.Range));
            parameters.Gains[request.Channel] = rangeCode;
            if (!request.Differential) continue;
            parameters.Gains[request.Channel + 1] = rangeCode;
            parameters.DifferentialFlags[request.Channel] = 1;
            parameters.DifferentialFlags[request.Channel + 1] = 1;
        }
        return parameters;
    }

    private async Task DisconnectCoreAsync()
    {
        Volatile.Write(ref _connected, 0);
        if (_scanCancellation is not null)
        {
            await _scanCancellation.CancelAsync().ConfigureAwait(false);
            if (_scanTask is not null)
            {
                try { await _scanTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { AppendLastError($"后台采集任务退出异常：{ex.Message}"); }
            }
            _scanCancellation.Dispose();
        }
        _scanCancellation = null;
        _scanTask = null;
        _firstSnapshot = null;
        lock (_snapshotGate)
        {
            _latest = null;
            _latestMonotonicTimestamp = 0;
        }

        if (_boardOpened)
        {
            try
            {
                var stopResult = Pcie1604Native.AdSetWorkStatus(ref _board, 0);
                if (_compatibility is not null && stopResult != _compatibility.ApiSuccessCode)
                    AppendLastError("停止 AD 失败：" + Pcie1604Native.DescribeError(stopResult));
            }
            catch (Exception ex)
            {
                AppendLastError($"停止 AD 时原生调用异常：{ex.Message}");
            }
            finally
            {
                try
                {
                    var closeResult = Pcie1604Native.DeviceClose(ref _board);
                    if (_compatibility is not null && closeResult != _compatibility.ApiSuccessCode)
                        AppendLastError("关闭板卡失败：" + Pcie1604Native.DescribeError(closeResult));
                }
                catch (Exception ex)
                {
                    AppendLastError($"关闭板卡时原生调用异常：{ex.Message}");
                }
                _boardOpened = false;
                _board.Handle = 0;
            }
        }
        _configuration = null;
        _compatibility = null;
    }

    private static void ValidateConfiguration(AnalogAcquisitionConfiguration configuration)
    {
        if (configuration.BoardId is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(configuration.BoardId));
        if (configuration.SampleRateHz is < 1 or > AnalogHardwareBaseline.MaximumSoftwareScanRateHz)
            throw new ArgumentOutOfRangeException(nameof(configuration.SampleRateHz));
        if (configuration.ReadTimeoutMilliseconds is < 50 or > 30_000)
            throw new ArgumentOutOfRangeException(nameof(configuration.ReadTimeoutMilliseconds));
        if (configuration.FilterWindow is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(configuration.FilterWindow));
        if (configuration.Channels.Count == 0) throw new ArgumentException("至少配置一个模拟量通道。", nameof(configuration));
        if (configuration.Channels.Select(x => x.Channel).Distinct().Count() != configuration.Channels.Count)
            throw new ArgumentException("模拟量通道不能重复。", nameof(configuration));

        if (configuration.InputTopology == AnalogInputTopology.Differential && configuration.Channels.Any(x => !x.Differential))
            throw new ArgumentException("输入拓扑为差分时，每个通道请求都必须标记 Differential=true。", nameof(configuration));
        if (configuration.InputTopology == AnalogInputTopology.SingleEnded && configuration.Channels.Any(x => x.Differential))
            throw new ArgumentException("输入拓扑为单端时，通道请求不能标记为差分。", nameof(configuration));

        var occupiedPhysicalChannels = new HashSet<int>();
        foreach (var channel in configuration.Channels)
        {
            if (channel.Channel is < 0 or >= AnalogHardwareBaseline.SingleEndedChannelCount)
                throw new ArgumentOutOfRangeException(nameof(configuration), $"AI{channel.Channel} 超出 PCIE-1604 通道范围。");
            if (channel.Differential && (channel.Channel % 2 != 0 || channel.Channel + 1 >= AnalogHardwareBaseline.SingleEndedChannelCount))
                throw new ArgumentException($"差分通道必须使用偶数起始通道，当前为 AI{channel.Channel}。", nameof(configuration));
            if (!Enum.IsDefined(channel.Range))
                throw new ArgumentOutOfRangeException(nameof(configuration), $"AI{channel.Channel} 使用了未定义的 AD 量程。");
            if (!occupiedPhysicalChannels.Add(channel.Channel) ||
                (channel.Differential && !occupiedPhysicalChannels.Add(channel.Channel + 1)))
            {
                throw new ArgumentException($"AI{channel.Channel} 与另一个通道请求占用了相同物理输入。", nameof(configuration));
            }
        }

        var firstChannel = configuration.Channels.Min(x => x.Channel);
        var lastChannel = configuration.Channels.Max(x => x.Differential ? x.Channel + 1 : x.Channel);
        var physicalChannelCount = lastChannel - firstChannel + 1;
        var totalConversionRate = checked((long)configuration.SampleRateHz * physicalChannelCount);
        if (totalConversionRate > AnalogHardwareBaseline.MaximumSampleRateHz)
            throw new ArgumentException(
                $"PCIE-1604 总转换率 {totalConversionRate} Hz 超过 {AnalogHardwareBaseline.MaximumSampleRateHz} Hz 上限。",
                nameof(configuration));
    }

    private static void EnsureRuntimePrerequisites()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("PCIE-1604 SDK 仅支持 Windows。");
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("当前项目固定使用 PCIE-1604 x64 SDK，请以 x64 进程运行。");
        var missing = new[] { AnalogHardwareBaseline.NativeLibraryName, AnalogHardwareBaseline.DriverCompanionLibraryName }
            .Where(name => !File.Exists(Path.Combine(AppContext.BaseDirectory, name)))
            .ToArray();
        if (missing.Length > 0)
            throw new DllNotFoundException($"缺少 PCIE-1604 x64 厂家文件：{string.Join("、", missing)}。请从官方 SDK 的 dll64 目录复制到程序目录，并先安装 DPInst64 驱动。");
    }

    private static void EnsureSuccess(
        int result,
        string operation,
        Pcie1604SdkCompatibility compatibility)
    {
        if (result == compatibility.ApiSuccessCode) return;
        throw new IOException($"PCIE-1604 {operation}失败：{Pcie1604Native.DescribeError(result)}");
    }

    private static string ReadDllVersion(Pcie1604SdkCompatibility compatibility)
    {
        var buffer = new byte[64];
        var result = Pcie1604Native.GetDllVersion(buffer);
        if (result != compatibility.ApiSuccessCode)
            throw new IOException("读取 PCIE-1604 DLL 版本失败：" + Pcie1604Native.DescribeError(result));
        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0) length = buffer.Length;
        return Encoding.ASCII.GetString(buffer, 0, length).Trim();
    }

    private void AppendLastError(string message)
    {
        var previous = LastError;
        Volatile.Write(ref _lastError, string.IsNullOrWhiteSpace(previous) ? message : $"{previous}；{message}");
    }

    private void ThrowIfDisposeStarted()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
            throw new ObjectDisposedException(nameof(Pcie1604Acquisition));
    }

    private static string BuildNativeFailureMessage(Exception exception) => exception switch
    {
        BadImageFormatException => "PCIE-1604 原生 DLL 位数不匹配；主程序、pcieAPI.dll 和 CH365.dll 必须全部为 x64。",
        EntryPointNotFoundException => "pcieAPI.dll 缺少当前适配器需要的导出函数，请核对厂家 SDK 版本和 pcieAPI.h。",
        _ => "无法加载 PCIE-1604 的 pcieAPI.dll 或 CH365.dll；请安装官方 x64 驱动并保持依赖文件同目录。"
    };
}
