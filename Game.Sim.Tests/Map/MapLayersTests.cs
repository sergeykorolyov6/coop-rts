using System;
using NUnit.Framework;

namespace Game.Sim.Tests;

// plan-terrain-and-factions.md, stage T2: a map carries kinds of rock (terrain.csv) and height
// levels with ramps. Stage T2 only loads them; what they do to walking and sight is T3 and T4.
public class MapLayersTests
{
    private static string Json(string rows, string? levels = null) =>
        "{\"width\": 4, \"height\": 2, \"rows_bottom_up\": [" + rows + "], "
        + (levels != null ? "\"levels_bottom_up\": [" + levels + "], " : "")
        + "\"starts\": [], \"camps\": []}";

    [Test]
    public void Terrain_TableFromTheRepo_HasGroundEternalRockAndDestructibleKinds()
    {
        GameData data = RepoData.Get();
        Assert.That(data.TerrainBySymbol('.')!.Passable, Is.True);
        TerrainStats rock = data.TerrainBySymbol('#')!;
        Assert.That((rock.Id, rock.Passable, rock.Destructible), Is.EqualTo(("rock", false, false)));
        TerrainStats soft = data.TerrainBySymbol('%')!;
        Assert.That((soft.Id, soft.Passable, soft.Destructible), Is.EqualTo(("soft_rock", false, true)));
        Assert.That(data.TerrainBySymbol('?'), Is.Null);
    }

    [Test]
    public void Rows_RockOfAnyKindBlocks_AndKeepsItsKind()
    {
        MapSetup setup = MapLoader.Parse(Json("\".#%o\", \"@RC.\""), Array.Empty<int>(), RepoData.Get());
        var map = new MapState(setup);

        Assert.That(map.IsBlocked(0, 0), Is.False);
        Assert.That(map.TerrainAt(0, 0), Is.Null, "plain ground");
        Assert.That(map.TerrainAt(1, 0)!.Id, Is.EqualTo("rock"));
        Assert.That(map.TerrainAt(2, 0)!.Id, Is.EqualTo("soft_rock"));
        Assert.That(map.OreAt(3, 0), Is.EqualTo(Resources.Primary));
        Assert.That(map.TerrainAt(0, 1)!.Id, Is.EqualTo("hard_rock"));
        Assert.That(map.TerrainAt(1, 1)!.Id, Is.EqualTo("ore_rock"));
        Assert.That(map.TerrainAt(2, 1)!.Id, Is.EqualTo("crystal_rock"));
        foreach ((int x, int y) in new[] { (1, 0), (2, 0), (0, 1), (1, 1), (2, 1) })
            Assert.That(map.IsBlocked(x, y) && !map.IsPassable(x, y), Is.True, $"({x}, {y})");
    }

    [Test]
    public void Rows_UnknownSymbol_IsAnError()
    {
        var ex = Assert.Throws<FormatException>(() => MapLoader.Parse(Json("\"..?.\", \"....\""), Array.Empty<int>(), RepoData.Get()));
        Assert.That(ex!.Message, Does.Contain("(2, 0)"));
    }

    [Test]
    public void Levels_DigitIsLevel_LetterIsRampFromThatLevel()
    {
        MapSetup setup = MapLoader.Parse(Json("\"....\", \"....\"", "\"00a1\", \"00a1\""), Array.Empty<int>(), RepoData.Get());
        var map = new MapState(setup);

        Assert.That((map.LevelAt(0, 0), map.IsRamp(0, 0)), Is.EqualTo((0, false)));
        Assert.That((map.LevelAt(2, 0), map.IsRamp(2, 0)), Is.EqualTo((0, true)));
        Assert.That((map.LevelAt(3, 1), map.IsRamp(3, 1)), Is.EqualTo((1, false)));
    }

    [Test]
    public void Levels_Absent_EverythingIsLevelZero()
    {
        var map = new MapState(MapLoader.Parse(Json("\"....\", \"....\""), Array.Empty<int>(), RepoData.Get()));
        Assert.That((map.LevelAt(3, 1), map.IsRamp(3, 1)), Is.EqualTo((0, false)));
    }

    [Test]
    public void Levels_WrongSizeOrSymbol_IsAnError()
    {
        Assert.Throws<FormatException>(() => MapLoader.Parse(Json("\"....\", \"....\"", "\"0000\""), Array.Empty<int>(), RepoData.Get()));
        Assert.Throws<FormatException>(() => MapLoader.Parse(Json("\"....\", \"....\"", "\"0000\", \"00x0\""), Array.Empty<int>(), RepoData.Get()));
        // Level 8 does not exist, and a ramp on the top level has nowhere to lead.
        Assert.Throws<FormatException>(() => MapLoader.Parse(Json("\"....\", \"....\"", "\"0000\", \"0080\""), Array.Empty<int>(), RepoData.Get()));
        Assert.Throws<FormatException>(() => MapLoader.Parse(Json("\"....\", \"....\"", "\"0000\", \"00h0\""), Array.Empty<int>(), RepoData.Get()));
    }

    [Test]
    public void Terrain_TwoKindsWithOneSymbol_IsAnError()
    {
        const string twice = "[{\"id\":\"a\",\"symbol\":\"#\",\"passable\":0,\"destructible\":0},{\"id\":\"b\",\"symbol\":\"#\",\"passable\":0,\"destructible\":1}]";
        Assert.Throws<FormatException>(() => GameData.Parse(new GameDataSources { Terrain = twice }));
    }
}
