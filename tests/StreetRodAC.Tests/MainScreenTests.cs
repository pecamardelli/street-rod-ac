using System.Numerics;
using Street_Rod_AC.Controls.Showcase;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Services.Showcase;

namespace StreetRodAC.Tests;

/// <summary>The main screen's showroom: the camera moves, the order cars and rooms come round in, and what it finds to show</summary>
public class MainScreenTests
{
    // A '69 Camaro's wheel hubs, as the model has them
    private static readonly CarFrame Camaro = CarFrame.FromWheels(
        new Vector3(0.74f, 0.33f, 1.39f), new Vector3(-0.74f, 0.33f, 1.39f),
        new Vector3(0.74f, 0.33f, -1.35f), new Vector3(-0.74f, 0.33f, -1.35f), 1.34f)!.Value;

    private static float Reach(CameraPose pose) => new Vector2(pose.Position.X, pose.Position.Z).Length();

    [Fact]
    public void A_car_is_framed_from_its_wheels()
    {
        Assert.Equal(2.74f, Camaro.FrontAxle - Camaro.RearAxle, 2);
        Assert.Equal(0.74f, Camaro.HalfTrack, 2);
        Assert.InRange(Camaro.Length, 4.5f, 5.1f);
        Assert.True(Camaro.Nose > Camaro.FrontAxle && Camaro.Tail < Camaro.RearAxle);
    }

    [Fact]
    public void A_model_built_facing_backwards_is_turned_round()
    {
        var frame = CarFrame.FromWheels(
            new Vector3(0.74f, 0.33f, -1.39f), new Vector3(-0.74f, 0.33f, -1.39f),
            new Vector3(0.74f, 0.33f, 1.35f), new Vector3(-0.74f, 0.33f, 1.35f), 1.34f);

        Assert.NotNull(frame);
        Assert.True(frame.Value.FrontAxle > frame.Value.RearAxle);
    }

    [Fact]
    public void Wheels_that_make_no_sense_give_no_frame()
    {
        var hub = new Vector3(0f, 0.3f, 0f);
        Assert.Null(CarFrame.FromWheels(hub, hub, hub, hub, 1.3f));
    }

    [Fact]
    public void Every_shot_stays_inside_the_room_and_off_the_floor()
    {
        foreach (var wall in new[] { 6f, 12f, 14.4f, 39f })
        foreach (var kind in Enum.GetValues<ShotKind>())
        for (var seed = 0; seed < 20; seed++)
        {
            var random = new Random(seed);
            var shot = ShowcaseShots.Make(kind, Camaro, wall, mirror: seed % 2 == 0, random);

            Assert.True(shot.Duration > 4f, $"{kind} too short");
            foreach (var t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                var pose = shot.At(t * shot.Duration);
                Assert.True(float.IsFinite(pose.Radius) && float.IsFinite(pose.Alpha) && float.IsFinite(pose.Beta));
                Assert.True(Reach(pose) <= wall - 1.5f + 0.01f, $"{kind} in a {wall} m room reaches {Reach(pose):F2} m");
                Assert.True(pose.Position.Y >= 0.18f - 0.01f, $"{kind} goes under the floor");
            }
        }
    }

    [Fact]
    public void A_camera_beyond_the_wall_is_brought_in_along_the_same_line()
    {
        var pose = new CameraPose(new Vector3(0f, 0.6f, 0f), 30f, 0.3f, 0.1f);

        var kept = ShowcaseShots.KeepInside(pose, 12f);

        Assert.Equal(10.5f, Reach(kept), 2);
        Assert.Equal(pose.Alpha, kept.Alpha);
        Assert.Equal(pose.Beta, kept.Beta);
        Assert.Equal(pose.Target, kept.Target);
    }

    [Fact]
    public void A_mirrored_shot_is_taken_from_the_other_side()
    {
        var pose = new CameraPose(new Vector3(0.7f, 0.3f, 1.4f), 2f, 0.4f, 0.1f);

        var mirrored = ShowcaseShots.Mirror(pose);

        Assert.Equal(-pose.Position.X, mirrored.Position.X, 3);
        Assert.Equal(pose.Position.Y, mirrored.Position.Y, 3);
        Assert.Equal(pose.Position.Z, mirrored.Position.Z, 3);
    }

