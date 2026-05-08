using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(TMP_InputField))]
public class UiInputSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
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
        if (AudioManager.Instance == null || AudioManager.Instance.UI == null) return;

        AudioManager.Instance.UI.PlayHover();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!playClickSound) return;
        if (AudioManager.Instance == null || AudioManager.Instance.UI == null) return;

        AudioManager.Instance.UI.PlayClick();
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

        if (AudioManager.Instance == null || AudioManager.Instance.UI == null)
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
        if (AudioManager.Instance == null || AudioManager.Instance.UI == null) return;

        if (Time.unscaledTime - lastInputSoundTime < inputSoundCooldown) return;

        lastInputSoundTime = Time.unscaledTime;

        if (inputSoundOverride != null)
            AudioManager.Instance.UI.Play(inputSoundOverride);
        else
            AudioManager.Instance.UI.PlayInput();
    }
} 
