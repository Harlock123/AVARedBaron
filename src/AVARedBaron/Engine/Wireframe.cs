namespace AVARedBaron.Engine;

/// <summary>
/// A vector-display model: nothing but line segments in 3D, about a unit in size, nose along +Z, up along +Y.
/// </summary>
public sealed class Wireframe
{
    public (Vec3 A, Vec3 B)[] Lines { get; }

    private Wireframe(List<(Vec3, Vec3)> lines) => Lines = lines.ToArray();

    private sealed class Builder
    {
        private readonly List<(Vec3, Vec3)> _lines = [];

        public Builder Line(Vec3 a, Vec3 b)
        {
            _lines.Add((a, b));
            return this;
        }

        public Builder Path(bool closed, params Vec3[] points)
        {
            for (int i = 0; i < points.Length - 1; i++) _lines.Add((points[i], points[i + 1]));
            if (closed && points.Length > 2) _lines.Add((points[^1], points[0]));
            return this;
        }

        /// <summary>Two outlines joined corner to corner.</summary>
        public Builder Prism(Vec3[] a, Vec3[] b)
        {
            Path(true, a).Path(true, b);
            for (int i = 0; i < a.Length; i++) Line(a[i], b[i]);
            return this;
        }

        /// <summary>A ring of points round the Z axis.</summary>
        public static Vec3[] Hoop(double z, double rx, double ry, int count, double y = 0)
        {
            var p = new Vec3[count];
            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count;
                p[i] = new Vec3(Math.Cos(a) * rx, y + Math.Sin(a) * ry, z);
            }
            return p;
        }

