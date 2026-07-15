using System;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(TMP_InputField))]
public class UiInputSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, IUiSoundServiceConsumer
{
    [Header("Settings")]
    [SerializeField] private bool playHoverSound = true;
    [SerializeField] private bool playClickSound = true;
    [SerializeField] private bool playInputSound = true;

    [Header("Input Sound Settings")]
    [SerializeField] private SoundEffect inputSoundOverride;
    [SerializeField] private bool playOnDelete = false;
    [SerializeField, Min(0f)] private float inputSoundCooldown = 0.03f;

    private TMP_InputField inputField;
    private string previousText = "";
    private float lastInputSoundTime;
    private IUiSoundService uiSoundService;

    public event Action InputSoundPlayed;

    public void Construct(IUiSoundService service)
    {
        uiSoundService = service;
    }

    public void ReleaseUiSoundService()
    {
        uiSoundService = null;
    }

    private void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
        previousText = inputField.text;
    }

    private void OnEnable()
    {
        inputField.onValueChanged.AddListener(OnInputValueChanged);
    }

    private void OnDisable()
    {
        inputField.onValueChanged.RemoveListener(OnInputValueChanged);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!playHoverSound) return;
        uiSoundService?.PlayHover();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!playClickSound) return;
        uiSoundService?.PlayClick();
    }

    public void SetInputSoundOverride(SoundEffect sound)
    {
        inputSoundOverride = sound;
    }

    private void OnInputValueChanged(string newText)
    {
        if (!playInputSound) 
        {
            previousText = newText;
            return;
        }

        if (uiSoundService == null)
        {
            previousText = newText;
            return;
        }

        bool textWasAdded = newText.Length > previousText.Length;
        bool textWasDeleted = newText.Length < previousText.Length;

        if (textWasAdded || (playOnDelete && textWasDeleted))
        {
            TryPlayInputSound();
        }

        previousText = newText;
    }

    private void TryPlayInputSound()
    {
        if (uiSoundService == null) return;

        if (Time.unscaledTime - lastInputSoundTime < inputSoundCooldown) return;

        lastInputSoundTime = Time.unscaledTime;

        bool played = inputSoundOverride != null
            ? uiSoundService.TryPlay(inputSoundOverride)
            : uiSoundService.TryPlay(UiSoundType.Input);

        if (played)
        {
            InputSoundPlayed?.Invoke();
        }
    }
} 
