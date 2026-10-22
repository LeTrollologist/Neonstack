using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.Numerics;

namespace NeonStack;

/// <summary>
/// The only file that touches BRUTAL. Window, context, input and buffer swap go through BRUTAL's GLFW binding.
/// Pixels are pushed with glDrawPixels (GL 1.1, no loader needed) because BRUTAL ships no renderer.
///
/// Calls from the BRUTAL README (known good):   Glfw.Init, Glfw.CreateWindow(new GlfwWindow.CreateInfo{...}),
///                                              window.ShouldClose, Glfw.PollEvents, Glfw.Terminate.
/// Checked against the BRUTAL source: MakeContextCurrent, SwapBuffers and GetKey are instance methods on GlfwWindow;
/// SwapInterval is static on Glfw; GlfwKey / GlfwKeyAction exist.
/// </summary>
public sealed unsafe class BrutalHost : IDisposable
{
    readonly GlfwWindow window;

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
    };

    public BrutalHost(string title, int width, int height)
    {
        if (!Glfw.Init())
            throw new Exception("Failed to initialize GLFW");

        // No ClientApi hint: GLFW's default is an OpenGL context, which is what glDrawPixels needs.
        window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
        {
            Title = title,
            Size = new int2(width, height),
        });

        window.MakeContextCurrent();
        Glfw.SwapInterval(1);               // vsync
    }

    public bool Running => !window.ShouldClose;

    public void Poll(bool[] down)
    {
        Glfw.PollEvents();
        foreach (var (key, codes) in Map)
        {
            bool d = false;
            foreach (int code in codes)
                if (window.GetKey((GlfwKey)code) == GlfwKeyAction.Press) { d = true; break; }
            down[(int)key] = d;
        }
    }

    public void Present(Framebuffer fb)
    {
        // The initial viewport equals the drawable size, so this also handles HiDPI scaling.
        int* vp = stackalloc int[4];
        Gl.glGetIntegerv(0x0BA2 /* GL_VIEWPORT */, vp);

        Gl.glClearColor(0f, 0f, 0f, 1f);
        Gl.glClear(0x4000 /* GL_COLOR_BUFFER_BIT */);
        Gl.glPixelStorei(0x0CF5 /* GL_UNPACK_ALIGNMENT */, 4);
        Gl.glRasterPos2f(-1f, 1f);                                        // top-left of the viewport
        Gl.glPixelZoom(vp[2] / (float)Framebuffer.W, -vp[3] / (float)Framebuffer.H); // negative Y = draw top-down
        fixed (uint* p = fb.Pixels)
            Gl.glDrawPixels(Framebuffer.W, Framebuffer.H, 0x1908 /* GL_RGBA */, 0x1401 /* GL_UNSIGNED_BYTE */, p);

        window.SwapBuffers();
    }

    public void Dispose()
    {
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

    [DllImport(Lib)] public static extern void glGetIntegerv(uint pname, int* data);
    [DllImport(Lib)] public static extern void glClearColor(float r, float g, float b, float a);
    [DllImport(Lib)] public static extern void glClear(uint mask);
    [DllImport(Lib)] public static extern void glPixelStorei(uint pname, int param);
    [DllImport(Lib)] public static extern void glRasterPos2f(float x, float y);
    [DllImport(Lib)] public static extern void glPixelZoom(float x, float y);
    [DllImport(Lib)] public static extern void glDrawPixels(int width, int height, uint format, uint type, void* data);
}