    [Fact]
    public void The_same_kind_of_shot_never_plays_twice_running()
    {
        // Boxed in by cars on both sides, where most kinds are spoilt and a recent one could otherwise win
        var row = new[] { new CarPlacement(-3.4f, 0f, 0f), new CarPlacement(0f, 0f, 0f), new CarPlacement(3.4f, 0f, 0f) };
        var others = new[] { new CarObstacle(row[0], Camaro), new CarObstacle(row[2], Camaro) };

        var random = new Random(7);
        var recent = new List<ShotKind>();
        ShotKind? previous = null;

        for (var i = 0; i < 200; i++)
        {
            var kind = ShowcaseLineup.Choose(Camaro, row[1], 14f, others, recent, mirror: i % 2 == 0, random).Kind;
            Assert.NotEqual(previous, kind);
            previous = kind;
            recent.Add(kind);
            if (recent.Count > 2) recent.RemoveAt(0);
        }
    }

    private static ShowcaseCar Car(string id, params string[] skins) => new(id, id, id, skins);

    [Fact]
    public void A_room_holds_different_cars_and_every_car_gets_its_turn()
    {
        var cars = Enumerable.Range(0, 5).Select(i => Car($"car{i}")).ToList();
        var scenes = new[] { new ShowcaseScene("a", "a.kn5", 12f), new ShowcaseScene("b", "b.kn5", 12f) };
        var playlist = new ShowcasePlaylist(cars, scenes, new Random(3), _ => 3);

        var sets = Enumerable.Range(0, 30).Select(_ => playlist.Next()).ToList();

        Assert.All(sets, set => Assert.Equal(3, set.Cars.Select(c => c.Car.Id).Distinct().Count()));
        var counts = sets.SelectMany(s => s.Cars).GroupBy(c => c.Car.Id).Select(g => g.Count()).ToList();
        Assert.Equal(5, counts.Count);
        Assert.True(counts.Max() - counts.Min() <= 2, string.Join(", ", counts));
    }

    [Fact]
    public void The_room_is_never_the_one_just_left()
    {
        var cars = Enumerable.Range(0, 4).Select(i => Car($"car{i}")).ToList();
        var scenes = new[] { new ShowcaseScene("a", "a.kn5", 12f), new ShowcaseScene("b", "b.kn5", 12f), new ShowcaseScene("c", "c.kn5", 30f) };
        var playlist = new ShowcasePlaylist(cars, scenes, new Random(5));

        var sets = Enumerable.Range(0, 30).Select(_ => playlist.Next()).ToList();

        for (var i = 1; i < sets.Count; i++) Assert.NotEqual(sets[i - 1].Scene.Id, sets[i].Scene.Id);

        // A big room holds four cars, a shed three
        Assert.All(sets, set => Assert.Equal(set.Scene.WallRadius >= 20f ? 4 : 3, set.Cars.Count));
    }

    [Fact]
    public void One_car_in_one_room_still_plays()
    {
        var playlist = new ShowcasePlaylist([Car("only", "red", "blue")], [new ShowcaseScene("a", "a.kn5", 12f)], new Random(1));

        for (var i = 0; i < 5; i++)
        {
            var set = playlist.Next();
            var entry = Assert.Single(set.Cars);
            Assert.Equal("only", entry.Car.Id);
            Assert.Contains(entry.Skin, new[] { "red", "blue" });
        }
    }

    [Fact]
    public void A_shot_framed_on_a_car_follows_the_car_wherever_it_stands()
    {
        var placement = new CarPlacement(3f, -4f, 0.7f);
        var local = new CameraPose(new Vector3(0.5f, 0.6f, 1.2f), 5f, 0.4f, 0.1f);

        var world = placement.ToWorld(local);

        var expected = placement.ToWorld(local.Position);
        Assert.Equal(expected.X, world.Position.X, 3);
        Assert.Equal(expected.Y, world.Position.Y, 3);
        Assert.Equal(expected.Z, world.Position.Z, 3);

        var back = placement.ToLocal(expected.X, expected.Z);
        Assert.Equal(local.Position.X, back.X, 3);
        Assert.Equal(local.Position.Z, back.Y, 3);
    }

