using System.Diagnostics;
using NeonStack;

// Options: --fullscreen starts on the primary monitor's native mode (F11 toggles at runtime),
// --scale=N forces the window to N x 640x360 (1..6) instead of the automatic fit for the monitor.
int forcedScale = 0;
string? dumpMusic = null;
bool? fullscreen = null;

foreach (string arg in args)
{
    if (arg.StartsWith("--scale=", StringComparison.Ordinal) && int.TryParse(arg[8..], out int s))
        forcedScale = Math.Clamp(s, 1, Framebuffer.MaxScale);
    else if (arg.StartsWith("--dump-music=", StringComparison.Ordinal))
        dumpMusic = arg[13..];
    else if (arg == "--fullscreen") fullscreen = true;
    else if (arg == "--windowed") fullscreen = false;
}

if (!string.IsNullOrWhiteSpace(dumpMusic))
{
    Audio.DumpMusic(dumpMusic);
    Console.WriteLine("Wrote " + Path.GetFullPath(dumpMusic));
    return;
}

Settings.Reload();

// Fullscreen by default; --windowed overrides it for a session.
using var host = new BrutalHost("NEON STACK", Framebuffer.W, Framebuffer.H, fullscreen ?? Settings.Current.Fullscreen, forcedScale);

// The taskbar/window icon is the generated NEON STACK mark. Missing the asset must not stop the game.
try { host.SetWindowIcon(ImageData.LoadEmbedded("NeonStack.Assets.icon_neonstack.png")); }
catch (Exception e) { Console.Error.WriteLine("Window icon unavailable: " + e.Message); }

Audio.Start();

var intro = new Intro();
var game = new TetrisGame();
var scene = new Scene();
var fb = new Framebuffer(host.RenderScale);
var down = new bool[(int)Key.Count];

var clock = Stopwatch.StartNew();
double last = clock.Elapsed.TotalSeconds;

while (host.Running && !game.Quit)
{
    host.Poll(down);
    Audio.Update(down);

    double now = clock.Elapsed.TotalSeconds;
    float dt = (float)(now - last);
    last = now;

    // The framebuffer renders at an integer multiple of 640x360 so it can be blitted 1:1.
    // Resizing the window across a scale boundary reallocates it (rare and cheap).
    int scale = host.RenderScale;
    if (scale != fb.Scale) fb = new Framebuffer(scale);

    // Track the intro state before it updates: the frame that closes the last splash must not let
    // the same Enter/Space press fall through to the title screen.
    bool introWasActive = intro.Active;
    if (introWasActive) intro.Update(dt, down);

    if (intro.Active)
    {
        scene.RenderIntro(fb, intro);
    }
    else
    {
        if (introWasActive) game.SuppressConfirm(0.35f);
        game.Update(dt, down);
        if (game.ConsumeFullscreenRequest()) host.ToggleFullscreen();
        scene.Render(fb, game);
    }

    Audio.SetDuck(game.Phase == Phase.Paused);
    host.Present(fb);

    if (dt < 0.004f) Thread.Sleep(1); // don't spin if vsync is off
}

Audio.Dispose();
