using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Everything about the player in one place, and the arithmetic nobody can do
// while looking at it.
//
// The fields were spread over a config asset, a dozen components on a prefab
// with twenty-two of them, and three preset assets in a folder of their own.
// Gathering them is the small half of the problem. The large half is that the
// numbers are not independent and the inspector cannot say so: how loud a
// footstep is depends on how fast walking is, whether a spent sprint is still
// heard as a sprint depends on three fields across two assets, and nothing
// anywhere shows the answer. The readout at the bottom does.
//
// Nothing is owned here. Every field drawn is the real field on the real
// asset, so there is nothing that can drift out of step with the inspector -
// this is the inspector, gathered and then asked questions.
public sealed class PlayerSetupWindow : EditorWindow
{
    private const string PrefabPath =
        "Assets/Collaborators/6aTowKa/Prefabs/Player.prefab";

    private const string ProfilePath =
        "Assets/Collaborators/6aTowKa/Configs/PlayerMovementProfile.asset";

    private const string FoldoutPrefix = "WhereverIAm.PlayerSetup.";

    // Ordered the way somebody tuning a player thinks: how it moves, what that
    // sounds like, then what it looks like through its own eyes. The rest of
    // the prefab is wiring - networking, scope lifetimes, the orchestrator -
    // and putting that here would rebuild the thing this window exists to
    // avoid.
    private static readonly (string Title, Type Type)[] SectionTypes =
    {
        ("Movement", typeof(PlayerController)),
        ("Posture", typeof(PlayerPostureController)),
        ("Footsteps", typeof(FootstepEmitter)),
        ("Breathing", typeof(PlayerBreathingSounds)),
        ("Noise Emitter", typeof(GameplayNoiseEmitter)),
        ("Hiding", typeof(PlayerHidingController)),
        ("Hiding Vignette", typeof(PlayerHidingVignette)),
        ("Interaction", typeof(PlayerInteraction)),
        ("Camera Look", typeof(CameraLook)),
        ("Camera Effects", typeof(PlayerCameraEffects)),
        ("Viewmodel Sway", typeof(ViewmodelSway)),
    };

    private Vector2 scroll;
    private GameObject prefab;
    private PlayerMovementProfile profile;
    private SerializedObject profileSerialized;

    private readonly List<(string Title, SerializedObject Serialized)> sections = new();

    [MenuItem("Tools/Wherever I Am/Player Setup", false, 101)]
    private static void Open()
    {
        GetWindow<PlayerSetupWindow>("Player Setup").Show();
    }

    private void OnEnable()
    {
        Reload();
    }

    // Somebody may have changed the prefab in the inspector while this sat
    // behind it. Rebuilding on focus is cheaper than being wrong.
    private void OnFocus()
    {
        Reload();
    }

    // Rebuilt rather than refreshed: a prefab can be reimported under a window
    // that is merely hidden, and a SerializedObject over a destroyed component
    // throws rather than going quiet.
    private void Reload()
    {
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        profile = AssetDatabase.LoadAssetAtPath<PlayerMovementProfile>(ProfilePath);
        profileSerialized = profile != null ? new SerializedObject(profile) : null;

        sections.Clear();

        if (prefab == null)
            return;

        foreach ((string title, Type type) in SectionTypes)
        {
            Component component = prefab.GetComponentInChildren(type, true);

            if (component != null)
                sections.Add((title, new SerializedObject(component)));
        }
    }

