using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.Numerics;

namespace NeonStack;

/// <summary>
/// The only file that touches BRUTAL. Window, context, input and buffer swap go through BRUTAL's GLFW binding.
///
/// Resolution handling: the window is resizable and the framebuffer is rendered at an integer multiple of the
/// 640x360 design space that fits the window (see <see cref="RenderScale"/>). The finished image is uploaded as a
/// texture and blitted 1:1, centred with black letterboxing, so pixels stay square and hairlines/shapes stay crisp
/// at any window size, display scaling or fullscreen mode. F11 toggles fullscreen.
/// </summary>
public sealed unsafe class BrutalHost : IDisposable
{
    const int KeyF11 = 300;                       // GLFW_KEY_F11

    // GL constants (GL 1.1 / compatibility profile; no extension loader needed).
    const uint GL_TEXTURE_2D = 0x0DE1, GL_RGBA = 0x1908, GL_UNSIGNED_BYTE = 0x1401;
    const uint GL_NEAREST = 0x2600, GL_TEXTURE_MIN_FILTER = 0x2801, GL_TEXTURE_MAG_FILTER = 0x2800;
    const uint GL_TEXTURE_WRAP_S = 0x2802, GL_TEXTURE_WRAP_T = 0x2803, GL_CLAMP_TO_EDGE = 0x812F;
    const uint GL_QUADS = 0x0007, GL_PROJECTION = 0x1701, GL_MODELVIEW = 0x1700;
    const uint GL_COLOR_BUFFER_BIT = 0x4000, GL_UNPACK_ALIGNMENT = 0x0CF5, GL_BLEND = 0x0BE2;

    readonly GlfwWindow window;
    readonly int designW, designH;

    uint texture;
    int texW = -1, texH = -1;
    bool f11Down;
    int2 windowedPos, windowedSize;

    // Raw GLFW key codes (these values are stable across GLFW versions).
    static readonly (Key key, int[] codes)[] Map =
    {
        (Key.Left,   new[] { 263, 65 }),   // Left, A
        (Key.Right,  new[] { 262, 68 }),   // Right, D
        (Key.Down,   new[] { 264, 83 }),   // Down, S
        (Key.Up,     new[] { 265, 87 }),   // Up, W
        (Key.Z,      new[] { 90 }),
        (Key.X,      new[] { 88 }),
        (Key.C,      new[] { 67 }),
        (Key.Space,  new[] { 32 }),
        (Key.Enter,  new[] { 257, 335 }),  // Enter, keypad Enter
        (Key.Escape, new[] { 256 }),
        (Key.P,      new[] { 80 }),
        (Key.M,      new[] { 77 }),
        (Key.S,      new[] { 83 }),   // S is also soft drop; only the menus read Key.S
        (Key.L,      new[] { 76 }),
    };

    public BrutalHost(string title, int width, int height, bool startFullscreen = false, int forcedScale = 0)
    {
        designW = width;
        designH = height;

        if (!Glfw.Init())
            throw new InvalidOperationException("Failed to initialize GLFW");

        // No ClientApi hint: GLFW's default is an OpenGL context, which is what this blit needs.
        window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
        {
            Title = title,
            Size = PickWindowSize(width, height, forcedScale),
        });

        window.MakeContextCurrent();
        window.SetSizeLimits(new int2(width, height), new int2(-1, -1)); // never smaller than 1x, no upper bound
        Glfw.SwapInterval(1);                                            // vsync

        if (startFullscreen) ToggleFullscreen();

