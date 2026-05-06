using TMPro;
using UnityEngine;

public class ChatMessageItemView : MonoBehaviour
{
    [SerializeField] private TMP_Text senderText;
    [SerializeField] private TMP_Text messageText;

    private void Awake()
    {
        if (senderText != null)
            senderText.richText = false;

        if (messageText != null)
            messageText.richText = false;
    }

    public void SetMessage(ChatMessageData message)
    {
        if (senderText != null)
        {
            senderText.text = message.Channel == ChatChannel.System
                ? "System"
                : message.SenderName.ToString();
        }

        if (messageText != null)
            messageText.text = message.Text.ToString();
    }
}
