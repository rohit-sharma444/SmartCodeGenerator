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
                .ExtractProperties(@class.File)
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
    }
}
