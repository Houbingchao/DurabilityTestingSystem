using System.Runtime.InteropServices;

namespace DurabilityTestingSystem.HardwareAdapter.ZlgUsbCanFd;

internal static class ZlgCanNative
{
    internal const string LibraryName = "zlgcan.dll";
    internal const uint StatusError = 0;
    internal const uint StatusOk = 1;
    internal const uint StatusOnline = 2;
    internal const uint DeviceTypeUsbCanFd200U = 41;
    internal const uint CanControllerTypeCanFd = 1;
    internal const uint ExtendedFrameFlag = 0x8000_0000;
    internal const uint CanIdMask = 0x1FFF_FFFF;
    internal const byte CanFdBitRateSwitch = 0x01;

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern IntPtr ZCAN_OpenDevice(uint deviceType, uint deviceIndex, uint reserved);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern uint ZCAN_CloseDevice(IntPtr deviceHandle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern uint ZCAN_GetDeviceInf(IntPtr deviceHandle, ref ZcanDeviceInfo info);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern uint ZCAN_IsDeviceOnLine(IntPtr deviceHandle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern IntPtr ZCAN_InitCAN(IntPtr deviceHandle, uint canIndex, ref ZcanChannelInitConfig config);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern uint ZCAN_StartCAN(IntPtr channelHandle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern uint ZCAN_ResetCAN(IntPtr channelHandle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern uint ZCAN_ClearBuffer(IntPtr channelHandle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true, CharSet = CharSet.Ansi)]
    internal static extern uint ZCAN_SetValue(
        IntPtr deviceHandle,
        [MarshalAs(UnmanagedType.LPStr)] string path,
        [MarshalAs(UnmanagedType.LPStr)] string value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern uint ZCAN_Transmit(IntPtr channelHandle, ref ZcanTransmitData transmit, uint length);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern uint ZCAN_Receive(IntPtr channelHandle, ref ZcanReceiveData receive, uint length, int waitTime);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern uint ZCAN_TransmitFD(IntPtr channelHandle, ref ZcanTransmitFdData transmit, uint length);

    [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern uint ZCAN_ReceiveFD(IntPtr channelHandle, ref ZcanReceiveFdData receive, uint length, int waitTime);
}

[StructLayout(LayoutKind.Sequential)]
internal struct ZcanDeviceInfo
{
    internal ushort HardwareVersion;
    internal ushort FirmwareVersion;
    internal ushort DriverVersion;
    internal ushort InterfaceVersion;
    internal ushort IrqNumber;
    internal byte CanChannelCount;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    internal byte[] SerialNumber;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)]
    internal byte[] HardwareType;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    internal ushort[] Reserved;

    internal static ZcanDeviceInfo Create() => new()
    {
        SerialNumber = new byte[20],
        HardwareType = new byte[40],
        Reserved = new ushort[4]
    };
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct ZcanChannelInitConfig
{
    [FieldOffset(0)] internal uint CanType;
    [FieldOffset(4)] internal ZcanClassicInitConfig Classic;
    [FieldOffset(4)] internal ZcanCanFdInitConfig CanFd;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ZcanClassicInitConfig
{
    internal uint AcceptanceCode;
    internal uint AcceptanceMask;
    internal uint Reserved;
    internal byte Filter;
    internal byte Timing0;
    internal byte Timing1;
    internal byte Mode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ZcanCanFdInitConfig
{
    internal uint AcceptanceCode;
    internal uint AcceptanceMask;
    internal uint ArbitrationTiming;
    internal uint DataTiming;
    internal uint Prescaler;
    internal byte Filter;
    internal byte Mode;
    internal ushort Padding;
    internal uint Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ZcanCanFrame
{
    internal uint CanId;
    internal byte DataLength;
    internal byte Padding;
    internal byte Reserved0;
    internal byte Reserved1;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    internal byte[] Data;

    internal static ZcanCanFrame Create() => new() { Data = new byte[8] };
}

[StructLayout(LayoutKind.Sequential)]
internal struct ZcanCanFdFrame
{
    internal uint CanId;
    internal byte Length;
    internal byte Flags;
    internal byte Reserved0;
    internal byte Reserved1;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    internal byte[] Data;

    internal static ZcanCanFdFrame Create() => new() { Data = new byte[64] };
}

[StructLayout(LayoutKind.Sequential)]
internal struct ZcanTransmitData
{
    internal ZcanCanFrame Frame;
    internal uint TransmitType;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ZcanReceiveData
{
    internal ZcanCanFrame Frame;
    internal ulong TimestampMicroseconds;

    internal static ZcanReceiveData Create() => new() { Frame = ZcanCanFrame.Create() };
}

[StructLayout(LayoutKind.Sequential)]
internal struct ZcanTransmitFdData
{
    internal ZcanCanFdFrame Frame;
    internal uint TransmitType;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ZcanReceiveFdData
{
    internal ZcanCanFdFrame Frame;
    internal ulong TimestampMicroseconds;

    internal static ZcanReceiveFdData Create() => new() { Frame = ZcanCanFdFrame.Create() };
}
