using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SimulatedTyping;

internal static class KeyboardInput
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkAlt = 0x12;
    public const ushort VkBack = 0x08;
    public const ushort VkEscape = 0x1B;
    public const ushort VkReturn = 0x0D;
    public const ushort VkSpace = 0x20;

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public Keyboard Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Keyboard
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    public static bool ModifiersAreReleased =>
        (GetAsyncKeyState(VkControl) & 0x8000) == 0 &&
        (GetAsyncKeyState(VkAlt) & 0x8000) == 0 &&
        (GetAsyncKeyState(VkShift) & 0x8000) == 0;

    public static void Key(ushort vk)
    {
        Send(new Input { Type = InputKeyboard, Data = new Keyboard { Vk = vk } },
             new Input { Type = InputKeyboard, Data = new Keyboard { Vk = vk, Flags = KeyUp } });
    }

    public static void ShiftEnter()
    {
        // Release Shift even if the Enter injection fails.
        KeyDown(VkShift);
        try { Key(VkReturn); }
        finally { KeyUpOnly(VkShift); }
    }

    public static void ControlC()
    {
        KeyDown(VkControl);
        try { Key((ushort)'C'); }
        finally { KeyUpOnly(VkControl); }
    }

    public static void Character(char c)
    {
        Send(new Input { Type = InputKeyboard, Data = new Keyboard { Scan = c, Flags = Unicode } },
             new Input { Type = InputKeyboard, Data = new Keyboard { Scan = c, Flags = Unicode | KeyUp } });
    }

    public static void LatinLetter(char c)
    {
        if (c is < 'a' or > 'z') throw new ArgumentOutOfRangeException(nameof(c));
        Key((ushort)char.ToUpperInvariant(c));
    }

    private static void KeyDown(ushort vk) => Send(new Input { Type = InputKeyboard, Data = new Keyboard { Vk = vk } });
    private static void KeyUpOnly(ushort vk) => Send(new Input { Type = InputKeyboard, Data = new Keyboard { Vk = vk, Flags = KeyUp } });

    private static void Send(params Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "模拟按键未送达目标窗口。目标程序可能以管理员权限运行。");
    }
}
