using System;
namespace Barotrauma.Extensions;

public static class RngExtensions
{
    public static float Range(this Random rng, float minimum, float maximum)
        => (float)rng.Range((double)minimum, (double)maximum);

    public static double Range(this Random rng, double minimum, double maximum)
        => rng.NextDouble() * (maximum - minimum) + minimum;
}
