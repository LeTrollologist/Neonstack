using System.Diagnostics;
using NeonStack;

using var host = new BrutalHost("NEON STACK", 1280, 720);

var game = new TetrisGame();
var scene = new Scene();
var fb = new Framebuffer();
var down = new bool[(int)Key.Count];

var clock = Stopwatch.StartNew();
double last = clock.Elapsed.TotalSeconds;

while (host.Running && !game.Quit)
{
    host.Poll(down);

    double now = clock.Elapsed.TotalSeconds;
    float dt = (float)(now - last);
    last = now;

    game.Update(dt, down);
    scene.Render(fb, game);
    host.Present(fb);

    if (dt < 0.004f) Thread.Sleep(1); // don't spin if vsync is off
}
