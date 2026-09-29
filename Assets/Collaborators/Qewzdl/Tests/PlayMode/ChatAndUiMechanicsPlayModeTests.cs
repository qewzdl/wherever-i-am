using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.UIElements;

// Both UI systems are in this file now, and each has an Image.
using Image = UnityEngine.UI.Image;

[Category("UI")]
public sealed class ChatAndUiMechanicsPlayModeTests
{
    private readonly List<Object> cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null)
                Object.DestroyImmediate(cleanup[i]);
        }

        cleanup.Clear();
    }

    [UnityTest]
    public IEnumerator ChatReadState_CountsOnlyUnreadMessagesAndClearsOnOpen()
    {
        ChatEventChannel channel = Track(ScriptableObject.CreateInstance<ChatEventChannel>());
        GameObject gameObject = Track(new GameObject("Chat state"));
        gameObject.SetActive(false);

        ChatVisibilityController visibility =
            gameObject.AddComponent<ChatVisibilityController>();
        ChatReadStateTracker tracker =
            gameObject.AddComponent<ChatReadStateTracker>();
        visibility.SetEventChannel(channel);
        tracker.SetEventChannel(channel);
        gameObject.SetActive(true);

        yield return null;

        channel.RaiseMessageReceived(CreateMessage(
            "remote",
            isLocalSender: false,
            isSystemMessage: false));
        channel.RaiseMessageReceived(CreateMessage(
            "own",
            isLocalSender: true,
            isSystemMessage: false));
        channel.RaiseMessageReceived(CreateMessage(
            "system",
            isLocalSender: false,
            isSystemMessage: true));

        Assert.That(tracker.UnreadCount, Is.EqualTo(2));
        Assert.That(channel.CurrentUnreadCount, Is.EqualTo(2));

        visibility.OpenChat();

        Assert.That(visibility.IsOpen, Is.True);
        Assert.That(tracker.IsChatOpen, Is.True);
        Assert.That(tracker.UnreadCount, Is.Zero);
        Assert.That(channel.CurrentUnreadCount, Is.Zero);

        channel.RaiseMessageReceived(CreateMessage(
            "while-open",
            isLocalSender: false,
            isSystemMessage: false));
        Assert.That(tracker.UnreadCount, Is.Zero);

        visibility.CloseChat();
        channel.RaiseMessageReceived(CreateMessage(
            "after-close",
            isLocalSender: false,
            isSystemMessage: false));
        Assert.That(tracker.UnreadCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator PhoneSpriteAnimator_PlaysBothDirectionsAndCommitsFinalFrame()
    {
        Texture2D texture = Track(new Texture2D(3, 1));
        Sprite closed = Track(Sprite.Create(
            texture, new Rect(0f, 0f, 1f, 1f), Vector2.zero));
        Sprite middle = Track(Sprite.Create(
            texture, new Rect(1f, 0f, 1f, 1f), Vector2.zero));
        Sprite opened = Track(Sprite.Create(
            texture, new Rect(2f, 0f, 1f, 1f), Vector2.zero));
        PhoneSpriteAnimationProfile profile =
            Track(ScriptableObject.CreateInstance<PhoneSpriteAnimationProfile>());
        PlayModeTestReflection.SetField(
            profile,
            "frames",
            new List<Sprite> { closed, middle, opened });
        PlayModeTestReflection.SetField(profile, "framesPerSecond", 1000f);

        GameObject gameObject = Track(new GameObject("Phone animation"));
        Image image = gameObject.AddComponent<Image>();
        PhoneSpriteAnimator animator = gameObject.AddComponent<PhoneSpriteAnimator>();
        animator.Configure(image, profile);

        int completed = 0;
        animator.PlaybackCompleted += _ => completed++;

        Assert.That(animator.PlayOpening(), Is.True);

        while (animator.IsPlaying)
            yield return null;

        Assert.That(image.sprite, Is.SameAs(opened));
        Assert.That(completed, Is.EqualTo(1));

        Assert.That(animator.PlayClosing(), Is.True);

        while (animator.IsPlaying)
            yield return null;

        Assert.That(image.sprite, Is.SameAs(closed));
        Assert.That(completed, Is.EqualTo(2));
    }

    [Test]
    public void PhoneScreenLayout_MapsTexturePixelsToAnchorsPaddingAndMask()
    {
        Texture2D texture = Track(new Texture2D(100, 200));
        Sprite sprite = Track(Sprite.Create(
            texture, new Rect(0f, 0f, 100f, 200f), Vector2.zero));

        GameObject rootObject = Track(new GameObject(
            "Phone",
            typeof(RectTransform),
            typeof(Image),
            typeof(PhoneScreenLayoutController)));
        RectTransform root = rootObject.GetComponent<RectTransform>();
        Image image = rootObject.GetComponent<Image>();
        image.sprite = sprite;

        RectTransform screen = CreateRect("Screen", root);
        RectTransform chat = CreateRect("ChatContainer", screen);
        PhoneScreenLayoutController layout =
            rootObject.GetComponent<PhoneScreenLayoutController>();
        layout.ConfigureReferences(root, image, screen, chat);
        layout.ConfigureLayout(
            fitScreenRectToTexture: true,
            new Rect(10f, 20f, 50f, 80f),
            new Vector4(8f, 9f, 10f, 11f),
            addScreenRectMask: true);

        layout.Apply(ensureMask: true);

        Assert.That(screen.anchorMin.x, Is.EqualTo(0.1f).Within(0.001f));
        Assert.That(screen.anchorMin.y, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(screen.anchorMax.x, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(screen.anchorMax.y, Is.EqualTo(0.9f).Within(0.001f));
        Assert.That(chat.offsetMin, Is.EqualTo(new Vector2(8f, 11f)));
        Assert.That(chat.offsetMax, Is.EqualTo(new Vector2(-10f, -9f)));
        Assert.That(screen.GetComponent<RectMask2D>(), Is.Not.Null);
    }

    // The screen this manager owns covers everything, so the thing worth
    // guarding is that it only takes the pointer while it is actually saying
    // something. An invisible screen that still swallows clicks reads as a
    // dead menu, which is a fault this project has shipped more than once.
    [Test]
    public void UiErrorManager_TakesThePointerOnlyWhileAnErrorIsUp()
    {
        GameObject root = Track(new GameObject("Error overlay"));
        root.SetActive(false);

        UIDocument document = root.AddComponent<UIDocument>();
        document.panelSettings = Track(ScriptableObject.CreateInstance<PanelSettings>());
        UiErrorManager manager = root.AddComponent<UiErrorManager>();
        root.SetActive(true);

        // Built here rather than loaded, so the test is about the manager and
        // not about the markup: it binds by name either way.
        VisualElement screen = new() { name = "Screen" };
        screen.Add(new Label { name = "ErrorText" });
        document.rootVisualElement.Add(screen);

        Assert.That(screen.pickingMode, Is.EqualTo(PickingMode.Position));

        // Blank messages still get a panel, because something went wrong even
        // if nobody said what.
        manager.ShowError("   ");

        Assert.That(screen.Q<Label>("ErrorText").text, Is.EqualTo("Unknown error."));
        Assert.That(screen.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        Assert.That(screen.pickingMode, Is.EqualTo(PickingMode.Position));

        manager.ShowError("Network error");
        Assert.That(screen.Q<Label>("ErrorText").text, Is.EqualTo("Network error"));

        manager.HideError();
        Assert.That(screen.pickingMode, Is.EqualTo(PickingMode.Ignore));
    }

    private RectTransform CreateRect(string name, Transform parent)
    {
        GameObject gameObject = Track(new GameObject(name, typeof(RectTransform)));
        gameObject.transform.SetParent(parent, false);
        return gameObject.GetComponent<RectTransform>();
    }

    private static ChatMessageReceivedEvent CreateMessage(
        string text,
        bool isLocalSender,
        bool isSystemMessage)
    {
        return new ChatMessageReceivedEvent(
            messageId: text,
            channelId: "game",
            senderClientId: isLocalSender ? 0UL : 1UL,
            senderDisplayName: "Player",
            text: text,
            isLocalSender: isLocalSender,
            isSystemMessage: isSystemMessage,
            serverTime: 1d);
    }

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }

    // One notch of the wheel moves the chat one fixed step, whichever way the
    // wheel reaches it, and never more than half of what is on screen - so no
    // line can be scrolled past unseen. It used to move 180 pixels a notch.
    [UnityTest]
    public IEnumerator ChatWheel_MovesAFixedStepThatSkipsNothing()
    {
        GameObject root = Track(new GameObject("Chat scroll", typeof(Canvas)));
        root.SetActive(false);

        RectTransform viewport = new GameObject("Viewport", typeof(RectTransform)).GetComponent<RectTransform>();
        viewport.SetParent(root.transform, false);
        viewport.sizeDelta = new Vector2(300f, 200f);

        RectTransform content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.sizeDelta = new Vector2(300f, 1000f);

        ScrollRect scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
        scrollRect.viewport = viewport;
        scrollRect.content = content;
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        ChatMessageListView list = root.AddComponent<ChatMessageListView>();
        PlayModeTestReflection.SetField(list, "scrollRect", scrollRect);
        root.SetActive(true);
        yield return null;

        Assert.That(scrollRect.scrollSensitivity, Is.EqualTo(0f),
            "The ScrollRect still scrolls the wheel by itself.");
        Assert.That(viewport.GetComponent<ChatWheelScroll>(), Is.Not.Null);

        const float Overflow = 800f;
        scrollRect.verticalNormalizedPosition = 1f;

        list.ScrollByWheelDelta(new Vector2(0f, -1f));
        Assert.That((1f - scrollRect.verticalNormalizedPosition) * Overflow, Is.EqualTo(24f).Within(0.5f));

        // A step larger than half the view is held to half of it.
        PlayModeTestReflection.SetField(list, "wheelStep", 500f);
        scrollRect.verticalNormalizedPosition = 1f;
        list.ScrollByNotches(-1f);
        Assert.That((1f - scrollRect.verticalNormalizedPosition) * Overflow, Is.EqualTo(100f).Within(0.5f));
    }

    // The same rule for UI Toolkit, where the lobby chat lives: one notch is
    // one step, and never more than half the view. Left alone, a ScrollView
    // multiplied the notch by its own wheel size and jumped most of a small
    // window at a time. (A wheel event cannot be delivered to a panel in a
    // batch run, so this checks the step the chat's wheel handler takes.)
    [UnityTest]
    public IEnumerator ScrollViewWheel_MovesOneStepAndSkipsNothing()
    {
        GameObject root = Track(new GameObject("Wheel view"));
        UIDocument document = root.AddComponent<UIDocument>();
        document.panelSettings = Track(ScriptableObject.CreateInstance<PanelSettings>());
        yield return null;

        ScrollView view = new();
        view.style.width = 300f;
        view.style.height = 200f;

        VisualElement tall = new();
        tall.style.height = 1000f;
        tall.style.flexShrink = 0f;
        view.Add(tall);
        document.rootVisualElement.Add(view);

        yield return null;
        yield return null;

        WheelStepScroll.Step(view, 24f, 1f);
        Assert.That(view.scrollOffset.y, Is.EqualTo(24f).Within(0.5f),
            "One notch did not move the view one step.");

        view.scrollOffset = Vector2.zero;
        WheelStepScroll.Step(view, 500f, 1f);
        Assert.That(view.scrollOffset.y, Is.EqualTo(100f).Within(0.5f),
            "A step bigger than half the view was not held to half of it.");

        WheelStepScroll.Step(view, 24f, -1f);
        WheelStepScroll.Step(view, 500f, -1f);
        Assert.That(view.scrollOffset.y, Is.EqualTo(0f).Within(0.5f),
            "Scrolling up went past the first line.");
    }
}
