using System;
using Avalonia.Controls;
using CircuitRF.Ui.Markdown;

namespace CircuitRF.Ui.Views.Content;

/// <summary>
/// The view for <see cref="MarkdownDocument"/>. It renders and does nothing else — no parse (that is
/// <see cref="SmallMarkdown"/>, which the document already ran), no file access, and no editing of
/// any kind.
/// </summary>
public partial class MarkdownDocumentView : UserControl
{
    public MarkdownDocumentView() => InitializeComponent();

    /// <summary>
    /// Built on DataContext rather than in the constructor: Dock hands a document view its document
    /// after construction, and it re-uses a view when a tab is re-activated, so a one-shot build in
    /// the constructor renders an empty page for every tab after the first.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        BodyText.Inlines?.Clear();
        if (DataContext is MarkdownDocument doc)
            MarkdownInlines.Append(BodyText.Inlines, doc.Lines, MarkdownInlines.DocumentBodyFontSize);
    }
}
