using Microsoft.VisualBasic;
using UltraPrint.Legacy.Scripting;

namespace UltraPrint.WinForms;

/// <summary>
/// WinForms implementation of the UI boundaries recovered from
/// Funzioni.Interpretariga. Empty/cancelled values are returned as null so the
/// caller follows the original cancellation path.
/// </summary>
internal sealed class WinFormsLegacyScriptInteraction : ILegacyScriptInteraction
{
    private readonly IWin32Window _owner;

    public WinFormsLegacyScriptInteraction(IWin32Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public string? Prompt(string prompt)
    {
        var value = Interaction.InputBox(prompt ?? string.Empty, "UltraPrint", string.Empty);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public string? SelectFile(string firstArgument, string secondArgument)
    {
        // Static analysis of File.ApriFile in UltraPrint 2.2.115 shows that this
        // build ignores both incoming arguments and hard-codes "Tutti i File|*.*".
        using var dialog = new OpenFileDialog
        {
            Title = "UltraPrint",
            Filter = "Tutti i File|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(_owner) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? SelectDirectory(string prompt)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = prompt ?? string.Empty,
            ShowNewFolderButton = true
        };
        return dialog.ShowDialog(_owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? SelectComputer(string prompt)
    {
        // The VB6 application used the shell/network browser and ultimately kept
        // only the host component of a UNC path. A simple prompt preserves the
        // functional contract without reintroducing an obsolete shell ActiveX UI.
        var value = Interaction.InputBox(
            string.IsNullOrWhiteSpace(prompt) ? "Computer / server:" : prompt,
            "UltraPrint",
            string.Empty);
        if (string.IsNullOrWhiteSpace(value)) return null;

        var candidate = value.Trim();
        if (candidate.StartsWith("\\\\", StringComparison.Ordinal))
        {
            candidate = candidate[2..];
            var separator = candidate.IndexOf('\\');
            if (separator >= 0) candidate = candidate[..separator];
        }
        else
        {
            candidate = candidate.Trim('\\');
        }

        return candidate.Length == 0 ? null : candidate;
    }
}