        /// <summary>A ring of points in the level plane.</summary>
        public static Vec3[] Ring(double y, double r, int count)
        {
            var p = new Vec3[count];
            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count;
                p[i] = new Vec3(Math.Sin(a) * r, y, Math.Cos(a) * r);
            }
            return p;
        }

        public Wireframe Build() => new(_lines);
    }

    private static Vec3 V(double x, double y, double z) => new(x, y, z);

    private static Vec3[] Box(double x0, double x1, double y, double z0, double z1) =>
        [V(x0, y, z0), V(x1, y, z0), V(x1, y, z1), V(x0, y, z1)];

    /// <summary>
    /// A biplane: two wings joined by struts, a slim fuselage tapering to the tail, tailplane and fin, and the
    /// propeller's disc at the nose. About two units across.
    /// </summary>
    public static readonly Wireframe Biplane = BuildBiplane();

    private static Wireframe BuildBiplane()
    {
        var b = new Builder();
        // The wings: upper a little forward of the lower.
        b.Prism(Box(-1.1, 1.1, 0.42, 0.0, 0.42), Box(-1.1, 1.1, 0.45, 0.0, 0.42));
        b.Path(true, Box(-1.0, 1.0, -0.12, -0.08, 0.32));
        // Struts.
        foreach (double x in new[] { -0.7, 0.7 })
        {
            b.Line(V(x, -0.12, 0.25), V(x, 0.42, 0.33));
            b.Line(V(x, -0.12, -0.04), V(x, 0.42, 0.04));
            b.Line(V(x, -0.12, 0.25), V(x, 0.42, 0.04));
        }
        // The fuselage: a box at the nose, tapering to a point at the tail.
        var nose = new[] { V(-0.16, -0.18, 0.75), V(0.16, -0.18, 0.75), V(0.16, 0.14, 0.75), V(-0.16, 0.14, 0.75) };
        var mid = new[] { V(-0.17, -0.2, 0.0), V(0.17, -0.2, 0.0), V(0.17, 0.16, 0.0), V(-0.17, 0.16, 0.0) };
        b.Prism(nose, mid);
        foreach (var p in mid) b.Line(p, V(0, 0, -1.25));
        // Tailplane and fin.
        b.Path(true, V(-0.45, 0, -1.0), V(0.45, 0, -1.0), V(0.3, 0, -1.25), V(-0.3, 0, -1.25));
        b.Path(false, V(0, 0, -0.9), V(0, 0.38, -1.15), V(0, 0.38, -1.25), V(0, 0, -1.25));
        // The propeller's disc, and the wheels.
        b.Path(true, Builder.Hoop(0.82, 0.38, 0.38, 10));
        b.Line(V(-0.2, -0.18, 0.4), V(-0.25, -0.5, 0.45)).Line(V(0.2, -0.18, 0.4), V(0.25, -0.5, 0.45));
        b.Path(true, Builder.Hoop(0.45, 0.06, 0.1, 6, -0.5).Select(p => p with { X = p.X - 0.25 }).ToArray());
        b.Path(true, Builder.Hoop(0.45, 0.06, 0.1, 6, -0.5).Select(p => p with { X = p.X + 0.25 }).ToArray());
        return b.Build();
    }

    /// <summary>A zeppelin: a long cigar of hoops and stringers, fins at the tail and a gondola beneath. About four units long.</summary>
    public static readonly Wireframe Zeppelin = BuildZeppelin();

    private static Wireframe BuildZeppelin()
    {
        var b = new Builder();
        const int around = 10;
        double[] zs = [2.0, 1.6, 0.8, 0, -0.8, -1.6, -2.0];
        var hoops = zs.Select(z =>
        {
            double r = 0.5 * Math.Sqrt(Math.Max(0, 1 - z * z / 4.2));
            return Builder.Hoop(z, r, r, around);
        }).ToList();
        foreach (var h in hoops.Skip(1).SkipLast(1)) b.Path(true, h);
        for (int i = 0; i < around; i++)
            for (int k = 0; k < hoops.Count - 1; k++) b.Line(hoops[k][i], hoops[k + 1][i]);
        // Fins.
        b.Path(false, V(0, 0.3, -1.4), V(0, 0.75, -1.9), V(0, 0.2, -2.0));
        b.Path(false, V(0, -0.3, -1.4), V(0, -0.75, -1.9), V(0, -0.2, -2.0));
        b.Path(false, V(0.3, 0, -1.4), V(0.75, 0, -1.9), V(0.2, 0, -2.0));
        b.Path(false, V(-0.3, 0, -1.4), V(-0.75, 0, -1.9), V(-0.2, 0, -2.0));
        // The gondola.
        b.Prism(Box(-0.1, 0.1, -0.5, -0.2, 0.5), Box(-0.1, 0.1, -0.68, -0.15, 0.45));
        b.Line(V(0, -0.46, 0.3), V(0, -0.5, 0.3));
        return b.Build();
    }

    /// <summary>A hangar: a long arched shed, standing on the ground (Y = 0).</summary>
    public static readonly Wireframe Hangar = BuildHangar();

    private static Wireframe BuildHangar()
    {
        var b = new Builder();
        Vec3[] Arch(double z)
        {
            var p = new List<Vec3> { V(-0.6, 0, z) };
            for (int i = 0; i <= 6; i++)
            {
                double a = Math.PI - i * Math.PI / 6;
                p.Add(V(Math.Cos(a) * 0.6, 0.35 + Math.Sin(a) * 0.35, z));
            }
            p.Add(V(0.6, 0, z));
            return p.ToArray();
        }
        var front = Arch(1);
        var back = Arch(-1);
        b.Path(false, front).Path(false, back);
        for (int i = 0; i < front.Length; i++) b.Line(front[i], back[i]);
        // The doors.
        b.Path(false, V(-0.35, 0, 1), V(-0.35, 0.45, 1), V(0.35, 0.45, 1), V(0.35, 0, 1)).Line(V(0, 0, 1), V(0, 0.45, 1));
        return b.Build();
    }

    /// <summary>A fuel tank: a squat cylinder with a domed top.</summary>
    public static readonly Wireframe FuelTank = BuildTank();

    private static Wireframe BuildTank()
    {
        var b = new Builder();
        var bottom = Builder.Ring(0, 0.6, 10);
        var top = Builder.Ring(0.7, 0.6, 10);
        b.Prism(bottom, top);
        var dome = Builder.Ring(0.95, 0.3, 10);
        b.Path(true, dome);
        for (int i = 0; i < 10; i += 2) b.Line(top[i], dome[i]);
        b.Path(false, V(-0.6, 0.5, 0), V(-0.75, 0.5, 0), V(-0.75, 0, 0));
        return b.Build();
    }

    /// <summary>An anti-aircraft gun: a sandbagged pit and a long barrel cocked at the sky.</summary>
    public static readonly Wireframe Gun = BuildGun();

    private static Wireframe BuildGun()
    {
        var b = new Builder();
        b.Prism(Builder.Ring(0, 0.7, 8), Builder.Ring(0.2, 0.6, 8));
        b.Prism(Box(-0.2, 0.2, 0.2, -0.2, 0.2), Box(-0.2, 0.2, 0.45, -0.2, 0.2));
        b.Line(V(-0.05, 0.4, 0.1), V(-0.05, 1.3, 0.8)).Line(V(0.05, 0.4, 0.1), V(0.05, 1.3, 0.8));
        b.Line(V(-0.05, 1.3, 0.8), V(0.05, 1.3, 0.8));
        return b.Build();
    }
}
