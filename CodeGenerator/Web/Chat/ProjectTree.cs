using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Web.Chat;

/// <summary>A class in the tree, and where its source lives.</summary>
/// <remarks>
/// The file is carried rather than looked up again when the node is expanded: the project it was
/// found under is known at that point, and searching the whole workspace a second time for a name
/// that may exist in two projects can land on the other one.
/// </remarks>
public sealed record TreeClass(string Name, string File);

/// <summary>One member of a class. <paramref name="IsMethod"/> picks the glyph.</summary>
public sealed record TreeMember(string Name, string Type, bool IsMethod);

/// <summary>
/// The kinds of node the tree draws, which is the set of glyphs it needs.
/// </summary>
/// <remarks>
/// A folder appears twice because an open one is drawn differently, the way Solution Explorer
/// draws it: the twisty says the same thing, and a reader glancing down a deep tree reads the
/// folders before the arrows.
/// </remarks>
public enum TreeIconKind
{
    Solution,
    Project,
    Folder,
    FolderOpen,
    Class,
    Property,
    Method,
}

/// <summary>A folder in the tree: what it holds, and where on disk it is.</summary>
/// <remarks>
/// <paramref name="Path"/> is the absolute folder, and it is what the view keys a folder's
/// open state on. Two projects in one solution routinely both have a <c>Core</c>, and keying on
/// the name — or on the path relative to the project — would open and close them together.
/// </remarks>
public sealed record TreeFolder(
    string Name,
    string Path,
    IReadOnlyList<TreeFolder> Folders,
    IReadOnlyList<TreeClass> Classes);

/// <summary>
/// What the tree is rooted in: the name to show, and whether a solution file was behind it.
/// </summary>
/// <remarks>
/// The distinction is the reader's, not the tool's. Every command works the same on a folder that
/// was never a solution — but a root node that called it a solution anyway would be the sidebar
/// inventing a file nobody would find.
/// </remarks>
public sealed record TreeRoot(string Name, bool IsSolution);

/// <summary>
/// The projects, classes and members the chat page's sidebar draws.
/// </summary>
/// <remarks>
/// Every level comes from the code the console commands print from — <see cref="ProjectManager"/>
/// for projects and classes, <see cref="EntityExtractor"/> and <see cref="ClassMethodLister"/> for
/// members. A sidebar with an enumeration of its own would eventually disagree with
/// <c>list classes</c>, and the reader would have no way to tell which one was lying.
/// <para>
/// Nothing here is eager. There is no caching anywhere beneath this — every call is a fresh
/// recursive directory walk — so the tree reads a project only when someone opens it, and a class
/// only when someone opens that. What has been read is cached here and thrown away whenever a
/// command has run, because a command is the only thing that changes what is on disk.
/// </para>
/// </remarks>
public sealed class ProjectTree
{
    private readonly Dictionary<string, IReadOnlyList<TreeClass>> _classes = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, IReadOnlyList<TreeMember>> _members = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, TreeFolder> _folders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The registered projects, newest registry state, cheapest level.</summary>
    public static IReadOnlyList<ProjectMetaData> Projects() => ProjectManager.GetProjects();

