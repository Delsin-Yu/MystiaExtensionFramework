using System.Reflection;
using Mystia.Numerics;
using UnityBounds = UnityEngine.Bounds;
using UnityBoundsInt = UnityEngine.BoundsInt;
using UnityColor = UnityEngine.Color;
using UnityRect = UnityEngine.Rect;
using UnityVector2 = UnityEngine.Vector2;
using UnityVector2Int = UnityEngine.Vector2Int;
using UnityVector3 = UnityEngine.Vector3;
using UnityVector3Int = UnityEngine.Vector3Int;
using UnityVector4 = UnityEngine.Vector4;
using Xunit;

namespace Mystia.Tests;

// The mirror types are meant to be a drop in for the engine's value types, so the tests pin the engine's own
// semantics: clamped Lerp, a half size Extents, and a Rect that owns its left and top edges but not its right
// and bottom ones.
public sealed class NumericsTests
{
    private const float Epsilon = 1e-5f;

    // The engine types come from the game interop, where they are il2cpp proxies: outside the game process their
    // type initializer cannot resolve the il2cpp class, so building one throws, and their explicit il2cpp layout
    // makes a hand filled copy read back wrong. The behavioural round trip therefore runs only in a host that
    // carries that runtime; the shape of every conversion is pinned without it by
    // EveryMirrorTypeCarriesAnInternalEngineBridge.
    private static readonly bool EngineRuntimeAvailable = ProbeEngineRuntime();