    private void OnGUI()
    {
        DrawToolbar();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        if (prefab == null)
        {
            EditorGUILayout.HelpBox(
                "No player prefab at " + PrefabPath + ".",
                MessageType.Error);
        }

        DrawSection(
            "Movement And Stamina",
            ProfilePath,
            profileSerialized,
            "No movement profile at " + ProfilePath + ". Create one through " +
            "Wherever I Am / Player / Player Movement Profile.");

        foreach ((string title, SerializedObject serialized) in sections)
            DrawSection(title, PrefabPath, serialized, string.Empty);

        DrawReadout();
        DrawUnwired();

        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                Reload();

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(profile == null))
            {
                if (GUILayout.Button("Profile", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                    Selection.activeObject = profile;
            }

            using (new EditorGUI.DisabledScope(prefab == null))
            {
                if (GUILayout.Button("Prefab", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                    Selection.activeObject = prefab;
            }
        }
    }

    private void DrawSection(
        string title,
        string assetPath,
        SerializedObject serialized,
        string missingMessage)
    {
        if (serialized == null || serialized.targetObject == null)
        {
            if (!string.IsNullOrEmpty(missingMessage))
                EditorGUILayout.HelpBox(missingMessage, MessageType.Warning);

            return;
        }

        if (!Foldout(title, title))
            return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        using (new EditorGUI.IndentLevelScope())
        {
            serialized.Update();

            SerializedProperty property = serialized.GetIterator();
            bool enterChildren = true;

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                // The script slot is the one field on every component nobody
                // tuning a player has ever wanted to change.
                if (property.propertyPath == "m_Script")
                    continue;

                EditorGUILayout.PropertyField(property, true);
            }

            if (serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(serialized.targetObject);

                AssetDatabase.SaveAssetIfDirty(
                    AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath));
            }
        }
    }

    private static bool Foldout(string key, string label)
    {
        string prefKey = FoldoutPrefix + key;
        bool open = EditorPrefs.GetBool(prefKey, true);
        bool next = EditorGUILayout.Foldout(open, label, true, EditorStyles.foldoutHeader);

        if (next != open)
            EditorPrefs.SetBool(prefKey, next);

        return next;
    }