        if (Environment.GetEnvironmentVariable("NEONSTACK_DEBUG") == "1")
        {
            int2 fb = window.FramebufferSize;
            Console.WriteLine($"window={window.Size.X}x{window.Size.Y} framebuffer={fb.X}x{fb.Y} fullscreen={window.IsFullscreen} scale={RenderScale}");
        }
    }

    /// <summary>
    /// Largest whole multiple of the design space that fits comfortably on the primary monitor's work area.
    /// Windowed 1080p cannot hold 3x (1920x1080), so it picks 2x; F11 gives the full 3x image.
    /// </summary>
    static int2 PickWindowSize(int w, int h, int forcedScale)
    {
        if (forcedScale > 0) return new int2(w * forcedScale, h * forcedScale);

        var mon = Glfw.PrimaryMonitor;
        if (mon.IsNull()) return new int2(w * 2, h * 2);

        mon.GetMonitorWorkArea(out _, out int2 work);
        int fitW = Math.Max(w, work.X - 24);
        int fitH = Math.Max(h, work.Y - 24);
        int scale = Math.Clamp(Math.Min(fitW / w, fitH / h), 1, Framebuffer.MaxScale);
        return new int2(w * scale, h * scale);
    }

    public bool Running => !window.ShouldClose;

    /// <summary>
    /// Sets the window/taskbar icon from an embedded image. GLFW takes 8-bit RGBA rows, which is
    /// exactly the layout <see cref="ImageData"/> decodes to.
    /// </summary>
    public unsafe void SetWindowIcon(ImageData icon)
    {
        if (icon.W <= 0 || icon.H <= 0) return;
        fixed (uint* pixels = icon.Pixels)
        {
            var image = new GlfwImage { Width = icon.W, Height = icon.H, Pixels = (byte*)pixels };
            window.SetIcon(1, image);
        }
    }

    /// <summary>Integer multiple of the design space that the current window can display 1:1.</summary>
    public int RenderScale
    {
        get
        {
            int2 ds = window.FramebufferSize;
            if (ds.X <= 0 || ds.Y <= 0) return 1;
            return Math.Clamp(Math.Min(ds.X / designW, ds.Y / designH), 1, Framebuffer.MaxScale);
        }
    }

    public void Poll(bool[] down)
    {
        Glfw.PollEvents();

        bool f11 = window.GetKey((GlfwKey)KeyF11) == GlfwKeyAction.Press;
        if (f11 && !f11Down) ToggleFullscreen();
        f11Down = f11;

        foreach (var (key, codes) in Map)
        {
            bool d = false;
            foreach (int code in codes)
                if (window.GetKey((GlfwKey)code) == GlfwKeyAction.Press) { d = true; break; }
            down[(int)key] = d;
        }
    }

    public void ToggleFullscreen()
    {
        if (window.IsFullscreen)
        {
            window.SetWindowed(windowedPos, windowedSize);
            return;
        }

        var mon = Glfw.PrimaryMonitor;
        if (mon.IsNull()) return;

        windowedPos = window.Position;
        windowedSize = window.Size;
        var mode = mon.VideoMode;
        window.SetFullscreen(mon, new int2(mode.Width, mode.Height), mode.RefreshRate);
    }

    public void Present(Framebuffer fb)
    {
        int2 ds = window.FramebufferSize;
        if (ds.X <= 0 || ds.Y <= 0) return;            // minimised

        // Letterbox: black everywhere, then the framebuffer exactly centred at 1:1.
        Gl.glViewport(0, 0, ds.X, ds.Y);
        Gl.glClearColor(0f, 0f, 0f, 1f);
        Gl.glClear(GL_COLOR_BUFFER_BIT);

        if (fb.PixelW > ds.X || fb.PixelH > ds.Y) return;

        Upload(fb);

        Gl.glViewport((ds.X - fb.PixelW) / 2, (ds.Y - fb.PixelH) / 2, fb.PixelW, fb.PixelH);
        Gl.glMatrixMode(GL_PROJECTION); Gl.glLoadIdentity();
        Gl.glMatrixMode(GL_MODELVIEW); Gl.glLoadIdentity();
        Gl.glDisable(GL_BLEND);                        // framebuffer pixels are already final
        Gl.glEnable(GL_TEXTURE_2D);
        Gl.glBindTexture(GL_TEXTURE_2D, texture);
        Gl.glBegin(GL_QUADS);
        Gl.glTexCoord2f(0f, 0f); Gl.glVertex2f(-1f, 1f);   // top-left
        Gl.glTexCoord2f(1f, 0f); Gl.glVertex2f(1f, 1f);    // top-right
        Gl.glTexCoord2f(1f, 1f); Gl.glVertex2f(1f, -1f);   // bottom-right
        Gl.glTexCoord2f(0f, 1f); Gl.glVertex2f(-1f, -1f);  // bottom-left
        Gl.glEnd();
        Gl.glDisable(GL_TEXTURE_2D);

        window.SwapBuffers();
    }

    void Upload(Framebuffer fb)
    {
        if (texture == 0)
        {
            uint t;
            Gl.glGenTextures(1, &t);
            texture = t;
            Gl.glBindTexture(GL_TEXTURE_2D, texture);
            Gl.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
            Gl.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
            Gl.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            Gl.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        }

        Gl.glBindTexture(GL_TEXTURE_2D, texture);
        Gl.glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
        fixed (uint* p = fb.Pixels)
        {
            if (fb.PixelW != texW || fb.PixelH != texH)
            {
                Gl.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA, fb.PixelW, fb.PixelH, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
                texW = fb.PixelW;
                texH = fb.PixelH;
            }
            else
            {
                Gl.glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, fb.PixelW, fb.PixelH, GL_RGBA, GL_UNSIGNED_BYTE, p);
            }
        }
    }

    public void Dispose()
    {
        if (texture != 0)
        {
            window.MakeContextCurrent();
            uint t = texture;
            Gl.glDeleteTextures(1, &t);
            texture = 0;
        }
        window.Dispose();
        Glfw.Terminate();
    }
}

