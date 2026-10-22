# NEON STACK

Neon, CRT-scanlined Tetris on the BRUTAL framework. C# only.

## Run
```
dotnet restore
dotnet run -c Release
```
Needs .NET 10 SDK, Windows or Linux, and a desktop OpenGL driver (the window uses a GL context for the final blit).

## Controls
Arrows / WASD move, Up or X rotate CW, Z rotate CCW, Space hard drop, C hold, P pause, Esc quit. Enter starts.

## Layout
- `Game/TetrisGame.cs`  logic: SRS + kicks, 7-bag, hold, ghost, lock delay (15 resets), DAS/ARR, guideline scoring, combos, best score in `best.txt`
- `Game/Framebuffer.cs` 640x360 software rasteriser, additive glow, scanlines, 3x5 font
- `Game/Scene.cs`       all drawing
- `Platform/BrutalHost.cs` the only BRUTAL-touching file

## BRUTAL licence (it applies to this game)
Per the BRUTAL README you must: show the BRUTAL logo before gameplay (needs to be done, this is simply just a prototype and isn't meant for public consumption), put the BRUTAL copyright notice in the credits, and ship `NOTICE.txt` next to the exe. The title screen says "BUILT ON BRUTAL" but that is not the logo. Drop `docs/branding/LOGO_BRUTAL_BLK.png` from the BRUTAL repo into a splash screen and copy `NOTICE.txt` into the output folder before you distribute.
