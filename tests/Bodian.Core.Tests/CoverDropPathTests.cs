using System.Numerics;
using Bodian.WinUI.Controls;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class CoverDropPathTests
{
    [Theory]
    [InlineData(-600, 500)]
    [InlineData(240, 720)]
    [InlineData(0, 32)]
    public void Flight_StartsAtClickedCoverAndEndsAtPlayerCover(float x, float y)
    {
        var displacement = new Vector2(x, y);
        Assert.Equal(Vector2.Zero, CoverDropPath.Translation(displacement, 0));
        Assert.Equal(displacement, CoverDropPath.Translation(displacement, 1));
    }

    [Fact]
    public void FallingMotion_AcceleratesDownwardWithoutOvershooting()
    {
        var displacement = new Vector2(-680, 540);
        var previous = Vector2.Zero;
        var previousStep = 0f;
        for (var frame = 1; frame <= 120; frame++)
        {
            var position = CoverDropPath.Translation(displacement, frame / 120f);
            Assert.InRange(position.X, displacement.X, previous.X);
            Assert.InRange(position.Y, previous.Y, displacement.Y);
            var step = position.Y - previous.Y;
            Assert.True(step + 0.0001f >= previousStep);
            previous = position;
            previousStep = step;
        }
    }

    [Fact]
    public void HorizontalMotion_ApproachesDestinationBeforeVerticalLanding()
    {
        var position = CoverDropPath.Translation(new Vector2(100, 100), 0.5f);
        Assert.True(position.X > 75);
        Assert.True(position.Y < 30);
    }

    [Theory]
    [InlineData(0.25f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(1.5f, 2f)]
    public void Scaling_LandsAtDestinationSizeWithoutOvershoot(float x, float y)
    {
        var target = new Vector2(x, y);
        Assert.Equal(Vector3.One, CoverDropPath.Scale(target, 0));
        Assert.Equal(new Vector3(target, 1), CoverDropPath.Scale(target, 1));
        for (var frame = 0; frame <= 120; frame++)
        {
            var scale = CoverDropPath.Scale(target, frame / 120f);
            Assert.InRange(scale.X, Math.Min(1, x), Math.Max(1, x));
            Assert.InRange(scale.Y, Math.Min(1, y), Math.Max(1, y));
            Assert.Equal(1, scale.Z);
        }
    }

    [Fact]
    public void OutOfRangeProgress_ClampsToFlightEndpoints()
    {
        var displacement = new Vector2(-720, 600);
        var scale = new Vector2(0.25f, 0.5f);
        Assert.Equal(Vector2.Zero, CoverDropPath.Translation(displacement, -1));
        Assert.Equal(displacement, CoverDropPath.Translation(displacement, 2));
        Assert.Equal(Vector3.One, CoverDropPath.Scale(scale, -1));
        Assert.Equal(new Vector3(scale, 1), CoverDropPath.Scale(scale, 2));
    }
}
