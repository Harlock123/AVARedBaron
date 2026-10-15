namespace AVARedBaron.Engine;

public sealed partial class GameEngine
{
    public const double BankMax = 0.75, TurnRate = 0.95, PitchMax = 0.5, PitchRate = 1.0, RollRate = 2.4;
    public const double GunRepeat = 0.09, TracerSpeed = 950, TracerLife = 1.3, EnemyTracerSpeed = 720, EnemyTracerLife = 1.6;
    public const double StartSeconds = 2.6, ReadySeconds = 1.6, DyingSeconds = 2.6, ClearSeconds = 2.6;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 40000;
    public const int FighterPoints = 250, ZeppelinPoints = 1000;
    public const double GunRange = 1300, FlakCeiling = 420;

    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Plane Player { get; } = new();
    public Camera Camera { get; } = new();
    public List<Enemy> Enemies { get; } = [];
    public List<GroundTarget> Targets { get; } = [];
    public List<Mountain> Mountains { get; } = [];
    public List<Tracer> Tracers { get; } = [];
    public List<Flak> Flaks { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double WaveTime { get; private set; }
    /// <summary>The plane is low: the pilot's warning light.</summary>
    public bool TooLow => Player.Alive && Player.Pos.Y - GroundAt(Player.Pos) < 60;
    /// <summary>For tests: the enemy neither attacks nor fires, and the guns stay quiet.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private double _attackIn, _warnIn;

    public double Pace => Math.Min(1.8, 1 + 0.08 * (Wave - 1));
    public int Remaining => Enemies.Count(e => !e.Dead);
    private int MaxAttackers => Math.Min(4, 1 + Wave / 2);

    public static int Points(TargetKind k) => k switch { TargetKind.Hangar => 300, TargetKind.FuelTank => 200, _ => 500 };

    /// <summary>How high the ground is here: the mountains, or the plain.</summary>
    public double GroundAt(Vec3 p)
    {
        double h = 0;
        foreach (var m in Mountains) h = Math.Max(h, m.HeightAt(p));
        return h;
    }

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = FirstExtraLife;
        BuildWorld();
        ResetPlayer(new Vec3(Field.Size / 2, 170, Field.Size / 2), 0);
        Enemies.Clear();
        Tracers.Clear();
        Flaks.Clear();
        Wreckage.Clear();
        SetPhase(Phase.Start);
        UpdateCamera();
    }

    /// <summary>The countryside: mountains scattered about, and hangars, fuel tanks and guns on the plains between.</summary>
    private void BuildWorld()
    {
        Mountains.Clear();
        var centre = new Vec3(Field.Size / 2, 0, Field.Size / 2);
        while (Mountains.Count < 22)
        {
            var m = new Mountain(_rng.NextDouble() * Field.Size, _rng.NextDouble() * Field.Size, 160 + _rng.NextDouble() * 160, 110 + _rng.NextDouble() * 190);
            if (Field.GroundDistance(new Vec3(m.X, 0, m.Z), centre) < 700) continue;
            if (Mountains.Any(o => Field.GroundDistance(new Vec3(o.X, 0, o.Z), new Vec3(m.X, 0, m.Z)) < o.Half + m.Half + 120)) continue;
            Mountains.Add(m);
        }
        PlaceTargets();
    }

    private void PlaceTargets()
    {
        Targets.Clear();
        var kinds = new[] { TargetKind.Hangar, TargetKind.FuelTank, TargetKind.Gun };
        int tries = 0;
        while (Targets.Count < 18 && tries++ < 1000)
        {
            var p = new Vec3(_rng.NextDouble() * Field.Size, 0, _rng.NextDouble() * Field.Size);
            if (Mountains.Any(m => Field.GroundDistance(new Vec3(m.X, 0, m.Z), p) < m.Half * 1.5 + 60)) continue;
            if (Targets.Any(t => Field.GroundDistance(t.Pos, p) < 300)) continue;
            Targets.Add(new GroundTarget { Kind = kinds[Targets.Count % 3], Pos = p, Yaw = _rng.NextDouble() * Math.PI, FireIn = 2 + _rng.NextDouble() * 3 });
        }
    }

