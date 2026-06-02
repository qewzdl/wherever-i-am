using System.Text;

public sealed class RuntimeDebugTextBuilder
{
    private const string Separator = "────────────────────────────";

    private readonly StringBuilder builder = new StringBuilder(2048);

    public RuntimeDebugTextBuilder Clear()
    {
        builder.Clear();
        return this;
    }

    public RuntimeDebugTextBuilder Header(string title)
    {
        builder.AppendLine(ToDisplayText(title));
        builder.AppendLine(Separator);
        return this;
    }

    public RuntimeDebugTextBuilder Section(string title)
    {
        builder.AppendLine();
        builder.AppendLine(ToDisplayText(title));
        builder.AppendLine(Separator);
        return this;
    }

    public RuntimeDebugTextBuilder Row(string label, object value)
    {
        builder.Append(ToDisplayText(label));
        builder.Append(": ");
        builder.AppendLine(ToDisplayText(value));
        return this;
    }

    public RuntimeDebugTextBuilder Line(object value)
    {
        builder.AppendLine(ToDisplayText(value));
        return this;
    }

    public string Build()
    {
        return builder.ToString();
    }

    private string ToDisplayText(object value)
    {
        if (value == null)
        {
            return "None";
        }

        string text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? "None" : text;
    }
}