    [Fact]
    public void Every_lineup_stands_inside_its_room_without_cars_on_top_of_each_other()
    {
        foreach (var wall in new[] { 12f, 14f, 24f, 39f })
        for (var seed = 0; seed < 30; seed++)
        {
            var count = ShowcaseLineup.CarsFor(wall);
            var cars = ShowcaseLineup.Arrange(count, wall, new Random(seed));

            Assert.All(cars, c => Assert.True(ShowcaseLineup.Fits(c, wall)));
            for (var i = 0; i < cars.Count; i++)
            for (var j = 0; j < cars.Count; j++)
            {
                if (i == j) continue;
                var other = new CarObstacle(cars[j], CarFrame.Nominal);
                Assert.False(other.Contains(new Vector3(cars[i].X, 0.5f, cars[i].Z), 0.3f), $"cars {i} and {j} overlap in a {wall} m room");
            }
        }
    }

    [Fact]
    public void The_garage_takes_three_cars()
    {
        for (var seed = 0; seed < 30; seed++)
            Assert.Equal(3, ShowcaseLineup.Arrange(3, 12f, new Random(seed)).Count);
    }

    [Fact]
    public void A_car_near_a_wall_is_filmed_from_where_there_is_room()
    {
        // Parked a metre and a half off the wall of a 12 m garage, nose to it
        var nearWall = new CarPlacement(0f, 7.5f, 0f);

        for (var seed = 0; seed < 20; seed++)
        {
            var shot = ShowcaseLineup.Choose(Camaro, nearWall, 12f, [], [], mirror: seed % 2 == 0, new Random(seed));
            Assert.True(ShowcaseLineup.IsClear(shot, new CarObstacle(nearWall, Camaro), []), $"{shot.Kind} ends up in the car");
        }
    }

    [Fact]
    public void A_car_in_the_way_blocks_the_view_but_not_from_over_its_roof()
    {
        var inTheWay = new CarObstacle(new CarPlacement(0f, 4f, 0f), CarFrame.Nominal);

        // Looking along the floor from behind it, at a car beyond it
        Assert.True(inTheWay.Blocks(new Vector3(0f, 0.8f, 9f), new Vector3(0f, 0.6f, 0f), 0f));

        // From high above, down past its roof
        Assert.False(inTheWay.Blocks(new Vector3(0f, 12f, 9f), new Vector3(0f, 0.6f, 0f), 0f));

        // From the side, missing it
        Assert.False(inTheWay.Blocks(new Vector3(6f, 0.8f, 0f), new Vector3(0f, 0.6f, 0f), 0f));

        Assert.True(inTheWay.Contains(new Vector3(0.3f, 0.5f, 4.2f), 0f));
    }

    [Fact]
    public void The_camera_on_a_car_in_a_row_keeps_clear_of_its_neighbours()
    {
        var row = new[] { new CarPlacement(-3.4f, 0f, 0f), new CarPlacement(0f, 0f, 0f), new CarPlacement(3.4f, 0f, 0f) };
        var others = new[] { new CarObstacle(row[0], Camaro), new CarObstacle(row[2], Camaro) };

        for (var seed = 0; seed < 20; seed++)
        {
            var shot = ShowcaseLineup.Choose(Camaro, row[1], 14f, others, [], mirror: seed % 2 == 0, new Random(seed));
            Assert.True(ShowcaseLineup.IsClear(shot, new CarObstacle(row[1], Camaro), others), $"{shot.Kind} runs into a car");
            Assert.True(shot.Squeeze >= 0.75f, $"{shot.Kind} squeezed to {shot.Squeeze:P0}");
        }
    }