    // What the numbers add up to. Nothing here is stored - it is the same
    // arithmetic the game does at runtime, done where a value can still be
    // argued with.
    private void DrawReadout()
    {
        if (profile == null)
            return;

        if (!Foldout("Readout", "What This Adds Up To"))
            return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            float tiredScale = profile.OverallScaleAtStamina(0f);
            float walk = profile.WalkSpeed;
            float run = walk * profile.RunScaleAtStamina(1f);
            float spentRun = walk * profile.RunScaleAtStamina(0f) * tiredScale;
            float tiredWalk = walk * tiredScale;
            float crouch = profile.CrouchSpeed;

            EditorGUILayout.LabelField("Speeds", EditorStyles.miniBoldLabel);
            Speed("Running, fresh", run);
            Speed("Running, empty", spentRun);
            Speed("Walking", walk);
            Speed("Walking, tired", tiredWalk);
            Speed("Crouching", crouch);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bands", EditorStyles.miniBoldLabel);
            Row("Heard as running from", profile.HeardAsRunningFrom.ToString("0.00") + " m/s");
            Row("Heard as walking from", profile.HeardAsWalkingFrom.ToString("0.00") + " m/s");

            if (spentRun < profile.HeardAsRunningFrom)
            {
                EditorGUILayout.HelpBox(
                    "A spent sprint falls under the running band, so the last " +
                    "seconds of a chase are heard as walking. Raise " +
                    "exhaustedRunScale or exhaustedOverallScale if that is not " +
                    "the reward you meant to hand out.",
                    MessageType.Info);
            }

            if (crouch >= profile.HeardAsWalkingFrom)
            {
                EditorGUILayout.HelpBox(
                    "Crouching is not under the walking band, so the quiet " +
                    "gait is not quiet. Lower crouchSpeedMultiplier.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Stamina", EditorStyles.miniBoldLabel);
            Row("Empties in", profile.RunSeconds.ToString("0.0") + " s of running");
            Row("Fills, standing", Recovery(false, false));
            Row("Fills, crouching still", Recovery(true, false));
            Row("Fills, walking", Recovery(false, true));
            Row("Fills, crouch walking", Recovery(true, true));
            Row("Breathing starts under", (profile.TiredBelow * 100f).ToString("0") + "%");

            if (profile.RecoveryCurveStrandsAtEmpty())
            {
                EditorGUILayout.HelpBox(
                    "The recovery curve is at or near zero on the left, which " +
                    "is the empty end. Taken literally that means an empty " +
                    "tank never refills - so a floor of " +
                    PlayerMovementProfile.MinimumRecoveryRate.ToString("0.00") +
                    " is being applied and the curve you drew is not the " +
                    "curve being used down there. Lift the left-hand key if " +
                    "you want the shape back.",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "Fill times ignore the recovery curve, which can only be read " +
                "against a tank that is actually filling.",
                MessageType.None);
        }
    }

    private string Recovery(bool crouching, bool moving)
    {
        float rate = profile.RecoveryRateFor(crouching, moving);

        if (rate <= 0f)
            return "never";

        return (profile.RecoverySeconds / rate).ToString("0.0") + " s";
    }

    private void Speed(string label, float speed)
    {
        Row(label, speed.ToString("0.00") + " m/s   " + Heard(speed));
    }

    private string Heard(float speed)
    {
        if (speed >= profile.HeardAsRunningFrom)
            return "heard running";

        if (speed >= profile.HeardAsWalkingFrom)
            return "heard walking";

        return "silent";
    }

    private static void Row(string label, string value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(170f));
            EditorGUILayout.LabelField(value);
        }
    }

    // What is wired wrong rather than tuned wrong. An empty slot is silence,
    // and silence looks exactly like a value somebody meant to leave at
    // nothing - which is why it is worth saying out loud.
    private void DrawUnwired()
    {
        EditorGUILayout.LabelField("Unwired", EditorStyles.boldLabel);

        bool wired = true;

        wired &= Warn("Movement", "movement", "the controller has no speeds to read.");
        wired &= Warn("Footsteps", "movement", "no profile, so no gait can be judged.");
        wired &= Warn("Footsteps", "noiseEmitter", "nothing to emit noise through.");
        wired &= Warn("Footsteps", "runningPreset", "running makes no noise the enemy can hear.");
        wired &= Warn("Footsteps", "walkingPreset", "walking makes no noise the enemy can hear.");
        wired &= Warn("Footsteps", "runningSound", "running is silent to people.");
        wired &= Warn("Footsteps", "walkingSound", "walking is silent to people.");
        wired &= Warn("Breathing", "inhale", "there is no breath in, only a breath out.");
        wired &= Warn("Breathing", "exhale", "being out of breath cannot be heard.");
        wired &= Warn("Breathing", "cough", "nobody ever coughs. This one may be on purpose.");

        if (wired)
            EditorGUILayout.HelpBox("Everything is wired.", MessageType.Info);
    }

    private bool Warn(string sectionTitle, string propertyName, string consequence)
    {
        SerializedObject serialized = FindSection(sectionTitle);

        if (serialized == null || serialized.targetObject == null)
            return true;

        SerializedProperty property = serialized.FindProperty(propertyName);

        // A name that matches nothing is a rename this window did not follow,
        // and staying quiet about it would be the one failure it cannot afford
        // - a check that silently stops checking.
        if (property == null)
        {
            EditorGUILayout.HelpBox(
                sectionTitle + " has no field called " + propertyName +
                ". This window is out of date with the component.",
                MessageType.Error);

            return false;
        }

        if (property.propertyType != SerializedPropertyType.ObjectReference ||
            property.objectReferenceValue != null)
        {
            return true;
        }

        EditorGUILayout.HelpBox(
            sectionTitle + " / " + propertyName + ": " + consequence,
            MessageType.Warning);

        return false;
    }

    private SerializedObject FindSection(string title)
    {
        foreach ((string sectionTitle, SerializedObject serialized) in sections)
        {
            if (sectionTitle == title)
                return serialized;
        }

        return null;
    }
}
