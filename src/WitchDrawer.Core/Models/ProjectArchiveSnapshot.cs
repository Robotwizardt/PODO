namespace WitchDrawer.Core.Models;

/// <summary>
/// The project and its linked desktop content as it enters or leaves the archive.
/// Links are intentionally retained so restore returns the original workspace.
/// </summary>
public sealed record ProjectArchiveSnapshot(
    Box ProjectBox,
    IReadOnlyList<ProjectBoxLink> LinkedBoxes,
    IReadOnlyList<ProjectPaperLink> LinkedPapers);
