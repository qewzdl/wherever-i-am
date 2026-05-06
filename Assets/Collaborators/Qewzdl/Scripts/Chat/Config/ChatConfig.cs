using UnityEngine;

[CreateAssetMenu(
    fileName = "ChatConfig",
    menuName = "Wherever I Am/Chat/Chat Config")]
public class ChatConfig : ScriptableObject, IChatConfig
{
    [Header("Storage")]
    [SerializeField, Min(1)] private int maxStoredMessages = 80;

    [Header("Validation")]
    [SerializeField, Min(1)] private int maxMessageLength = 120;

    [Header("Anti Spam")]
    [SerializeField, Min(0f)] private float messageCooldownSeconds = 0.5f;

    public int MaxStoredMessages => Mathf.Max(1, maxStoredMessages);

    public int MaxMessageLength => Mathf.Clamp(maxMessageLength, 1, 120);

    public float MessageCooldownSeconds => Mathf.Max(0f, messageCooldownSeconds);
}
