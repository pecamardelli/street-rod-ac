using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;

namespace StreetRodAC.Tests;

/// <summary>
/// Part content as hand-edited or migrated files leave it: pack.json and engine_builds.json with nulls in them,
/// and saved part trees brought up to date through part_aliases.json, on a small catalog in a temp folder
/// </summary>
public sealed class PartsContentTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ---- Nulls in pack.json ----

    [Fact]
    public void A_pack_with_null_lists_and_null_elements_still_mates()
    {
        _temp.File("engines/generic/pack.json", """
            {
              "id": "engines/generic", "source": null,
              "parts": [
                { "id": "engines/generic/manifold", "name": "manifold", "class_chain": null, "properties": null, "derived": null,
                  "slot_roles": null, "stock_parts": [null], "required_slots": null, "categories": [null, "intake"], "config": { "x": null },
                  "slots": [ null, { "id": 5, "name": null, "takes": ["carb:4bbl", null], "fits": null, "attaches_to": null, "compatible_with": null } ] },
                { "id": "engines/generic/carb", "name": null, "class_chain": [null], "properties": null,
                  "slots": [ { "id": 1, "fits": [null, "carb:4bbl"], "takes": null, "attaches_to": [null], "compatible_with": [null] } ] },
                { "id": "engines/generic/nothing", "slots": null }
              ]
            }
            """);

        var catalog = PartsCatalog.Load(_temp.Path);
        var manifold = catalog.Get("engines/generic/manifold")!;
        var carb = catalog.Get("engines/generic/carb")!;

        Assert.Single(manifold.Slots);
        Assert.Equal(["carb:4bbl"], manifold.Slots[0].Takes);
        Assert.Empty(manifold.StockParts);
        Assert.Equal(["intake"], manifold.Categories);
        Assert.Empty(manifold.Config);
        Assert.Empty(carb.ClassChain);
        Assert.Equal(string.Empty, carb.Name);
        Assert.Empty(catalog.Get("engines/generic/nothing")!.Slots);

        var mountable = catalog.FindMountable(manifold, manifold.Slots[0]);
        Assert.Contains(mountable, m => m.Part == carb && m.Slot.Id == 1);
        // Asked again: the mating index was built once and is not broken
        Assert.Contains(catalog.FindMountable(manifold, manifold.Slots[0]), m => m.Part == carb);
    }

    // ---- Nulls in engine_builds.json ----

    [Fact]
    public void Builds_with_null_parts_or_no_id_cost_only_themselves()
    {
        WriteBlockPacks();
        _temp.File(EngineBuild.FileName, """
            [
              null,
              { "id": null, "name": "nameless", "parts": [ { "part": "engines/new/block" } ] },
              { "id": "notes/a", "name": null, "parts": null },
              { "id": "notes/b", "name": "b", "parts": [ null, { "part": null, "source": "x#0x1" }, { "part": "engines/new/block" } ] }
            ]
            """);

        var catalog = PartsCatalog.Load(_temp.Path);

        Assert.Equal(["notes/a", "notes/b"], catalog.EngineBuilds.Select(b => b.Id));
        Assert.Contains(catalog.Problems, p => p.Contains(EngineBuild.FileName) && p.Contains("without an id"));
        Assert.Empty(catalog.EngineBuilds[0].Parts);
        Assert.Equal(string.Empty, catalog.EngineBuilds[0].Name);
        Assert.Equal(2, catalog.EngineBuilds[1].Parts.Count);

        // Scriptless parts don't run, but nothing throws: the index is empty of runnable builds, not missing
        var index = EngineBuildIndex.Create(catalog);
        Assert.Empty(index.Runnable);
    }

    // ---- Saved trees brought up to date ----

    /// <summary>
    /// The current release: a block whose pan now hangs by slot 10 (it was 9) and a head pad at 20, and another
    /// pack's head that bolts on it. Aliases: old -> mid -> new, and a loop x -> y -> x.
    /// </summary>
    private void WriteBlockPacks()
    {
        _temp.File("engines/new/pack.json", """
            {
              "id": "engines/new", "source": "new.rpk",
              "parts": [
                { "id": "engines/new/block", "name": "block", "slots": [ { "id": 10, "name": "pan" }, { "id": 20, "name": "head" } ] },
                { "id": "engines/new/pan", "name": "pan",
                  "slots": [ { "id": 1, "attaches_to": [ { "part": "engines/new/block", "slot": 10, "source": "" } ] } ] }
              ]
            }
            """);
        _temp.File("engines/other/pack.json", """
            {
              "id": "engines/other", "source": "other.rpk",
              "parts": [
                { "id": "engines/other/head", "name": "head",
                  "slots": [ { "id": 1, "attaches_to": [ { "part": "engines/new/block", "slot": 20, "source": "" } ] } ] }
              ]
            }
            """);
        _temp.File(PartPack.AliasesFileName, """
            {
              "engines/old/block": "engines/mid/block",
              "engines/mid/block": "engines/new/block",
              "engines/old/pan": "engines/new/pan",
              "engines/loop/x": "engines/loop/y",
              "engines/loop/y": "engines/loop/x"
            }
            """);
    }

    [Fact]
    public void An_alias_chain_leads_to_the_current_part_and_a_renumbered_slot_is_found_again()
    {
        WriteBlockPacks();
        var catalog = PartsCatalog.Load(_temp.Path);

        var pan = new PartInstance("engines/old/pan") { ParentSlot = 9, OwnSlot = 1 };
        var block = new PartInstance("engines/old/block") { ParentSlot = PartInstance.CarEngineSlot, Children = [pan] };
        var loose = new List<PartInstance>();

        Assert.True(SavedParts.BringUpToDate(catalog, block, loose));

        Assert.Equal("engines/new/block", block.DefinitionId);
        Assert.Equal("engines/new/pan", pan.DefinitionId);
        Assert.Same(pan, Assert.Single(block.Children));
        Assert.Equal(10, pan.ParentSlot);
        Assert.Equal(1, pan.OwnSlot);
        Assert.Empty(loose);

        // Up to date now: a second pass changes nothing
        Assert.False(SavedParts.BringUpToDate(catalog, block, loose));
    }

    [Fact]
    public void An_alias_loop_ends_at_the_hop_limit_and_the_unknown_part_stays_as_it_is()
    {
        WriteBlockPacks();
        var catalog = PartsCatalog.Load(_temp.Path);

        var stranger = new PartInstance("engines/loop/x") { ParentSlot = 20, OwnSlot = 1 };
        var block = new PartInstance("engines/new/block") { ParentSlot = PartInstance.CarEngineSlot, Children = [stranger] };
        var loose = new List<PartInstance>();

        SavedParts.BringUpToDate(catalog, block, loose);

        Assert.StartsWith("engines/loop/", stranger.DefinitionId);
        Assert.Null(catalog.Get(stranger.DefinitionId));
        Assert.Same(stranger, Assert.Single(block.Children));
        Assert.Equal(20, stranger.ParentSlot);
        Assert.Empty(loose);
    }

    [Fact]
    public void A_second_part_on_a_taken_slot_comes_off_loose()
    {
        WriteBlockPacks();
        var catalog = PartsCatalog.Load(_temp.Path);

        var first = new PartInstance("engines/other/head") { ParentSlot = 20, OwnSlot = 1 };
        var second = new PartInstance("engines/other/head") { ParentSlot = 20, OwnSlot = 1 };
        var block = new PartInstance("engines/new/block") { ParentSlot = PartInstance.CarEngineSlot, Children = [first, second] };
        var loose = new List<PartInstance>();

        Assert.True(SavedParts.BringUpToDate(catalog, block, loose));

        Assert.Same(first, Assert.Single(block.Children));
        Assert.Equal(20, first.ParentSlot);
        Assert.Same(second, Assert.Single(loose));
        Assert.Equal(0, second.ParentSlot);
        Assert.Equal(0, second.OwnSlot);
    }
}
