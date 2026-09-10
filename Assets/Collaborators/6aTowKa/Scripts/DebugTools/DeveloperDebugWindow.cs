#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Сценовое F4-окно для Editor/Development Build без вкладок.</summary>
[DisallowMultipleComponent]
public sealed class DeveloperDebugWindow : MonoBehaviour, ISettingsServiceConsumer
{
    private const float MetricsInterval = 0.2f;
    private const float EnemyConfirmationSeconds = 3f;

    private readonly NoClipController noClip = new();
    private readonly Dictionary<string, GameObject> metricSections = new();
    private ISettingsService settings;
    private GameObject panel;
    private TextMeshProUGUI noClipText;
    private TextMeshProUGUI speedText;
    private TextMeshProUGUI removeEnemiesText;
    private TMP_InputField shakeAmountField;
    private TMP_InputField shakeDurationField;
    private TextMeshProUGUI shakeText;
    private bool cursorReleased;
    private bool enemyRemovalArmed;
    private float enemyRemovalArmedUntil;
    private float nextMetricsAt;

    private void Awake()
    {
        Build();
        panel.SetActive(false);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f4Key.wasPressedThisFrame)
            SetVisible(!panel.activeSelf);

        noClip.Update(Time.unscaledDeltaTime);
        UpdateCursorRelease();
        if (!panel.activeSelf)
            return;

        if (enemyRemovalArmed && Time.unscaledTime >= enemyRemovalArmedUntil)
            CancelEnemyRemoval();

