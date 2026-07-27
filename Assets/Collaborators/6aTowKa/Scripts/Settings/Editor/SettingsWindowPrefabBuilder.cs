#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Одноразовый editor-builder. В игру попадает только готовый prefab и его ссылки.</summary>
public static class SettingsWindowPrefabBuilder
{
    private const string PrefabPath = "Assets/Collaborators/6aTowKa/Prefabs/SettingsWindow.prefab";

    public static void Build()
    {
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Collaborators/6aTowKa/Prefabs"));
        Button styleSource = FindStyleSource();
        GameObject root = new GameObject("Settings Window", typeof(RectTransform), typeof(SettingsWindow));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);

        RectTransform overlayRect = Rect("Overlay", root.transform);
        Stretch(overlayRect);
        Image dimmer = overlayRect.gameObject.AddComponent<Image>();
        dimmer.color = new Color(0f, 0f, 0f, 0.72f);

        RectTransform window = Rect("Window", overlayRect);
        window.anchorMin = new Vector2(0.5f, 0.5f);
        window.anchorMax = new Vector2(0.5f, 0.5f);
        window.sizeDelta = new Vector2(900f, 650f);
        Image background = window.gameObject.AddComponent<Image>();
        background.color = new Color(0.09f, 0.12f, 0.16f, 0.98f);
        VerticalLayoutGroup windowLayout = window.gameObject.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(26, 26, 20, 20);
        windowLayout.spacing = 12;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;
        windowLayout.childForceExpandHeight = false;

        CreateText("Настройки", window, 32f, TextAlignmentOptions.Center);
        HorizontalLayoutGroup tabRow = Row(window, 38f);
        List<Button> tabButtons = new();
        AddTab(tabRow.transform, tabButtons, "Графика", "Graphics", styleSource);
        AddTab(tabRow.transform, tabButtons, "Звук", "Sound", styleSource);
        AddTab(tabRow.transform, tabButtons, "Управление", "Controls", styleSource);
        AddTab(tabRow.transform, tabButtons, "Интерфейс", "Interface", styleSource);

        RectTransform pagesRoot = Rect("Pages", window);
        pagesRoot.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        List<GameObject> pages = new();
        List<string> ids = new();

        GameObject graphics = Page(pagesRoot, pages, ids, "Graphics");
        Section(graphics.transform, "Экран");
        ChoiceRow(graphics.transform, "Разрешение", styleSource, out TMP_Text resolution, out Button previousResolution, out Button nextResolution);
        ActionRow(graphics.transform, "Режим окна", styleSource, out TMP_Text displayMode, out Button displayModeButton);
        ChoiceRow(graphics.transform, "Качество", styleSource, out TMP_Text quality, out Button previousQuality, out Button nextQuality);
        ActionRow(graphics.transform, "VSync", styleSource, out TMP_Text vsync, out Button vsyncButton);
        Slider frameRateSlider = SliderRow(graphics.transform, "Лимит кадров", 30f, 1001f, out TMP_Text frameRate);
        frameRateSlider.wholeNumbers = true;
        Section(graphics.transform, "Кино");
        Slider fov = SliderRow(graphics.transform, "FOV", 50f, 110f);
        ActionRow(graphics.transform, "Сглаживание камеры", styleSource, out TMP_Text smoothing, out Button smoothingButton);
        Slider smoothingIntensity = SliderRow(graphics.transform, "Интенсивность сглаживания", 0f, 1f);

        GameObject sound = Page(pagesRoot, pages, ids, "Sound");
        Section(sound.transform, "Звук применяется сразу");
        Slider master = SliderRow(sound.transform, "Общая громкость", 0f, 1f);
        Slider music = SliderRow(sound.transform, "Музыка", 0f, 1f);
        Slider effects = SliderRow(sound.transform, "Эффекты", 0f, 1f);
        Slider interfaceVolume = SliderRow(sound.transform, "Громкость интерфейса", 0f, 1f);

        GameObject controls = Page(pagesRoot, pages, ids, "Controls");
        Section(controls.transform, "Управление");
        Slider sensitivity = SliderRow(controls.transform, "Чувствительность", 10f, 300f);
        ActionRow(controls.transform, "Инверсия Y", styleSource, out TMP_Text invert, out Button invertButton);

        GameObject ui = Page(pagesRoot, pages, ids, "Interface");
        Section(ui.transform, "Интерфейс");
        Slider interfaceOpacity = SliderRow(ui.transform, "Прозрачность интерфейса", 0f, 1f);
        Slider crosshairSize = SliderRow(ui.transform, "Размер прицела", 0.5f, 2f);

        HorizontalLayoutGroup bottom = Row(window, 42f);
        Button defaults = Button("По умолчанию", bottom.transform, styleSource);
        Button close = Button("Закрыть", bottom.transform, styleSource);
        Button apply = Button("Применить", bottom.transform, styleSource);

        GameObject confirmation = CreateConfirmation(overlayRect, styleSource, out TMP_Text confirmationText, out Button confirm, out Button cancel);
        confirmation.SetActive(false);
        overlayRect.gameObject.SetActive(false);

