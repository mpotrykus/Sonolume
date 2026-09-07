namespace Sonolume.Engine.Core;

public enum Curve
{
    Linear,
    Exponential,
    Logarithmic,
    SCurve,
}

public static class CurveMath
{
    public static float Apply(Curve curve, float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return curve switch
        {
            Curve.Exponential => x * x,
            Curve.Logarithmic => MathF.Sqrt(x),
            Curve.SCurve => x * x * (3f - 2f * x),
            _ => x,
        };
    }
}