    private static bool ProbeEngineRuntime()
    {
        try
        {
            _ = new UnityVector3(1f, 2f, 3f);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void AssertClose(float expected, float actual)
    {
        Assert.True(MathF.Abs(expected - actual) <= Epsilon, $"expected {expected} but got {actual}");
    }

    [Fact]
    public void Vector2ArithmeticMatchesTheEngineOperators()
    {
        var a = new Vector2(1f, 2f);
        var b = new Vector2(3f, -4f);

        Assert.Equal(new Vector2(4f, -2f), a + b);
        Assert.Equal(new Vector2(-2f, 6f), a - b);
        Assert.Equal(new Vector2(-1f, -2f), -a);
        Assert.Equal(new Vector2(2f, 4f), a * 2f);
        Assert.Equal(new Vector2(2f, 4f), 2f * a);
        Assert.Equal(new Vector2(0.5f, 1f), a / 2f);
        Assert.Equal(new Vector2(0f, 0f), Vector2.Zero);
        Assert.Equal(new Vector2(1f, 1f), Vector2.One);
    }

    [Fact]
    public void Vector3ArithmeticMatchesTheEngineOperators()
    {
        var a = new Vector3(1f, 2f, 3f);
        var b = new Vector3(-1f, 0f, 1f);

        Assert.Equal(new Vector3(0f, 2f, 4f), a + b);
        Assert.Equal(new Vector3(2f, 2f, 2f), a - b);
        Assert.Equal(new Vector3(-1f, -2f, -3f), -a);
        Assert.Equal(new Vector3(3f, 6f, 9f), a * 3f);
        Assert.Equal(new Vector3(3f, 6f, 9f), 3f * a);
        Assert.Equal(new Vector3(0.5f, 1f, 1.5f), a / 2f);
        Assert.Equal(new Vector3(0f, -1f, 0f), Vector3.Down);
    }

    [Fact]
    public void VectorLengthsAndNormalizationMatchTheEngine()
    {
        var flat = new Vector2(3f, 4f);
        var spatial = new Vector3(0f, 3f, 4f);

        AssertClose(25f, flat.SqrMagnitude);
        AssertClose(5f, flat.Magnitude);
        AssertClose(0.6f, flat.Normalized.X);
        AssertClose(0.8f, flat.Normalized.Y);

        AssertClose(25f, spatial.SqrMagnitude);
        AssertClose(5f, spatial.Magnitude);
        AssertClose(0f, spatial.Normalized.X);
        AssertClose(0.6f, spatial.Normalized.Y);
        AssertClose(0.8f, spatial.Normalized.Z);
    }

    [Fact]
    public void NormalizingSomethingTooShortYieldsZeroInsteadOfNaN()
    {
        Assert.Equal(Vector2.Zero, Vector2.Zero.Normalized);
        Assert.Equal(Vector2.Zero, new Vector2(1e-8f, 0f).Normalized);
        Assert.Equal(Vector3.Zero, Vector3.Zero.Normalized);
        Assert.Equal(Vector3.Zero, new Vector3(0f, 0f, 1e-8f).Normalized);
    }

    [Fact]
    public void VectorLerpInterpolatesAndClampsT()
    {
        Assert.Equal(new Vector2(2.5f, 5f), Vector2.Lerp(new Vector2(0f, 0f), new Vector2(10f, 20f), 0.25f));
        Assert.Equal(new Vector2(10f, 20f), Vector2.Lerp(new Vector2(0f, 0f), new Vector2(10f, 20f), 4f));
        Assert.Equal(new Vector2(0f, 0f), Vector2.Lerp(new Vector2(0f, 0f), new Vector2(10f, 20f), -4f));

        Assert.Equal(
            new Vector3(1f, 2f, 3f),
            Vector3.Lerp(new Vector3(0f, 0f, 0f), new Vector3(2f, 4f, 6f), 0.5f));
        Assert.Equal(
            new Vector3(2f, 4f, 6f),
            Vector3.Lerp(new Vector3(0f, 0f, 0f), new Vector3(2f, 4f, 6f), 2f));
    }

    [Fact]
    public void Vector2AndVector3ConvertBothWays()
    {
        Vector2 flat = new Vector3(3f, 4f, 5f);
        AssertClose(3f, flat.X);
        AssertClose(4f, flat.Y);

        Vector3 wide = flat;
        AssertClose(3f, wide.X);
        AssertClose(4f, wide.Y);
        AssertClose(0f, wide.Z);
    }

    [Fact]
    public void IntegerVectorsAddSubtractAndStartAtZero()
    {
        Assert.Equal(new Vector2Int(4, 6), new Vector2Int(1, 2) + new Vector2Int(3, 4));
        Assert.Equal(new Vector2Int(-2, -2), new Vector2Int(1, 2) - new Vector2Int(3, 4));
        Assert.Equal(new Vector2Int(0, 0), Vector2Int.Zero);

        Assert.Equal(new Vector3Int(4, 6, 8), new Vector3Int(1, 2, 3) + new Vector3Int(3, 4, 5));
        Assert.Equal(new Vector3Int(-2, -2, -2), new Vector3Int(1, 2, 3) - new Vector3Int(3, 4, 5));
        Assert.Equal(new Vector3Int(0, 0, 0), Vector3Int.Zero);
    }

    [Fact]
    public void Vector4ExposesItsComponentsAndTheZeroAndOneVectors()
    {
        var value = new Vector4(1f, 2f, 3f, 4f);

        AssertClose(1f, value.X);
        AssertClose(2f, value.Y);
        AssertClose(3f, value.Z);
        AssertClose(4f, value.W);
        Assert.Equal(new Vector4(0f, 0f, 0f, 0f), Vector4.Zero);
        Assert.Equal(new Vector4(1f, 1f, 1f, 1f), Vector4.One);
    }

    [Fact]
    public void RectContainsOwnsTheLeftAndTopEdgesButNotTheRightAndBottomOnes()
    {
        var rect = new Rect(10f, 20f, 30f, 40f);

        AssertClose(40f, rect.XMax);
        AssertClose(60f, rect.YMax);
        AssertClose(10f, rect.Position.X);
        AssertClose(20f, rect.Position.Y);
        AssertClose(30f, rect.Size.X);
        AssertClose(40f, rect.Size.Y);

        Assert.True(rect.Contains(new Vector2(10f, 20f)));
        Assert.True(rect.Contains(10f, 20f));
        Assert.True(rect.Contains(39.99f, 59.99f));
        Assert.False(rect.Contains(40f, 60f));
        Assert.False(rect.Contains(40f, 30f));
        Assert.False(rect.Contains(30f, 60f));
        Assert.False(rect.Contains(9.99f, 30f));
        Assert.False(rect.Contains(30f, 19.99f));
    }

    [Fact]
    public void BoundsDerivesItsExtentsAndCornersFromTheCentreAndSize()
    {
        var bounds = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 6f, 8f));

        AssertClose(2f, bounds.Extents.X);
        AssertClose(3f, bounds.Extents.Y);
        AssertClose(4f, bounds.Extents.Z);
        Assert.Equal(new Vector3(-1f, -1f, -1f), bounds.Min);
        Assert.Equal(new Vector3(3f, 5f, 7f), bounds.Max);
    }

