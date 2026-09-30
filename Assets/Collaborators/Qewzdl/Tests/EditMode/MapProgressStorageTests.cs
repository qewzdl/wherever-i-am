using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Where the maps won are kept, and how two copies of them become one. These
// use the editor's own progress file, so it is put back as it was found.
public sealed class MapProgressStorageTests
{
    private static string Backup => MapProgress.FilePath + ".bak";
    private static string Temporary => MapProgress.FilePath + ".tmp";

    private string savedFile;
    private string savedBackup;
    private string savedPrefs;

    [SetUp]
    public void SetAsideProgress()
    {
        savedFile = File.Exists(MapProgress.FilePath) ? File.ReadAllText(MapProgress.FilePath) : null;
        savedBackup = File.Exists(Backup) ? File.ReadAllText(Backup) : null;
        savedPrefs = PlayerPrefs.HasKey(MapProgress.LegacyPrefsKey)
            ? PlayerPrefs.GetString(MapProgress.LegacyPrefsKey)
            : null;

        File.Delete(MapProgress.FilePath);
        File.Delete(Backup);
        PlayerPrefs.DeleteKey(MapProgress.LegacyPrefsKey);
    }

    [TearDown]
    public void PutProgressBack()
    {
        Restore(MapProgress.FilePath, savedFile);
        Restore(Backup, savedBackup);
        File.Delete(MapProgress.FilePath + ".unreadable");

        if (Directory.Exists(Temporary))
            Directory.Delete(Temporary);

        File.Delete(Temporary);

        if (savedPrefs != null)
            PlayerPrefs.SetString(MapProgress.LegacyPrefsKey, savedPrefs);
        else
            PlayerPrefs.DeleteKey(MapProgress.LegacyPrefsKey);

        PlayerPrefs.Save();
    }

    [Test]
    public void TheFile_ReadsBackWhatWasWritten()
    {
        Assert.That(MapProgress.TryParse(MapProgress.Serialize(new[] { 30, 10 }), out var won), Is.True);
        Assert.That(won, Is.EquivalentTo(new[] { 10, 30 }));

        Assert.That(MapProgress.TryParse("not progress", out _), Is.False);
        Assert.That(MapProgress.TryParse("", out _), Is.False);
    }

    // Two machines, each with a win the other lacks: after merging, both
    // wins are here, and merging what is already here changes nothing.
    [Test]
    public void Merging_KeepsTheWinsOfBothCopies()
    {
        MapProgress.RecordWin(10);

        Assert.That(MapProgress.Merge(new[] { 20 }), Is.True);
        Assert.That(MapProgress.Won, Is.EquivalentTo(new[] { 10, 20 }));
        Assert.That(MapProgress.Merge(new[] { 10 }), Is.False);
        Assert.That(MapProgress.Won, Is.EquivalentTo(new[] { 10, 20 }));
    }

    // Progress kept before there was a file is carried into it, once.
    [Test]
    public void ProgressFromBeforeTheFile_IsCarriedIntoIt()
    {
        PlayerPrefs.SetString(MapProgress.LegacyPrefsKey, "10,20");

        Assert.That(MapProgress.Won, Is.EquivalentTo(new[] { 10, 20 }));
        Assert.That(File.Exists(MapProgress.FilePath), Is.True);
        Assert.That(PlayerPrefs.HasKey(MapProgress.LegacyPrefsKey), Is.False);
    }

    // An unreadable file is put aside, not written over by the next win.
    [Test]
    public void AnUnreadableFile_IsPutAsideNotOverwritten()
    {
        File.WriteAllText(MapProgress.FilePath, "{ broken");

        LogAssert.Expect(LogType.Error, new Regex("could not be read"));
        Assert.That(MapProgress.Won, Is.Empty);
        Assert.That(File.ReadAllText(MapProgress.FilePath + ".unreadable"), Is.EqualTo("{ broken"));
    }

    // The copy kept from before the last save stands in for a file gone bad.
    [Test]
    public void AFileGoneBad_IsReadBackFromTheCopyBeforeIt()
    {
        MapProgress.RecordWin(10);
        MapProgress.RecordWin(20);
        File.WriteAllText(MapProgress.FilePath, "{ broken");

        LogAssert.Expect(LogType.Error, new Regex("could not be read"));
        Assert.That(MapProgress.Won, Is.EquivalentTo(new[] { 10 }));
    }

    // Saved at the end of a match, which has to end whatever the disk says.
    [Test]
    public void ASaveThatFails_LetsTheGameGoOn()
    {
        Directory.CreateDirectory(Temporary);

        LogAssert.Expect(LogType.Error, new Regex("could not be saved"));
        Assert.DoesNotThrow(() => MapProgress.RecordWin(10));
    }

    // Held open by somebody else - Steam syncing it, a scanner - the file
    // reads as nothing. A win saved then would be all that was left.
    [Test]
    public void AFileThatCannotBeOpened_IsNotSavedOver()
    {
        MapProgress.RecordWin(10);

        using (new FileStream(MapProgress.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            LogAssert.Expect(LogType.Error, new Regex("not saved over"));
            MapProgress.RecordWin(20);
        }

        Assert.That(MapProgress.Won, Is.EquivalentTo(new[] { 10 }));
    }

    private static void Restore(string path, string contents)
    {
        if (contents != null)
            File.WriteAllText(path, contents);
        else
            File.Delete(path);
    }
}
