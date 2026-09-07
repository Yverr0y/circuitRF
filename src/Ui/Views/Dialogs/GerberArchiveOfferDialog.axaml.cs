using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CircuitRF.Ui.Views.Dialogs;

/// <summary>
/// GI4 R-gi4-10: the chosen folder holds no Gerber artwork of its own, and there is an archive
/// sitting in it. That is currently a dead end — the archive is classified "not text" and skipped,
/// which is true and useless.
///
/// <para><b>An offer, never an action.</b> R-L4g-3's doctrine applies here word for word: an import
/// that quietly reached outside what the user pointed at is a surprise, and a surprise in a file
/// importer is a support question forever. So the archive is named, what will happen to it is stated
/// (unpacked to a temporary folder, imported from there, deleted again), and nothing happens until
/// someone says yes. Headless, the same question is a refusal naming <c>--open-archives</c>.</para>
///
/// <para>Never shown when the folder already yields artwork (R-gi4-11) — importing both is how two
/// versions of one board get silently merged.</para>
///
/// <para>Returns true via <c>ShowDialog&lt;bool&gt;</c> when the user chose to look inside.</para>
/// </summary>
public partial class GerberArchiveOfferDialog : Window
{
    public GerberArchiveOfferDialog()
    {
        InitializeComponent();
        Opened += (_, _) => LookInsideButton.Focus();
    }

    public GerberArchiveOfferDialog(IReadOnlyList<string> archivePaths) : this()
        => MessageText.Text = Question(archivePaths);

    /// <summary>The prompt's own text, built here rather than in XAML so it is assertable with no
    /// window — the same split <c>GerberImportEntry.FolderSurvey.Question</c> already uses.</summary>
    public static string Question(IReadOnlyList<string> archivePaths)
    {
        string names = string.Join(", ", archivePaths.Select(System.IO.Path.GetFileName));
        return $"This folder holds no Gerber artwork of its own. It does hold {names}.\n\n" +
               "circuitRF can unpack it to a temporary folder, import the file set from there, and " +
               "delete the temporary folder again. Nothing is added to the folder you chose, and the " +
               "archive itself is not modified.";
    }

    private void OnDeclineClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnAcceptClick(object? sender, RoutedEventArgs e) => Close(true);
}
