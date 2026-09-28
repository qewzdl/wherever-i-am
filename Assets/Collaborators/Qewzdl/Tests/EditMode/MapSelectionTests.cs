using System.Collections.Generic;
using System.Net;
using NUnit.Framework;
using UnityEngine;

// Choosing a map: which ones are open, how a host says which one it is
// playing, and how somebody looking for that map finds it.
public sealed class MapSelectionTests
{
    private readonly List<Object> created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
                Object.DestroyImmediate(instance);
        }

        created.Clear();
    }

    // First is open; each after it opens when the one before it is won; a
    // map nobody knows is not open at all.
    [Test]
    public void Maps_OpenOneAfterAnother_AsTheOneBeforeIsWon()
    {
        GameMapCatalog catalog = Catalog(Map(10, "House"), Map(20, "Barn"), Map(30, "Church"));

        HashSet<int> won = new();
        Assert.That(MapProgress.IsUnlocked(catalog, 10, won), Is.True, "The first map is never locked.");
        Assert.That(MapProgress.IsUnlocked(catalog, 20, won), Is.False);
        Assert.That(MapProgress.IsUnlocked(catalog, 30, won), Is.False);
        Assert.That(MapProgress.IsUnlocked(catalog, 99, won), Is.False, "A map not in the catalog was open.");

        won.Add(10);
        Assert.That(MapProgress.IsUnlocked(catalog, 20, won), Is.True, "Winning the first did not open the second.");
        Assert.That(MapProgress.IsUnlocked(catalog, 30, won), Is.False, "Winning the first opened the third.");

        Assert.That(MapProgress.GateOf(catalog, 30).MapId, Is.EqualTo(20));
        Assert.That(MapProgress.GateOf(catalog, 10), Is.Null);
    }

    // What was won is kept as maps, not as a place in the line: moving a map
    // moves the gate in front of it and takes nothing away.
    [Test]
    public void ReorderingTheMaps_MovesTheGates_WithoutTakingAWinAway()
    {
        GameMapDefinition house = Map(10, "House");
        GameMapDefinition barn = Map(20, "Barn");
        GameMapDefinition church = Map(30, "Church");
        GameMapCatalog catalog = Catalog(house, barn, church);
        HashSet<int> won = new() { 10 };

        Assert.That(catalog.MoveMapEditor(2, -1), Is.True);

        Assert.That(MapProgress.GateOf(catalog, 30), Is.SameAs(house));
        Assert.That(MapProgress.IsUnlocked(catalog, 30, won), Is.True,
            "The map moved next to one already won is still locked.");
        Assert.That(MapProgress.IsUnlocked(catalog, 20, won), Is.False,
            "The map moved behind an unwon one is still open.");
    }

    // A hole left in the list is not a gate nobody can pass.
    [Test]
    public void AHoleInTheList_IsSteppedOver()
    {
        GameMapCatalog catalog = Catalog(Map(10, "House"), null, Map(30, "Church"));

        Assert.That(MapProgress.GateOf(catalog, 30).MapId, Is.EqualTo(10));
        Assert.That(MapProgress.IsUnlocked(catalog, 30, new HashSet<int> { 10 }), Is.True);
    }

    [Test]
    public void AnAdvertSaysWhichMapTheRoomIsPlaying()
    {
        LanLobbyAdvert sent = new(2, 7777, 1, 4, "Alex", mapId: 20);

        Assert.That(LanLobbyAdvert.TryParse(sent.Serialize(), 2, out LanLobbyAdvert read), Is.True);
        Assert.That(read.mapId, Is.EqualTo(20));
    }

    // An older build's advert has no map, and reading it as the first map
    // would put a room in front of somebody looking for a map it is not on.
    [Test]
    public void AnAdvertWithoutAMap_FromAnOlderBuild_IsNotAlobby()
    {
        LanLobbyAdvert old = new(2, 7777, 1, 4, "Alex") { format = 1 };

        Assert.That(LanLobbyAdvert.TryParse(old.Serialize(), 2, out _), Is.False);
    }

    [Test]
    public void TheBrowser_ShowsTheRoomsOnTheChosenMap_OrEveryRoom()
    {
        List<LanLobbyDiscovery.Entry> lobbies = new()
        {
            Entry("192.168.1.2", 10),
            Entry("192.168.1.3", 20),
            Entry("192.168.1.4", 20),
        };

        Assert.That(MainMenuDocument.FilterByMap(lobbies, 20).ConvertAll(e => e.Address),
            Is.EqualTo(new[] { "192.168.1.3", "192.168.1.4" }));
        Assert.That(MainMenuDocument.FilterByMap(lobbies, 30), Is.Empty);
        Assert.That(MainMenuDocument.FilterByMap(lobbies, MainMenuDocument.AnyMap), Has.Count.EqualTo(3));
    }

    private static LanLobbyDiscovery.Entry Entry(string address, int mapId)
    {
        return new LanLobbyDiscovery.Entry(
            new IPEndPoint(IPAddress.Parse(address), 7777),
            new LanLobbyAdvert(2, 7777, 1, 4, address, mapId),
            0f);
    }

    private GameMapDefinition Map(int id, string name)
    {
        GameMapDefinition map = ScriptableObject.CreateInstance<GameMapDefinition>();
        map.ConfigureEditor(id, name, $"Map_{id}", $"Assets/Scenes/Map_{id}.unity");
        created.Add(map);
        return map;
    }

    private GameMapCatalog Catalog(params GameMapDefinition[] maps)
    {
        GameMapCatalog catalog = ScriptableObject.CreateInstance<GameMapCatalog>();
        TestReflection.SetField(catalog, "maps", maps);
        created.Add(catalog);
        return catalog;
    }

    // The reset has to find the build's entry by the name Unity gave it in the
    // registry. These three are copied from a build's saves on a real machine.
    [TestCase("wia.mapsWon", "wia.mapsWon_h2230816237")]
    [TestCase("wia.playerName", "wia.playerName_h3070940928")]
    [TestCase("wia.joinAddress", "wia.joinAddress_h765580160")]
    public void TheResetNamesTheBuildsEntryAsUnityDoes(string prefsKey, string registryName)
    {
        Assert.That(MapProgressReset.RegistryName(prefsKey), Is.EqualTo(registryName));
    }
}
