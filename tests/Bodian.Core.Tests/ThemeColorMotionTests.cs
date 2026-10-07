using System.Numerics;
using Bodian.WinUI.Services;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class ThemeColorMotionTests
{
    private static readonly Vector4 Light = new(255, 245, 246, 248);
    private static readonly Vector4 Dark = new(255, 24, 25, 28);

    [Fact]
    public void RapidReversal_StartsAtTheDisplayedIntermediateColor()
    {
        var displayed = ThemeColorMotion.Sample(Light, Dark, 0.37);
        Assert.NotEqual(Light, displayed);
        Assert.NotEqual(Dark, displayed);
        Assert.Equal(displayed, ThemeColorMotion.Sample(displayed, Light, 0));
        Assert.Equal(Light, ThemeColorMotion.Sample(displayed, Light, 1));
    }

    [Fact]
    public void RepeatedDirectionChanges_DoNotJumpToEitherThemeEndpoint()
    {
        var current = Light;
        for (var request = 0; request < 20; request++)
        {
            var destination = request % 2 == 0 ? Dark : Light;
            var next = ThemeColorMotion.Sample(current, destination, 0.19);
            Assert.Equal(next, ThemeColorMotion.Sample(next, current, 0));
            Assert.InRange(next.X, 255, 255);
            Assert.InRange(next.Y, Dark.Y, Light.Y);
            Assert.InRange(next.Z, Dark.Z, Light.Z);
            Assert.InRange(next.W, Dark.W, Light.W);
            current = next;
        }
    }

    [Fact]
    public void TransparentAndOpaqueBrushes_KeepChannelsWithinTheirEndpoints()
    {
        var from = new Vector4(26, 255, 255, 255);
        var to = new Vector4(230, 12, 24, 36);
        for (var frame = 0; frame <= 120; frame++)
        {
            var sample = ThemeColorMotion.Sample(from, to, frame / 120d);
            Assert.InRange(sample.X, from.X, to.X);
            Assert.InRange(sample.Y, to.Y, from.Y);
            Assert.InRange(sample.Z, to.Z, from.Z);
            Assert.InRange(sample.W, to.W, from.W);
        }
    }

    [Fact]
    public void EaseInOut_HasGentleStartAndEndWithoutASeparateOpacityPulse()
    {
        var from = Vector4.Zero;
        var to = Vector4.One;
        Assert.Equal(new Vector4(0.5f), ThemeColorMotion.Sample(from, to, 0.5));
        Assert.True(ThemeColorMotion.Sample(from, to, 0.1).X < 0.04f);
        Assert.True(ThemeColorMotion.Sample(from, to, 0.9).X > 0.96f);
        Assert.Equal(from, ThemeColorMotion.Sample(from, to, -1));
        Assert.Equal(to, ThemeColorMotion.Sample(from, to, 2));
    }
}