        SettingsWindow settingsWindow = root.GetComponent<SettingsWindow>();
        settingsWindow.EditorAssignReferences(
            overlayRect.gameObject, confirmation, confirmationText, close, apply, defaults, confirm, cancel,
            tabButtons.ToArray(), pages.ToArray(), ids.ToArray(),
            previousResolution, nextResolution, resolution, displayModeButton, displayMode,
            previousQuality, nextQuality, quality, vsyncButton, vsync, frameRateSlider, frameRate,
            fov, smoothingButton, smoothing, smoothingIntensity, master, music, effects,
            sensitivity, invertButton, invert, interfaceVolume, interfaceOpacity, crosshairSize);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Created {PrefabPath}");
    }

    private static Button FindStyleSource()
    {
        GameObject source = GameObject.Find("Main Canvas/Settings Button");
        return source != null ? source.GetComponent<Button>() : null;
    }

    private static GameObject Page(RectTransform parent, List<GameObject> pages, List<string> ids, string id)
    {
        RectTransform page = Rect("Page " + id, parent);
        VerticalLayoutGroup layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        pages.Add(page.gameObject);
        ids.Add(id);
        return page.gameObject;
    }

    private static void AddTab(Transform parent, List<Button> buttons, string title, string id, Button styleSource)
    {
        Button button = Button(title, parent, styleSource);
        button.name = id;
        buttons.Add(button);
    }

    private static void Section(Transform parent, string title)
    {
        TMP_Text label = CreateText(title, parent, 22f, TextAlignmentOptions.Left);
        label.fontStyle = FontStyles.Bold;
    }

    private static void ChoiceRow(Transform parent, string title, Button styleSource, out TMP_Text value, out Button previous, out Button next)
    {
        HorizontalLayoutGroup row = Row(parent, 34f);
        TMP_Text label = CreateText(title, row.transform, 18f, TextAlignmentOptions.Left);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        previous = Button("‹", row.transform, styleSource);
        value = CreateText("", row.transform, 18f, TextAlignmentOptions.Center);
        value.gameObject.AddComponent<LayoutElement>().preferredWidth = 210f;
        next = Button("›", row.transform, styleSource);
    }

    private static void ActionRow(Transform parent, string title, Button styleSource, out TMP_Text value, out Button button)
    {
        HorizontalLayoutGroup row = Row(parent, 34f);
        TMP_Text label = CreateText(title, row.transform, 18f, TextAlignmentOptions.Left);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        button = Button("Изменить", row.transform, styleSource);
        value = button.GetComponentInChildren<TMP_Text>();
        value.fontSize = 16f;
    }

    private static Slider SliderRow(Transform parent, string title, float min, float max)
    {
        return SliderRow(parent, title, min, max, out _);
    }

    private static Slider SliderRow(Transform parent, string title, float min, float max, out TMP_Text label)
    {
        HorizontalLayoutGroup row = Row(parent, 38f);
        label = CreateText(title, row.transform, 18f, TextAlignmentOptions.Left);
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 280f;
        GameObject sliderObject = new GameObject(title + " Slider", typeof(RectTransform), typeof(Image), typeof(Slider), typeof(LayoutElement));
        sliderObject.transform.SetParent(row.transform, false);
        sliderObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
        Image background = sliderObject.GetComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.18f);
        Slider slider = sliderObject.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        RectTransform fillArea = Rect("Fill Area", sliderObject.transform);
        Stretch(fillArea);
        RectTransform fill = Rect("Fill", fillArea);
        Stretch(fill);
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = new Color(0.27f, 0.65f, 0.95f, 1f);
        slider.fillRect = fill;
        RectTransform handle = Rect("Handle", sliderObject.transform);
        handle.anchorMin = new Vector2(0f, 0.5f);
        handle.anchorMax = new Vector2(0f, 0.5f);
        handle.sizeDelta = new Vector2(18f, 28f);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = Color.white;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        return slider;
    }

    private static GameObject CreateConfirmation(RectTransform parent, Button styleSource, out TMP_Text text, out Button confirm, out Button cancel)
    {
        RectTransform dialog = Rect("Confirmation", parent);
        dialog.anchorMin = new Vector2(0.5f, 0.5f);
        dialog.anchorMax = new Vector2(0.5f, 0.5f);
        dialog.sizeDelta = new Vector2(470f, 180f);
        Image image = dialog.gameObject.AddComponent<Image>();
        image.color = new Color(0.13f, 0.17f, 0.23f, 1f);
        VerticalLayoutGroup layout = dialog.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 22, 22);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        text = CreateText("", dialog, 22f, TextAlignmentOptions.Center);
        text.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        HorizontalLayoutGroup buttons = Row(dialog, 36f);
        cancel = Button("Отмена", buttons.transform, styleSource);
        confirm = Button("Подтвердить", buttons.transform, styleSource);
        return dialog.gameObject;
    }

    private static HorizontalLayoutGroup Row(Transform parent, float height)
    {
        GameObject row = new GameObject("Row", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = height;
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        return layout;
    }

    private static Button Button(string title, Transform parent, Button styleSource)
    {
        GameObject buttonObject = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(parent, false);
        buttonObject.GetComponent<LayoutElement>().preferredWidth = 150f;
        Image image = buttonObject.GetComponent<Image>();
        Button button = buttonObject.GetComponent<Button>();
        if (styleSource != null)
        {
            image.sprite = styleSource.image != null ? styleSource.image.sprite : null;
            image.type = styleSource.image != null ? styleSource.image.type : Image.Type.Sliced;
            button.colors = styleSource.colors;
            button.transition = styleSource.transition;
        }
        else
        {
            image.color = new Color(0.20f, 0.37f, 0.55f, 1f);
        }
        TMP_Text label = CreateText(title, buttonObject.transform, 18f, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        return button;
    }

    private static TMP_Text CreateText(string value, Transform parent, float size, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.text = value;
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        GameObject objectRoot = new GameObject(name, typeof(RectTransform));
        objectRoot.transform.SetParent(parent, false);
        return objectRoot.GetComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
#endif
