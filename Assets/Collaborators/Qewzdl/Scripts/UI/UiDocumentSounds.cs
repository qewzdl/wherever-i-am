using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public enum UiSoundTrigger
{
    PointerEnter,
    Click,
    TextChanged,
    FocusIn,
    ValueChanged,
    Toggled
}

// Which class, on which event, plays which sound. A screen earns its sounds by
// naming its elements, not by anybody writing code for it.
[Serializable]
public struct UiElementSoundBinding
{
    public string className;
    public UiSoundTrigger trigger;
    public UiSoundType sound;

    public UiElementSoundBinding(string className, UiSoundTrigger trigger, UiSoundType sound)
    {
        this.className = className;
        this.trigger = trigger;
        this.sound = sound;
    }
}

// Sound for a UI Toolkit document.
//
// uGUI gets this from UiButtonSound, a component on every button. Elements here
// are not scene objects and cannot carry components, so one binder listens for
// the whole screen - which is also how elements added later are covered.
//
// The bindings are data rather than code so that the next screen needs neither.
// A later binding wins over an earlier one for the same element and event, so
// a modifier class can say something different from the class it modifies.
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class UiDocumentSounds : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    [SerializeField]
    private UiElementSoundBinding[] bindings =
    {
        new UiElementSoundBinding("button", UiSoundTrigger.PointerEnter, UiSoundType.Hover),
        new UiElementSoundBinding("button", UiSoundTrigger.Click, UiSoundType.Click),
        new UiElementSoundBinding("button--confirm", UiSoundTrigger.Click, UiSoundType.Confirm),
        new UiElementSoundBinding("button--cancel", UiSoundTrigger.Click, UiSoundType.Cancel),
        new UiElementSoundBinding("button--danger", UiSoundTrigger.Click, UiSoundType.Confirm),
        new UiElementSoundBinding("input", UiSoundTrigger.TextChanged, UiSoundType.Input),
        new UiElementSoundBinding("input", UiSoundTrigger.FocusIn, UiSoundType.Click),
        new UiElementSoundBinding("unity-base-slider", UiSoundTrigger.ValueChanged, UiSoundType.Slider),
        new UiElementSoundBinding("unity-toggle", UiSoundTrigger.Toggled, UiSoundType.Checkbox)
    };

    // A dragged slider reports a change every frame it moves. Played as they
    // come, the ticks run together into a rattle that says nothing about how
    // far the value went; spaced out, they read as steps under the hand. Left
    // adjustable because the right spacing depends on the clip.
    [SerializeField, Min(0f)] private float valueChangeInterval = 0.05f;

    private float nextValueChangeTime;

    private readonly Dictionary<(VisualElement, UiSoundTrigger), UiSoundType> bound = new();
    private readonly List<(VisualElement element, UiSoundTrigger trigger)> registered = new();

    private IUiSoundService uiSoundService;

    // Asked for on first sound, like every other sound consumer: whoever owns
    // this screen may never have thought to hand it anything.
    private IUiSoundService ResolvedUiSoundService =>
        uiSoundService ??= AudioServices.Ui();

    private void OnEnable()
    {
        Bind();
    }

    // UIDocument builds its tree in its own OnEnable, and component order on a
    // single object is not something to rely on. Binding twice is harmless.
    private void Start()
    {
        Bind();
    }

    private void OnDisable()
    {
        Unbind();
    }

    // Public because a screen that builds elements at runtime - a list of
    // players, a row per lobby member - has to say when it has finished.
    public void Bind()
    {
        if (document == null)
            document = GetComponent<UIDocument>();

        VisualElement root = document != null ? document.rootVisualElement : null;

        if (root == null || bindings == null)
            return;

        Unbind();

        for (int i = 0; i < bindings.Length; i++)
        {
            UiElementSoundBinding binding = bindings[i];

            if (string.IsNullOrWhiteSpace(binding.className))
                continue;

            root.Query<VisualElement>(className: binding.className).ForEach(element =>
                bound[(element, binding.trigger)] = binding.sound);
        }

        foreach (KeyValuePair<(VisualElement element, UiSoundTrigger trigger), UiSoundType> pair in bound)
            Register(pair.Key.element, pair.Key.trigger);
    }

    // Screen-level moments belong to whoever opens and closes the screen; this
    // is here so that they ask for a sound rather than for the audio service.
    public void Play(UiSoundType sound)
    {
        ResolvedUiSoundService?.Play(sound);
    }

    private void Register(VisualElement element, UiSoundTrigger trigger)
    {
        switch (trigger)
        {
            case UiSoundTrigger.PointerEnter:
                element.RegisterCallback<PointerEnterEvent>(HandlePointerEnter);
                break;

            case UiSoundTrigger.Click:
                element.RegisterCallback<ClickEvent>(HandleClick);
                break;

            case UiSoundTrigger.TextChanged:
                element.RegisterCallback<ChangeEvent<string>>(HandleTextChanged);
                break;

            case UiSoundTrigger.FocusIn:
                element.RegisterCallback<FocusInEvent>(HandleFocusIn);
                break;

            case UiSoundTrigger.ValueChanged:
                element.RegisterCallback<ChangeEvent<float>>(HandleValueChanged);
                break;

            case UiSoundTrigger.Toggled:
                element.RegisterCallback<ChangeEvent<bool>>(HandleToggled);
                break;
        }

        registered.Add((element, trigger));
    }

    private void Unbind()
    {
        for (int i = 0; i < registered.Count; i++)
        {
            (VisualElement element, UiSoundTrigger trigger) = registered[i];

            if (element == null)
                continue;

            switch (trigger)
            {
                case UiSoundTrigger.PointerEnter:
                    element.UnregisterCallback<PointerEnterEvent>(HandlePointerEnter);
                    break;

                case UiSoundTrigger.Click:
                    element.UnregisterCallback<ClickEvent>(HandleClick);
                    break;

                case UiSoundTrigger.TextChanged:
                    element.UnregisterCallback<ChangeEvent<string>>(HandleTextChanged);
                    break;

                case UiSoundTrigger.FocusIn:
                    element.UnregisterCallback<FocusInEvent>(HandleFocusIn);
                    break;

                case UiSoundTrigger.ValueChanged:
                    element.UnregisterCallback<ChangeEvent<float>>(HandleValueChanged);
                    break;

                case UiSoundTrigger.Toggled:
                    element.UnregisterCallback<ChangeEvent<bool>>(HandleToggled);
                    break;
            }
        }

        registered.Clear();
        bound.Clear();
    }

    private void HandlePointerEnter(PointerEnterEvent evt)
    {
        PlayFor(evt.currentTarget, UiSoundTrigger.PointerEnter);
    }

    private void HandleClick(ClickEvent evt)
    {
        PlayFor(evt.currentTarget, UiSoundTrigger.Click);
    }

    private void HandleTextChanged(ChangeEvent<string> evt)
    {
        PlayFor(evt.currentTarget, UiSoundTrigger.TextChanged);
    }

    private void HandleFocusIn(FocusInEvent evt)
    {
        PlayFor(evt.currentTarget, UiSoundTrigger.FocusIn);
    }

    // Unscaled, because the screens that own sliders are the ones that stop
    // the game to show themselves.
    private void HandleValueChanged(ChangeEvent<float> evt)
    {
        if (Time.unscaledTime < nextValueChangeTime)
            return;

        nextValueChangeTime = Time.unscaledTime + valueChangeInterval;
        PlayFor(evt.currentTarget, UiSoundTrigger.ValueChanged);
    }

    // A checkbox changes once per click, so this one is not spaced out the way
    // the slider is.
    private void HandleToggled(ChangeEvent<bool> evt)
    {
        PlayFor(evt.currentTarget, UiSoundTrigger.Toggled);
    }

    private void PlayFor(IEventHandler target, UiSoundTrigger trigger)
    {
        if (target is VisualElement element &&
            bound.TryGetValue((element, trigger), out UiSoundType sound))
        {
            ResolvedUiSoundService?.TryPlay(sound);
        }
    }
}
