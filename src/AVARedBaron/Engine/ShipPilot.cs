namespace AVARedBaron.Engine;

/// <summary>
/// The computer pilot, for the demonstration (and the tests). It keeps clear of the ground and the mountains
/// first of all; otherwise it turns and climbs or dives after the nearest enemy (one that's attacking it
/// first), leading it so the bullets meet it, and fires when it's lined up and in range.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var p = g.Player;
        if (!p.Alive || g.Phase == Phase.Dying) return default;
        // The ground below and ahead.
        var flat = new Vec3(Math.Sin(p.Yaw), 0, Math.Cos(p.Yaw));
        double terrain = g.GroundAt(p.Pos);
        foreach (double k in new[] { 120.0, 240, 360, 480 }) terrain = Math.Max(terrain, g.GroundAt(Field.Wrap(p.Pos + flat * k)));
        bool low = p.Pos.Y - terrain < 90 || p.Pos.Y < 70;

        Enemy? target = null;
        double best = double.MaxValue;
        foreach (var e in g.Enemies)
        {
            if (e.Dead) continue;
            double d = Field.Distance(p.Pos, e.Pos) - (e.State == EnemyState.Attack ? 300 : 0);
            if (d < best) { best = d; target = e; }
        }
        if (target == null)
        {
            Doing = low ? "climb" : "cruise";
            return new Controls(0, low ? 1 : p.Pos.Y > 260 ? -1 : 0, false);
        }
        double dist = Field.Distance(p.Pos, target.Pos);
        var aim = Field.Wrap(target.Pos + target.Forward * target.Speed * (dist / GameEngine.TracerSpeed));
        double yawErr = Field.AngleDiff(Field.Bearing(p.Pos, aim), p.Yaw);
        var d3 = Field.Delta(p.Pos, aim);
        double wantPitch = Math.Clamp(Math.Atan2(d3.Y, Math.Sqrt(d3.X * d3.X + d3.Z * d3.Z)), -GameEngine.PitchMax, GameEngine.PitchMax);
        int dx = Math.Abs(yawErr) > 0.03 ? Math.Sign(yawErr) : 0;
        int dy = Math.Abs(wantPitch - p.Pitch) > 0.03 ? Math.Sign(wantPitch - p.Pitch) : 0;
        // Something about to fly into us: dive under it.
        foreach (var e in g.Enemies)
        {
            if (e.Dead || e.Kind == EnemyKind.Zeppelin) continue;
            var rel = Field.Delta(p.Pos, e.Pos);
            var closing = p.Forward * Plane.Speed - e.Forward * e.Speed;
            double r = rel.Length, rate = (rel.X * closing.X + rel.Y * closing.Y + rel.Z * closing.Z) / Math.Max(1, r);
            if (r < 260 && rate > 150 && !low)
            {
                Doing = "evade";
                return new Controls(dx, rel.Y >= 0 ? -1 : 1, false);
            }
        }
        if (low) { dy = 1; Doing = "pull up"; }
        else Doing = target.Kind == EnemyKind.Zeppelin ? "zeppelin" : target.State == EnemyState.Attack ? "dogfight" : "hunt";
        var to = d3.Normalized();
        var f = p.Forward;
        bool lined = to.X * f.X + to.Y * f.Y + to.Z * f.Z > Math.Cos(0.05);
        return new Controls(dx, dy, lined && dist < GameEngine.GunRange && g.Phase == Phase.Play);
    }
}
