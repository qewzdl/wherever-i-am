using System.Text;
using UnityEngine;

public static class EnemyValidationLogger
{
    public static bool ValidateAndLog(
        Object context,
        string ownerName,
        StringBuilder errorBuilder,
        ref bool errorLogged,
        bool logErrors,
        string disabledMessage
    )
    {
        if (errorBuilder.Length == 0)
        {
            errorLogged = false;
            return true;
        }

        if (logErrors && !errorLogged)
        {
            errorLogged = true;

            Debug.LogError(
                $"{ownerName} has invalid configuration:\n" +
                errorBuilder +
                disabledMessage,
                context
            );
        }

        return false;
    }

    public static void AppendMissingDependency(StringBuilder builder, string dependencyName)
    {
        builder.Append("- ");
        builder.Append(dependencyName);
        builder.AppendLine(" is not assigned.");
    }

    public static void AppendEmptyLayerMask(StringBuilder builder, string maskName)
    {
        builder.Append("- ");
        builder.Append(maskName);
        builder.AppendLine(" is empty.");
    }

    public static bool ValidateConfig(
        Object context,
        string ownerName,
        EnemyConfig config,
        ref bool errorLogged
    )
    {
        if (config != null)
        {
            errorLogged = false;
            return true;
        }

        if (!errorLogged)
        {
            errorLogged = true;

            Debug.LogError(
                $"{ownerName} requires non-null {nameof(EnemyConfig)}.",
                context
            );
        }

        return false;
    }
}