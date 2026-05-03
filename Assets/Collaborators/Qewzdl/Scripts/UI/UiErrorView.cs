using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class UiErrorView : MonoBehaviour
{
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button backdropButton;

    public event Action CloseRequested;

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(RequestClose);

        if (backdropButton != null)
            backdropButton.onClick.AddListener(RequestClose);
    }

    private void OnDestroy()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(RequestClose);

        if (backdropButton != null)
            backdropButton.onClick.RemoveListener(RequestClose);
    }

    public void Show(string message)
    {
        if (messageText != null)
            messageText.text = message;

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void RequestClose()
    {
        CloseRequested?.Invoke();
    }
}
