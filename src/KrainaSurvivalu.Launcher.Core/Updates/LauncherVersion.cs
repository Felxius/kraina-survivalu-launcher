using System.Globalization;
using System.Text.RegularExpressions;

namespace KrainaSurvivalu.Launcher.Core.Updates;

/// <summary>Bounded SemVer comparison; build metadata does not affect precedence.</summary>
public sealed partial class LauncherVersion : IComparable<LauncherVersion>
{
    private readonly int[] _numbers;
    private readonly string[] _prerelease;
    private LauncherVersion(int[] numbers, string[] prerelease) { _numbers = numbers; _prerelease = prerelease; }

    public static LauncherVersion Parse(string value)
    {
        if (value is null || value.Length > 100) throw new FormatException("Nieprawidłowa wersja launchera.");
        var match = VersionPattern().Match(value);
        if (!match.Success) throw new FormatException("Wersja musi mieć postać SemVer, np. 1.2.3 lub 1.2.3-alpha.1.");
        var numbers = new int[3];
        for (var index = 0; index < 3; index++)
            if (!int.TryParse(match.Groups[index + 1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index])) throw new FormatException("Numer wersji jest zbyt duży.");
        var pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        if (pre.Any(part => part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit))) throw new FormatException("Identyfikator numeryczny SemVer nie może mieć wiodących zer.");
        return new(numbers, pre);
    }

    public int CompareTo(LauncherVersion? other)
    {
        if (other is null) return 1;
        for (var index = 0; index < 3; index++) { var result = _numbers[index].CompareTo(other._numbers[index]); if (result != 0) return result; }
        if (_prerelease.Length == 0) return other._prerelease.Length == 0 ? 0 : 1;
        if (other._prerelease.Length == 0) return -1;
        for (var index = 0; index < Math.Min(_prerelease.Length, other._prerelease.Length); index++)
        {
            var left = _prerelease[index]; var right = other._prerelease[index];
            var leftNumeric = left.All(char.IsAsciiDigit); var rightNumeric = right.All(char.IsAsciiDigit);
            int result;
            if (leftNumeric && rightNumeric) result = left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
            else if (leftNumeric != rightNumeric) result = leftNumeric ? -1 : 1;
            else result = string.CompareOrdinal(left, right);
            if (result != 0) return result;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
