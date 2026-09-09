namespace UltraPrint.Legacy.Startup;

public sealed record LegacyStartupOptions(
    bool ErasePassword,
    bool PrintForm,
    string? Layout,
    IReadOnlyList<string> UnknownArguments)
{
    public static LegacyStartupOptions Parse(IEnumerable<string> arguments)
    {
        var erasePassword = false;
        var printForm = false;
        string? layout = null;
        var unknown = new List<string>();

        foreach (var arg in arguments)
        {
            if (string.Equals(arg, "/erasepw", StringComparison.OrdinalIgnoreCase))
                erasePassword = true;
            else if (string.Equals(arg, "/printform", StringComparison.OrdinalIgnoreCase))
                printForm = true;
            else if (arg.StartsWith("/Layout=", StringComparison.OrdinalIgnoreCase))
                layout = arg[8..].Trim().Trim('"');
            else
                unknown.Add(arg);
        }

        return new LegacyStartupOptions(erasePassword, printForm, layout, unknown);
    }
}
