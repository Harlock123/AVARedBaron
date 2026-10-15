using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVARedBaron.Engine;
using Effect = AVARedBaron.Engine.Effect;

namespace AVARedBaron.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVARedBaron/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private const uint Vector = 0xFFE8F0FF, Sky = 0xFF8AB0FF, Amber = 0xFFFFB050, EnemyRed = 0xFFFF6A5A, GroundLine = 0xFF6ADA8A;
    private const double ViewDistance = 3200;

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000006), new Rect(0, 0, Camera.ScreenWidth, Camera.ScreenHeight));
        if (InGame)
        {
            DrawWorld(dc);
            DrawCockpit(dc);
            DrawInstruments(dc);
        }
        else DrawStars(dc);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Camera.ScreenWidth, Camera.ScreenHeight));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 98), 26, Yellow, center: true);
    }

    private void DrawStars(DrawingContext dc)
    {
        for (int k = 0; k < 90; k++)
        {
            double x = Hash(k, 1, 2) % 1024, y = Hash(k, 2, 2) % 768;
            double tw = 0.5 + 0.5 * Math.Sin(_clock * (1 + k % 4) + k);
            dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.2 + 0.5 * tw)), new Rect(x, y, 2, 2));
        }
    }

    // ───────────────────────────── vector drawing ─────────────────────────────

    /// <summary>A line as a vector display draws it: a soft glow under a bright core.</summary>
    private static void Glow(DrawingContext dc, Point a, Point b, uint colour, double alpha, double width)
    {
        if (alpha <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.22 * alpha), width * 3), a, b);
        dc.DrawLine(Pen(Alpha(colour, alpha), width), a, b);
    }

    private static Point P((double X, double Y) p) => new(p.X, p.Y);

    /// <summary>A line given as the pilot sees it, trimmed where it passes behind the cockpit.</summary>
    private static void CameraLine(DrawingContext dc, Vec3 a, Vec3 b, uint colour, double alpha, double width)
    {
        const double near = Camera.Near;
        if (a.Z < near && b.Z < near) return;
        if (a.Z < near || b.Z < near)
        {
            var c = a + (b - a) * ((near - a.Z) / (b.Z - a.Z));
            if (a.Z < near) a = c; else b = c;
        }
        Glow(dc, P(Camera.Project(a)), P(Camera.Project(b)), colour, alpha, width);
    }

    private static double Fade(double depth, double start = 2200) => Math.Clamp(1 - (depth - start) / (ViewDistance - start), 0, 1);

    /// <summary>A model in the world, turned by its heading, pitch and bank, at a size, fading with distance.</summary>
    private void DrawModel(DrawingContext dc, Wireframe model, Vec3 at, double size, double yaw, double pitch, double roll, uint colour, double alpha = 1)
    {
        var cam = Engine.Camera;
        var c = cam.ToCamera(at);
        if (c.Z < -size * 3 || c.Length > ViewDistance) return;
        alpha *= Fade(c.Length);
        double width = Math.Clamp(500 / Math.Max(c.Z, 1), 1.0, 2.4);
        foreach (var (a, b) in model.Lines)
        {
            var wa = at + (a * size).RotateZ(-roll).RotateX(pitch).RotateY(yaw);
            var wb = at + (b * size).RotateZ(-roll).RotateX(pitch).RotateY(yaw);
            CameraLine(dc, cam.ToCamera(wa), cam.ToCamera(wb), colour, alpha, width);
        }
    }

    // ───────────────────────────── the world ─────────────────────────────

    private void DrawWorld(DrawingContext dc)
    {
        var g = Engine;
        var cam = g.Camera;
        DrawHorizon(dc, cam);
        DrawGroundMarks(dc, cam);
        foreach (var m in g.Mountains) DrawMountain(dc, cam, m);
        foreach (var t in g.Targets.Where(t => !t.Dead))
        {
            var model = t.Kind switch { TargetKind.Hangar => Wireframe.Hangar, TargetKind.FuelTank => Wireframe.FuelTank, _ => Wireframe.Gun };
            DrawModel(dc, model, t.Pos, t.Kind == TargetKind.Hangar ? 34 : 24, t.Yaw, 0, 0, Amber);
        }
        foreach (var e in g.Enemies.Where(e => !e.Dead))
            DrawModel(dc, e.Kind == EnemyKind.Zeppelin ? Wireframe.Zeppelin : Wireframe.Biplane, e.Pos, e.Kind == EnemyKind.Zeppelin ? 30 : 22, e.Yaw, e.Pitch, e.Roll,
                e.Kind == EnemyKind.Zeppelin ? 0xFFB0D0FF : e.State == EnemyState.Attack && (int)(_clock * 6) % 2 == 0 ? EnemyRed : Vector);
        foreach (var d in g.Wreckage)
        {
            var a = d.A.RotateY(d.Angle).RotateX(d.Angle * 0.7);
            var b = d.B.RotateY(d.Angle).RotateX(d.Angle * 0.7);
            double alpha = Math.Clamp(1 - d.Age / d.Life, 0, 1);
            CameraLine(dc, cam.ToCamera(d.Pos + a), cam.ToCamera(d.Pos + b), d.Colour, alpha, 1.5);
        }
        foreach (var f in g.Flaks.Where(f => f.Burst)) DrawFlak(dc, cam, f);
        foreach (var t in g.Tracers)
        {
            var head = cam.ToCamera(t.Pos);
            var tail = cam.ToCamera(t.Pos - t.Vel * 0.025);
            CameraLine(dc, tail, head, t.Mine ? 0xFFFFF0A0 : 0xFFFF8A5A, 1, 2);
        }
        foreach (var e in g.Effects) DrawEffect(dc, cam, e);
    }

    /// <summary>The horizon: the ground below it a dark green, tilting as the plane banks.</summary>
    private void DrawHorizon(DrawingContext dc, Camera cam)
    {
        var p = Engine.Player;
        // Two points far off on the level, either side of straight ahead.
        Vec3 Far(double a) => cam.ToCamera(Field.Wrap(p.Pos + new Vec3(Math.Sin(p.Yaw + a), 0, Math.Cos(p.Yaw + a)) * 100000) with { Y = p.Pos.Y });
        var a = Far(-0.6);
        var b = Far(0.6);
        var below = cam.ToCamera(Field.Wrap(p.Pos + new Vec3(Math.Sin(p.Yaw), 0, Math.Cos(p.Yaw)) * 300) with { Y = p.Pos.Y - 400 });
        if (a.Z <= 0.1 || b.Z <= 0.1)
        {
            // Looking straight down or up: all ground or all sky.
            if (below.Z > 0) dc.FillRectangle(Brush(0xFF04120A), new Rect(0, 0, 1024, 768));
            return;
        }
        var (ax, ay) = Camera.Project(a);
        var (bx, by) = Camera.Project(b);
        double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return;
        (dx, dy) = (dx / len, dy / len);
        // Which side is the ground?
        double side;
        if (below.Z > 0.1) { var (qx, qy) = Camera.Project(below); side = Math.Sign(dx * (qy - ay) - dy * (qx - ax)); }
        else side = 1;
        var corners = new[] { new Point(0, 0), new Point(1024, 0), new Point(1024, 768), new Point(0, 768) };
        var poly = new List<Point>();
        for (int i = 0; i < 4; i++)
        {
            var c0 = corners[i];
            var c1 = corners[(i + 1) % 4];
            double s0 = side * (dx * (c0.Y - ay) - dy * (c0.X - ax)), s1 = side * (dx * (c1.Y - ay) - dy * (c1.X - ax));
            if (s0 >= 0) poly.Add(c0);
            if (s0 * s1 < 0)
            {
                double t = s0 / (s0 - s1);
                poly.Add(new Point(c0.X + (c1.X - c0.X) * t, c0.Y + (c1.Y - c0.Y) * t));
            }
        }
        if (poly.Count >= 3) Poly(dc, 0xFF04140A, null, 0, poly.ToArray());
        // The line itself, right across.
        var l0 = new Point(ax - dx * 3000, ay - dy * 3000);
        var l1 = new Point(ax + dx * 3000, ay + dy * 3000);
        Glow(dc, l0, l1, GroundLine, 0.85, 1.6);
    }

    /// <summary>Little crosses scattered over the fields, so you can see the ground go by.</summary>
    private void DrawGroundMarks(DrawingContext dc, Camera cam)
    {
        var p = Engine.Player;
        const double cell = 220;
        int cx = (int)Math.Floor(p.Pos.X / cell), cz = (int)Math.Floor(p.Pos.Z / cell);
        int n = (int)(1800 / cell);
        int cells = (int)(Field.Size / cell);
        for (int i = -n; i <= n; i++)
            for (int j = -n; j <= n; j++)
            {
                int gx = ((cx + i) % cells + cells) % cells, gz = ((cz + j) % cells + cells) % cells;
                uint h = Hash(gx, gz, 11);
                if (h % 3 == 0) continue;
                var at = new Vec3(gx * cell + h % 150, 0, gz * cell + (h >> 8) % 150);
                if (Engine.GroundAt(at) > 0) continue;
                var c = cam.ToCamera(at);
                if (c.Z < 20 || c.Length > 1900) continue;
                double alpha = 0.55 * Math.Clamp(1 - (c.Length - 1300) / 600, 0, 1);
                double s = 8;
                CameraLine(dc, cam.ToCamera(at + new Vec3(-s, 0, 0)), cam.ToCamera(at + new Vec3(s, 0, 0)), GroundLine, alpha, 1.2);
                CameraLine(dc, cam.ToCamera(at + new Vec3(0, 0, -s)), cam.ToCamera(at + new Vec3(0, 0, s)), GroundLine, alpha, 1.2);
            }
    }

    private void DrawMountain(DrawingContext dc, Camera cam, Mountain m)
    {
        var centre = new Vec3(m.X, 0, m.Z);
        var c = cam.ToCamera(centre with { Y = m.Height / 2 });
        if (c.Length > ViewDistance + m.Half || c.Z < -m.Half * 1.5) return;
        double alpha = Fade(c.Length - m.Half, 2000);
        var peak = cam.ToCamera(centre with { Y = m.Height });
        var base4 = new[] { new Vec3(-1, 0, -1), new Vec3(1, 0, -1), new Vec3(1, 0, 1), new Vec3(-1, 0, 1) }
            .Select(d => cam.ToCamera(Field.Wrap(centre + d * m.Half))).ToArray();
        for (int i = 0; i < 4; i++)
        {
            CameraLine(dc, base4[i], base4[(i + 1) % 4], GroundLine, alpha, 1.5);
            CameraLine(dc, base4[i], peak, GroundLine, alpha, 1.5);
        }
    }

    private void DrawFlak(DrawingContext dc, Camera cam, Flak f)
    {
        var c = cam.ToCamera(f.Pos);
        if (c.Z < Camera.Near) return;
        var (x, y) = Camera.Project(c);
        double t = f.Age / Flak.PuffSeconds;
        double r = Flak.BurstRadius / c.Z * Camera.Focal * (0.4 + 0.8 * Math.Sqrt(t));
        if (t < 0.15) dc.DrawEllipse(Brush(Alpha(0xFFFFE080, 1 - t / 0.15)), null, new Point(x, y), r * 0.5, r * 0.5);
        for (int k = 0; k < 3; k++)
        {
            double a = k * 2.1 + f.Pos.X;
            dc.DrawEllipse(null, Pen(Alpha(0xFFC0C0C8, (1 - t) * 0.8), 1.5), new Point(x + Math.Cos(a) * r * 0.35, y + Math.Sin(a) * r * 0.35), r * 0.6, r * 0.6);
        }
    }

    private void DrawEffect(DrawingContext dc, Camera cam, Effect e)
    {
        var c = cam.ToCamera(e.Pos);
        if (c.Z < Camera.Near) return;
        var (x, y) = Camera.Project(c);
        double t = e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Score:
                Text(dc, e.Text, new Point(x, y - t * 30), Math.Clamp(9000 / c.Z, 18, 40), Alpha(e.Colour, 1 - t * t), center: true);
                break;
            case EffectKind.Burst:
            {
                double r = Math.Clamp((20 + 40 * t) * e.Size / c.Z * Camera.Focal / 10, 4, 220);
                dc.DrawEllipse(Brush(Alpha(e.Colour, 0.35 * (1 - t))), Pen(Alpha(e.Colour, 1 - t), 2), new Point(x, y), r, r);
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.PI / 5 + e.Pos.Z;
                    Glow(dc, new Point(x + Math.Cos(a) * r * 0.6, y + Math.Sin(a) * r * 0.6), new Point(x + Math.Cos(a) * r * 1.3, y + Math.Sin(a) * r * 1.3), e.Colour, 1 - t, 1.5);
                }
                break;
            }
        }
    }

    // ───────────────────────────── the cockpit ─────────────────────────────

    /// <summary>The upper wing across the top, its struts, the twin guns on the cowling and the ring sight.</summary>
    private void DrawCockpit(DrawingContext dc)
    {
        var g = Engine;
        var p = g.Player;
        if (!p.Alive && g.Phase == Phase.Dying && g.PhaseTime > 0.4)
        {
            // Smoke across the view as the plane goes in.
            double k = Math.Min(1, (g.PhaseTime - 0.4) / 1.5);
            dc.FillRectangle(Brush(Alpha(0xFF100808, 0.7 * k)), new Rect(0, 0, 1024, 768));
        }
        // The wing's trailing edge and its ribs.
        Poly(dc, 0xFF0A0A14, null, 0, new Point(0, 0), new Point(1024, 0), new Point(1024, 92), new Point(0, 92));
        Glow(dc, new Point(0, 92), new Point(1024, 92), Vector, 0.9, 2);
        for (int i = 1; i < 12; i++) Glow(dc, new Point(i * 1024 / 12.0, 74), new Point(i * 1024 / 12.0, 92), Vector, 0.35, 1);
        // Struts down either side.
        Glow(dc, new Point(70, 92), new Point(20, 600), Vector, 0.8, 2.5);
        Glow(dc, new Point(954, 92), new Point(1004, 600), Vector, 0.8, 2.5);
        Glow(dc, new Point(70, 92), new Point(36, 330), Vector, 0.4, 1);
        Glow(dc, new Point(954, 92), new Point(988, 330), Vector, 0.4, 1);
        // The cowling and the twin guns.
        Poly(dc, 0xFF06060E, null, 0, new Point(250, 768), new Point(370, 668), new Point(654, 668), new Point(774, 768));
        Glow(dc, new Point(250, 768), new Point(370, 668), Vector, 0.9, 2);
        Glow(dc, new Point(370, 668), new Point(654, 668), Vector, 0.9, 2);
        Glow(dc, new Point(654, 668), new Point(774, 768), Vector, 0.9, 2);
        foreach (int side in new[] { -1, 1 })
        {
            double x0 = 512 + side * 46, x1 = 512 + side * 20;
            Glow(dc, new Point(x0 - 9, 768), new Point(x1 - 4, 640), Vector, 0.9, 1.8);
            Glow(dc, new Point(x0 + 9, 768), new Point(x1 + 4, 640), Vector, 0.9, 1.8);
            Glow(dc, new Point(x1 - 4, 640), new Point(x1 + 4, 640), Vector, 0.9, 1.8);
            bool flash = p.Alive && p.GunCool > GameEngine.GunRepeat * 0.4 && p.GunSide == side;
            if (flash) dc.DrawEllipse(Brush(Alpha(0xFFFFF0A0, 0.8)), null, new Point(x1, 630), 9, 12);
        }
        // The ring sight.
        double cx = Camera.CentreX, cy = Camera.CentreY;
        dc.DrawEllipse(null, Pen(Alpha(Sky, 0.6), 1.4), new Point(cx, cy), 42, 42);
        dc.DrawEllipse(null, Pen(Alpha(Sky, 0.35), 1), new Point(cx, cy), 18, 18);
        Glow(dc, new Point(cx - 56, cy), new Point(cx - 46, cy), Sky, 0.6, 1.4);
        Glow(dc, new Point(cx + 46, cy), new Point(cx + 56, cy), Sky, 0.6, 1.4);
        Glow(dc, new Point(cx, cy - 56), new Point(cx, cy - 46), Sky, 0.6, 1.4);
        // Bullet holes in the wing, one for each hit taken.
        for (int i = 0; i < p.Damage; i++)
        {
            double x = 340 + Hash(i, 3, 5) % 520, y = 70 + Hash(i, 4, 5) % 14;
            for (int k = 0; k < 5; k++)
            {
                double a = k * 1.3 + i;
                Glow(dc, new Point(x, y), new Point(x + Math.Cos(a) * 16, y + Math.Sin(a) * 12), 0xFFFF8A5A, 0.8, 1.2);
            }
        }
        if (p.Hurt > 0) dc.FillRectangle(Brush(Alpha(0xFFFF2010, p.Hurt * 0.8)), new Rect(0, 0, 1024, 768));
    }

    /// <summary>The altimeter, the damage gauge, arrows to enemies out of sight, and the low-level warning.</summary>
    private void DrawInstruments(DrawingContext dc)
    {
        var g = Engine;
        var p = g.Player;
        // Altimeter, down the right.
        var bar = new Rect(1000, 160, 10, 420);
        dc.DrawRectangle(null, Pen(Alpha(Sky, 0.6), 1.2), bar);
        double frac = Math.Clamp(p.Pos.Y / Field.Ceiling, 0, 1);
        dc.FillRectangle(Brush(Alpha(g.TooLow ? Red : Sky, 0.8)), new Rect(bar.X + 2, bar.Bottom - bar.Height * frac, bar.Width - 4, bar.Height * frac));
        Text(dc, "ALT", new Point(1005, 146), 20, Sky, center: true);
        // Damage, down the left.
        Text(dc, "DAMAGE", new Point(48, 146), 20, Sky, center: true);
        for (int i = 0; i < (int)Plane.MaxDamage; i++)
        {
            var r = new Rect(32, 170 + i * 30, 32, 22);
            if (i < p.Damage) dc.FillRectangle(Brush(Red), r, 3);
            else dc.DrawRectangle(null, Pen(Alpha(Sky, 0.6), 1.2), r, 3);
        }
        // Enemies out of sight: arrows round the edge pointing their way.
        var cam = g.Camera;
        foreach (var e in g.Enemies.Where(e => !e.Dead))
        {
            var c = cam.ToCamera(e.Pos);
            if (c.Z > 0)
            {
                var (sx, sy) = Camera.Project(c);
                if (sx > 40 && sx < 984 && sy > 110 && sy < 650) continue;
            }
            double ang = Math.Atan2(-c.Y, c.X);
            if (c.Z < 0 && Math.Abs(c.X) < 1 && Math.Abs(c.Y) < 1) ang = Math.PI / 2;
            double ex = Math.Cos(ang), ey = Math.Sin(ang);
            double kx = ex == 0 ? double.MaxValue : 430 / Math.Abs(ex), ky = ey == 0 ? double.MaxValue : 250 / Math.Abs(ey);
            double k = Math.Min(kx, ky);
            var tip = new Point(512 + ex * k, 390 + ey * k);
            var back = new Point(tip.X - ex * 22, tip.Y - ey * 22);
            uint col = e.Kind == EnemyKind.Zeppelin ? 0xFFB0D0FF : e.State == EnemyState.Attack ? EnemyRed : Yellow;
            Poly(dc, Alpha(col, 0.85), null, 0, tip, new Point(back.X - ey * 9, back.Y + ex * 9), new Point(back.X + ey * 9, back.Y - ex * 9));
        }
        if (g.TooLow && g.Phase == Phase.Play && (int)(_clock * 4) % 2 == 0)
            Text(dc, "PULL UP!", new Point(512, 560), 48, Red, center: true);
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(1008, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(1008, 48), 30, Dim, right: true);
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused)
        {
            Text(dc, $"WAVE {g.Wave}", new Point(512, 24), 34, White, center: true);
            Text(dc, $"ENEMY {g.Remaining}", new Point(512, 58), 24, Dim, center: true);
            int spare = Math.Max(0, g.Lives - (g.Player.Alive ? 1 : 0));
            for (int i = 0; i < Math.Min(spare, 6); i++)
                DrawShowcase(dc, Wireframe.Biplane, new Point(200 + i * 44, 44), 15, 0.3, 0.5, Vector, 1);
        }
    }

    /// <summary>A model turned towards the viewer, for the title and the score bar.</summary>
    private static void DrawShowcase(DrawingContext dc, Wireframe model, Point centre, double size, double yaw, double pitch, uint colour, double width)
    {
        const double dist = 5;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        Point Pt(Vec3 v)
        {
            double x = v.X * cy + v.Z * sy, z = -v.X * sy + v.Z * cy;
            double up = v.Y * cp + z * sp, away = z * cp - v.Y * sp;
            double k = dist / (dist + away);
            return new Point(centre.X + x * size * k, centre.Y - up * size * k);
        }
        foreach (var (a, b) in model.Lines) Glow(dc, Pt(a), Pt(b), colour, 1, width);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = 512, cy = 250;
        var (w, h) = (Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xA0000010), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 12 ? 46 : 58, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = 512;
        Text(dc, "AVA RED BARON", new Point(cx, 96), 110, Red, center: true);
        Text(dc, "AFTER THE 1980 ARCADE CLASSIC", new Point(cx, 166), 28, Dim, center: true);
        DrawShowcase(dc, Wireframe.Biplane, new Point(cx, 280), 95, _clock * 0.6, 0.35, Vector, 1.8);
        string[] lines =
        [
            "A DOGFIGHT OVER THE MOUNTAINS IN AN OPEN-COCKPIT BIPLANE",
            "SHOOT DOWN EACH SQUADRON - THREE HITS AND YOU GO DOWN",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 406 + i * 30), 25, White, center: true);
        double[] xs = [cx - 330, cx - 110, cx + 110, cx + 330];
        DrawShowcase(dc, Wireframe.Biplane, new Point(xs[0], 498), 26, _clock, 0.4, Vector, 1.2);
        DrawShowcase(dc, Wireframe.Zeppelin, new Point(xs[1], 498), 18, _clock * 0.5 + 1, 0.3, 0xFFB0D0FF, 1.2);
        DrawShowcase(dc, Wireframe.Hangar, new Point(xs[2], 508), 30, _clock * 0.7, 0.5, Amber, 1.2);
        DrawShowcase(dc, Wireframe.Gun, new Point(xs[3], 512), 28, _clock * 0.7, 0.4, Amber, 1.2);
        (string Name, string Points)[] table = [("FIGHTER", $"{GameEngine.FighterPoints}"), ("ZEPPELIN", $"{GameEngine.ZeppelinPoints}"), ("HANGAR / TANK", "300 / 200"), ("FLAK GUN", "500")];
        for (int i = 0; i < table.Length; i++)
        {
            Text(dc, table[i].Name, new Point(xs[i], 552), 26, Yellow, center: true);
            Text(dc, table[i].Points, new Point(xs[i], 578), 24, Dim, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 636), 46, Yellow, center: true);
        Text(dc, "ARROWS: BANK, DIVE (UP) AND CLIMB (DOWN)    SPACE: FIRE    P: PAUSE", new Point(cx, 690), 25, Dim, center: true);
        Text(dc, $"EXTRA PLANE AT {GameEngine.FirstExtraLife} AND EVERY {GameEngine.ExtraLifeEvery}", new Point(cx, 722), 23, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Camera.ScreenWidth / 2;
        Text(dc, "THE ACES", new Point(cx, 130), 72, Red, center: true);
        Text(dc, "RANK NAME    SCORE   WAVE", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Camera.ScreenWidth / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
