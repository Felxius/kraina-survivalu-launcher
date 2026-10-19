using System.Globalization;
using System.Text;

namespace KrainaSurvivalu.Launcher.Core;

public static class PlayerNameValidation
{
    public static ValidationResult Validate(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return new(false, "Wpisz nick gracza używany na serwerze.");
        if (playerName != playerName.Trim())
            return new(false, "Nick nie może zaczynać ani kończyć się spacją.");
        if (string.Equals(playerName, "Survivor", StringComparison.OrdinalIgnoreCase))
            return new(false, "Wybierz własny nick. Domyślna nazwa Survivor jest niedozwolona przez regulamin.");

        var count = 0;
        foreach (var rune in playerName.EnumerateRunes())
        {
            if (++count > 32)
                return new(false, "Nick może mieć maksymalnie 32 znaki Unicode.");
            var category = Rune.GetUnicodeCategory(rune);
            var isCombiningMark = category is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
            if (!Rune.IsLetterOrDigit(rune) && !isCombiningMark &&
                rune.Value is not (' ' or '_' or '-' or '[' or ']' or '(' or ')'))
                return new(false, "Nick może zawierać litery, cyfry, spacje oraz znaki _ - [ ] ( ). Bez cudzysłowów i znaków sterujących.");
        }
        return new(true, "Nick gracza jest poprawny.");
    }

    internal static string? ValidateStoredName(string? playerName)
    {
        if (playerName is null)
            return "Nick w ustawieniach nie może mieć wartości null.";
        // An empty name represents an unconfigured or migrated installation, never launch permission.
        if (playerName.Length == 0)
            return null;
        var validation = Validate(playerName);
        return validation.IsValid ? null : validation.Message;
    }
}