    /// <summary>Whether this project is the one the next command will act on.</summary>
    public static bool IsCurrent(ProjectMetaData project)
        => ProjectManager.CurrentProject is { } current
           && string.Equals(current.ProjectName, project.ProjectName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The classes of one project, read once and remembered until a command runs.
    /// </summary>
    public IReadOnlyList<TreeClass> ClassesOf(ProjectMetaData project)
    {
        var folder = ProjectManager.ProjectFolderOf(project);

        if (_classes.TryGetValue(folder, out var cached))
            return cached;

        var found = ProjectManager
            .ClassesOf(project)
            .Select(name => new TreeClass(name, ClassMethodLister.FileOf(folder, name) ?? string.Empty))
            .ToList();

        _classes[folder] = found;

        return found;
    }

    /// <summary>
    /// What to call the node everything hangs under: the solution that was loaded, or the folder.
    /// </summary>
    /// <remarks>
    /// Told, not discovered. This looked for a solution file in the root and in the folders just
    /// below it once — and on a repository holding four of them it named whichever sorted first,
    /// which was a project's own solution rather than the one that had been opened. The workspace
    /// already knows what it loaded, so there is nothing here to read from disk and nothing to
    /// cache.
    /// </remarks>
    public static TreeRoot RootOf(string root, string? solution)
        => string.IsNullOrWhiteSpace(solution)
            ? new TreeRoot(NameOf(root), IsSolution: false)
            : new TreeRoot(Path.GetFileNameWithoutExtension(solution), IsSolution: true);

    /// <summary>The last segment of a path, with or without a separator on the end of it.</summary>
    private static string NameOf(string path)
        => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>
    /// One project as Solution Explorer draws it: its folders, then the classes at its root.
    /// </summary>
    /// <remarks>
    /// Nothing is read that <see cref="ClassesOf"/> has not read already. Every class carries the
    /// file it was found in, so the folder it belongs to is that path relative to the project —
    /// which is why a nested tree cannot show a class the flat list would not.
    /// </remarks>
    public TreeFolder FolderOf(ProjectMetaData project)
    {
        var folder = ProjectManager.ProjectFolderOf(project);

        if (_folders.TryGetValue(folder, out var cached))
            return cached;

        var nested = Nest(folder, ClassesOf(project));

        _folders[folder] = nested;

        return nested;
    }

    /// <summary>
    /// Classes grouped into the folders their files sit in, under <paramref name="root"/>.
    /// </summary>
    /// <remarks>
    /// Folders come out in name order and classes in the order they arrived, which is the order
    /// <c>list classes</c> prints them in. A class whose file was never found, or whose file lies
    /// outside the root, sits at the root: there is no folder to put it in, and dropping it would
    /// make the tree shorter than the command's own listing.
    /// </remarks>
    public static TreeFolder Nest(string root, IReadOnlyList<TreeClass> classes)
    {
        var top = new Level(NameOf(root), root);

        foreach (var @class in classes)
            Place(top, root, @class);

        return top.Freeze();
    }

    /// <summary>Puts one class in the level its file belongs to, creating levels on the way down.</summary>
    private static void Place(Level top, string root, TreeClass @class)
    {
        var segments = SegmentsOf(root, @class.File);
        var level = top;

        foreach (var segment in segments)
        {
            if (!level.Folders.TryGetValue(segment, out var next))
            {
                next = new Level(segment, Path.Combine(level.Path, segment));
                level.Folders[segment] = next;
            }

            level = next;
        }

        level.Classes.Add(@class);
    }

    /// <summary>
    /// The folders between <paramref name="root"/> and <paramref name="file"/>, the file itself
    /// excluded. Empty when the file is unknown, at the root, or not under it at all.
    /// </summary>
    private static string[] SegmentsOf(string root, string file)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(file))
            return [];

        var relative = Path.GetRelativePath(root, file);

        // Rooted or climbing out: GetRelativePath returns the path unchanged when the two share
        // no prefix, and a `..` when the file is above the root. Neither has a folder in this tree.
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
            return [];

        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Length <= 1 ? [] : segments[..^1];
    }

    /// <summary>One folder while it is still being filled. <see cref="Freeze"/> ends that.</summary>
    /// <remarks>
    /// A <see cref="SortedDictionary{TKey, TValue}"/> so the folders come out in name order without
    /// a sort afterwards, and case-insensitively because Windows paths are.
    /// </remarks>
    private sealed class Level(string name, string path)
    {
        public string Name { get; } = name;

        public string Path { get; } = path;

        public SortedDictionary<string, Level> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<TreeClass> Classes { get; } = [];

        public TreeFolder Freeze()
            => new(Name, Path, [.. Folders.Values.Select(f => f.Freeze())], Classes);
    }

    /// <summary>
    /// The members of one class: properties first, then methods, each in the order the parser
    /// found them.
    /// </summary>
    /// <remarks>
    /// Properties before methods, and not sorted: a class file reads top to bottom, and a member
    /// list that reorders it is harder to check against the source it claims to describe.
    /// </remarks>
    public IReadOnlyList<TreeMember> MembersOf(TreeClass @class)
    {
        if (string.IsNullOrWhiteSpace(@class.File))
            return [];

        if (_members.TryGetValue(@class.File, out var cached))
            return cached;

        List<TreeMember> found =
        [
            .. EntityExtractor
                .ExtractProperties(@class.File, @class.Name)
                .Select(p => new TreeMember(p.Name, p.Type, IsMethod: false)),

            .. ClassMethodLister
                .MethodsIn(@class.File)
                .Select(m => new TreeMember(m.Name, m.ReturnType, IsMethod: true)),
        ];

        _members[@class.File] = found;

        return found;
    }

    /// <summary>
    /// Forgets everything read so far.
    /// </summary>
    /// <remarks>
    /// Called when a turn has been added, which is the only way the files under a project change
    /// while the page is open. Clearing the whole cache rather than the one project a command
    /// touched is deliberate: <c>build app</c> aside, plenty of commands write outside the project
    /// they name, and a tree that is stale in one corner is worse than one that re-reads a folder.
    /// </remarks>
    public void Forget()
    {
        _classes.Clear();
        _members.Clear();
        _folders.Clear();
    }
}
