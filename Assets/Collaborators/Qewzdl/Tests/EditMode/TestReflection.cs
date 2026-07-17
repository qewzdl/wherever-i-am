using System;
using System.Reflection;

internal static class TestReflection
{
    internal static void SetField(object target, string fieldName, object value)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        FieldInfo field = FindField(target.GetType(), fieldName);
        field.SetValue(target, value);
    }

    internal static T GetField<T>(object target, string fieldName)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        FieldInfo field = FindField(target.GetType(), fieldName);
        return (T)field.GetValue(target);
    }

    internal static object Invoke(object target, string methodName, params object[] arguments)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        MethodInfo method = FindMethod(target.GetType(), methodName);
        return method.Invoke(target, arguments);
    }

    private static FieldInfo FindField(Type type, string fieldName)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                fieldName,
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            if (field != null)
                return field;
        }

        throw new MissingFieldException(type.FullName, fieldName);
    }

    private static MethodInfo FindMethod(Type type, string methodName)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            MethodInfo method = current.GetMethod(
                methodName,
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            if (method != null)
                return method;
        }

        throw new MissingMethodException(type.FullName, methodName);
    }
}
