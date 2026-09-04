using UnityEngine;

public static class ChatUiEventChannelBinder
{
    public static void Apply(GameObject root, ChatEventChannel chatEvents)
    {
        if (root == null)
        {
            return;
        }

        // Asked for by what it is rather than by what it is made of: the lobby
        // has a UI Toolkit window and the phone has the old uGUI one.
        IChatWindowView[] chatWindows = root.GetComponentsInChildren<IChatWindowView>(true);

        for (int i = 0; i < chatWindows.Length; i++)
        {
            chatWindows[i].SetEventChannel(chatEvents);
        }

        ChatVisibilityController[] visibilityControllers =
            root.GetComponentsInChildren<ChatVisibilityController>(true);

        for (int i = 0; i < visibilityControllers.Length; i++)
        {
            visibilityControllers[i].SetEventChannel(chatEvents);
        }

        ChatReadStateTracker[] readStateTrackers =
            root.GetComponentsInChildren<ChatReadStateTracker>(true);

        for (int i = 0; i < readStateTrackers.Length; i++)
        {
            readStateTrackers[i].SetEventChannel(chatEvents);
        }

        ChatMessageNotificationAudioController[] messageNotificationAudioControllers =
            root.GetComponentsInChildren<ChatMessageNotificationAudioController>(true);

        for (int i = 0; i < messageNotificationAudioControllers.Length; i++)
        {
            messageNotificationAudioControllers[i].SetEventChannel(chatEvents);
        }

        ChatSendRejectedAudioController[] sendRejectedAudioControllers =
            root.GetComponentsInChildren<ChatSendRejectedAudioController>(true);

        for (int i = 0; i < sendRejectedAudioControllers.Length; i++)
        {
            sendRejectedAudioControllers[i].SetEventChannel(chatEvents);
        }
    }
}
