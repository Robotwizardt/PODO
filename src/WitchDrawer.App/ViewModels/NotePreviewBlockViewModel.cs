namespace WitchDrawer.App.ViewModels;

public enum NotePreviewBlockKind
{
    Paragraph,
    Heading,
    Bullet,
    Ordered,
    Quote,
    Code,
    Divider,
    Blank
}

public sealed record NotePreviewBlockViewModel(
    string Text,
    NotePreviewBlockKind Kind,
    string Prefix = "")
{
    public bool IsHeading => Kind == NotePreviewBlockKind.Heading;

    public bool IsCode => Kind == NotePreviewBlockKind.Code;

    public bool IsQuote => Kind == NotePreviewBlockKind.Quote;

    public bool IsDivider => Kind == NotePreviewBlockKind.Divider;

    public bool IsBlank => Kind == NotePreviewBlockKind.Blank;

    public bool IsText => Kind is NotePreviewBlockKind.Paragraph
        or NotePreviewBlockKind.Heading
        or NotePreviewBlockKind.Bullet
        or NotePreviewBlockKind.Ordered
        or NotePreviewBlockKind.Blank;
}
