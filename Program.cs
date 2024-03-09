using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

var minecraftRegex = MinecraftRegex();
var display = XOpenDisplay(null);

var mousefile = args.Length < 1 ? "/dev/input/by-id/usb-SINOWEALTH_Wired_Gaming_Mouse-event-mouse" : args[0];
var mousename = args.Length < 2 ? "Glorious Model O" : args[1];

{
    var info = new ProcessStartInfo("/bin/sh");
    info.ArgumentList.Add("-c");
    info.ArgumentList.Add($$"""
        ids=$(xinput --list | awk -v search='{{mousename}}' '$0 ~ search {match($0, /id=[0-9]+/); if (RSTART) print substr($0, RSTART+3, RLENGTH-3) }')
        for i in $ids
        do
            xinput set-button-map $i 1 2 3 4 5 6 7 10 11 8 9 12 13 14 15 16
        done
        """.Replace("\r", "")
    );

    Process.Start(info)!.WaitForExit();
}


var leftDelay = 100;
bool leftClicking = false;
var lefthandle = new EventWaitHandle(false, EventResetMode.AutoReset);
var leftThread = new Thread(() =>
{
    while (true)
    {
        lefthandle.WaitOne();

        while (leftClicking)
        {
            Click(Button.Left);
            Thread.Sleep(leftDelay);
        }
    }
});
leftThread.IsBackground = true;
leftThread.Start();

var rightDelay = 100;
bool rightClicking = false;
var righthandle = new EventWaitHandle(false, EventResetMode.AutoReset);
var rightThread = new Thread(() =>
{
    while (true)
    {
        righthandle.WaitOne();

        while (rightClicking)
        {
            Click(Button.Right);
            Thread.Sleep(rightDelay);
        }
    }
});
rightThread.IsBackground = true;
rightThread.Start();

const ulong clickDelay = 10;


var stream = File.OpenRead(mousefile);

Span<byte> buffer = stackalloc byte[16 + 8];
while (true)
{
    var read = stream.Read(buffer);
    var span = buffer.Slice(16, 8);

    var button = span[2];
    var pressed = span[4] == 1;

    if (button == 19) // back
    {
        if (IsMinecraftFocused())
        {
            if (pressed)
            {
                leftClicking = !leftClicking;
                if (leftClicking) lefthandle.Set();
            }
        }
        else
        {
            if (pressed)
                DoubleClick();
        }
    }
    else if (button == 20) // front
    {
        if (IsMinecraftFocused())
        {
            rightClicking = pressed;
            if (pressed) righthandle.Set();
        }
        else
        {
            leftClicking = !leftClicking;
            if (leftClicking) lefthandle.Set();
        }
    }
}




[MethodImpl(MethodImplOptions.NoInlining)]
void DoubleClick()
{
    XTestFakeButtonEvent(display, Button.Left, 1, 0);
    XTestFakeButtonEvent(display, Button.Left, 0, 10);
    XTestFakeButtonEvent(display, Button.Left, 1, 0 + clickDelay);
    XTestFakeButtonEvent(display, Button.Left, 0, 10 + clickDelay);

    Flush();
}

bool IsMinecraftFocused()
{
    var name = GetFocusedName();
    return name is not null && minecraftRegex.IsMatch(name);
}
string? GetFocusedName()
{
    var focus = XGetInputFocus(display, out var window, out var revert);
    var name = XGetWMName(display, window, out var wname);

    return wname.Value;
}


[MethodImpl(256)]
void Click(Button button)
{
    XTestFakeButtonEvent(display, button, 1, 0);
    XTestFakeButtonEvent(display, button, 0, 10);
    Flush();
}

[MethodImpl(256)] void Flush() => XFlush(display);



[DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(string? display);
[DllImport("libX11.so.6")] static extern int XGetInputFocus(IntPtr display, out IntPtr window, out int revert_to);
[DllImport("libX11.so.6")] static extern int XGetWMName(IntPtr display, IntPtr window, out XTextProperty window_name_return);
[DllImport("libXtst.so")] static extern int XTestFakeButtonEvent(IntPtr display, Button button, int is_press, ulong delay);
[DllImport("libXtst.so")] static extern int XFlush(IntPtr display);

enum Button : uint { Left = 1, Middle, Right, Fourth, Fifth }


readonly struct XTextProperty
{
    public readonly string Value;
    public readonly ulong Encoding;
    public readonly uint Format;
    public readonly ulong NItems;

    public XTextProperty(string value, ulong encoding, uint format, ulong nitems)
    {
        Value = value;
        Encoding = encoding;
        Format = format;
        NItems = nitems;
    }
}

partial class Program
{
    [GeneratedRegex("Minecraft\\*? 1\\.\\d\\d?\\.?\\d?\\d?.*", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MinecraftRegex();
}
