using System.ComponentModel;
using System.Runtime.InteropServices;
using NoTSleep.Core;

namespace NoTSleep;

internal sealed class WindowsCapsLockState : ICapsLockState
{
    private const int VkCapsLock = 0x14;

    public bool IsOn => (GetKeyState(VkCapsLock) & 1) == 1;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}

internal sealed class WindowsWakePlatform : IWakePlatform
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private static readonly Guid SubButtons = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    private static readonly Guid LidSwitchAction = new("5ca83367-6e45-459f-a27b-476b1d01c936");

    public void SetAutomaticSleepBlocked(bool blocked)
    {
        var flags = blocked ? EsContinuous | EsSystemRequired : EsContinuous;
        if (SetThreadExecutionState(flags) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível atualizar o estado de energia.");
        }
    }

    public LidActions GetLidActions()
    {
        var scheme = GetActiveScheme();
        try
        {
            ThrowOnPowerError(PowerReadACValueIndex(IntPtr.Zero, scheme, SubButtons, LidSwitchAction, out var ac));
            ThrowOnPowerError(PowerReadDCValueIndex(IntPtr.Zero, scheme, SubButtons, LidSwitchAction, out var dc));
            return new LidActions(ac, dc);
        }
        finally
        {
            LocalFree(scheme);
        }
    }

    public void SetLidActions(LidActions actions)
    {
        var scheme = GetActiveScheme();
        try
        {
            ThrowOnPowerError(PowerWriteACValueIndex(IntPtr.Zero, scheme, SubButtons, LidSwitchAction, actions.OnAcPower));
            ThrowOnPowerError(PowerWriteDCValueIndex(IntPtr.Zero, scheme, SubButtons, LidSwitchAction, actions.OnBattery));
            ThrowOnPowerError(PowerSetActiveScheme(IntPtr.Zero, scheme));
        }
        finally
        {
            LocalFree(scheme);
        }
    }

    public void JiggleMouse()
    {
        var inputs = new[]
        {
            new Input { Type = 0, MouseInput = new MouseInput { Dx = 1, Flags = 0x0001 } },
            new Input { Type = 0, MouseInput = new MouseInput { Dx = -1, Flags = 0x0001 } }
        };

        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível movimentar o mouse.");
        }
    }

    private static IntPtr GetActiveScheme()
    {
        ThrowOnPowerError(PowerGetActiveScheme(IntPtr.Zero, out var scheme));
        return scheme;
    }

    private static void ThrowOnPowerError(uint errorCode)
    {
        if (errorCode != 0)
        {
            throw new Win32Exception((int)errorCode, "Não foi possível alterar a configuração de energia.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, Guid subgroupGuid, Guid powerSettingGuid, out uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, Guid subgroupGuid, Guid powerSettingGuid, out uint dcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, Guid subgroupGuid, Guid powerSettingGuid, uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, Guid subgroupGuid, Guid powerSettingGuid, uint dcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, IntPtr schemeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, [In] Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput MouseInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
