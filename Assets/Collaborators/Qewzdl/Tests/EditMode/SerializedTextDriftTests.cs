using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

// Every serialised string in a scene or a prefab is a copy of what its class
// said the day the asset was last saved, and a copy wins over the class. So a
// sentence that is rewritten in code goes on being shown in its old wording,
// silently, until somebody opens the game and reads it.
//
// The lobby had three of those at once. Its door said "Lobby closed" and then,
// underneath, "Shut. Nobody can reach this lobby until you open it." - the word
// Shut had moved into the status line months earlier and the hint kept it too.
//
// The rule is narrower than "the asset must agree with the class", because
// that is not true: an asset exists to be configured. It rests on which of the
// two declared the text in the first place.
//
// A field the class gives a sentence to is the class's own words - a screen's
// copy, written once and read everywhere. A field the class leaves undeclared
// is per-instance data: the text of a label, the id of a room, the name of an
// action map. The class has no opinion about those and cannot be drifted from.
// This looks only at the first kind.
//
// An asset may still blank one, because blank is how this project turns a piece
// of text off - the enemy prefab does exactly that with an animator parameter it
// does not drive. What it may not do is hold a different sentence, which is
// never a configuration; it is a copy nobody updated.
public sealed class SerializedTextDriftTests
{
    private const string ScriptLinePrefix = "  m_Script:";

    [Test]
    public void NoAssetHoldsATextItsClassHasRewritten()
    {
        Dictionary<string, Type> typesByGuid = FindSerialisableTypes();
        Dictionary<Type, Dictionary<string, string>> defaults = new();
        List<string> drifted = new();

        foreach (string path in FindAssets())
        {
            string[] lines = File.ReadAllLines(path);
            Dictionary<string, string> fields = null;
            string typeName = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (line.StartsWith(ScriptLinePrefix, StringComparison.Ordinal))
                {
                    fields = null;
                    typeName = null;

                    if (TryReadGuid(line, out string guid) &&
                        typesByGuid.TryGetValue(guid, out Type type))
                    {
                        fields = DefaultsFor(type, defaults);
                        typeName = type.Name;
                    }

                    continue;
                }

                if (fields == null || !TryReadField(line, out string key, out string stored))
                    continue;

                if (!fields.TryGetValue(key, out string expected))
                    continue;

                // The class said nothing, so there is nothing to drift from:
                // this field is whatever each instance makes of it.
                if (string.IsNullOrEmpty(expected))
                    continue;

                // Blank is how a field is switched off, and switching one off is
                // a decision the asset is entitled to make.
                if (string.IsNullOrEmpty(stored) || stored == expected)
                    continue;

                drifted.Add(
                    $"{path}\n    {typeName}.{key}\n" +
                    $"      asset: \"{stored}\"\n" +
                    $"      class: \"{expected}\"");
            }
        }

        Assert.That(
            drifted,
            Is.Empty,
            BuildFailureMessage(drifted));
    }

    private static string BuildFailureMessage(List<string> drifted)
    {
        if (drifted.Count == 0)
            return string.Empty;

        StringBuilder message = new StringBuilder();

        message.AppendLine(
            $"{drifted.Count} serialised text(s) no longer match the class that declares them.");
        message.AppendLine(
            "Open the asset, set the field to what the class says, and save it - " +
            "or, if the asset is meant to say something else, say it in the class.");
        message.AppendLine();

        for (int i = 0; i < drifted.Count; i++)
            message.AppendLine(drifted[i]);

        return message.ToString();
    }

    // Read off real instances rather than out of the source, so the answer is
    // whatever the field initialiser actually produces.
    //
    // On a switched-off object, because a component added to a live one wakes
    // up: half the behaviours in this game check their configuration on the way
    // in and say so when it is missing, which it always is on a bare probe. An
    // inactive host runs the field initialisers and nothing else.
    //
    // What still gets through is OnValidate, which the editor calls whatever
    // the object is doing. Those complaints are not this test's business - it
    // is poking components in a way nobody designed them for - so they are
    // ignored while it pokes, and only while.
    private static Dictionary<string, string> DefaultsFor(
        Type type,
        Dictionary<Type, Dictionary<string, string>> cache)
    {
        if (cache.TryGetValue(type, out Dictionary<string, string> known))
            return known;

        Dictionary<string, string> values = new();
        GameObject host = new GameObject("SerializedTextDriftProbe")
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        host.SetActive(false);

        bool ignoring = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;

        try
        {
            Component probe = host.AddComponent(type);

            if (probe != null)
            {
                foreach (FieldInfo field in SerialisedStringFields(type))
                    values[field.Name] = field.GetValue(probe) as string ?? string.Empty;
            }
        }
        catch (Exception)
        {
            // A component that refuses to be added to a bare object tells us
            // nothing about its texts, and is not what this test is about.
        }
        finally
        {
            LogAssert.ignoreFailingMessages = ignoring;
            UnityEngine.Object.DestroyImmediate(host);
        }

        cache[type] = values;
        return values;
    }

    private static IEnumerable<FieldInfo> SerialisedStringFields(Type type)
    {
        const BindingFlags Flags = BindingFlags.Instance |
                                   BindingFlags.Public |
                                   BindingFlags.NonPublic |
                                   BindingFlags.DeclaredOnly;

        for (Type current = type; current != null && current != typeof(MonoBehaviour); current = current.BaseType)
        {
            foreach (FieldInfo field in current.GetFields(Flags))
            {
                if (field.FieldType != typeof(string))
                    continue;

                if (field.IsDefined(typeof(NonSerializedAttribute), false))
                    continue;

                if (field.IsPublic || field.IsDefined(typeof(SerializeField), false))
                    yield return field;
            }
        }
    }

    private static Dictionary<string, Type> FindSerialisableTypes()
    {
        Dictionary<string, Type> typesByGuid = new();

        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Type type = script != null ? script.GetClass() : null;

            if (type == null || type.IsAbstract || type.IsGenericTypeDefinition)
                continue;

            if (!typeof(MonoBehaviour).IsAssignableFrom(type))
                continue;

            typesByGuid[guid] = type;
        }

        return typesByGuid;
    }

    private static IEnumerable<string> FindAssets()
    {
        foreach (string path in Directory.EnumerateFiles("Assets", "*.*", SearchOption.AllDirectories))
        {
            if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    private static bool TryReadGuid(string line, out string guid)
    {
        guid = null;
        int start = line.IndexOf("guid: ", StringComparison.Ordinal);

        if (start < 0)
            return false;

        start += "guid: ".Length;

        if (start + 32 > line.Length)
            return false;

        guid = line.Substring(start, 32);
        return true;
    }

    // A field of the component being read, which is written at exactly two
    // spaces. Anything deeper belongs to a structure inside it and is somebody
    // else's business.
    private static bool TryReadField(string line, out string key, out string value)
    {
        key = null;
        value = null;

        if (line.Length < 4 || line[0] != ' ' || line[1] != ' ' || line[2] == ' ' || line[2] == '-')
            return false;

        int colon = line.IndexOf(':');

        if (colon < 0)
            return false;

        key = line.Substring(2, colon - 2);
        value = line.Substring(colon + 1).Trim();

        // Unity quotes a value that would not survive being read back plainly,
        // and doubles any quote inside it.
        if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
            value = value.Substring(1, value.Length - 2).Replace("''", "'");

        return true;
    }
}
