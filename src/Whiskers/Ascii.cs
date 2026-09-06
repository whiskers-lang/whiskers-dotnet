namespace Whiskers;

internal static class Ascii
{
    public static bool IsDigit(char c) => (uint)(c - '0') <= 9;
    public static bool IsLetter(char c) => (uint)((c | 0x20) - 'a') <= (uint)('z' - 'a');
    public static bool IsLetterOrDigit(char c) => IsLetter(c) || IsDigit(c);
}