/// <summary>GL 1.1 entry points. opengl32.dll on Windows and libGL on Linux export all of these directly.</summary>
static unsafe class Gl
{
    const string Lib = "gl";

    static Gl()
    {
        NativeLibrary.SetDllImportResolver(typeof(Gl).Assembly, (name, _, _) =>
        {
            if (name != Lib) return IntPtr.Zero;
            string native = OperatingSystem.IsWindows() ? "opengl32" : "libGL.so.1";
            return NativeLibrary.TryLoad(native, out var h) ? h : IntPtr.Zero;
        });
    }

    [DllImport(Lib)] public static extern void glViewport(int x, int y, int width, int height);
    [DllImport(Lib)] public static extern void glClearColor(float r, float g, float b, float a);
    [DllImport(Lib)] public static extern void glClear(uint mask);
    [DllImport(Lib)] public static extern void glPixelStorei(uint pname, int param);
    [DllImport(Lib)] public static extern void glEnable(uint cap);
    [DllImport(Lib)] public static extern void glDisable(uint cap);
    [DllImport(Lib)] public static extern void glMatrixMode(uint mode);
    [DllImport(Lib)] public static extern void glLoadIdentity();
    [DllImport(Lib)] public static extern void glBegin(uint mode);
    [DllImport(Lib)] public static extern void glEnd();
    [DllImport(Lib)] public static extern void glTexCoord2f(float s, float t);
    [DllImport(Lib)] public static extern void glVertex2f(float x, float y);
    [DllImport(Lib)] public static extern void glGenTextures(int n, uint* textures);
    [DllImport(Lib)] public static extern void glDeleteTextures(int n, uint* textures);
    [DllImport(Lib)] public static extern void glBindTexture(uint target, uint texture);
    [DllImport(Lib)] public static extern void glTexParameteri(uint target, uint pname, int param);
    [DllImport(Lib)] public static extern void glTexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, void* pixels);
    [DllImport(Lib)] public static extern void glTexSubImage2D(uint target, int level, int xoffset, int yoffset, int width, int height, uint format, uint type, void* pixels);
}