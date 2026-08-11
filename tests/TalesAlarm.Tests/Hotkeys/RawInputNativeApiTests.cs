using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using TalesAlarm.Hotkeys;

namespace TalesAlarm.Tests.Hotkeys;

[Collection("Raw input integration")]
public sealed class RawInputNativeApiTests
{
    // Break caught: keyboard pass-through registration targets the wrong usage or suppresses legacy input.
    [Fact]
    public void TryRegisterKeyboard_ForwardsPassThroughKeyboardDescriptor()
    {
        var methods = new RecordingRawInputNativeMethods();
        var api = new Win32RawInputNativeApi(methods);

        Assert.True(api.TryRegisterKeyboard((nint)42, out var errorCode));

        Assert.Equal(0, errorCode);
        var device = Assert.Single(methods.Registrations);
        Assert.Equal((ushort)0x01, device.UsagePage);
        Assert.Equal((ushort)0x06, device.Usage);
        Assert.Equal((nint)42, device.TargetWindow);
        Assert.Equal(
            RawInputDeviceFlags.InputSink | RawInputDeviceFlags.DeviceNotify,
            device.Flags);
    }

    // Break caught: cleanup leaves keyboard raw-input registration attached to a window.
    [Fact]
    public void TryUnregisterKeyboard_ForwardsRemoveWithNullTarget()
    {
        var methods = new RecordingRawInputNativeMethods();
        var api = new Win32RawInputNativeApi(methods);

        Assert.True(api.TryUnregisterKeyboard(out var errorCode));

        Assert.Equal(0, errorCode);
        var device = Assert.Single(methods.Registrations);
        Assert.Equal((ushort)0x01, device.UsagePage);
        Assert.Equal((ushort)0x06, device.Usage);
        Assert.Equal(RawInputDeviceFlags.Remove, device.Flags);
        Assert.Equal(0, device.TargetWindow);
    }

    // Break caught: native registration failures are replaced with a generic error and cannot be diagnosed later.
    [Fact]
    public void TryRegisterKeyboard_WhenNativeRegistrationFails_PreservesWin32Error()
    {
        var methods = new RecordingRawInputNativeMethods { RegistrationError = 87 };
        var api = new Win32RawInputNativeApi(methods);

        Assert.False(api.TryRegisterKeyboard((nint)42, out var errorCode));

        Assert.Equal(87, errorCode);
    }

    // Break caught: the boundary reads only a header or loses the keyboard device and scan-code details.
    [Fact]
    public void ReadKeyboard_WhenNativePayloadIsKeyboard_ReturnsDecodedKeyboardInput()
    {
        var methods = new KeyboardPayloadRawInputNativeMethods(
            new RawInputHeader
            {
                Type = 1,
                Device = (nint)123,
            },
            new RawKeyboard
            {
                MakeCode = 0x1E,
                VirtualKey = 0x41,
                Flags = RawKeyboardFlags.E0,
            });
        var api = new Win32RawInputNativeApi(methods);

        var result = api.ReadKeyboard((nint)99);

        Assert.Equal(RawInputReadStatus.Keyboard, result.Status);
        Assert.Equal(0, result.ErrorCode);
        Assert.Equal((nint)123, result.Keyboard.DeviceHandle);
        Assert.Equal((ushort)0x41, result.Keyboard.VirtualKey);
        Assert.Equal((ushort)0x1E, result.Keyboard.MakeCode);
        Assert.Equal(RawKeyboardFlags.E0, result.Keyboard.Flags);
        Assert.False(result.Keyboard.IsKeyUp);
    }

    // Break caught: non-keyboard Raw Input reports are interpreted as keyboard hotkey events.
    [Fact]
    public void ReadKeyboard_WhenNativePayloadIsNotKeyboard_ReturnsIgnored()
    {
        var methods = new KeyboardPayloadRawInputNativeMethods(
            new RawInputHeader { Type = 2 },
            new RawKeyboard());
        var api = new Win32RawInputNativeApi(methods);

        var result = api.ReadKeyboard((nint)99);

        Assert.Equal(RawInputReadStatus.Ignored, result.Status);
        Assert.Equal(0, result.ErrorCode);
    }

    // Break caught: GetRawInputData failures are reclassified as malformed data and the original Win32 error is lost.
    [Fact]
    public void ReadKeyboard_WhenSizeQueryFails_PreservesWin32Error()
    {
        var methods = new RecordingRawInputNativeMethods { ReadError = 122 };
        var api = new Win32RawInputNativeApi(methods);

        var result = api.ReadKeyboard((nint)99);

        Assert.Equal(RawInputReadStatus.Failed, result.Status);
        Assert.Equal(122, result.ErrorCode);
    }

