using System;
using System.Reflection;

internal static class PlayModeTestReflection
{
    internal static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = FindField(target.GetType(), fieldName);
        field.SetValue(target, value);
    }

    internal static T GetField<T>(object target, string fieldName)
    {
        FieldInfo field = FindField(target.GetType(), fieldName);
        return (T)field.GetValue(target);
    }

    internal static object Invoke(object target, string methodName, params object[] arguments)
    {
        Type type = target.GetType();

        while (type != null)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (method != null)
                return method.Invoke(target, arguments);

            type = type.BaseType;
        }

        throw new MissingMethodException(target.GetType().FullName, methodName);
    }

    private static FieldInfo FindField(Type type, string fieldName)
    {
        Type current = type;

        while (current != null)
        {
            FieldInfo field = current.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field != null)
                return field;

            current = current.BaseType;
        }

        throw new MissingFieldException(type.FullName, fieldName);
    }
}
