using AVARedBaron.Engine;

namespace AVARedBaron.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaredbaron-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly Vec3 Home = new(Field.Size / 2, 200, Field.Size / 2);

    /// <summary>A wave in play over open country (no mountains or targets unless a test puts them), with no enemies unless asked and nothing firing on its own.</summary>
    private static GameEngine Game(int wave = 1, bool keepEnemies = false)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        g.ClearGround();
        if (!keepEnemies) g.ClearEnemies();
        g.SetPlayer(Home);
        return g;
    }

    /// <summary>A real game in play, with plenty of planes.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void The_plane_flies_on_at_full_speed()
    {
        var g = Game();
        g.Idle(60);
        Assert.Equal(Home.Z + Plane.Speed, g.Player.Pos.Z, 0);
        Assert.Equal(Home.Y, g.Player.Pos.Y, 3);
    }

    [Fact]
    public void Banking_turns_the_plane_and_tilts_the_view()
    {
        var g = Game();
        g.Run(60, new Controls(Dx: 1));
        Assert.True(g.Player.Yaw > 0.3, "it turned right");
        Assert.True(g.Player.Roll > 0.5, "it banked right");
        Assert.Equal(g.Player.Roll, g.Camera.Roll);
        g.Run(90, default);
        Assert.Equal(0, g.Player.Roll, 3);
    }

    [Fact]
    public void Pushing_forward_dives_and_pulling_back_climbs()
    {
        var g = Game();
        g.Run(60, new Controls(Dy: -1));
        Assert.True(g.Player.Pos.Y < Home.Y - 40);
        g.SetPlayer(Home);
        g.Run(60, new Controls(Dy: 1));
        Assert.True(g.Player.Pos.Y > Home.Y + 40);
    }

    [Fact]
    public void There_is_a_ceiling()
    {
        var g = Game();
        g.Run(60 * 10, new Controls(Dy: 1));
        Assert.True(g.Player.Pos.Y <= Field.Ceiling);
        Assert.True(g.Player.Alive);
    }

    [Fact]
    public void Flying_into_the_ground_is_a_crash()
    {
        var g = Game();
        int lives = g.Lives;
        g.Run(60 * 6, new Controls(Dy: -1));
        Assert.Equal("CRASHED", g.Cause);
        Assert.Equal(lives - 1, g.Lives);
    }

    [Fact]
    public void Flying_into_a_mountain_is_a_crash_and_the_warning_sounds_first()
    {
        var g = Game();
        g.PutMountain(new Mountain(Home.X, Home.Z + 500, 200, 400));
        bool warned = false;
        for (int i = 0; i < 60 * 4 && g.Player.Alive; i++)
        {
            g.Tick(default);
            if (g.TooLow) warned = true;
        }
        Assert.Equal("CRASHED", g.Cause);
        Assert.True(warned);
    }

    [Fact]
    public void The_world_wraps_round()
    {
        var g = Game();
        g.SetPlayer(new Vec3(Field.Size - 10, 200, 100), Math.PI / 2);
        g.Idle(10);
        Assert.InRange(g.Player.Pos.X, 0, 100);
    }

    // ───────────────────────────── shooting ─────────────────────────────

    [Fact]
    public void The_guns_fire_in_turn_while_the_trigger_is_held()
    {
        var g = Game();
        g.Run(30, FireButton);
        var mine = g.Tracers.Where(t => t.Mine).ToList();
        Assert.InRange(mine.Count, 4, 7);
        Assert.Contains(mine, t => Field.Delta(g.Player.Pos, t.Pos).X > 0);
        Assert.Contains(mine, t => Field.Delta(g.Player.Pos, t.Pos).X < 0);
    }

    [Fact]
    public void A_fighter_in_the_sights_goes_down_for_250()
    {
        var g = Game();
        var e = g.PutEnemy(EnemyKind.Fighter, Home + new Vec3(0, 0, 500), 0);
        e.Speed = 0;
        g.Run(60, FireButton);
        Assert.True(e.Dead);
        Assert.Equal(GameEngine.FighterPoints, g.Score);
        Assert.NotEmpty(g.Wreckage);
    }

    [Fact]
    public void A_zeppelin_takes_a_lot_of_hits_and_is_worth_1000()
    {
        var g = Game();
        var z = g.PutEnemy(EnemyKind.Zeppelin, Home + new Vec3(0, 0, 900), Math.PI / 2);
        z.Speed = 0;
        g.Run(12, FireButton);
        g.Idle(70);
        Assert.False(z.Dead);
        g.Run(120, FireButton);
        Assert.True(z.Dead);
        Assert.Equal(GameEngine.ZeppelinPoints, g.Score);
    }

    [Fact]
    public void Ground_targets_can_be_strafed()
    {
        var g = Game();
        var t = g.PutTarget(TargetKind.Gun, new Vec3(Home.X, 0, Home.Z + 700));
        g.SetPlayer(Home with { Y = 150 }, 0, -Math.Atan2(150, 700));
        // Hold the dive while firing.
        for (int i = 0; i < 90 && !t.Dead; i++) g.Tick(new Controls(Dy: g.Player.Pitch > -0.2 ? -1 : 0, Fire: true));
        Assert.True(t.Dead);
        Assert.Equal(GameEngine.Points(TargetKind.Gun), g.Score);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Fact]
    public void Fighters_circle_then_come_at_you_a_few_at_a_time()
    {
        var g = Real(4);
        Assert.All(g.Enemies, e => Assert.Equal(EnemyState.Patrol, e.State));
        int most = 0;
        for (int i = 0; i < 60 * 15; i++)
        {
            g.Tick(default);
            most = Math.Max(most, g.Enemies.Count(e => !e.Dead && e.State == EnemyState.Attack));
        }
        Assert.InRange(most, 1, 3);
    }

    [Fact]
    public void An_attacker_fires_and_its_bullets_hurt()
    {
        var g = Game();
        g.Calm = false;
        var e = g.PutEnemy(EnemyKind.Fighter, Home + new Vec3(0, 0, 800), Math.PI, EnemyState.Attack);
        e.FireIn = 0;
        bool fired = false;
        for (int i = 0; i < 60 * 3 && g.Player.Alive; i++)
        {
            g.Tick(default);
            if (g.Tracers.Any(t => !t.Mine)) fired = true;
        }
        Assert.True(fired);
        Assert.True(g.Player.Damage > 0 || !g.Player.Alive, "and one found its mark");
        Assert.NotNull(e);
    }

    [Fact]
    public void Three_hits_bring_you_down()
    {
        var g = Game();
        int lives = g.Lives;
        for (int i = 0; i < 2; i++)
        {
            g.PutTracer(g.Player.Pos + g.Player.Forward * 60, -g.Player.Forward * 600);
            g.Idle(10);
        }
        Assert.Equal(2, g.Player.Damage);
        Assert.True(g.Player.Alive);
        g.PutTracer(g.Player.Pos + g.Player.Forward * 60, -g.Player.Forward * 600);
        g.Idle(10);
        Assert.False(g.Player.Alive);
        Assert.Equal("SHOT DOWN", g.Cause);
        Assert.Equal(lives - 1, g.Lives);
    }

    [Fact]
    public void Flak_bursting_close_by_does_damage()
    {
        var g = Game();
        g.PutFlak(g.Player.Pos + g.Player.Forward * 10, 0.02);
        g.Idle(3);
        Assert.Equal(1, g.Player.Damage);
        g.PutFlak(g.Player.Pos + new Vec3(0, 200, 0), 0.02);
        g.Idle(3);
        Assert.Equal(1, g.Player.Damage);
    }

    [Fact]
    public void Flak_guns_only_reach_low_flyers()
    {
        var g = Game();
        g.Calm = false;
        g.PutTarget(TargetKind.Gun, new Vec3(Home.X, 0, Home.Z + 600));
        g.SetPlayer(Home with { Y = Field.Ceiling - 10 });
        g.Run(60 * 4, new Controls(Dy: 1));
        Assert.Empty(g.Flaks);
        g.SetPlayer(Home with { Y = 200 });
        int most = 0;
        for (int i = 0; i < 60 * 5; i++) { g.Tick(default); most = Math.Max(most, g.Flaks.Count); }
        Assert.True(most > 0);
    }

    [Fact]
    public void Running_into_a_fighter_brings_you_both_down()
    {
        var g = Game();
        var e = g.PutEnemy(EnemyKind.Fighter, Home + new Vec3(0, 0, 20), Math.PI);
        g.Tick(default);
        Assert.True(e.Dead);
        Assert.Equal("COLLIDED", g.Cause);
    }

    // ───────────────────────────── waves and lives ─────────────────────────────

    [Fact]
    public void Each_wave_brings_a_bigger_squadron_and_every_third_a_zeppelin()
    {
        int Fighters(int w) => Real(w).Enemies.Count(e => e.Kind == EnemyKind.Fighter);
        Assert.Equal(2, Fighters(1));
        Assert.True(Fighters(4) > Fighters(2));
        Assert.Contains(Real(3).Enemies, e => e.Kind == EnemyKind.Zeppelin);
        Assert.DoesNotContain(Real(2).Enemies, e => e.Kind == EnemyKind.Zeppelin);
    }

    [Fact]
    public void Shooting_down_the_squadron_repairs_the_plane_and_brings_the_next()
    {
        var g = Game();
        g.PutTracer(g.Player.Pos + g.Player.Forward * 60, -g.Player.Forward * 600);
        g.Idle(10);
        Assert.Equal(1, g.Player.Damage);
        var e = g.PutEnemy(EnemyKind.Fighter, g.Player.Pos + g.Player.Forward * 400, 0);
        e.Speed = 0;
        g.Run(40, FireButton);
        g.Calm = false;
        g.Idle(2);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(0, g.Player.Damage);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(2, g.Wave);
    }

    [Fact]
    public void The_next_plane_comes_high_over_open_country_with_the_enemy_pulled_back()
    {
        var g = Game(keepEnemies: false);
        var e = g.PutEnemy(EnemyKind.Fighter, Home + new Vec3(300, 0, 300), 0, EnemyState.Attack);
        for (int i = 0; i < 60 * 6 && g.Player.Alive; i++) g.Tick(new Controls(Dy: -1));
        Assert.False(g.Player.Alive);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(g.Player.Alive);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.True(g.Player.Pos.Y > 150);
        Assert.Equal(EnemyState.Patrol, e.State);
        Assert.True(Field.Distance(e.Pos, g.Player.Pos) > 800);
    }

    [Fact]
    public void The_game_ends_with_the_last_plane_and_a_good_score_gets_initials()
    {
        var g = Game();
        g.SetLives(1);
        g.SetScore(500000);
        for (int i = 0; i < 60 * 6 && g.Player.Alive; i++) g.Tick(new Controls(Dy: -1));
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(4 * 60);
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void An_extra_plane_at_20000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(19800);
        var e = g.PutEnemy(EnemyKind.Fighter, g.Player.Pos + g.Player.Forward * 400, 0);
        e.Speed = 0;
        g.Run(40, FireButton);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_world_has_mountains_and_targets_and_none_on_top_of_you()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        Assert.True(g.Mountains.Count >= 15);
        Assert.True(g.Targets.Count >= 12);
        Assert.Equal(0, g.GroundAt(g.Player.Pos));
        Assert.All(g.Targets, t => Assert.Equal(0, g.GroundAt(t.Pos)));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_pilot_shoots_down_the_first_squadrons()
    {
        var g = Real();
        for (int i = 0; i < 60 * 120 && g.Wave < 3; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 3, $"reached wave {g.Wave}");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Player.Pos.X:0.00} {g.Player.Pos.Z:0.00} {g.Remaining}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real(2);
        g.Idle(60);
        var before = g.Player.Pos;
        g.Pause();
        g.Idle(120);
        Assert.Equal(before, g.Player.Pos);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
