namespace AppScaffolding;

/// <summary>A single file a target contributes: its folder relative to the project
/// root (<c>""</c> for the root itself), file name, and source text.</summary>
public sealed record GeneratedFile(string RelativeFolder, string Name, string Source);