    [Fact]
    public void BoundsIntRunsFromItsPositionToPositionPlusSize()
    {
        var bounds = new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));

        Assert.Equal(new Vector3Int(1, 2, 3), bounds.Min);
        Assert.Equal(new Vector3Int(5, 7, 9), bounds.Max);
    }

    [Fact]
    public void ColorDefaultsToOpaqueAndCarriesTheEnginePalette()
    {
        var colour = new Color(0.25f, 0.5f, 0.75f);

        AssertClose(0.25f, colour.R);
        AssertClose(0.5f, colour.G);
        AssertClose(0.75f, colour.B);
        AssertClose(1f, colour.A);

        Assert.Equal(new Color(1f, 1f, 1f, 1f), Color.White);
        Assert.Equal(new Color(0f, 0f, 0f, 1f), Color.Black);
        Assert.Equal(new Color(0f, 0f, 0f, 0f), Color.Clear);
    }

    [Fact]
    public void ColorScalesAndBlendsComponentWise()
    {
        Assert.Equal(new Color(0.5f, 0.25f, 0.125f, 0.5f), new Color(1f, 0.5f, 0.25f, 1f) * 0.5f);
        Assert.Equal(
            new Color(0.5f, 0f, 0f, 0.5f),
            new Color(1f, 0f, 0f, 1f) * new Color(0.5f, 0.5f, 0.5f, 0.5f));

        // Both ends are opaque, so only the colour channels move.
        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), Color.Lerp(Color.Black, Color.White, 0.5f));
        Assert.Equal(Color.White, Color.Lerp(Color.Black, Color.White, 7f));
        Assert.Equal(Color.Black, Color.Lerp(Color.Black, Color.White, -7f));
    }

    [Fact]
    public void RecordEqualityComparesEveryComponent()
    {
        Assert.True(new Vector2(1f, 2f) == new Vector2(1f, 2f));
        Assert.True(new Vector2(1f, 2f) != new Vector2(1f, 3f));
        Assert.Equal(new Vector3(1f, 2f, 3f), new Vector3(1f, 2f, 3f));
        Assert.NotEqual(new Vector3(1f, 2f, 3f), new Vector3(1f, 2f, 4f));
        Assert.Equal(Color.White, new Color(1f, 1f, 1f));
    }

    // A record prints every public property, derived ones included, and the derived ones here are mirror values
    // that print their own derived values: without the explicit PrintMembers on those types ToString recurses
    // until the stack dies, which is exactly what a mod would hit when it logs or interpolates a value.
    [Fact]
    public void ToStringPrintsTheStoredComponentsWithoutRecursing()
    {
        Assert.Equal("Vector2 { X = 1, Y = 2 }", new Vector2(1f, 2f).ToString());
        Assert.Equal("Vector3 { X = 1, Y = 2, Z = 3 }", new Vector3(1f, 2f, 3f).ToString());
        Assert.Equal("Rect { X = 1, Y = 2, Width = 3, Height = 4 }", new Rect(1f, 2f, 3f, 4f).ToString());
        Assert.Equal(
            "Bounds { Center = Vector3 { X = 1, Y = 2, Z = 3 }, Size = Vector3 { X = 4, Y = 5, Z = 6 } }",
            new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f)).ToString());
        Assert.Equal("Color { R = 1, G = 1, B = 1, A = 1 }", new Color(1f, 1f, 1f).ToString());
    }

    [Fact]
    public void EveryMirrorTypeCarriesAnInternalEngineBridge()
    {
        AssertEngineBridge(typeof(Vector2), typeof(UnityVector2));
        AssertEngineBridge(typeof(Vector3), typeof(UnityVector3));
        AssertEngineBridge(typeof(Vector2Int), typeof(UnityVector2Int));
        AssertEngineBridge(typeof(Vector3Int), typeof(UnityVector3Int));
        AssertEngineBridge(typeof(Vector4), typeof(UnityVector4));
        AssertEngineBridge(typeof(Color), typeof(UnityColor));
        AssertEngineBridge(typeof(Rect), typeof(UnityRect));
        AssertEngineBridge(typeof(Bounds), typeof(UnityBounds));
        AssertEngineBridge(typeof(BoundsInt), typeof(UnityBoundsInt));
    }

    private static void AssertEngineBridge(Type mirror, Type engine)
    {
        var constructor = mirror.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [engine], null);
        Assert.True(constructor is not null, $"{mirror.Name} has no internal constructor taking {engine.Name}");

        var toUnity = mirror.GetMethod("ToUnity", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(toUnity is not null, $"{mirror.Name} has no internal ToUnity()");
        Assert.Equal(engine, toUnity!.ReturnType);
    }

    [Fact]
    public void MirrorValuesRoundTripThroughTheEngineTypes()
    {
        if (!EngineRuntimeAvailable)
            return;

        var flat = new UnityVector2(1.5f, -2.5f);
        var flatBack = new Vector2(flat).ToUnity();
        AssertClose(flat.x, flatBack.x);
        AssertClose(flat.y, flatBack.y);
        Assert.Equal(flat, flatBack);

        var spatial = new UnityVector3(1.5f, -2.5f, 3.5f);
        var spatialBack = new Vector3(spatial).ToUnity();
        AssertClose(spatial.x, spatialBack.x);
        AssertClose(spatial.y, spatialBack.y);
        AssertClose(spatial.z, spatialBack.z);
        Assert.Equal(spatial, spatialBack);

        var flatInt = new UnityVector2Int(3, -4);
        Assert.Equal(flatInt, new Vector2Int(flatInt).ToUnity());

        var spatialInt = new UnityVector3Int(3, -4, 5);
        Assert.Equal(spatialInt, new Vector3Int(spatialInt).ToUnity());

        var wide = new UnityVector4(1f, 2f, 3f, 4f);
        Assert.Equal(wide, new Vector4(wide).ToUnity());

        var colour = new UnityColor(0.1f, 0.2f, 0.3f, 0.4f);
        Assert.Equal(colour, new Color(colour).ToUnity());

        var rect = new UnityRect(1f, 2f, 3f, 4f);
        Assert.Equal(rect, new Rect(rect).ToUnity());

        var bounds = new UnityBounds(new UnityVector3(1f, 2f, 3f), new UnityVector3(4f, 6f, 8f));
        var boundsBack = new Bounds(bounds).ToUnity();
        Assert.Equal(bounds.center, boundsBack.center);
        Assert.Equal(bounds.size, boundsBack.size);

        var boundsInt = new UnityBoundsInt(new UnityVector3Int(1, 2, 3), new UnityVector3Int(4, 5, 6));
        var boundsIntBack = new BoundsInt(boundsInt).ToUnity();
        Assert.Equal(boundsInt.position, boundsIntBack.position);
        Assert.Equal(boundsInt.size, boundsIntBack.size);
    }
}
