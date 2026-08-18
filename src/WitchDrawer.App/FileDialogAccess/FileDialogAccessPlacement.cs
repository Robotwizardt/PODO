namespace WitchDrawer.App.FileDialogAccess;

internal readonly record struct FileDialogScreenRect(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);
}

internal enum FileDialogAccessSide
{
    Right,
    Left,
    Inner
}

internal readonly record struct FileDialogAccessPlacementResult(
    FileDialogAccessSide Side,
    FileDialogScreenRect Bounds);

internal static class FileDialogAccessPlacement
{
    public static FileDialogAccessPlacementResult Calculate(
        FileDialogScreenRect dialogBounds,
        FileDialogScreenRect workArea,
        int preferredWidth,
        int reservedFooterHeight)
    {
        var width = Math.Clamp(preferredWidth, 1, Math.Max(1, workArea.Width));
        var top = Math.Max(dialogBounds.Top, workArea.Top);
        var bottom = Math.Min(dialogBounds.Bottom, workArea.Bottom);

        if (workArea.Right - dialogBounds.Right >= width)
        {
            return new FileDialogAccessPlacementResult(
                FileDialogAccessSide.Right,
                new FileDialogScreenRect(dialogBounds.Right, top, dialogBounds.Right + width, bottom));
        }

        if (dialogBounds.Left - workArea.Left >= width)
        {
            return new FileDialogAccessPlacementResult(
                FileDialogAccessSide.Left,
                new FileDialogScreenRect(dialogBounds.Left - width, top, dialogBounds.Left, bottom));
        }

        var innerRight = Math.Min(dialogBounds.Right, workArea.Right);
        var innerLeft = Math.Max(workArea.Left, innerRight - Math.Min(width, dialogBounds.Width));
        var innerBottom = Math.Max(top, bottom - Math.Max(0, reservedFooterHeight));
        return new FileDialogAccessPlacementResult(
            FileDialogAccessSide.Inner,
            new FileDialogScreenRect(innerLeft, top, innerRight, innerBottom));
    }
}
