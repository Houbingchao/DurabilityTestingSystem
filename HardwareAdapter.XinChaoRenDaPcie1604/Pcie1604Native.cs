using System.Runtime.InteropServices;
using System.Text;
using DurabilityTestingSystem.Models;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]

namespace DurabilityTestingSystem.HardwareAdapter.XinChaoRenDaPcie1604;

internal static class Pcie1604Native
{
    internal const string LibraryName = "pcieAPI.dll";
    // The BoardInfo comment in the V1.0.2 manual uses this exact board type text.
    // Keep it separate from the upper-case product display name used by the UI.
    internal const string BoardTypeName = "pcie-1604";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
    internal struct BoardInfo
    {
        public int Id;
        public int Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 60)]
        public string BoardType;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 60)]
        public string BoardVersion;

        public static BoardInfo Create(int boardId) => new()
        {
            Id = boardId,
            Handle = 0,
            BoardType = BoardTypeName,
            BoardVersion = string.Empty
        };
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct AdParameters
    {
        public byte StartChannel;
        public byte ChannelCount;
        public double SampleFrequency;
        public byte Mode;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U4)]
        public uint[] Gains;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U4)]
        public uint[] DifferentialFlags;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10, ArraySubType = UnmanagedType.U2)]
        public ushort[] PreTriggerLengths;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10, ArraySubType = UnmanagedType.U2)]
        public ushort[] FixedLengths;

        public ushort FifoThreshold;
        public ushort DataPacketLength;

        public static AdParameters Create() => new()
        {
            Gains = new uint[32],
            DifferentialFlags = new uint[32],
            PreTriggerLengths = new ushort[10],
            FixedLengths = new ushort[10],
            FifoThreshold = 16,
            DataPacketLength = 1024
        };
    }

    [DllImport(LibraryName, EntryPoint = "pcie_Sys_DeviceOpen", CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, ExactSpelling = true)]
    internal static extern int DeviceOpen(ref BoardInfo boardInfo);

    [DllImport(LibraryName, EntryPoint = "pcie_Sys_DeviceClose", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    internal static extern int DeviceClose(ref BoardInfo boardInfo);

    [DllImport(LibraryName, EntryPoint = "pcie_Sys_BoardReset", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    internal static extern int BoardReset(ref BoardInfo boardInfo);

    [DllImport(LibraryName, EntryPoint = "pcie_Sys_GetDLLVersion", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    internal static extern int GetDllVersion([Out] byte[] version);

    [DllImport(LibraryName, EntryPoint = "pcie_Sys_GetErrorMessage", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    internal static extern int GetErrorMessage(int errorCode, [Out] byte[] message);

    [DllImport(LibraryName, EntryPoint = "pcie_AD_SetPara", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    internal static extern int AdSetParameters(ref BoardInfo boardInfo, ref AdParameters parameters);

    [DllImport(LibraryName, EntryPoint = "pcie_AD_SetWorkStatus", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    internal static extern int AdSetWorkStatus(ref BoardInfo boardInfo, int status);

    [DllImport(LibraryName, EntryPoint = "pcie_AD_ClearFIFO", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    internal static extern int AdClearFifo(ref BoardInfo boardInfo);

    [DllImport(LibraryName, EntryPoint = "pcie_AD_GetFIFOStatus", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    internal static extern int AdGetFifoStatus(ref BoardInfo boardInfo, out byte status);

    [DllImport(LibraryName, EntryPoint = "pcie_AD_GetSingleData", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    internal static extern int AdGetSingleData(ref BoardInfo boardInfo, [Out] byte[] buffer, ref uint bufferSize);

    [DllImport(LibraryName, EntryPoint = "pcie_AD_GetAFromD", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    internal static extern int AdConvertToVoltage(ref BoardInfo boardInfo, byte range, ref double value);

    [DllImport(LibraryName, EntryPoint = "pcie_DI_GetWord", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    internal static extern int DigitalInputGetWord(ref BoardInfo boardInfo, byte group, out ushort value);

    internal static void ValidateManagedLayout(Pcie1604SdkCompatibility compatibility)
    {
        var boardSize = Marshal.SizeOf<BoardInfo>();
        var parametersSize = Marshal.SizeOf<AdParameters>();
        if (boardSize != compatibility.BoardInfoSize)
            throw new PlatformNotSupportedException(
                $"PCIE-1604 BoardInfo 托管布局异常：已验证 SDK 要求 {compatibility.BoardInfoSize} 字节，实际 {boardSize} 字节。");
        if (parametersSize != compatibility.AdParametersSize)
            throw new PlatformNotSupportedException(
                $"PCIE-1604 stADPara 托管布局异常：已验证 SDK 要求 {compatibility.AdParametersSize} 字节，实际 {parametersSize} 字节。");
    }

    internal static string DescribeError(int errorCode)
    {
        try
        {
            var buffer = new byte[256];
            _ = GetErrorMessage(errorCode, buffer);
            var length = Array.IndexOf(buffer, (byte)0);
            if (length < 0) length = buffer.Length;
            var text = Encoding.Default.GetString(buffer, 0, length).Trim();
            return string.IsNullOrWhiteSpace(text) ? $"错误码 {errorCode}" : $"{text}（{errorCode}）";
        }
        catch
        {
            return $"错误码 {errorCode}";
        }
    }
}
