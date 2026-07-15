using System;
using UnityEngine;

internal static class RuntimeEventDispatcher
{
    internal static void Invoke(
        Action handlers,
        string eventName,
        UnityEngine.Object context = null)
    {
        if (handlers == null)
            return;

        Delegate[] subscribers = handlers.GetInvocationList();

        for (int i = 0; i < subscribers.Length; i++)
        {
            try
            {
                ((Action)subscribers[i]).Invoke();
            }
            catch (Exception exception)
            {
                LogSubscriberFailure(eventName, exception, context);
            }
        }
    }

    internal static void Invoke<TFirst, TSecond>(
        Action<TFirst, TSecond> handlers,
        TFirst first,
        TSecond second,
        string eventName,
        UnityEngine.Object context = null)
    {
        if (handlers == null)
            return;

        Delegate[] subscribers = handlers.GetInvocationList();

        for (int i = 0; i < subscribers.Length; i++)
        {
            try
            {
                ((Action<TFirst, TSecond>)subscribers[i]).Invoke(first, second);
            }
            catch (Exception exception)
            {
                LogSubscriberFailure(eventName, exception, context);
            }
        }
    }

    internal static void Invoke<TValue>(
        Action<TValue> handlers,
        TValue value,
        string eventName,
        UnityEngine.Object context = null)
    {
        if (handlers == null)
            return;

        Delegate[] subscribers = handlers.GetInvocationList();

        for (int i = 0; i < subscribers.Length; i++)
        {
            try
            {
                ((Action<TValue>)subscribers[i]).Invoke(value);
            }
            catch (Exception exception)
            {
                LogSubscriberFailure(eventName, exception, context);
            }
        }
    }

    internal static void Invoke<TFirst, TSecond, TThird>(
        Action<TFirst, TSecond, TThird> handlers,
        TFirst first,
        TSecond second,
        TThird third,
        string eventName,
        UnityEngine.Object context = null)
    {
        if (handlers == null)
            return;

        Delegate[] subscribers = handlers.GetInvocationList();

        for (int i = 0; i < subscribers.Length; i++)
        {
            try
            {
                ((Action<TFirst, TSecond, TThird>)subscribers[i]).Invoke(
                    first,
                    second,
                    third);
            }
            catch (Exception exception)
            {
                LogSubscriberFailure(eventName, exception, context);
            }
        }
    }

    private static void LogSubscriberFailure(
        string eventName,
        Exception exception,
        UnityEngine.Object context)
    {
        string name = string.IsNullOrWhiteSpace(eventName)
            ? "runtime event"
            : eventName;

        Debug.LogError($"Subscriber failed while handling {name}.", context);
        Debug.LogException(exception, context);
    }
}
