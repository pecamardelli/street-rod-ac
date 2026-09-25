using System.Numerics;
using Street_Rod_AC.Services.Catalog;

namespace StreetRodAC.Tests;

/// <summary>Telling an encrypted car model (scrambled normals) from a real one, so the catalog can leave it out</summary>
public class EncryptedCarsTests
{
    /// <summary>A sphere cut into triangles, each corner's normal as a real model has it: pointing out of the surface</summary>
    private static IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> Sphere()
    {
        const int rings = 24, segments = 48;
        Vector3 At(int ring, int segment)
        {
            var theta = Math.PI * ring / rings;
            var phi = 2 * Math.PI * segment / segments;
            return new Vector3((float)(Math.Sin(theta) * Math.Cos(phi)), (float)Math.Cos(theta), (float)(Math.Sin(theta) * Math.Sin(phi)));
        }

        for (var r = 0; r < rings; r++)
        for (var s = 0; s < segments; s++)
        {
            // Wound so the face normal points outwards
            yield return (At(r, s), At(r, s + 1), At(r + 1, s));
            yield return (At(r, s + 1), At(r + 1, s + 1), At(r + 1, s));
        }
    }

    private static NormalAgreementResult Measure(Func<Vector3, Vector3> normalOf)
    {
        var agreement = new EncryptedCars.NormalAgreement();
        foreach (var (a, b, c) in Sphere()) agreement.Add(a, b, c, normalOf(a), normalOf(b), normalOf(c));
        return new NormalAgreementResult(agreement.MeanCosine, agreement.BackShare, agreement.LooksScrambled);
    }

    private readonly record struct NormalAgreementResult(double MeanCosine, double BackShare, bool Scrambled);

    [Fact]
    public void A_real_model_is_not_taken_for_an_encrypted_one()
    {
        var result = Measure(p => Vector3.Normalize(p));

        Assert.True(result.MeanCosine > 0.9);
        Assert.False(result.Scrambled);
    }

    [Fact]
    public void A_model_with_its_faces_flipped_is_not_taken_for_an_encrypted_one()
    {
        var result = Measure(p => -Vector3.Normalize(p));

        Assert.True(result.BackShare > 0.9);
        Assert.False(result.Scrambled);
    }

    [Fact]
    public void Normals_pointing_anywhere_are_an_encrypted_model()
    {
        var random = new Random(11);
        var result = Measure(_ => Vector3.Normalize(new Vector3(
            (float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1))));

        Assert.InRange(result.MeanCosine, -0.05, 0.05);
        Assert.True(result.Scrambled);
    }

    [Fact]
    public void Too_little_to_go_on_is_not_evidence()
    {
        var agreement = new EncryptedCars.NormalAgreement();
        var up = Vector3.UnitY;
        agreement.Add(Vector3.Zero, Vector3.UnitZ, Vector3.UnitX, -up, up, -up);

        Assert.False(agreement.LooksScrambled);
    }

    [Fact]
    public void The_car_model_is_the_biggest_that_is_not_the_collider_or_a_lod()
    {
        using var dir = new TempDir();
        dir.Bytes("car/collider.kn5", new byte[5000]);
        dir.Bytes("car/car_lod_b.kn5", new byte[4000]);
        dir.Bytes("car/car.kn5", new byte[3000]);
        dir.Bytes("car/interior.kn5", new byte[100]);

        Assert.Equal(dir.Combine("car", "car.kn5"), EncryptedCars.MainModel(dir.Combine("car")));
        Assert.Null(EncryptedCars.MainModel(dir.Combine("missing")));
    }
}
