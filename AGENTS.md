# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A first-person biplane dogfight in the style of Atari's 1980 vector arcade game Red Baron, in C# on .NET 10 and
Avalonia 12, drawn through Avalonia's `DrawingContext` as glowing vector lines. One self-contained executable per platform (Windows,
macOS and Linux, x64 and ARM). Everything in it is original: the wireframe models are defined in code, and the
sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVARedBaron.slnx                     # the SDK version is in global.json (10.0.400 or later)
dotnet test AVARedBaron.slnx                      # every test runs headless: no window opens
dotnet run --project src/AVARedBaron              # play it; --no-audio for silence
BARON_SHOTS=$PWD/Images dotnet test tests/AVARedBaron.Tests --filter ScreenshotTests   # refresh the README's pictures
BARON_PROBE=1 dotnet test tests/AVARedBaron.Tests --filter PilotProbe   # how the computer pilot gets on (for tuning)
python3 tools/make_sounds.py                      # rebuild src/AVARedBaron/Sounds (standard-library Python only)
```

`PilotProbe` does nothing unless `BARON_PROBE` is set; `BARON_PROBE=lazy` plays a pilot that just sits and
fires.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVARedBaron/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and a wave's phases.
  - `GameEngine.Play.cs` steps the world: the plane, the enemy squadrons and zeppelins, ground targets and flak,
    the ground and mountains, and the scoring.
  - `Entities.cs` has the `Controls` record, the wrapping `Field` and its fixed step, the `Camera`, and the
    planes, targets, tracers, flak and debris.
  - `Vec3.cs` is the 3D vector (X right, Y up, Z ahead); `Wireframe.cs` has the line-segment models.
  - `ShipPilot.cs` is the computer pilot, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVARedBaron/Views/`: `GameCanvas` (split over partial files) projects the models from the cockpit, draws
  the frame and runs the 60 Hz timer.
- `src/AVARedBaron/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVARedBaron.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests` and
  `PilotProbe`.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so a seed always plays the same game and tests can fly whole waves deterministically. Draw random
  numbers from the engine's seeded `_rng` (reseeded for each new game), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it. Tests that want a quiet sky set the internal `Calm` flag.
- **Input goes through `InputHandler`.** `Dy` works like a flight stick: Up/W or the stick pushed forward gives
  -1 and dives; Down/S or the stick pulled back gives 1 and climbs. Keep keys and stick in step, and test new
  controls through `InputHandler`, not by setting `Controls` directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVARedBaron`); tests load
  the table from a temporary folder (`TempFolder()`) and run silent (`Silent = true`).
- **Line endings.** Keep each file's existing line endings (`app.manifest` is CRLF) so diffs show only real
  changes.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list, the project structure) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
