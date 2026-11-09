# Changelog

All notable changes to NEON STACK are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses semantic versioning.

## [1.1.0] - 2026-11-09

### Added

- **Game logo** — a generated `NEON STACK` lockup (`Assets/logo_neonstack.png`) replaces the
  plain-text title, and the same block mark (`Assets/icon_neonstack.png`) is set as the window and
  taskbar icon through GLFW. Closes
  [#5](https://github.com/LeTrollologist/Neonstack/issues/5).
- **T-spin detection** — three-corner rule, full and mini classification (including the fifth-kick
  exception), `T-SPIN`, `T-SPIN MINI`, `T-SPIN DOUBLE`, … popups and the guideline scores.
- **Back-to-back scoring** — consecutive tetrises and T-spin clears pay 1.5×, shown as a
  `BACK TO BACK` popup and a `B2B CHAIN` HUD badge. A line-less T-spin keeps the chain alive.
- **Level themes** — each campaign level carries a neon accent used for the board frame, the grid
  glow, the background gradient and the level-name label.
- **Pause ducking** — music drops to 30% while paused and ramps back inside the mixer, with no click.
- **In-repo test harness** — `tests/NeonStack.Tests`, 30 headless tests covering rotation kicks,
  7-bag, drop scoring, T-spins, back-to-back, combos, campaign progression, hold, ghost, pause,
  input suppression, leaderboard, settings, PNG decoding, framebuffer clipping and a render smoke
  test for every screen. Run with `dotnet run --project tests/NeonStack.Tests -c Release`.
- `NEONSTACK_SAVEDIR` environment override so tools and tests can redirect the save folder.
- `logo-gen title` and `logo-gen mark` modes for the new artwork.
- Scoring documentation, refreshed screenshots (`docs/title.png`, `docs/settings.png`, new
  `docs/gameplay.png`) and release-build instructions in the README.

### Changed

- Title screen layout re-balanced around the new logo.
- Game-over screen now states both exit keys (`ENTER` retry, `ESC` title).
- Project version is 1.1.0.

### Fixed

- The `Enter`/`Space` that advanced the last startup splash no longer started a campaign in the same
  frame; confirmation keys are ignored for 0.35 s while control hands over to the title screen.
- `Left`/`Right` on the settings `BACK` row no longer leaves the screen — only `Enter`/`Esc` do.
- Changing the music volume in settings now plays the same feedback blip as the other rows.
- `SpinDetected`/`BackToBack` state can no longer leak across level transitions.

## [1.0.0] - 2026-10-28

### Added

- First release: SRS rotation with kicks, 7-bag randomiser, hold, ghost piece, lock delay, DAS/ARR.
- Ten-level campaign with line goals, unlocks, level-clear and victory screens.
- 640×360 pixel art with integer scaling (1×–6×), scanlines and additive glow.
- Fullscreen by default, F11 toggle, resizable window, `--scale` and `--windowed` flags.
- Procedural chiptune loop and fifteen sound effects, mixed in software (`waveOut` / PCM pipe).
- Settings and a local top-10 leaderboard, persisted per user.
- BRUTAL licence compliance: official logo splash, credits screen, `NOTICE.txt` in the output.
