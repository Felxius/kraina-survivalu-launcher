namespace KrainaSurvivalu.Launcher.Core;

public static class DayZLaunchArguments
{
    /// <summary>
    /// Returns one unquoted argument for a ProcessStartInfo.ArgumentList entry.
    /// Never concatenate this into shell commands, Steam URI strings, or a complete command line.
    /// This method does not launch the game.
    /// </summary>
    public static string CreatePlayerNameArgument(string playerName)
    {
        var validation = PlayerNameValidation.Validate(playerName);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Message, nameof(playerName));
        return "-name=" + playerName;
    }
}
