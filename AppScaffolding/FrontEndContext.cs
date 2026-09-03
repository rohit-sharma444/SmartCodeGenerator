using AppScaffolding.Screens;

namespace AppScaffolding;

/// <summary>
/// Everything a target needs to emit its front end, once names alone are not enough.
/// </summary>
/// <remarks>
/// <see cref="ITargetScaffolder.FrontEndFiles(string, IReadOnlyList{string}, AppCustomization)"/>
/// is handed entity <i>names</i>, which cannot say whether an entity is a master, a transaction or
/// the lines of one. Rather than widen that signature for the fourth time, the arguments become a
/// record — the same move <see cref="ScaffoldRequest"/> made when its own overload chain reached
/// four.
/// <para>
/// <see cref="Roles"/> is carried rather than recomputed by each target: classifying twice could
/// disagree with the summary the build has already printed.
/// </para>
/// </remarks>
/// <param name="Classes">The mapped classes, for the front end that has to know which properties
/// are required. Null for a caller that has only names.</param>
public sealed record FrontEndContext(
    string ProjectName,
    IReadOnlyList<string> EntityNames,
    AppCustomization Customization,
    EntityRoleMap Roles,
    IReadOnlyList<CodeGenerationEngine.Models.ClassDefinition>? Classes = null);