    /// <summary>The next squadron, coming in from all round; a zeppelin with every third.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        WaveTime = 0;
        Enemies.Clear();
        Tracers.Clear();
        Flaks.Clear();
        Player.Damage = 0;
        _attackIn = 2;
        if (Targets.All(t => t.Dead)) PlaceTargets();
        int fighters = Math.Min(7, 1 + wave);
        for (int i = 0; i < fighters; i++)
        {
            double bearing = Player.Yaw + (i - (fighters - 1) / 2.0) * 0.5 + (_rng.NextDouble() - 0.5) * 0.3;
            double dist = 1500 + _rng.NextDouble() * 600;
            var pos = Field.Wrap(Player.Pos + new Vec3(Math.Sin(bearing) * dist, 0, Math.Cos(bearing) * dist)) with { Y = 140 + _rng.NextDouble() * 160 };
            Enemies.Add(new Enemy
            {
                Kind = EnemyKind.Fighter, Pos = pos, Yaw = bearing + Math.PI, Speed = Math.Min(290, 225 + 8 * wave),
                CircleX = _rng.NextDouble() * Math.PI * 2, CircleAlt = pos.Y, FireIn = 1 + _rng.NextDouble(),
            });
        }
        if (wave % 3 == 0)
        {
            double bearing = Player.Yaw + (_rng.NextDouble() - 0.5);
            var pos = Field.Wrap(Player.Pos + new Vec3(Math.Sin(bearing) * 1300, 0, Math.Cos(bearing) * 1300)) with { Y = 230 };
            Enemies.Add(new Enemy { Kind = EnemyKind.Zeppelin, Pos = pos, Yaw = bearing + Math.PI / 2, Speed = 60, Hp = 8 });
        }
        SetPhase(Phase.Start);
        Say($"WAVE {wave}", wave % 3 == 0 ? "A ZEPPELIN AND ITS ESCORT" : fighters == 1 ? "A LONE FIGHTER" : $"A SQUADRON OF {fighters}", StartSeconds);
        Play(Sfx.WaveStart);
    }

    private void ResetPlayer(Vec3 pos, double yaw)
    {
        var p = Player;
        p.Pos = pos;
        p.Yaw = yaw;
        p.Pitch = p.Roll = 0;
        p.Alive = true;
        p.Damage = 0;
        p.GunCool = p.Hurt = 0;
    }

    private void UpdateCamera()
    {
        Camera.Position = Player.Pos;
        Camera.Yaw = Player.Yaw;
        Camera.Pitch = Player.Pitch;
        Camera.Roll = Player.Roll;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        WaveTime += dt;
        Player.Hurt = Math.Max(0, Player.Hurt - dt);
        switch (Phase)
        {
            case Phase.Start:
                Fly(c, dt, canFire: false);
                MoveEnemies(dt, active: false);
                MoveTracers(dt);
                Crashes();
                if (Phase == Phase.Start && PhaseTime > (Message == "READY" ? ReadySeconds : StartSeconds))
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                Fly(c, dt, canFire: true);
                MoveEnemies(dt, active: true);
                GroundFire(dt);
                MoveTracers(dt);
                MoveFlak(dt);
                Crashes();
                if (Phase == Phase.Play) Progress();
                break;
            case Phase.Dying:
                SpinIn(dt);
                MoveEnemies(dt, active: false);
                MoveTracers(dt);
                MoveFlak(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.Clear:
                Fly(c, dt, canFire: false);
                MoveTracers(dt);
                Crashes();
                if (Phase == Phase.Clear && PhaseTime > ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        MoveWreckage(dt);
        UpdateCamera();
    }

    // ───────────────────────────── flying ─────────────────────────────

    /// <summary>Bank to turn, push forward to dive and pull back to climb; always at full speed.</summary>
    private void Fly(Controls c, double dt, bool canFire)
    {
        var p = Player;
        if (!p.Alive) return;
        p.Roll = Approach(p.Roll, Math.Sign(c.Dx) * BankMax, RollRate * dt);
        p.Yaw += p.Roll / BankMax * TurnRate * dt;
        p.Pitch = Approach(p.Pitch, Math.Sign(c.Dy) * PitchMax, PitchRate * dt);
        if (p.Pos.Y >= Field.Ceiling && p.Pitch > 0) p.Pitch = Approach(p.Pitch, 0, PitchRate * 2 * dt);
        p.Pos = Field.Wrap(p.Pos + p.Forward * Plane.Speed * dt);
        if (p.Pos.Y > Field.Ceiling) p.Pos = p.Pos with { Y = Field.Ceiling };
        // Twin guns, firing in turn.
        p.GunCool -= dt;
        if (canFire && c.Fire && p.GunCool <= 0)
        {
            p.GunCool = GunRepeat;
            p.GunSide = -p.GunSide;
            var right = new Vec3(Math.Cos(p.Yaw), 0, -Math.Sin(p.Yaw));
            Tracers.Add(new Tracer { Pos = Field.Wrap(p.Pos + right * (5 * p.GunSide) + p.Forward * 6), Vel = p.Forward * TracerSpeed, Life = TracerLife, Mine = true });
            if (p.GunSide > 0) Play(Sfx.Guns);
        }
        _warnIn -= dt;
        if (canFire && TooLow && _warnIn <= 0)
        {
            _warnIn = 0.7;
            Play(Sfx.Warning);
        }
    }

    private static double Approach(double v, double target, double step) =>
        v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);

    /// <summary>Into the ground or a mountainside.</summary>
    private void Crashes()
    {
        var p = Player;
        if (!p.Alive) return;
        if (p.Pos.Y < Math.Max(Field.Floor, GroundAt(p.Pos) + 3)) Die("CRASHED");
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    private void MoveEnemies(double dt, bool active)
    {
        var p = Player;
        foreach (var e in Enemies)
        {
            if (e.Dead) continue;
            e.T += dt;
            e.StateTime += dt;
            if (e.Kind == EnemyKind.Zeppelin)
            {
                // It drones on, turning slowly.
                e.Yaw += 0.04 * dt;
                e.Pos = Field.Wrap(e.Pos + e.Forward * e.Speed * dt);
                continue;
            }
            switch (e.State)
            {
                case EnemyState.Patrol:
                {
                    // Circling at a distance, waiting its turn.
                    double a = e.CircleX + e.T * 0.22;
                    var spot = Field.Wrap(p.Pos + new Vec3(Math.Sin(a) * 900, 0, Math.Cos(a) * 900)) with { Y = e.CircleAlt };
                    Steer(e, spot, 0.8, dt);
                    break;
                }
                case EnemyState.Attack:
                {
                    double dist = Field.Distance(e.Pos, p.Pos);
                    var lead = Field.Wrap(p.Pos + p.Forward * Plane.Speed * (dist / EnemyTracerSpeed));
                    // Close in, it aims to pass just over you rather than into you.
                    var steerTo = dist < 380 ? lead with { Y = lead.Y + 70 } : lead;
                    Steer(e, steerTo, Math.Min(1.5, 0.9 + 0.07 * Wave), dt);
                    if (active && !Calm && p.Alive) Shoot(e, lead, dist, dt);
                    if (dist < 220 || e.StateTime > 14) { e.State = EnemyState.Break; e.StateTime = 0; }
                    break;
                }
                case EnemyState.Break:
                {
                    // Away and up after a pass, then round again.
                    var away = Field.Delta(p.Pos, e.Pos);
                    var spot = Field.Wrap(e.Pos + new Vec3(away.X, 0, away.Z).Normalized() * 600) with { Y = Math.Min(Field.Ceiling - 40, p.Pos.Y + 120) };
                    Steer(e, spot, 1.0, dt);
                    if (e.StateTime > 2.6) { e.State = EnemyState.Patrol; e.StateTime = 0; e.CircleAlt = 140 + _rng.NextDouble() * 180; e.CircleX = Field.Bearing(p.Pos, e.Pos) - e.T * 0.22; }
                    break;
                }
            }
            e.Pos = Field.Wrap(e.Pos + e.Forward * e.Speed * dt);
            // Rammed.
            if (active && p.Alive && Field.Distance(e.Pos, p.Pos) < 26)
            {
                Kill(e);
                Die("COLLIDED");
                return;
            }
        }
        if (active && !Calm) Scramble(dt);
    }

    /// <summary>Turn and climb or dive towards a point, keeping clear of the ground and the mountains.</summary>
    private void Steer(Enemy e, Vec3 to, double turn, double dt)
    {
        double diff = Field.AngleDiff(Field.Bearing(e.Pos, to), e.Yaw);
        e.Yaw += Math.Clamp(diff, -turn * dt, turn * dt);
        e.Roll = Approach(e.Roll, Math.Clamp(diff * 2, -0.8, 0.8), 2 * dt);
        var d = Field.Delta(e.Pos, to);
        double want = Math.Clamp(Math.Atan2(d.Y, Math.Sqrt(d.X * d.X + d.Z * d.Z)), -0.45, 0.45);
        // Pull up from the ground, and over any mountain ahead.
        var ahead = Field.Wrap(e.Pos + e.Forward * 220);
        if (e.Pos.Y < 70 || GroundAt(ahead) > e.Pos.Y - 50 || GroundAt(e.Pos) > e.Pos.Y - 60) want = 0.45;
        if (e.Pos.Y > Field.Ceiling - 20) want = Math.Min(want, 0);
        e.Pitch = Approach(e.Pitch, want, 0.9 * dt);
    }

    /// <summary>A burst at the lead point when it's lined up and in range.</summary>
    private void Shoot(Enemy e, Vec3 lead, double dist, double dt)
    {
        e.FireIn -= dt;
        if (e.Burst > 0)
        {
            if (e.FireIn > 0) return;
            e.Burst--;
            e.FireIn = e.Burst > 0 ? 0.08 : (1.6 + _rng.NextDouble()) / Pace;
            double spread = 0.03 / Pace;
            var dir = (Field.Delta(e.Pos, lead).Normalized() + new Vec3(_rng.NextDouble() - 0.5, _rng.NextDouble() - 0.5, _rng.NextDouble() - 0.5) * spread).Normalized();
            Tracers.Add(new Tracer { Pos = e.Pos, Vel = dir * EnemyTracerSpeed, Life = EnemyTracerLife });
            if (e.Burst % 2 == 0) Play(Sfx.EnemyGuns);
            return;
        }
        if (e.FireIn > 0 || dist > 900) return;
        var to = Field.Delta(e.Pos, lead).Normalized();
        var f = e.Forward;
        if (to.X * f.X + to.Y * f.Y + to.Z * f.Z < Math.Cos(0.14)) return;
        e.Burst = 4;
        e.FireIn = 0;
    }

    /// <summary>Every so often another fighter breaks from circling to attack, a few at a time.</summary>
    private void Scramble(double dt)
    {
        _attackIn -= dt;
        if (_attackIn > 0) return;
        int attacking = Enemies.Count(e => !e.Dead && e.State == EnemyState.Attack);
        if (attacking >= MaxAttackers) return;
        var next = Enemies.Where(e => !e.Dead && e.Kind == EnemyKind.Fighter && e.State == EnemyState.Patrol)
            .OrderBy(e => Field.Distance(e.Pos, Player.Pos)).FirstOrDefault();
        if (next == null) return;
        next.State = EnemyState.Attack;
        next.StateTime = 0;
        _attackIn = Math.Max(1.2, 4 - 0.3 * Wave) * (0.6 + 0.8 * _rng.NextDouble());
    }

    /// <summary>The anti-aircraft guns throw flak at where you'll be, when you're near and low enough.</summary>
    private void GroundFire(double dt)
    {
        if (Calm) return;
        var p = Player;
        foreach (var t in Targets)
        {
            if (t.Dead || t.Kind != TargetKind.Gun) continue;
            t.FireIn -= dt;
            if (t.FireIn > 0) continue;
            t.FireIn = (2.4 + _rng.NextDouble() * 1.5) / Pace;
            if (Field.GroundDistance(t.Pos, p.Pos) > 1100 || p.Pos.Y > FlakCeiling) continue;
            double fuse = 0.9;
            var aim = Field.Wrap(p.Pos + p.Forward * Plane.Speed * fuse + new Vec3(_rng.NextDouble() - 0.5, _rng.NextDouble() - 0.5, _rng.NextDouble() - 0.5) * 110);
            Flaks.Add(new Flak { Pos = aim, Fuse = fuse });
        }
    }

    private void MoveFlak(double dt)
    {
        foreach (var f in Flaks)
        {
            if (!f.Burst)
            {
                f.Fuse -= dt;
                if (f.Fuse > 0) continue;
                f.Burst = true;
                Play(Sfx.Flak);
                if (Player.Alive && Phase == Phase.Play && Field.Distance(f.Pos, Player.Pos) < Flak.BurstRadius) Hurt("FLAK");
            }
            else f.Age += dt;
        }
        Flaks.RemoveAll(f => f.Done);
    }

    private void MoveTracers(double dt)
    {
        foreach (var t in Tracers)
        {
            var from = t.Pos;
            t.Pos = Field.Wrap(t.Pos + t.Vel * dt);
            t.Life -= dt;
            if (t.Life <= 0 || t.Pos.Y < GroundAt(t.Pos)) { t.Dead = true; continue; }
            if (Phase != Phase.Play) continue;
            if (t.Mine) MyTracerHits(t, from);
            else if (Player.Alive && SegmentNear(from, t.Pos, Player.Pos, 11))
            {
                t.Dead = true;
                Hurt("SHOT DOWN");
            }
        }
        Tracers.RemoveAll(t => t.Dead);
    }

    /// <summary>Whether the path from one point to the next passes within a distance of a centre.</summary>
    private static bool SegmentNear(Vec3 a, Vec3 b, Vec3 centre, double r)
    {
        var ab = Field.Delta(a, b);
        var ac = Field.Delta(a, centre);
        double len2 = ab.X * ab.X + ab.Y * ab.Y + ab.Z * ab.Z;
        double t = len2 < 1e-9 ? 0 : Math.Clamp((ac.X * ab.X + ac.Y * ab.Y + ac.Z * ab.Z) / len2, 0, 1);
        var miss = ac - ab * t;
        return miss.Length < r;
    }

    private void MyTracerHits(Tracer t, Vec3 from)
    {
        foreach (var e in Enemies)
        {
            if (e.Dead) continue;
            bool hit = SegmentNear(from, t.Pos, e.Pos, e.Radius);
            // A zeppelin is long: try its nose and its tail too.
            if (!hit && e.Kind == EnemyKind.Zeppelin)
                hit = SegmentNear(from, t.Pos, Field.Wrap(e.Pos + e.Forward * 55), 40) || SegmentNear(from, t.Pos, Field.Wrap(e.Pos - e.Forward * 55), 40);
            if (!hit) continue;
            t.Dead = true;
            if (--e.Hp <= 0) Kill(e);
            else { Play(Sfx.Hit); Effects.Add(new Effect { Kind = EffectKind.Burst, Pos = t.Pos, Life = 0.25, Size = 0.5, Colour = 0xFFFFE070 }); }
            return;
        }
        foreach (var g in Targets)
        {
            if (g.Dead || !SegmentNear(from, t.Pos, g.Pos with { Y = g.Radius * 0.6 }, g.Radius)) continue;
            t.Dead = true;
            g.Dead = true;
            int points = Points(g.Kind);
            AddScore(points);
            Effects.Add(new Effect { Kind = EffectKind.Score, Pos = g.Pos with { Y = 60 }, Text = points.ToString(), Colour = 0xFFFFE070 });
            Effects.Add(new Effect { Kind = EffectKind.Burst, Pos = g.Pos with { Y = 20 }, Life = 0.9, Size = 2.2, Colour = 0xFFFF9A40 });
            Shatter(g.Kind switch { TargetKind.Hangar => Wireframe.Hangar, TargetKind.FuelTank => Wireframe.FuelTank, _ => Wireframe.Gun }, g.Pos, g.Yaw, 0, 0, 34, 0xFFFFB060, up: true);
            Play(Sfx.TargetDown);
            return;
        }
    }

    private void Kill(Enemy e)
    {
        e.Dead = true;
        bool zep = e.Kind == EnemyKind.Zeppelin;
        int points = zep ? ZeppelinPoints : FighterPoints;
        AddScore(points);
        Effects.Add(new Effect { Kind = EffectKind.Score, Pos = e.Pos with { Y = e.Pos.Y + 30 }, Text = points.ToString(), Colour = zep ? 0xFFFFE070 : 0xFFFFFFFF });
        Effects.Add(new Effect { Kind = EffectKind.Burst, Pos = e.Pos, Life = zep ? 1.4 : 0.8, Size = zep ? 3 : 1.4, Colour = 0xFFFFB040 });
        Shatter(zep ? Wireframe.Zeppelin : Wireframe.Biplane, e.Pos, e.Yaw, e.Pitch, e.Roll, zep ? 30 : 22, zep ? 0xFFB0D0FF : 0xFFFFFFFF, up: false);
        Play(zep ? Sfx.BlimpDown : Sfx.PlaneDown);
    }

    /// <summary>A model blown into its separate lines, which tumble away and fall.</summary>
    private void Shatter(Wireframe model, Vec3 at, double yaw, double pitch, double roll, double size, uint colour, bool up)
    {
        int step = Math.Max(1, model.Lines.Length / 28);
        for (int i = 0; i < model.Lines.Length; i += step)
        {
            var (a, b) = model.Lines[i];
            var wa = (a * size).RotateZ(-roll).RotateX(pitch).RotateY(yaw);
            var wb = (b * size).RotateZ(-roll).RotateX(pitch).RotateY(yaw);
            var mid = (wa + wb) * 0.5;
            var vel = new Vec3(_rng.NextDouble() - 0.5, _rng.NextDouble() * (up ? 1.2 : 0.6) - (up ? 0 : 0.3), _rng.NextDouble() - 0.5) * 140;
            Wreckage.Add(new Debris { Pos = Field.Wrap(at + mid), Vel = vel, A = wa - mid, B = wb - mid, Spin = (_rng.NextDouble() - 0.5) * 8, Colour = colour });
        }
    }

    private void MoveWreckage(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.Vel = d.Vel with { Y = d.Vel.Y - 160 * dt };
            d.Pos = Field.Wrap(d.Pos + d.Vel * dt);
            if (d.Pos.Y < 0) d.Pos = d.Pos with { Y = 0 };
            d.Angle += d.Spin * dt;
        }
        Wreckage.RemoveAll(d => d.Age > d.Life);
    }

    // ───────────────────────────── losing a plane ─────────────────────────────

    /// <summary>A hit: the plane can take a couple, but the third brings it down.</summary>
    private void Hurt(string cause)
    {
        var p = Player;
        p.Damage++;
        p.Hurt = 0.35;
        Play(Sfx.Damage);
        if (p.Damage >= Plane.MaxDamage) Die(cause);
    }

    private void Die(string cause)
    {
        Cause = cause;
        Player.Alive = false;
        Lives--;
        Effects.Add(new Effect { Kind = EffectKind.Burst, Pos = Player.Pos + Player.Forward * 30, Life = 1, Size = 2, Colour = 0xFFFF7030 });
        SetPhase(Phase.Dying);
        Music(false);
        Play(cause == "CRASHED" ? Sfx.Crash : Sfx.PlaneDown);
        Say(cause == "CRASHED" ? "CRASHED" : cause == "COLLIDED" ? "COLLISION" : "SHOT DOWN", null, DyingSeconds);
    }

    /// <summary>Spinning in: the nose drops and the world turns about you.</summary>
    private void SpinIn(double dt)
    {
        var p = Player;
        if (p.Pos.Y <= GroundAt(p.Pos) + 3) return;
        p.Roll += 3.5 * dt;
        p.Yaw += 0.6 * dt;
        p.Pitch = Approach(p.Pitch, -0.9, 0.8 * dt);
        p.Pos = Field.Wrap(p.Pos + p.Forward * Plane.Speed * 0.8 * dt);
        p.Pos = p.Pos with { Y = Math.Max(GroundAt(p.Pos) + 3, p.Pos.Y) };
    }

    /// <summary>The next plane, high over open country, with the enemy pulled back to circling.</summary>
    private void Respawn()
    {
        var p = Player;
        var at = p.Pos with { Y = 200 };
        // Somewhere clear of the mountains.
        for (int i = 0; i < 40 && GroundAt(at) > 0; i++) at = Field.Wrap(at + new Vec3(150, 0, 90));
        ResetPlayer(at, p.Yaw);
        Tracers.Clear();
        Flaks.Clear();
        foreach (var e in Enemies.Where(e => !e.Dead))
        {
            if (e.Kind == EnemyKind.Fighter) { e.State = EnemyState.Patrol; e.StateTime = 0; e.Burst = 0; e.FireIn = 2; }
            var d = Field.Delta(at, e.Pos);
            if (d.Length < 900)
            {
                var push = new Vec3(d.X, 0, d.Z).Normalized() * 1100;
                e.Pos = Field.Wrap(at + (push.Length < 1 ? new Vec3(0, 0, 1100) : push)) with { Y = e.Pos.Y };
            }
        }
        _attackIn = 3;
        SetPhase(Phase.Start);
        Say("READY", null, ReadySeconds);
    }

    private void Progress()
    {
        if (Calm || Remaining > 0) return;
        SetPhase(Phase.Clear);
        Player.Damage = 0;
        Tracers.RemoveAll(t => !t.Mine);
        Flaks.Clear();
        Music(false);
        Say("SQUADRON DOWN", "DAMAGE REPAIRED", ClearSeconds);
        Play(Sfx.Clear);
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal void ClearEnemies() => Enemies.Clear();
    internal void ClearGround() { Mountains.Clear(); Targets.Clear(); }

    internal Enemy PutEnemy(EnemyKind kind, Vec3 pos, double yaw = 0, EnemyState state = EnemyState.Patrol)
    {
        var e = new Enemy { Kind = kind, Pos = pos, Yaw = yaw, State = state, Speed = kind == EnemyKind.Zeppelin ? 60 : 230, Hp = kind == EnemyKind.Zeppelin ? 8 : 1, CircleAlt = pos.Y };
        Enemies.Add(e);
        return e;
    }

    internal GroundTarget PutTarget(TargetKind kind, Vec3 pos)
    {
        var t = new GroundTarget { Kind = kind, Pos = pos };
        Targets.Add(t);
        return t;
    }

    internal void PutMountain(Mountain m) => Mountains.Add(m);
    internal void PutTracer(Vec3 pos, Vec3 vel, bool mine = false) => Tracers.Add(new Tracer { Pos = pos, Vel = vel, Life = 2, Mine = mine });
    internal void PutFlak(Vec3 pos, double fuse = 0.05) => Flaks.Add(new Flak { Pos = pos, Fuse = fuse });

    internal void SetPlayer(Vec3 pos, double yaw = 0, double pitch = 0)
    {
        ResetPlayer(pos, yaw);
        Player.Pitch = pitch;
        UpdateCamera();
    }

    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
