using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

var config = JsonSerializer.Deserialize<Config>(File.ReadAllText("k.cfg"), new JsonSerializerOptions() { Converters = { new JsonStringEnumConverter() } })!;
var mousefile = config.MouseFile;
var mousename = config.MouseName;

var defaultConfig = new AppliedConfiguration()
{
    Modes = config.Default.Modes.ToFrozenDictionary(),

    LeftDelay = config.Default.LeftDelay,
    RightDelay = config.Default.RightDelay,
};
var modes = config.Regexes
    .Select(r =>
    {
        var regex = new Regex(r.Key, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var cfgf = new AppliedConfiguration()
        {
            Modes = defaultConfig.Modes
                .Concat(r.Value.Modes ?? [])
                .ToFrozenDictionary(),

            LeftDelay = r.Value.LeftDelay ?? defaultConfig.LeftDelay,
            RightDelay = r.Value.RightDelay ?? defaultConfig.RightDelay,
        };

        return new ASD() { Regex = regex, Config = cfgf };
    })
    .ToArray();

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

    using var process = Process.Start(info)!;
    process.WaitForExit();
}

var state = new ProgramState(XOpenDisplay(null));

new Thread(async () =>
{
    using var process = Process.Start(new ProcessStartInfo("i3-msg")
    {
        ArgumentList = { "-t", "subscribe", "[ \"window\" ]", "-rm" },
        RedirectStandardOutput = true,
    })!;

    process.OutputDataReceived += (obj, evt) =>
    {
        if (evt.Data is null) return;

        var data = JsonSerializer.Deserialize<I3MsgData>(evt.Data);
        state.CurrentWindowTitle = data.container.window_properties.title ?? "";
        state.Left.Clicking = state.Right.Clicking = false;
    };

    process.BeginOutputReadLine();
    process.WaitForExit();
})
{ IsBackground = true }.Start();


static void a(Button button, ProgramState state, ProgramStatePart part)
{
    while (true)
    {
        part.Handle.WaitOne();

        while (part.Clicking)
        {
            Click(button, state.Display);
            Thread.Sleep(part.Delay);
        }
    }
}

new Thread(() => a(Button.Left, state, state.Left)) { IsBackground = true }.Start();
new Thread(() => a(Button.Right, state, state.Right)) { IsBackground = true }.Start();
// new Thread(() =>
// {
//     while (true)
//     {
//         state.LeftHandle.WaitOne();

//         while (state.LeftClicking)
//         {
//             Click(Button.Left, state.Display);
//             Thread.Sleep(state.LeftDelay);
//         }
//     }
// })
// { IsBackground = true }.Start();

// new Thread(() =>
// {
//     while (true)
//     {
//         state.RightHandle.WaitOne();

//         while (state.RightClicking)
//         {
//             Click(Button.Right, state.Display);
//             Thread.Sleep(state.RightDelay);
//         }
//     }
// })
// { IsBackground = true }.Start();


using var mouseStream = File.OpenRead(mousefile);
Span<byte> buffer = stackalloc byte[16 + 8];
while (true)
{
    var read = mouseStream.Read(buffer);
    var span = buffer.Slice(16, 8);

    var button = (InKey) span[2];
    if (button != InKey.Back && button != InKey.Front)
        continue;

    var pressed = span[4] == 1;
    testButton(button, pressed, modes, defaultConfig, state);
}

static void testButton(InKey button, bool pressed, ASD[] modes, AppliedConfiguration defaultConfig, ProgramState state)
{
    foreach (var mode in modes)
    {
        if (!mode.Regex.IsMatch(state.CurrentWindowTitle))
            continue;

        execute(button, pressed, mode.Config, state);
        return;
    }

    execute(button, pressed, defaultConfig, state);
}
static void execute(InKey key, bool pressed, AppliedConfiguration config, ProgramState state)
{
    state.Left.Delay = config.LeftDelay;
    state.Right.Delay = config.RightDelay;
    var mode = config.Modes[key];

    if (mode.Mode == Mode.DoubleClick)
    {
        if (pressed)
            DoubleClick((Button) (int) mode.Key, state.Display);
    }
    else if (mode.Mode == Mode.ToggleClicking)
    {
        var part = mode.Key == Key.Left ? state.Left : state.Right;

        if (pressed) part.Clicking = !part.Clicking;
        if (part.Clicking) part.Handle.Set();
    }
    else if (mode.Mode == Mode.HoldClicking)
    {
        var part = mode.Key == Key.Left ? state.Left : state.Right;

        part.Clicking = pressed;
        if (part.Clicking) part.Handle.Set();
    }
}


[MethodImpl(MethodImplOptions.NoInlining)]
static void DoubleClick(Button button, nint display)
{
    const ulong clickDelay = 10;

    XTestFakeButtonEvent(display, button, 1, 0);
    XTestFakeButtonEvent(display, button, 0, 10);
    XTestFakeButtonEvent(display, button, 1, 0 + clickDelay);
    XTestFakeButtonEvent(display, button, 0, 10 + clickDelay);

    Flush(display);
}

[MethodImpl(256)]
static void Click(Button button, nint display)
{
    XTestFakeButtonEvent(display, button, 1, 0);
    XTestFakeButtonEvent(display, button, 0, 10);
    Flush(display);
}

[MethodImpl(256)]
static void Flush(nint display) => XFlush(display);


[DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(string? display);
[DllImport("libXtst.so")] static extern int XTestFakeButtonEvent(IntPtr display, Button button, int is_press, ulong delay);
[DllImport("libXtst.so")] static extern int XFlush(IntPtr display);

enum Button : uint { Left = 1, Middle, Right, Fourth, Fifth }

class ProgramState
{
    public readonly ProgramStatePart Left = new();
    public readonly ProgramStatePart Right = new();
    public readonly nint Display;

    public string CurrentWindowTitle = "";

    public ProgramState(nint display) => Display = display;
}
class ProgramStatePart
{
    public readonly EventWaitHandle Handle = new EventWaitHandle(false, EventResetMode.AutoReset);
    public bool Clicking;
    public int Delay;
}

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

class ASD
{
    public required Regex Regex { get; init; }
    public required AppliedConfiguration Config { get; init; }
}
class AppliedConfiguration
{
    public required FrozenDictionary<InKey, Mode2> Modes { get; init; }

    public required int LeftDelay { get; init; }
    public required int RightDelay { get; init; }
}
class Mode2
{
    public required Key Key { get; init; }
    public required Mode Mode { get; init; }
}

enum Mode { DoubleClick, ToggleClicking, HoldClicking }
enum InKey { Back = 19, Front = 20 }
enum Key { Left = (int) Button.Left, Right = (int) Button.Right }

class Config
{
    public required string MouseFile { get; init; }
    public required string MouseName { get; init; }
    public required Configuration Default { get; init; }
    public required IReadOnlyDictionary<string, Regex2> Regexes { get; init; }


    public class Configuration
    {
        public required IReadOnlyDictionary<InKey, Mode2> Modes { get; init; }

        public required int LeftDelay { get; init; }
        public required int RightDelay { get; init; }
    }
    public class Regex2
    {
        public Dictionary<InKey, Mode2>? Modes { get; init; }
        public int? LeftDelay { get; init; }
        public int? RightDelay { get; init; }
    }
}

#pragma warning disable CS0649
struct I3MsgData
{
    public Container container { get; set; }

    public struct Container
    {
        public WindowProperties window_properties { get; set; }

        public struct WindowProperties
        {
            public string? title { get; set; }
        }
    }
}