    [Fact]
    public void Scenes_are_found_in_the_games_own_garages_first_then_in_the_installed_showrooms()
    {
        using var dir = new TempDir();
        var garages = dir.Combine("Garages");
        var showrooms = dir.Combine("showroom");
        dir.File("Garages/garage/garage.kn5", "own");
        dir.File("showroom/garage/garage.kn5", "ac");
        dir.File("showroom/Hangar/hangar_v2.kn5", "spelled otherwise");
        var file = dir.File("scenes.json", """
            { "scenes": [
                { "id": "garage", "wallRadius": 12 },
                { "id": "Hangar", "wallRadius": 14.4 },
                { "id": "beach", "wallRadius": 24 },
                { "id": "", "wallRadius": 10 },
                { "id": "bad", "wallRadius": 0 }
            ] }
            """);

        var scenes = ShowcaseContent.LoadScenes(file, garages, showrooms);

        Assert.Equal(["garage", "Hangar"], scenes.Select(s => s.Id));
        Assert.Equal(dir.Combine("Garages", "garage", "garage.kn5"), scenes[0].Kn5);
        Assert.Equal(dir.Combine("showroom", "Hangar", "hangar_v2.kn5"), scenes[1].Kn5);
        Assert.Equal(14.4f, scenes[1].WallRadius);
    }

    [Fact]
    public void A_missing_or_broken_scene_list_leaves_nothing_to_show()
    {
        using var dir = new TempDir();

        Assert.Empty(ShowcaseContent.LoadScenes(dir.Combine("none.json"), dir.Path, dir.Path));
        Assert.Empty(ShowcaseContent.LoadScenes(dir.File("broken.json", "{ scenes: ["), dir.Path, dir.Path));
    }

    [Fact]
    public void The_shipped_scene_list_reads()
    {
        using var dir = new TempDir();
        foreach (var id in new[] { "garage", "Hangar", "showroom", "industrial", "beach" }) dir.File($"{id}/{id}.kn5", "");

        var scenes = ShowcaseContent.LoadScenes(Path.Combine(RepoPaths.GameProject, "Assets", "Showcase", "scenes.json"), dir.Path, dir.Path);

        Assert.Equal(5, scenes.Count);
        Assert.All(scenes, s => Assert.True(s.WallRadius >= 10f));
    }

    [Fact]
    public void Only_installed_cars_are_shown_under_their_year_and_name()
    {
        var catalog = new[]
        {
            new CarDefinition { Id = "camaro", Name = "Chevrolet Camaro Z/28", Year = 1969, AvailableSkins = ["red"] },
            new CarDefinition { Id = "cuda", Name = "1971 Plymouth 'Cuda", Year = 1971 },
            new CarDefinition { Id = "gone", Name = "Not Installed", Year = 1960 }
        };
        var installed = new HashSet<string>(["camaro", "CUDA"], StringComparer.OrdinalIgnoreCase);

        var cars = ShowcaseContent.Cars(catalog.Where(c => installed.Contains(c.Id)), @"C:\ac\content\cars");

        Assert.Equal(["camaro", "cuda"], cars.Select(c => c.Id));
        Assert.Equal("1969 Chevrolet Camaro Z/28", cars[0].Title);
        Assert.Equal("1971 Plymouth 'Cuda", cars[1].Title);
        Assert.Equal(Path.Combine(@"C:\ac\content\cars", "camaro"), cars[0].Directory);
        Assert.Equal(["red"], cars[0].Skins);
    }

    [Fact]
    public void A_room_that_will_not_load_is_never_picked_again()
    {
        var cars = Enumerable.Range(0, 5).Select(i => Car($"car{i}")).ToList();
        var scenes = new[] { new ShowcaseScene("a", "a.kn5", 12f), new ShowcaseScene("b", "b.kn5", 12f), new ShowcaseScene("c", "c.kn5", 12f) };
        var playlist = new ShowcasePlaylist(cars, scenes, new Random(3));

        Assert.True(playlist.Drop(scenes[1]));
        Assert.DoesNotContain(Enumerable.Range(0, 30).Select(_ => playlist.Next()), s => s.Scene.Id == "b");

        Assert.True(playlist.Drop(scenes[0]));
        Assert.False(playlist.Drop(scenes[2]));
    }

    [Fact]
    public void A_car_stored_without_skins_is_shown_in_its_default()
    {
        var catalog = new[] { new CarDefinition { Id = "camaro", Name = "Camaro", AvailableSkins = null! } };

        var cars = ShowcaseContent.Cars(catalog, @"C:\ac\content\cars");

        Assert.Empty(Assert.Single(cars).Skins);
    }
}
