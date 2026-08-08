using LilAgents.Rendering;
using Xunit;

namespace LilAgents.Tests;

/// <summary>
/// The curve controls where a character is at each instant of its walk. If it drifts from
/// the Swift original the feet stop matching the sprite footfalls, so these lock down the
/// shape rather than just spot values.
/// </summary>
public class MovementCurveTests
{
    // Bruce's tuning, from LilAgentsController.start().
    private static readonly MovementCurve Bruce = new(3.0, 3.75, 8.0, 8.5);

    [Fact]
    public void StaysAtZeroBeforeAcceleration()
    {
        Assert.Equal(0.0, Bruce.Evaluate(0.0));
        Assert.Equal(0.0, Bruce.Evaluate(2.9));
        Assert.Equal(0.0, Bruce.Evaluate(3.0));
    }

    [Fact]
    public void ReachesExactlyOneAtWalkStop()
    {
        Assert.Equal(1.0, Bruce.Evaluate(8.5), 9);
    }

    [Fact]
    public void ClampsAfterWalkStop()
    {
        Assert.Equal(1.0, Bruce.Evaluate(9.0));
        Assert.Equal(1.0, Bruce.Evaluate(100.0));
    }

    [Fact]
    public void IsMonotonicAcrossTheWholeWalk()
    {
        var previous = -1.0;
        for (var t = 0.0; t <= 10.0; t += 0.01)
        {
            var value = Bruce.Evaluate(t);
            Assert.True(value >= previous - 1e-12, $"went backwards at t={t}: {value} < {previous}");
            previous = value;
        }
    }

    [Fact]
    public void IsContinuousAtPhaseBoundaries()
    {
        foreach (var boundary in new[] { 3.0, 3.75, 8.0, 8.5 })
        {
            var before = Bruce.Evaluate(boundary - 1e-6);
            var after = Bruce.Evaluate(boundary + 1e-6);
            Assert.True(Math.Abs(after - before) < 1e-4,
                $"discontinuity at {boundary}: {before} -> {after}");
        }
    }

    [Fact]
    public void EaseInCoversHalfTheDistanceOfAnEqualConstantSpeedSpan()
    {
        // Velocity ramps linearly from zero, so the ease-in phase covers exactly half of
        // what the same duration at full speed would.
        var easeInDistance = Bruce.Evaluate(3.75);
        var constantSpanDistance = Bruce.Evaluate(4.5) - Bruce.Evaluate(3.75);
        Assert.Equal(constantSpanDistance / 2.0, easeInDistance, 6);
    }

    [Fact]
    public void HandlesDegenerateTuningWithoutDividingByZero()
    {
        var degenerate = new MovementCurve(5.0, 5.0, 5.0, 5.0);
        Assert.Equal(0.0, degenerate.Evaluate(4.0));
        Assert.Equal(1.0, degenerate.Evaluate(6.0));
    }
}
