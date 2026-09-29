using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

// The mouse wheel over the chat's messages, handed to the list so it moves
// a fixed step per notch. Added at runtime to the object with the ScrollRect,
// which is the one the wheel event reaches.
[DisallowMultipleComponent]
public sealed class ChatWheelScroll : MonoBehaviour, IScrollHandler
{
    private ChatMessageListView list;

    public void Bind(ChatMessageListView listView)
    {
        list = listView;
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (list == null)
            return;

        // The UI module hands over notches already multiplied by its own
        // per-notch size; undone here so a notch is a notch.
        float perNotch =
            EventSystem.current != null &&
            EventSystem.current.currentInputModule is InputSystemUIInputModule module &&
            Mathf.Abs(module.scrollDeltaPerTick) > Mathf.Epsilon
                ? module.scrollDeltaPerTick
                : 1f;

        list.ScrollByNotches(eventData.scrollDelta.y / perNotch);
    }
}
