using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// A map definition that exists as an asset and is in no catalog cannot be
// loaded, cannot be listed, and said nothing about itself - the only way into
// the catalog was to make a new map or duplicate one already in it. The
// manager offers to add it now, and this is the decision that offer rests on.
public sealed class GameMapRegistrationTests
{
    private readonly List<Object> made = new();

    [TearDown]
    public void TearDown()
    {
        foreach (Object asset in made)
        {
            if (asset != null)
                Object.DestroyImmediate(asset);
        }

        made.Clear();
    }

    [Test]
    public void ADefinitionTheCatalogDoesNotHoldIsOffered()
    {
        GameMapCatalog catalog = Make<GameMapCatalog>();
        GameMapDefinition inside = Definition(1, "Inside");
        GameMapDefinition outside = Definition(2, "Outside");

        Assert.That(catalog.AddMapEditor(inside), Is.True);

        List<GameMapDefinition> found = new();

        GameMapManagerWindow.CollectUnregistered(
            catalog,
            new[] { inside, outside },
            found);

        Assert.That(found, Is.EqualTo(new[] { outside }));
    }

    // The case that makes comparing by id wrong: a copy carrying a number the
    // catalog already uses. By id it would look registered and never be.
    [Test]
    public void ACopyWearingATakenIdIsStillOffered()
    {
        GameMapCatalog catalog = Make<GameMapCatalog>();
        GameMapDefinition original = Definition(7, "Original");
        GameMapDefinition copy = Definition(7, "Copy from another branch");

        Assert.That(catalog.AddMapEditor(original), Is.True);

        List<GameMapDefinition> found = new();

        GameMapManagerWindow.CollectUnregistered(
            catalog,
            new[] { original, copy },
            found);

        Assert.That(
            found,
            Is.EqualTo(new[] { copy }),
            "A definition sharing a registered map's id was taken for that " +
            "map, so it would never be offered and never be loadable.");
    }

    [Test]
    public void AnEmptyCatalogOffersEverything()
    {
        GameMapCatalog catalog = Make<GameMapCatalog>();
        GameMapDefinition first = Definition(0, "First");
        GameMapDefinition second = Definition(1, "Second");

        List<GameMapDefinition> found = new();

        GameMapManagerWindow.CollectUnregistered(
            catalog,
            new[] { first, second },
            found);

        Assert.That(found, Is.EqualTo(new[] { first, second }));
    }

    private GameMapDefinition Definition(int mapId, string displayName)
    {
        GameMapDefinition definition = Make<GameMapDefinition>();
        definition.ConfigureEditor(mapId, displayName, displayName, $"Assets/{displayName}.unity");
        return definition;
    }

    private T Make<T>() where T : ScriptableObject
    {
        T asset = ScriptableObject.CreateInstance<T>();
        made.Add(asset);
        return asset;
    }
}
