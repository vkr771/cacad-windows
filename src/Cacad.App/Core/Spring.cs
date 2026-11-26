namespace Cacad;

/// <summary>Damped spring (mass 1) solved in 1/240 s substeps so it behaves the same at any frame rate.
/// Retargeting keeps the velocity, so an interrupted open/close flows on instead of restarting.</summary>
public sealed class Spring
{
    public double Value;
    public double Velocity;
    public double Target;
    public (double k, double c) Spec;
    private readonly double _snap;

    public Spring(double initial, (double k, double c) spec, double snap = 0.05)
    {
        Value = Target = initial;
        Spec = spec;
        _snap = snap;
    }

    public bool IsSettled => Value == Target && Velocity == 0;

    public void To(double target) => Target = target;

    public void To(double target, (double k, double c) spec)
    {
        Target = target;
        Spec = spec;
    }

    public void Snap(double v)
    {
        Value = Target = v;
        Velocity = 0;
    }

    /// <summary>Advance by <paramref name="dt"/> seconds; returns true while moving.</summary>
    public bool Step(double dt)
    {
        if (IsSettled) return false;
        int n = Math.Max(1, (int)Math.Ceiling(dt * 240));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            double a = -Spec.k * (Value - Target) - Spec.c * Velocity;
            Velocity += a * h;
            Value += Velocity * h;
        }
        if (Math.Abs(Value - Target) < _snap && Math.Abs(Velocity) < Math.Max(_snap, 1))
            Snap(Target);
        return true;
    }
}

/// <summary>Named spring specs (stiffness, damping).</summary>
public static class SpringSpec
{
    public static readonly (double k, double c) Open = (223.8, 23.94);
    public static readonly (double k, double c) Close = (195.0, 27.93);
    public static readonly (double k, double c) Fade = (438.6, 41.89);
    public static readonly (double k, double c) Panel = (815.7, 57.12);

    public static (double k, double c) Scaled((double k, double c) s, double speed) => (s.k * speed * speed, s.c * speed);
}
