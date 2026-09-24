namespace LibraryBookManager.Api.Catalogue;

public static class Isbn
{
    /// <summary>Removes the hyphens and spaces people write ISBNs with, and upper-cases a check digit X.</summary>
    public static string Normalize(string raw) =>
        new string(raw.Where(c => c != '-' && c != ' ').ToArray()).ToUpperInvariant();

    /// <summary>
    /// Shape only: 13 digits, or 9 digits followed by a digit or X. The check digit is
    /// not verified (see QUESTIONS.md Q-BOOK-03).
    /// </summary>
    public static bool HasValidShape(string normalized) =>
        normalized.Length switch
        {
            13 => normalized.All(char.IsAsciiDigit),
            10 => normalized[..9].All(char.IsAsciiDigit) && (char.IsAsciiDigit(normalized[9]) || normalized[9] == 'X'),
            _ => false,
        };
}
