namespace RlpTool;

internal static class Ansi
{
    public static bool Enabled { get; set; } = true;

    public static string Bold(string text) => Wrap(text, "\u001b[1m");

    public static string Dim(string text) => Wrap(text, "\u001b[2m");

    public static string Red(string text) => Wrap(text, "\u001b[31m");

    public static string Yellow(string text) => Wrap(text, "\u001b[33m");

    public static string Green(string text) => Wrap(text, "\u001b[32m");

    public static string Cyan(string text) => Wrap(text, "\u001b[36m");

    public static string Magenta(string text) => Wrap(text, "\u001b[35m");

    public static string Foreground256(string text, int color) => Wrap(text, $"\u001b[38;5;{color}m");

    private static string Wrap(string text, string code) => Enabled ? $"{code}{text}\u001b[0m" : text;
}