    // Break caught: failures while filling the Raw Input buffer discard their distinct Win32 error.
    [Fact]
    public void ReadKeyboard_WhenBufferFillFails_PreservesWin32Error()
    {
        var methods = new CopyFailingRawInputNativeMethods { CopyError = 23 };
        var api = new Win32RawInputNativeApi(methods);

        var result = api.ReadKeyboard((nint)99);

        Assert.Equal(RawInputReadStatus.Failed, result.Status);
        Assert.Equal(23, result.ErrorCode);
    }

    // Break caught: malformed raw input with a mismatched reported size is decoded as a keyboard event.
    [Fact]
    public void ReadKeyboard_WhenHeaderSizeDoesNotMatchCopiedData_ReturnsInvalidData()
    {
        var methods = new KeyboardPayloadRawInputNativeMethods(
            new RawInputHeader { Type = 1, Size = 1 },
            new RawKeyboard());
        var api = new Win32RawInputNativeApi(methods);

        var result = api.ReadKeyboard((nint)99);

        Assert.Equal(RawInputReadStatus.Failed, result.Status);
        Assert.Equal(13, result.ErrorCode);
    }

    // Break caught: platform registration/removal fails for a valid top-level window handle.
    [Fact]
    public void RegisterAndUnregisterKeyboard_WithRealWindowHandle_Succeeds()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var source = new HwndSource(new HwndSourceParameters(
                    "TalesAlarm.RawInputNativeApiTests")
                {
                    Width = 1,
                    Height = 1,
                });
                var api = new Win32RawInputNativeApi();

                Assert.True(
                    api.TryRegisterKeyboard(source.Handle, out var registerError),
                    $"Raw Input registration failed: {registerError}");
                Assert.True(
                    api.TryUnregisterKeyboard(out var unregisterError),
                    $"Raw Input removal failed: {unregisterError}");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class RecordingRawInputNativeMethods : IRawInputNativeMethods
    {
        public List<RawInputDevice> Registrations { get; } = [];

        public int RegistrationError { get; init; }

        public int ReadError { get; init; }

        public bool TryRegisterRawInputDevice(
            ref RawInputDevice device,
            out int errorCode)
        {
            Registrations.Add(device);
            errorCode = RegistrationError;
            return errorCode == 0;
        }

        public uint GetRawInputData(
            nint rawInputHandle,
            uint command,
            nint data,
            ref uint dataSize,
            uint headerSize,
            out int errorCode)
        {
            errorCode = ReadError;
            return uint.MaxValue;
        }
    }

    private sealed class KeyboardPayloadRawInputNativeMethods : IRawInputNativeMethods
    {
        private readonly RawInputHeader header;
        private readonly RawKeyboard keyboard;
        private readonly uint size;

        public KeyboardPayloadRawInputNativeMethods(
            RawInputHeader header,
            RawKeyboard keyboard)
        {
            this.header = header;
            this.keyboard = keyboard;
            size = checked((uint)(Marshal.SizeOf<RawInputHeader>() + Marshal.SizeOf<RawKeyboard>()));
        }

        public bool TryRegisterRawInputDevice(
            ref RawInputDevice device,
            out int errorCode) =>
            throw new NotSupportedException();

        public uint GetRawInputData(
            nint rawInputHandle,
            uint command,
            nint data,
            ref uint dataSize,
            uint headerSize,
            out int errorCode)
        {
            errorCode = 0;
            if (data == 0)
            {
                dataSize = size;
                return 0;
            }

            var payloadHeader = header;
            if (payloadHeader.Size == 0)
            {
                payloadHeader.Size = size;
            }

            Marshal.StructureToPtr(payloadHeader, data, false);
            Marshal.StructureToPtr(
                keyboard,
                nint.Add(data, checked((int)headerSize)),
                false);
            dataSize = size;
            return size;
        }
    }

    private sealed class CopyFailingRawInputNativeMethods : IRawInputNativeMethods
    {
        public int CopyError { get; init; }

        public bool TryRegisterRawInputDevice(
            ref RawInputDevice device,
            out int errorCode) =>
            throw new NotSupportedException();

        public uint GetRawInputData(
            nint rawInputHandle,
            uint command,
            nint data,
            ref uint dataSize,
            uint headerSize,
            out int errorCode)
        {
            if (data == 0)
            {
                dataSize = headerSize;
                errorCode = 0;
                return 0;
            }

            errorCode = CopyError;
            return uint.MaxValue;
        }
    }
}

[CollectionDefinition("Raw input integration", DisableParallelization = true)]
public sealed class RawInputIntegrationCollection
{
}
