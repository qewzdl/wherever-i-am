using TMPro;
using UnityEngine;

[CreateAssetMenu(fileName = "ChatTypographyProfile", menuName = "Wherever I Am/Chat/Typography Profile")]
public class ChatTypographyProfile : ScriptableObject
{
    [Header("Font")]
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private Material fontSharedMaterial;

    [Header("Colour")]
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color scrollbarHandleColor = Color.white;

    [Header("Apply")]
    [SerializeField] private bool includeInactiveText = true;

    public TMP_FontAsset FontAsset => fontAsset;
    public Material FontSharedMaterial => fontSharedMaterial;
    public bool IncludeInactiveText => includeInactiveText;
    public Color TextColor => textColor;
    public Color ScrollbarHandleColor => scrollbarHandleColor;
    public bool HasFont => fontAsset != null;
}