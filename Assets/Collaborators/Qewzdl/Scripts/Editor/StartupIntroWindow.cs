using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

// The film's own page, so that turning it down is not a reason to open the
// scene the whole game is wired through.
//
// It edits the asset and nothing else - the same arrangement the map manager
// has with its catalog - which is what lets it work with no scene open at all.
public sealed class StartupIntroWindow : EditorWindow
{
    private const string DefaultConfigPath =
        "Assets/Collaborators/Qewzdl/Configs/Intro/StartupIntroConfig.asset";

    [SerializeField] private StartupIntroConfig config;

    private SerializedObject serialized;

    [MenuItem("Tools/Wherever I Am/Startup Intro", false, 102)]
    private static void OpenWindow()
    {
        StartupIntroWindow window = GetWindow<StartupIntroWindow>("Startup Intro");
        window.minSize = new Vector2(420f, 320f);
        window.Show();
    }

    private void OnEnable()
    {
        if (config == null)
            config = AssetDatabase.LoadAssetAtPath<StartupIntroConfig>(DefaultConfigPath);

        Rebind();
    }

    private void OnFocus()
    {
        Rebind();
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Startup Intro", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "The film that plays before the main menu.",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space();

        using (EditorGUI.ChangeCheckScope changed = new())
        {
            config = (StartupIntroConfig)EditorGUILayout.ObjectField(
                "Config",
                config,
                typeof(StartupIntroConfig),
                false);

            if (changed.changed)
                Rebind();
        }

        if (config == null)
        {
            EditorGUILayout.HelpBox(
                $"Assign a {nameof(StartupIntroConfig)}. The default was not " +
                $"found at '{DefaultConfigPath}'.",
                MessageType.Error);

            return;
        }

        EditorGUILayout.Space();
        DrawFields();
        DrawState();
    }

    private void DrawFields()
    {
        if (serialized == null)
            return;

        serialized.Update();

        DrawProperty("clip", "Clip");
        DrawProperty("volume", "Volume");

        EditorGUILayout.Space();
        DrawProperty("playInEditor", "Play in editor");

        EditorGUILayout.Space();
        DrawProperty("prepareTimeoutSeconds", "Give up opening after");
        DrawProperty("playbackSlackSeconds", "Give up playing after (+ length)");
        DrawProperty("fadeSeconds", "Fade out over");

        serialized.ApplyModifiedProperties();
    }

    private void DrawProperty(string name, string label)
    {
        SerializedProperty property = serialized.FindProperty(name);

        if (property != null)
            EditorGUILayout.PropertyField(property, new GUIContent(label));
    }

    // What the settings above add up to, said in the terms somebody tuning
    // them is thinking in: how long the game will sit on this screen, and
    // whether there is anything to sit through at all.
    private void DrawState()
    {
        EditorGUILayout.Space();

        if (!config.HasPicture)
        {
            EditorGUILayout.HelpBox(
                "No clip, or a clip with no picture in it. The game will " +
                "start straight into the menu.",
                MessageType.Warning);

            return;
        }

        VideoClip clip = config.Clip;

        EditorGUILayout.HelpBox(
            $"{clip.width}x{clip.height}, {clip.length:0.0}s. " +
            $"The menu appears after it, or after " +
            $"{clip.length + config.PlaybackSlackSeconds:0.0}s at the latest. " +
            "The intro cannot be skipped.",
            MessageType.Info);
    }

    private void Rebind()
    {
        serialized = config != null ? new SerializedObject(config) : null;
    }
}
