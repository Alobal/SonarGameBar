using System.Runtime.InteropServices;
using SonarGameBar.Core;

namespace SonarGameBar.Bridge;

internal static class AudioActivityDetector
{
    private const float ActivePeakThreshold = 0.01f;
    private const int DeviceStateActive = 0x00000001;
    private const int StgmRead = 0x00000000;
    private const int ClsctxAll = 23;

    private static readonly Guid AudioMeterInformationId =
        new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    private static readonly PropertyKey DeviceFriendlyNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);

    public static MixerState ApplyToState(MixerState state)
    {
        try
        {
            var endpoints = GetEndpointChannels();
            return state.WithEndpointChannels(endpoints.ActiveChannels, endpoints.AvailableChannels);
        }
        catch (Exception exception)
        {
            BridgeLog.Write($"Audio activity detection failed: {exception.Message}");
            return state;
        }
    }

    private static EndpointChannels GetEndpointChannels()
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        var available = new HashSet<string>(StringComparer.Ordinal);
        ScanEndpoints(DataFlow.Render, available, active);
        ScanEndpoints(DataFlow.Capture, available, active);

        if (active.Count > 0)
        {
            active.Add(SonarChannels.Master);
        }

        return new EndpointChannels(available, active);
    }

    private static void ScanEndpoints(
        DataFlow flow,
        ISet<string> available,
        ISet<string> active)
    {
        object? enumeratorObject = null;
        IMMDeviceCollection? collection = null;

        try
        {
            enumeratorObject = new MMDeviceEnumerator();
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));

            for (var index = 0u; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(index, out device));
                    var friendlyName = GetFriendlyName(device);
                    var channel = MapEndpointToChannel(friendlyName, flow);
                    if (channel is null)
                    {
                        continue;
                    }

                    available.Add(channel);

                    var peak = GetPeakValue(device);
                    if (peak >= ActivePeakThreshold)
                    {
                        active.Add(channel);
                    }
                }
                finally
                {
                    ReleaseIfComObject(device);
                }
            }
        }
        finally
        {
            ReleaseIfComObject(collection);
            ReleaseIfComObject(enumeratorObject);
        }
    }

    private static string? MapEndpointToChannel(string friendlyName, DataFlow flow)
    {
        var name = friendlyName.ToLowerInvariant();
        if (!name.Contains("sonar", StringComparison.Ordinal))
        {
            return null;
        }

        if (flow == DataFlow.Capture)
        {
            return name.Contains("chat", StringComparison.Ordinal) ||
                name.Contains("microphone", StringComparison.Ordinal) ||
                name.Contains("mic", StringComparison.Ordinal)
                    ? SonarChannels.ChatCapture
                    : null;
        }

        if (name.Contains("gaming", StringComparison.Ordinal) ||
            name.Contains("game", StringComparison.Ordinal))
        {
            return SonarChannels.Game;
        }

        if (name.Contains("chat", StringComparison.Ordinal))
        {
            return SonarChannels.Chat;
        }

        if (name.Contains("media", StringComparison.Ordinal))
        {
            return SonarChannels.Media;
        }

        if (name.Contains("aux", StringComparison.Ordinal))
        {
            return SonarChannels.Aux;
        }

        return null;
    }

    private static float GetPeakValue(IMMDevice device)
    {
        object? meterObject = null;
        try
        {
            var interfaceId = AudioMeterInformationId;
            Marshal.ThrowExceptionForHR(device.Activate(
                ref interfaceId,
                ClsctxAll,
                IntPtr.Zero,
                out meterObject));

            if (meterObject is null)
            {
                return 0;
            }

            var meter = (IAudioMeterInformation)meterObject;
            Marshal.ThrowExceptionForHR(meter.GetPeakValue(out var peak));
            return peak;
        }
        finally
        {
            ReleaseIfComObject(meterObject);
        }
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? propertyStore = null;
        var value = new PropVariant();

        try
        {
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(StgmRead, out propertyStore));
            var key = DeviceFriendlyNameKey;
            Marshal.ThrowExceptionForHR(propertyStore.GetValue(ref key, out value));
            return value.GetString() ?? string.Empty;
        }
        finally
        {
            PropVariantClear(ref value);
            ReleaseIfComObject(propertyStore);
        }
    }

    private static void ReleaseIfComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    private sealed record EndpointChannels(ISet<string> AvailableChannels, ISet<string> ActiveChannels);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    private enum DataFlow
    {
        Render = 0,
        Capture = 1,
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(DataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint deviceIndex, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, int classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

        [PreserveSig]
        int OpenPropertyStore(int access, out IPropertyStore properties);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint propertyCount);

        [PreserveSig]
        int GetAt(uint propertyIndex, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float peak);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey
    {
        public PropertyKey(Guid formatId, int propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }

        public Guid FormatId { get; }

        public int PropertyId { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        private ushort _valueType;
        private ushort _reserved1;
        private ushort _reserved2;
        private ushort _reserved3;
        private IntPtr _value;
        private IntPtr _value2;

        public string? GetString()
        {
            return _valueType == (ushort)VarEnum.VT_LPWSTR
                ? Marshal.PtrToStringUni(_value)
                : null;
        }
    }
}
