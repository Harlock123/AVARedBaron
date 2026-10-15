namespace AVARedBaron.Engine;

/// <summary>The stick and the trigger: bank left/right (Dx), dive (Dy -1) or climb (Dy 1), and fire.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>
/// The countryside: a square that wraps at its edges, so it seems to go on for ever. Distances and bearings
/// are always taken the short way round. X is east, Y is up, Z is north.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Size = 8000;
    public const double Ceiling = 520, Floor = 4;

    public static Vec3 Wrap(Vec3 p)
    {
        double x = p.X % Size, z = p.Z % Size;
        if (x < 0) x += Size;
        if (z < 0) z += Size;
        return new Vec3(x, p.Y, z);
    }

    /// <summary>The shortest offset from one point to another (Y is just the difference).</summary>
    public static Vec3 Delta(Vec3 from, Vec3 to)
    {
        double dx = to.X - from.X, dz = to.Z - from.Z;
        if (dx > Size / 2) dx -= Size;
        else if (dx < -Size / 2) dx += Size;
        if (dz > Size / 2) dz -= Size;
        else if (dz < -Size / 2) dz += Size;
        return new Vec3(dx, to.Y - from.Y, dz);
    }

    public static double Distance(Vec3 a, Vec3 b) => Delta(a, b).Length;
    public static double GroundDistance(Vec3 a, Vec3 b) { var d = Delta(a, b); return Math.Sqrt(d.X * d.X + d.Z * d.Z); }
    public static double Bearing(Vec3 from, Vec3 to) { var d = Delta(from, to); return Math.Atan2(d.X, d.Z); }
    public static double AngleDiff(double target, double current) => Math.IEEERemainder(target - current, Math.PI * 2);

    /// <summary>Which way something with this heading and pitch is flying.</summary>
    public static Vec3 Forward(double yaw, double pitch) => new(Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch), Math.Cos(yaw) * Math.Cos(pitch));
}

/// <summary>
/// The view from the cockpit: where the plane is and which way it's pointing, banked and pitched, and how
/// points in front of it land on the 1024×768 screen.
/// </summary>
public sealed class Camera
{
    public const double ScreenWidth = 1024, ScreenHeight = 768, Focal = 560, Near = 2;
    public const double CentreX = ScreenWidth / 2, CentreY = 400;

    public Vec3 Position;
    public double Yaw, Pitch, Roll;

    /// <summary>A point in the world as the pilot sees it: X right, Y up, Z ahead.</summary>
    public Vec3 ToCamera(Vec3 world)
    {
        var d = Field.Delta(Position, world);
        double c = Math.Cos(Yaw), s = Math.Sin(Yaw);
        double x = d.X * c - d.Z * s, z = d.X * s + d.Z * c, y = d.Y;
        double cp = Math.Cos(Pitch), sp = Math.Sin(Pitch);
        (y, z) = (y * cp - z * sp, y * sp + z * cp);
        // Banked right, the world turns the other way: the horizon rises on the right.
        double cr = Math.Cos(Roll), sr = Math.Sin(Roll);
        return new Vec3(x * cr - y * sr, x * sr + y * cr, z);
    }

    public static (double X, double Y) Project(Vec3 c) => (CentreX + c.X / c.Z * Focal, CentreY - c.Y / c.Z * Focal);
}

public sealed class Plane
{
    public const double Speed = 250, MaxDamage = 3;
    public Vec3 Pos = new(Field.Size / 2, 160, Field.Size / 2);
    public double Yaw, Pitch, Roll;
    public bool Alive = true;
    public int Damage;
    public double GunCool, Hurt;
    public int GunSide = 1;
    public Vec3 Forward => Field.Forward(Yaw, Pitch);
}

public enum EnemyKind
{
    /// <summary>A biplane of the enemy squadron.</summary>
    Fighter,
    /// <summary>A slow, huge zeppelin.</summary>
    Zeppelin,
}

public enum EnemyState
{
    /// <summary>Circling, waiting its turn.</summary>
    Patrol,
    /// <summary>Coming at you, guns blazing.</summary>
    Attack,
    /// <summary>Breaking away after a pass.</summary>
    Break,
}

public sealed class Enemy
{
    public EnemyKind Kind;
    public Vec3 Pos;
    public double Yaw, Pitch, Roll, Speed = 220, T, FireIn = 2, StateTime;
    public EnemyState State;
    public int Hp = 1, Burst;
    public bool Dead;
    /// <summary>Where it circles while it patrols.</summary>
    public double CircleX, CircleZ, CircleAlt;
    public double Radius => Kind == EnemyKind.Zeppelin ? 44 : 22;
    public Vec3 Forward => Field.Forward(Yaw, Pitch);
}

public enum TargetKind
{
    Hangar,
    FuelTank,
    /// <summary>An anti-aircraft gun: it throws flak up at you.</summary>
    Gun,
}

public sealed class GroundTarget
{
    public TargetKind Kind;
    public Vec3 Pos;
    public double Yaw, FireIn = 3;
    public bool Dead;
    public double Radius => Kind == TargetKind.Hangar ? 34 : 24;
}

/// <summary>A pyramid of a mountain: a square base and a peak.</summary>
public readonly record struct Mountain(double X, double Z, double Half, double Height)
{
    /// <summary>How high the mountain's surface is at a point (0 off it).</summary>
    public double HeightAt(Vec3 p)
    {
        var d = Field.Delta(new Vec3(X, 0, Z), p);
        double m = Math.Max(Math.Abs(d.X), Math.Abs(d.Z));
        return m >= Half ? 0 : Height * (1 - m / Half);
    }
}

public sealed class Tracer
{
    public Vec3 Pos, Vel;
    public double Life;
    public bool Mine, Dead;
}

/// <summary>A flak shell: it climbs to where it's set to burst, then hangs as a puff of smoke.</summary>
public sealed class Flak
{
    public const double BurstRadius = 42, PuffSeconds = 1.2;
    public Vec3 Pos;
    public double Fuse, Age;
    public bool Burst;
    public bool Done => Burst && Age > PuffSeconds;
}

/// <summary>A piece of something shot down, tumbling as it falls.</summary>
public sealed class Debris
{
    public Vec3 Pos, Vel, A, B;
    public double Spin, Angle, Life = 2.5, Age;
    public uint Colour;
}

public enum EffectKind { Score, Burst }

/// <summary>A score or a flash, floating where it happened.</summary>
public sealed class Effect
{
    public EffectKind Kind;
    public Vec3 Pos;
    public double Age, Life = 1.2, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
