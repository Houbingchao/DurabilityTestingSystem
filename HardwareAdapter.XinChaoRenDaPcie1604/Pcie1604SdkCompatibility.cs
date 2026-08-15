using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.HardwareAdapter.XinChaoRenDaPcie1604;

/// <summary>
/// Records the SDK semantics that are not defined by the PCIE-1604 V1.0.2 PDF.
/// This file is deliberately supplied by commissioning rather than guessed in code.
/// </summary>
internal sealed class Pcie1604SdkCompatibility
{
    internal const string FileName = "pcie1604-sdk-compatibility.json";
    private const string PhysicalChannelCount = "PhysicalChannels";
    private const string PhysicalChannelAscending = "PhysicalChannelAscending";
    private const string Bytes = "Bytes";
    private const string HighByteFirst = "HighByteFirst";
    private const string LowByteFirst = "LowByteFirst";
    private const string AggregateConversionsPerSecond = "AggregateConversionsPerSecond";
    private const string ScansPerSecond = "ScansPerSecond";

    public required string VerificationSource { get; init; }
    public required string PcieApiSha256 { get; init; }
    public required int ApiSuccessCode { get; init; }
    public required int BoardInfoSize { get; init; }
    public required int AdParametersSize { get; init; }
    public required string DifferentialChannelCountSemantics { get; init; }
    public required string SingleDataOutputLayout { get; init; }
    public required string SingleDataBufferSizeUnit { get; init; }
    public required string SingleDataByteOrder { get; init; }
    public required string SampleFrequencySemantics { get; init; }
    public required Dictionary<string, int> RangeCodes { get; init; }

    internal static Pcie1604SdkCompatibility LoadAndValidate(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new NotSupportedException(
                $"缺少 {FileName}。PCIE-1604 V1.0.2 手册没有定义 API 成功码、量程代码、" +
                "差分通道计数和单点数据字节序；取得厂家 x64 SDK、pcieAPI.h 与 C# 例程并完成台架确认前，" +
                "适配器按故障关闭处理，禁止猜测后连接真实板卡。");
        }

        Pcie1604SdkCompatibility compatibility;
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            };
            compatibility = JsonSerializer.Deserialize<Pcie1604SdkCompatibility>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("兼容性文件为空。");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"无法读取 PCIE-1604 SDK 兼容性文件 {path}：{ex.Message}", ex);
        }

        compatibility.Validate(baseDirectory);
        return compatibility;
    }

    internal int GetRangeCode(AnalogVoltageRange range)
    {
        var key = range.ToString();
        if (!RangeCodes.TryGetValue(key, out var code) || code is < 0 or > byte.MaxValue)
            throw new InvalidDataException($"兼容性文件没有提供有效的 {key} 厂家量程代码。");
        return code;
    }

    internal ushort DecodeSingleSample(byte first, byte second) => SingleDataByteOrder switch
    {
        HighByteFirst => (ushort)((first << 8) | second),
        LowByteFirst => (ushort)(first | (second << 8)),
        _ => throw new InvalidDataException($"不支持的单点数据字节序：{SingleDataByteOrder}。")
    };

    internal double ToDriverFrequency(int scanRateHz, int physicalChannelCount) => SampleFrequencySemantics switch
    {
        AggregateConversionsPerSecond => checked((double)scanRateHz * physicalChannelCount),
        ScansPerSecond => scanRateHz,
        _ => throw new InvalidDataException($"不支持的采样频率语义：{SampleFrequencySemantics}。")
    };

    private void Validate(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(VerificationSource))
            throw new InvalidDataException("兼容性文件必须记录厂家确认文件、头文件版本或台架验证记录。 ");
        if (RangeCodes is null)
            throw new InvalidDataException("兼容性文件必须提供 RangeCodes 对象。");
        if (ApiSuccessCode == -1)
            throw new InvalidDataException("API 成功码不能与手册明确的 DeviceOpen 失败码 -1 相同。");
        if (BoardInfoSize <= 0 || AdParametersSize <= 0)
            throw new InvalidDataException("兼容性文件必须给出由厂家头文件确认的原生结构体大小。");
        if (!string.Equals(DifferentialChannelCountSemantics, PhysicalChannelCount, StringComparison.Ordinal))
            throw new NotSupportedException(
                $"当前适配器只实现已验证为 {PhysicalChannelCount} 的差分通道计数方式；" +
                $"兼容性文件为 {DifferentialChannelCountSemantics}，必须先按实际 SDK 修改缓冲区解析。");
        if (!string.Equals(SingleDataOutputLayout, PhysicalChannelAscending, StringComparison.Ordinal))
            throw new NotSupportedException(
                $"当前适配器只实现 {PhysicalChannelAscending} 单点输出布局；实际值为 {SingleDataOutputLayout}。");
        if (!string.Equals(SingleDataBufferSizeUnit, Bytes, StringComparison.Ordinal))
            throw new NotSupportedException(
                $"当前适配器按字节解释 GetSingleData 的 bufferSize；实际值为 {SingleDataBufferSizeUnit}。");
        if (SingleDataByteOrder is not (HighByteFirst or LowByteFirst))
            throw new InvalidDataException("SingleDataByteOrder 只能是 HighByteFirst 或 LowByteFirst。");
        if (SampleFrequencySemantics is not (AggregateConversionsPerSecond or ScansPerSecond))
            throw new InvalidDataException(
                "SampleFrequencySemantics 只能是 AggregateConversionsPerSecond 或 ScansPerSecond。");

        foreach (var range in Enum.GetValues<AnalogVoltageRange>())
            _ = GetRangeCode(range);
        if (RangeCodes.Values.Distinct().Count() != Enum.GetValues<AnalogVoltageRange>().Length)
            throw new InvalidDataException("四个 AD 量程必须映射到四个互不重复的厂家量程代码。");

        var dllPath = Path.Combine(baseDirectory, AnalogHardwareBaseline.NativeLibraryName);
        if (string.IsNullOrWhiteSpace(PcieApiSha256) || PcieApiSha256.Length != 64)
            throw new InvalidDataException("兼容性文件必须记录经过验证的 pcieAPI.dll SHA-256。");
        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dllPath)));
        if (!string.Equals(actualHash, PcieApiSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"pcieAPI.dll 与兼容性文件记录的版本不一致。期望 {PcieApiSha256}，实际 {actualHash}。禁止混用未验证 DLL。");
        }
    }
}
