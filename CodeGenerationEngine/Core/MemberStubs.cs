using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SemanticModel = Microsoft.CodeAnalysis.SemanticModel;

namespace CodeGenerationEngine.Core;

/// <summary>One member a type lacks: <see cref="Code"/> is one line with no indentation; <see cref="From"/> is the interface or abstract base the member comes from.</summary>
public sealed record MemberStub(string Code, string MemberName, string From);

/// <summary>
/// Writes the stubs for the members a type lacks: an override for each abstract member of an abstract
/// base, and an implementation (an abstract declaration, for an abstract type) for each interface member.
/// </summary>
public static class MemberStubs
{
    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.MinimallyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>The stubs <paramref name="type"/> lacks, named as they read at <paramref name="position"/> in <paramref name="model"/>.</summary>
    public static IReadOnlyList<MemberStub> Missing(INamedTypeSymbol type, SemanticModel model, int position)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<MemberStub>();

        // A class that is not abstract must override every abstract member of its abstract bases.
        if (!type.IsAbstract)
        {
            for (var baseType = type.BaseType; baseType is { IsAbstract: true }; baseType = baseType.BaseType)
                foreach (var member in baseType.GetMembers())
                {
                    if (member.IsStatic || !member.IsAbstract
                        || IsOverridden(type, member)
                        || keys.Contains(Key(member))
                        || Stub(member, model, position, StubKind.Override) is not { } stub)
                        continue;

                    result.Add(new MemberStub(stub, member.Name, baseType.Name));
                    keys.Add(Key(member));
                }
        }

        var interfaceKind = type.IsAbstract ? StubKind.Abstract : StubKind.Implement;
        foreach (var iface in type.AllInterfaces)
            foreach (var member in iface.GetMembers())
            {
                if (member.IsStatic || !member.IsAbstract
                    || type.FindImplementationForInterfaceMember(member) is not null
                    || keys.Contains(Key(member))
                    || Stub(member, model, position, interfaceKind) is not { } stub)
                    continue;

                result.Add(new MemberStub(stub, member.Name, iface.Name));
                keys.Add(Key(member));
            }

        return result;
    }

    // A method is told apart by its parameter types, so overloads each get a stub.
    private static string Key(ISymbol member) => member is IMethodSymbol m
        ? m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.RefKind + p.Type.ToDisplayString())) + ")"
        : member.Name;

    private enum StubKind { Implement, Abstract, Override }

    // Whether a class already overrides an abstract member with a body, anywhere up to the member's owner.
    private static bool IsOverridden(INamedTypeSymbol type, ISymbol member)
    {
        for (var t = type; t is not null && !SymbolEqualityComparer.Default.Equals(t, member.ContainingType); t = t.BaseType)
            foreach (var m in t.GetMembers(member.Name))
            {
                if (m.IsAbstract || !m.IsOverride)
                    continue;
                var overridden = m switch
                {
                    IMethodSymbol ms => (ISymbol?)ms.OverriddenMethod,
                    IPropertySymbol ps => ps.OverriddenProperty,
                    IEventSymbol es => es.OverriddenEvent,
                    _ => null
                };
                if (overridden is not null
                    && SymbolEqualityComparer.Default.Equals(overridden.OriginalDefinition, member.OriginalDefinition))
                    return true;
            }

        return false;
    }

    private static string Access(Accessibility a) => a switch
    {
        Accessibility.Protected => "protected",
        Accessibility.Internal => "internal",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "public"
    };

    // A public member that implements the interface's one implicitly (or an abstract one, or an
    // override of a base class's abstract member). Null for what has no plain stub (an indexer,
    // a by-ref return), which the compile then reports.
    private static string? Stub(ISymbol member, SemanticModel model, int position, StubKind kind = StubKind.Implement)
    {
        string Type(ITypeSymbol t) => t.ToMinimalDisplayString(model, position, TypeFormat);
        static string Name(string n) => SyntaxFacts.GetKeywordKind(n) != SyntaxKind.None ? "@" + n : n;
        var access = kind == StubKind.Override ? Access(member.DeclaredAccessibility) : "public";
        var modifier = kind switch { StubKind.Abstract => "abstract ", StubKind.Override => "override ", _ => "" };
        var body = kind == StubKind.Abstract ? ";" : " => throw new System.NotImplementedException();";

        switch (member)
        {
            case IPropertySymbol { IsIndexer: false, ReturnsByRef: false, ReturnsByRefReadonly: false } p:
                var accessors = kind == StubKind.Override
                    ? p.IsWriteOnly ? "{ set => throw new System.NotImplementedException(); }"
                        : p.IsReadOnly ? "{ get; }" : "{ get; set; }"
                    : "{ get; set; }";
                return $"{access} {modifier}{Type(p.Type)} {Name(p.Name)} {accessors}";

            case IMethodSymbol { MethodKind: MethodKind.Ordinary, ReturnsByRef: false, ReturnsByRefReadonly: false } m:
                var generics = m.TypeParameters.Length == 0 ? "" : $"<{string.Join(", ", m.TypeParameters.Select(t => t.Name))}>";
                var parameters = string.Join(", ", m.Parameters.Select(p =>
                    (p.IsParams ? "params " : "")
                    + (p.RefKind switch { RefKind.Ref => "ref ", RefKind.Out => "out ", RefKind.In => "in ", _ => "" })
                    + $"{Type(p.Type)} {Name(p.Name)}"));
                var returns = m.ReturnsVoid ? "void" : Type(m.ReturnType);
                return $"{access} {modifier}{returns} {Name(m.Name)}{generics}({parameters}){body}";

            case IEventSymbol e:
                return $"{access} {modifier}event {Type(e.Type)}? {Name(e.Name)};";

            default:
                return null;
        }
    }
}