        if (Time.unscaledTime >= nextMetricsAt)
        {
            nextMetricsAt = Time.unscaledTime + MetricsInterval;
            RefreshMetrics();
        }
    }

    private void OnDestroy()
    {
        ReleaseSettingsService();
    }

    public void Construct(ISettingsService settingsService)
    {
        settings = settingsService;
    }

    public void ReleaseSettingsService()
    {
        noClip.Restore();
        SetCursorReleased(false);
        settings = null;
    }

    private void SetVisible(bool visible)
    {
        if (visible && settings == null)
            return;

        panel.SetActive(visible);
        if (!visible)
        {
            noClip.Restore();
            SetCursorReleased(false);
            CancelEnemyRemoval();
            return;
        }

        RefreshSections();
        RefreshMetrics();
    }

    // Зажатый Alt при открытом окне отдаёт курсор мыши, чтобы можно было ткнуть в кнопку,
    // не выходя из игры. Отпустил — курсор снова уходит в игру.
    private void UpdateCursorRelease()
    {
        Keyboard keyboard = Keyboard.current;
        bool altHeld = keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
        SetCursorReleased(panel.activeSelf && altHeld);
    }

    private void SetCursorReleased(bool released)
    {
        if (cursorReleased == released)
            return;

        CameraLook look = TryGetLocalPlayerComponent<CameraLook>();
        if (look == null)
            return;

        cursorReleased = released;
        // Через блокировку обзора, а не записью в Cursor напрямую: CameraLook сам снимает
        // лок, когда обзор заблокирован, и заодно перестаёт крутить камеру мышью - иначе
        // прицеливание уезжало бы вслед за тем, как целишься в кнопку.
        look.SetLookActive(this, !released);
    }

    private void ToggleNoClip()
    {
        PlayerController localPlayer = TryGetLocalPlayer();
        if (!noClip.SetEnabled(localPlayer, !noClip.IsEnabled))
        {
            noClipText.text = "NoClip: локальный игрок не найден";
            return;
        }

        noClipText.text = noClip.IsEnabled ? "NoClip: включён" : "NoClip: выключен";
    }

    private void ChangeSpeed(float delta)
    {
        float next = Mathf.Clamp((settings?.Current.debugNoClipSpeed ?? noClip.Speed) + delta, 2f, 30f);
        settings?.SetDebugNoClipSpeed(next);
        noClip.Speed = next;
        speedText.text = $"Скорость NoClip: {next:0.0}";
    }

    private void TriggerShake()
    {
        PlayerCameraEffects effects = TryGetLocalPlayerComponent<PlayerCameraEffects>();
        if (effects == null)
        {
            shakeText.text = "Тряска: игрок не найден";
            return;
        }

        if (!TryReadField(shakeAmountField, out float strength) || !TryReadField(shakeDurationField, out float duration))
        {
            shakeText.text = "Тряска: не число";
            return;
        }

        effects.AddShake(strength, duration);
        shakeText.text = $"Трахнуть камеру ({strength:0.00} / {duration:0.00}с)";
    }

    // Запятая вместо точки — обычный ввод на русской раскладке, а парсим инвариантно,
    // чтобы поле вело себя одинаково независимо от локали машины.
    private static bool TryReadField(TMP_InputField field, out float value)
    {
        string typed = field.text.Replace(',', '.');
        return float.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void HandleRemoveEnemies()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsListening && !network.IsServer)
        {
            removeEnemiesText.text = "Удаление врагов: только host/server";
            return;
        }

        if (!enemyRemovalArmed)
        {
            enemyRemovalArmed = true;
            enemyRemovalArmedUntil = Time.unscaledTime + EnemyConfirmationSeconds;
            removeEnemiesText.text = "Нажми ещё раз: удалить врагов навсегда*";
            return;
        }

        enemyRemovalArmed = false;
        int removed = RemoveEnemiesOnce();
        removeEnemiesText.text = removed > 0
            ? $"Удалено врагов: {removed}*"
            : "Враги не найдены";
    }

    // Единственный намеренный scene-search: кнопка-костыль для dev-удаления врагов.
    private static int RemoveEnemiesOnce()
    {
        NetworkObject[] objects = Object.FindObjectsByType<NetworkObject>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        int removed = 0;
        for (int i = 0; i < objects.Length; i++)
        {
            NetworkObject networkObject = objects[i];
            if (networkObject == null || networkObject.GetComponent("NetworkEnemyController") == null)
                continue;

            if (networkObject.IsSpawned)
                networkObject.Despawn(true);
            else
                Destroy(networkObject.gameObject);
            removed++;
        }
        return removed;
    }

    private void CancelEnemyRemoval()
    {
        enemyRemovalArmed = false;
        if (removeEnemiesText != null)
            removeEnemiesText.text = "Убрать врага навсегда*";
    }

    private void RefreshSections()
    {
        if (settings == null)
            return;
        metricSections["performance"].SetActive(true);
        metricSections["player"].SetActive(true);
        metricSections["network"].SetActive(true);
        metricSections["enemies"].SetActive(true);
        metricSections["scene"].SetActive(true);
        noClip.Speed = settings.Current.debugNoClipSpeed;
        speedText.text = $"Скорость NoClip: {noClip.Speed:0.0}";
    }

    private void RefreshMetrics()
    {
        if (settings == null)
            return;

        SetMetric("performance", $"Производительность\nFPS: {(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)):0}\nПамять: {Profiler.GetTotalAllocatedMemoryLong() / (1024L * 1024L)} МБ");
        PlayerController player = TryGetLocalPlayer();
        SetMetric("player", player == null
            ? "Игрок\nЛокальный игрок не найден"
            : $"Игрок\nПозиция: {player.transform.position:F2}\nNoClip: {(noClip.IsEnabled ? "вкл." : "выкл.")}");

        NetworkManager network = NetworkManager.Singleton;
        string role = network == null || !network.IsListening ? "Offline" : network.IsHost ? "Host" : network.IsServer ? "Server" : "Client";
        SetMetric("network", $"Сеть\nРоль: {role}");
        SetMetric("enemies", "Враги\nУдаление доступно только host/server.");
        SetMetric("scene", $"Сцена\nАктивная: {SceneManager.GetActiveScene().name}\nЗагружено: {SceneManager.loadedSceneCount}");
    }

    private void SetMetric(string id, string value)
    {
        if (metricSections.TryGetValue(id, out GameObject section))
        {
            TextMeshProUGUI text = section.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
                text.text = value;
        }
    }

    // Сначала netcode, потом поиск по сцене — второй нужен, когда сцену запускают из
    // редактора без хоста: SpawnManager тогда пуст, а игрок лежит в сцене обычным объектом.
    private static T TryGetLocalPlayerComponent<T>() where T : Component
    {
        PlayerController localPlayer = TryGetLocalPlayer();
        if (localPlayer != null)
        {
            T owned = localPlayer.GetComponentInChildren<T>(true);
            if (owned != null)
                return owned;
        }

        return FindFirstObjectByType<T>();
    }

    private static PlayerController TryGetLocalPlayer()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || network.SpawnManager == null)
            return null;

        foreach (NetworkObject networkObject in network.SpawnManager.SpawnedObjects.Values)
        {
            if (networkObject != null && networkObject.IsOwner && networkObject.TryGetComponent(out PlayerController player))
                return player;
        }
        return null;
    }

    private void Build()
    {
        RectTransform root = CreateRect("F4 Developer Tools", transform);
        root.anchorMin = new Vector2(0f, 1f);
        root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(20f, -20f);
        root.sizeDelta = new Vector2(460f, 720f);
        panel = root.gameObject;
        Image image = panel.AddComponent<Image>();
        image.color = new Color(0.05f, 0.07f, 0.1f, 0.94f);
        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 16, 16);
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        CreateText("F4 — Developer Tools", panel.transform, 24, FontStyles.Bold);
        CreateText("Быстрые действия", panel.transform, 18, FontStyles.Bold);
        noClipText = CreateButton("NoClip: выключен", panel.transform, ToggleNoClip);
        HorizontalLayoutGroup speed = CreateRow(panel.transform, 32f);
        CreateButton("−", speed.transform, () => ChangeSpeed(-1f));
        speedText = CreateText("Скорость NoClip: 10.0", speed.transform, 16, FontStyles.Normal);
        speedText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton("+", speed.transform, () => ChangeSpeed(1f));
        CreateText("Тряска: сила 0..1 и длительность в секундах", panel.transform, 13, FontStyles.Italic);
        HorizontalLayoutGroup shake = CreateRow(panel.transform, 34f);
        shakeAmountField = CreateInputField("1.0", shake.transform);
        shakeDurationField = CreateInputField("0.4", shake.transform);
        shakeText = CreateButton("Трахнуть камеру", shake.transform, TriggerShake);
        removeEnemiesText = CreateButton("Убрать врага навсегда*", panel.transform, HandleRemoveEnemies);
        CreateText("*Костыль: до перезагрузки сцены или нового спавна.", panel.transform, 13, FontStyles.Italic);

        CreateMetric("performance", panel.transform);
        CreateMetric("player", panel.transform);
        CreateMetric("network", panel.transform);
        CreateMetric("enemies", panel.transform);
        CreateMetric("scene", panel.transform);
    }

    private void CreateMetric(string id, Transform parent)
    {
        GameObject section = new GameObject(id, typeof(RectTransform), typeof(LayoutElement));
        section.transform.SetParent(parent, false);
        section.GetComponent<LayoutElement>().preferredHeight = 72f;
        Image image = section.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.07f);
        TextMeshProUGUI text = CreateText("", section.transform, 14, FontStyles.Normal);
        Stretch(text.rectTransform);
        text.margin = new Vector4(8f, 6f, 8f, 6f);
        metricSections.Add(id, section);
    }

    private static HorizontalLayoutGroup CreateRow(Transform parent, float height)
    {
        GameObject row = new GameObject("Row", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = height;
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 6;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return layout;
    }

    private static TextMeshProUGUI CreateButton(string title, Transform parent, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonRoot = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonRoot.transform.SetParent(parent, false);
        buttonRoot.GetComponent<LayoutElement>().preferredWidth = 120f;
        buttonRoot.GetComponent<LayoutElement>().preferredHeight = 34f;
        Image image = buttonRoot.GetComponent<Image>();
        image.color = new Color(0.20f, 0.37f, 0.55f, 1f);
        Button button = buttonRoot.GetComponent<Button>();
        button.onClick.AddListener(action);
        TextMeshProUGUI text = CreateText(title, buttonRoot.transform, 16, FontStyles.Normal);
        Stretch(text.rectTransform);
        text.alignment = TextAlignmentOptions.Center;
        return text;
    }

    private static TMP_InputField CreateInputField(string value, Transform parent)
    {
        GameObject fieldRoot = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        fieldRoot.transform.SetParent(parent, false);
        fieldRoot.GetComponent<LayoutElement>().preferredWidth = 90f;
        fieldRoot.GetComponent<LayoutElement>().preferredHeight = 34f;
        fieldRoot.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

        // TMP_InputField требует отдельный viewport с маской: без него текст и каретка
        // рисуются за границами рамки.
        RectTransform viewport = CreateRect("Text Area", fieldRoot.transform);
        Stretch(viewport);
        viewport.offsetMin = new Vector2(8f, 4f);
        viewport.offsetMax = new Vector2(-8f, -4f);
        viewport.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI text = CreateText(value, viewport, 16, FontStyles.Normal);
        Stretch(text.rectTransform);

        TMP_InputField field = fieldRoot.AddComponent<TMP_InputField>();
        field.textViewport = viewport;
        field.textComponent = text;
        field.contentType = TMP_InputField.ContentType.DecimalNumber;
        // Только после textComponent — иначе присвоенное значение некуда рисовать.
        field.text = value;
        return field;
    }

    private static TextMeshProUGUI CreateText(string value, Transform parent, float size, FontStyles style)
    {
        GameObject textRoot = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textRoot.transform.SetParent(parent, false);
        TextMeshProUGUI text = textRoot.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject.GetComponent<RectTransform>();
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
