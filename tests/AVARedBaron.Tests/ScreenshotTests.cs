using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVARedBaron.Engine;
using AVARedBaron.Views;

namespace AVARedBaron.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set BARON_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaredbaron-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("BARON_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVARedBaron-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 64250, 12);
        g.HighScores.Insert("AVA", 41800, 8);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_dogfight()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.SetLives(4);
        g.Idle(40);
        c.Advance(0.5);
        Shot(w, c, "wave-start");
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 6 && g.Player.Alive && g.Tracers.Count(t => t.Mine) >= 4 && g.Enemies.Any(e => !e.Dead && g.Camera.ToCamera(e.Pos) is { Z: > 60 and < 700 } p && Math.Abs(p.X / p.Z) < 0.4 && Math.Abs(p.Y / p.Z) < 0.3)));
        Shot(w, c, "dogfight");
    }

    [AvaloniaFact]
    public void A_bank_over_the_mountains()
    {
        var (w, c, g) = Open(3);
        g.Go(2);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 4 && g.Player.Alive && Math.Abs(g.Player.Roll) > 0.5 && g.Mountains.Count(m => g.Camera.ToCamera(new Vec3(m.X, 0, m.Z)) is { Z: > 200 and < 2500 }) >= 2));
        Shot(w, c, "banking");
    }

    [AvaloniaFact]
    public void The_zeppelin()
    {
        var (w, c, g) = Open(4);
        g.Go(3);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Player.Alive && g.Enemies.Any(e => e.Kind == EnemyKind.Zeppelin && !e.Dead && g.Camera.ToCamera(e.Pos) is { Z: > 150 and < 900 } p && Math.Abs(p.X / p.Z) < 0.5), 120));
        Shot(w, c, "zeppelin");
    }

    [AvaloniaFact]
    public void Shot_down()
    {
        var (w, c, g) = Open(5);
        g.Go(2);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 5 && g.Player.Alive && g.Player.Pos.Y > 120));
        for (int i = 0; i < 3; i++) g.PutTracer(g.Player.Pos + g.Player.Forward * (40 + i * 30), -g.Player.Forward * 400);
        for (int t = 0; t < 70; t++) { g.Tick(default); c.Advance(1 / 60.0); }
        Shot(w, c, "shot-down");
    }
}
