using System.Text.RegularExpressions;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;
using CodeIntelligence.Abstractions;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

/// <summary>
/// The single place user text becomes a <see cref="ParsedCommand"/>. Order:
/// literal meta-commands (exact table) → residual pattern guards that the
/// literal table can't express → the NLE-driven intent mapper → prose field
/// extraction. CodeGenerationEngine receives the result and never re-parses text.
/// Stateless.
/// </summary>
public static class CommandFront
{
    // Plan 1 found these two guards un-absorbable by LiteralCommandTable:
    // "classes in <project>" (NLE drops the trailing "in"), and the bare
    // \bvalidator\b substring. Both read the raw sentence here, ahead of the mapper.
    private static readonly Regex ClassesInProject =
        new(@"^(?:list|show|display)\s+classes\s+in\s+(?<project>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Validator =
        new(@"\bvalidator\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "add validation <args>" and "show/list/view validation <args>" — the
    // argument-bearing forms LiteralCommandTable's exact match can't cover, and
    // the mapper has no AddValidation/ShowValidation entries for. Without these,
    // e.g. "add validation Email should not be blank on Email in Customer" fell
    // through to Unknown.
    private static readonly Regex AddValidationRe =
        new(@"\badd\s+validation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ShowValidationRe =
        new(@"\b(?:show|list|view)\s+validation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AddBusinessValidationRe =
        new(@"\badd\s+business\s+validation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ShowBusinessValidationRe =
        new(@"\b(?:show|list|view)\s+business\s+validation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "use project <name>" and its synonyms. The old ProjectManager.UseProject
    // sliced the sentence itself; the bridge now captures the name so the handler
    // reads a field. Kept ahead of the mapper because the mapper's UseProject rule
    // only covers the bare "use project" form (no name).
    private static readonly Regex UseProjectRe =
        new(@"^(?:use|select|switch|open|activate)\s+project\s+(?<project>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `Order inherits AuditableBase` on an EXISTING class. Anchored whole-sentence and
    // it cannot begin with create/generate, so `create a Customer class that inherits X`
    // still routes to CreateClass, which sets a base type at generation time.
    // Fields come from these groups, not from CommandEntityExtractor: Normalize maps
    // entity/model/object -> class, which would rewrite a base type named `Entity`.
    private static readonly Regex ClassInheritRe =
        new(@"^(?:class\s+)?(?<class>\w+)(?:\s+class)?\s+(?:inherits?|extends|derives\s+from)\s+(?<base>\w+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `create class <name>` and `create class <name> with ...`, ahead of the mapper. NLE tags a
    // lower-case name as part of the target, so the mapper looked up "class employee" and
    // "create class employee" was not recognised at all, with or without properties. The name
    // comes from this group, PascalCased: Normalize rewrites manager -> service and
    // model/entity -> class, which would rename a class the user typed in lower case.
    // "class library" is a project, and a connective is not a name ("create class called X"
    // and "create class with ..." are left to the mapper as before).
    private static readonly Regex CreateClassRe =
        new(@"^(?:create|add|generate)\s+(?:(?:a|an|new)\s+)?class\s+"
            + @"(?!(?:library|with|called|named|that|which|for|to|in|of|from)\b)(?<class>[A-Za-z_]\w*)"
            + @"(?:\s+with\s+.+)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `create [abstract|sealed|static] class|record|struct <rest>`: the kind words are read here and
    // the rest is parsed as `create class <rest>`, so every create-class phrasing works for them.
    private static readonly Regex TypeKindRe =
        new(@"^(?<verb>create|add|generate)\s+(?:(?:a|an|new)\s+)?(?<mods>(?:(?:abstract|sealed|static)\s+)*)"
            + @"(?<kind>class|record|struct)\s+(?<rest>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `create enum <name> [with values|with members|with|having <a, b and c>]`, ahead of the mapper,
    // which has no enum. An `inherits|implements|extends` tail is kept so the handler can refuse it.
    private static readonly Regex CreateEnumRe =
        new(@"^(?:create|add|generate)\s+(?:(?:a|an|new)\s+)?enum\s+(?<name>[A-Za-z_]\w*)"
            + @"(?:\s+(?<bad>inherits|implements|extends)\b.*"
            + @"|\s+(?:with\s+(?:values|members)|with|having)\s+(?<values>.+?))?\s*[.!]?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ApplyKind(ParsedCommand cmd, Match match)
    {
        var kind = match.Groups["kind"].Value.ToLowerInvariant();
        cmd.Kind = kind switch
        {
            "record" => DeclaredKind.Record,
            "struct" => DeclaredKind.Struct,
            _ => DeclaredKind.Class
        };

        var words = match.Groups["mods"].Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (words.Count > 1)
        {
            cmd.Metadata[ParsedCommand.KindErrorKey] =
                $"A {kind} cannot be both {words[0]} and {words[1]}.";
            return;
        }

        if (words.Count == 1)
        {
            cmd.Modifier = words[0] switch
            {
                "abstract" => TypeModifier.Abstract,
                "sealed" => TypeModifier.Sealed,
                _ => TypeModifier.Static
            };

            var refused = cmd.Modifier == TypeModifier.Static && cmd.Kind != DeclaredKind.Class
                || cmd.Modifier != TypeModifier.None && cmd.Kind == DeclaredKind.Struct;
            if (refused)
                cmd.Metadata[ParsedCommand.KindErrorKey] = $"A {kind} cannot be {words[0]}.";
        }
    }

    // `create [<type>] project [<name>]`, `create class library [<name>]`, `create worker service
    // [<name>]`, ahead of the mapper, which did not recognise a lower-case name ("create console
    // project shop" was "Command not recognized"). The name, type, folder and framework are read by
    // ExtractProjectInfo from the sentence as typed.
    private static readonly Regex CreateProjectRe =
        new(@"^(?:create|new|make|generate)\s+(?:(?:a|an|new)\s+)?"
            + @"(?:(?:console|webapi|web\s+api|api|blazor|blazorserver|blazorwasm|wasm|wpf|winforms|mvc|worker)\s+)?"
            + @"(?:project|class\s+library|classlib|worker\s+service)(?:\s+.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `create interface <name>`, ahead of the mapper, for the create class reason: a lower-case
    // name is tagged as part of the target, so "create interface shape" was not recognised.
    private static readonly Regex CreateInterfaceRe =
        new(@"^(?:create|generate|build)\s+(?:(?:a|an|new)\s+)?interface\s+"
            + @"(?!(?:with|called|named|that|which|for|to|in|of|from)\b)(?<name>[A-Za-z_]\w*)"
            + @"(?:\s+with\s+.+)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `list|show properties|methods [of|in|for] [class] <Class>`. The mapper took the third word as
    // the class - "list methods of Employee" looked for a class called of - and found none at all
    // in the documented "show methods Customer"; a lower-case name was not recognised.
    private static readonly Regex MemberListRe =
        new(@"^(?<verb>list|show|display|view)\s+(?<what>properties|methods)\s+(?:(?:of|in|for|on)\s+)?(?:(?:the\s+)?class\s+)?"
            + @"(?<class>[A-Za-z_]\w*)\s*[.!?]?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `show|analyze|delete|restore class <Class>`, ahead of the mapper, which did not recognise a
    // lower-case name ("show class customer" was "Command not recognized"). The verbs are the
    // mapper's own for these intents.
    private static readonly Regex ClassVerbRe =
        new(@"^(?<verb>show|view|display|open|analy[sz]e|delete|remove|restore|recover)\s+(?:the\s+)?class\s+"
            + @"(?<class>[A-Za-z_]\w*)\s*[.!?]?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `rename class <old> to|as <new>`, for the same reason; "as" was "A new class name is required".
    private static readonly Regex RenameClassRe =
        new(@"^rename\s+(?:the\s+)?class\s+(?<old>[A-Za-z_]\w*)\s+(?:to|as|into)\s+(?<new>[A-Za-z_]\w*)\s*[.!]?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `add method <name> [returns <type>] to <Class>`, for the same reason: "add method save to
    // Circle" was not recognised.
    // Parameters in the C# form, `Ship(string address)`, or the English form,
    // `Ship with parameters address string`, before ` to <Class>`.
    private static readonly Regex AddMethodRe =
        new(@"^(?:add|create|generate)\s+(?:(?:a|an|new)\s+)?method\s+(?<method>[A-Za-z_]\w*)"
            + @"(?:\s*\((?<cparams>[^)]*)\))?"
            + $@"(?:\s+returns?\s+(?<returns>{MethodParameterParser.TypePattern}))?"
            // "returns" can follow the English list too: "Ship with parameters address string
            // returns bool to Order" read "string returns bool" as the type of address.
            + $@"(?:\s+{MethodParameterParser.EnglishLead}\s+(?<eparams>.+?)"
            + $@"(?:\s+returns?\s+(?<returns>{MethodParameterParser.TypePattern}))?)?"
            + @"\s+(?:to|for|in|on)\s+(?:class\s+(?=\S))?(?<class>[A-Za-z_]\w*)\s*[.!]?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The start of `add property ...`; the rest is CommandEntityExtractor.AddPropertyRe, which
    // reads the fields.
    private static readonly Regex AddPropertyStartRe =
        new(@"^(?:add|create)\s+(?:(?:a|an|new)\s+)?(?:property|field)\s",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `rename property <old> to <new> in <Class>`. The mapper's Table is keyed on
    // (verb, whole remainder), so it cannot match an argument-bearing form; and
    // Normalize maps field -> property, so the fields come from these groups.
    private static readonly Regex RenamePropertyRe =
        new(@"^rename\s+(?:property|field)\s+(?<old>\w+)\s+to\s+(?<new>\w+)(?:\s+(?:in|on|of)\s+(?<class>\w+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `remove method <name> from <Class>` / `delete method <name> in <Class>`. Ahead of the
    // mapper, whose (delete|remove) + property rule would otherwise not see a method at all.
    private static readonly Regex DeleteMethodRe =
        new(@"^(?:remove|delete)\s+method\s+(?<method>\w+)(?:\s+(?:from|in|on|of)\s+(?<class>\w+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `remove property <name> from <Class>`. Ahead of the mapper, which recognised it only with a
    // capitalised name: "remove property price from item" was not recognised at all.
    private static readonly Regex DeletePropertyRe =
        new(@"^(?:remove|delete|drop)\s+(?:the\s+)?(?:property\s+(?<property>\w+)|(?<property>\w+)\s+property)"
            + @"\s+(?:from|in|on|of)\s+(?:the\s+)?(?<class>\w+)(?:\s+class)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `rename method <old> to <new> in <Class>`, shaped like RenamePropertyRe.
    private static readonly Regex RenameMethodRe =
        new(@"^rename\s+method\s+(?<old>\w+)\s+to\s+(?<new>\w+)(?:\s+(?:in|on|of)\s+(?<class>\w+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `change (the) type of (property|field) <name> to <type> in <Class>` and
    // `retype (property|field) <name> to <type> in <Class>`. The type is any run of
    // non-spaces so `List<string>` and `decimal?` arrive whole and a malformed one reaches
    // the handler to be refused by name, rather than falling through to the mapper; inside
    // angle brackets (one level of nesting) spaces are allowed too, because that is how
    // `Dictionary<string, int>` is written.
    // `retype <class> <field> <type>` without "property" is the build-app review loop's own.
    private static readonly Regex ChangePropertyTypeRe =
        new(@"^(?:change\s+(?:the\s+)?type\s+of|retype)\s+(?:(?:property|field)\s+)?(?<property>\w+)\s+to\s+(?<type>(?:[^\s<]|<[^<>]*(?:<[^<>]*>[^<>]*)*>|<)+)(?:\s+(?:in|on|of)\s+(?<class>\w+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // `remove (business) validation` naming a field — `from Employee.Name` or, as add
    // validation says it, `on Name in Employee` — or a number `show validation` printed:
    // `2 from Employee`. A bare `from Employee` is matched too, so the handler can say
    // which of those to type instead of the mapper guessing at it.
    private static readonly Regex RemoveValidationRe =
        new(@"^(?:remove|delete)\s+(?<business>business\s+)?validation(?:\s+rules?)?\s+"
            + @"(?:(?<number>\d+)\s+(?:from|in|on|of)\s+(?<class>\w+)"
            + @"|from\s+(?<class>\w+)\.(?<property>\w+)"
            + @"|(?:on|from)\s+(?<property>\w+)\s+(?:in|on|of)\s+(?<class>\w+)"
            + @"|(?:from|in|on|of)\s+(?<class>\w+))$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The intents the regex guards in <see cref="Parse"/> can produce.
    /// Hand-maintained: the guards are individual patterns, not a table.
    /// ADD TO THIS LIST WHEN YOU ADD A GUARD — the reachable-intent gate test
    /// reads it to check the intent has a handler behind it.</summary>
    public static IReadOnlyCollection<IntentType> GuardedIntents { get; } =
    [
        IntentType.ListClassesInProject,
        IntentType.UseProject,
        IntentType.AddBusinessValidation,
        IntentType.ShowBusinessValidation,
        IntentType.AddValidation,
        IntentType.ShowValidation,
        IntentType.GenerateValidator,
        IntentType.ClassInherit,
        IntentType.CreateProject,
        IntentType.CreateClass,
        IntentType.CreateInterface,
        IntentType.CreateEnum,
        IntentType.CreateProperty,
        IntentType.ListProperties,
        IntentType.ListMethods,
        IntentType.ShowClass,
        IntentType.DeleteClass,
        IntentType.RestoreClass,
        IntentType.AnalyzeClass,
        IntentType.RenameClass,
        IntentType.CreateMethod,
        IntentType.RenameProperty,
        IntentType.DeleteMethod,
        IntentType.RenameMethod,
        IntentType.ChangePropertyType,
        IntentType.RemoveValidation,
        IntentType.RemoveBusinessValidation
    ];

    public static ParsedCommand Parse(string input, string workspace)
    {
        // Correct near-miss spellings of the distinctive validation keywords up
        // front (e.g. "busniess" -> "business") so the guards below and the
        // extractor both see canonical text. Only those keywords are touched;
        // entity names and rule text pass through unchanged.
        var text = KeywordCorrector.Correct((input ?? string.Empty).Trim());

        if (LiteralCommandTable.TryResolve(text, out var literalIntent))
            return new ParsedCommand { Intent = literalIntent, OriginalSentence = input ?? string.Empty, Confidence = 1.0 };

        var classesIn = ClassesInProject.Match(text);
        if (classesIn.Success)
            return new ParsedCommand
            {
                Intent = IntentType.ListClassesInProject,
                ProjectName = classesIn.Groups["project"].Value.Trim(),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

        var useMatch = UseProjectRe.Match(text);
        if (useMatch.Success)
            return new ParsedCommand
            {
                Intent = IntentType.UseProject,
                ProjectName = useMatch.Groups["project"].Value.Trim(),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

        var inheritMatch = ClassInheritRe.Match(text);
        if (inheritMatch.Success)
            return new ParsedCommand
            {
                Intent = IntentType.ClassInherit,
                ClassName = inheritMatch.Groups["class"].Value.Trim(),
                BaseClass = inheritMatch.Groups["base"].Value.Trim(),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

        if (CreateProjectRe.IsMatch(text))
        {
            var projectCmd = CommandEntityExtractor.Extract(text, IntentType.CreateProject, workspace);
            projectCmd.OriginalSentence = input ?? string.Empty;
            projectCmd.Confidence = 1.0;
            return projectCmd;
        }

        var kindMatch = TypeKindRe.Match(text);
        if (kindMatch.Success
            && (kindMatch.Groups["mods"].Value.Length > 0
                || !kindMatch.Groups["kind"].Value.Equals("class", StringComparison.OrdinalIgnoreCase)))
        {
            var plain = Parse($"{kindMatch.Groups["verb"].Value} class {kindMatch.Groups["rest"].Value}", workspace);
            if (plain.Intent == IntentType.CreateClass)
            {
                ApplyKind(plain, kindMatch);
                plain.OriginalSentence = input ?? string.Empty;
                return plain;
            }
        }

        var createClassMatch = CreateClassRe.Match(text);
        if (createClassMatch.Success)
        {
            var createCmd = CommandEntityExtractor.Extract(text, IntentType.CreateClass, workspace);
            createCmd.ClassName = CommandEntityExtractor.ToPascalCase(createClassMatch.Groups["class"].Value);
            createCmd.OriginalSentence = input ?? string.Empty;
            createCmd.Confidence = 1.0;
            SessionContext.CurrentClass = createCmd.ClassName;
            return createCmd;
        }

        var createEnumMatch = CreateEnumRe.Match(text);
        if (createEnumMatch.Success)
        {
            // Fields from this guard's own groups, on the raw text (Normalize rewrites model/entity/object).
            var name = CommandEntityExtractor.ToPascalCase(createEnumMatch.Groups["name"].Value);
            var enumCmd = new ParsedCommand
            {
                Intent = IntentType.CreateEnum,
                Kind = DeclaredKind.Enum,
                ClassName = name,
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

            if (createEnumMatch.Groups["bad"].Success)
                enumCmd.Metadata[ParsedCommand.KindErrorKey] =
                    "An enum cannot inherit from a type or implement an interface.";
            else if (EnumValueParser.TryParse(createEnumMatch.Groups["values"].Value, out var enumValues, out var enumError))
                enumCmd.EnumValues = enumValues;
            else
                enumCmd.Metadata[ParsedCommand.KindErrorKey] = enumError!;

            SessionContext.CurrentClass = name;
            return enumCmd;
        }

        var createInterfaceMatch = CreateInterfaceRe.Match(text);
        if (createInterfaceMatch.Success)
        {
            var interfaceCmd = CommandEntityExtractor.Extract(text, IntentType.CreateInterface, workspace);

            // The one rule (InterfaceNaming): IShape kept, anything else PascalCased and prefixed.
            var name = InterfaceNaming.NameFor(createInterfaceMatch.Groups["name"].Value);

            interfaceCmd.InterfaceName = name;
            interfaceCmd.ClassName = name;
            interfaceCmd.OriginalSentence = input ?? string.Empty;
            interfaceCmd.Confidence = 1.0;
            SessionContext.CurrentClass = name;
            return interfaceCmd;
        }

        var memberListMatch = MemberListRe.Match(text);
        if (memberListMatch.Success)
            return ClassCommand(
                memberListMatch.Groups["what"].Value.Equals("methods", StringComparison.OrdinalIgnoreCase)
                    ? IntentType.ListMethods
                    : IntentType.ListProperties,
                memberListMatch.Groups["class"].Value, input, workspace);

        var classVerbMatch = ClassVerbRe.Match(text);
        if (classVerbMatch.Success)
        {
            var verb = classVerbMatch.Groups["verb"].Value.ToLowerInvariant();
            var classIntent = verb switch
            {
                "delete" or "remove" => IntentType.DeleteClass,
                "restore" or "recover" => IntentType.RestoreClass,
                "analyze" or "analyse" => IntentType.AnalyzeClass,
                _ => IntentType.ShowClass,
            };
            return ClassCommand(classIntent, classVerbMatch.Groups["class"].Value, input, workspace);
        }

        var renameClassMatch = RenameClassRe.Match(text);
        if (renameClassMatch.Success)
        {
            var renameClassCmd = ClassCommand(
                IntentType.RenameClass, renameClassMatch.Groups["old"].Value, input, workspace);
            // The new name as typed: the rename service refuses one that is no identifier.
            renameClassCmd.NewClassName = renameClassMatch.Groups["new"].Value;
            return renameClassCmd;
        }

        var addMethodMatch = AddMethodRe.Match(text);
        if (addMethodMatch.Success)
        {
            var addMethodCmd = ClassCommand(
                IntentType.CreateMethod, addMethodMatch.Groups["class"].Value, input, workspace);
            addMethodCmd.MethodName = CommandEntityExtractor.ToPascalCase(addMethodMatch.Groups["method"].Value);
            addMethodCmd.ReturnType = addMethodMatch.Groups["returns"].Success
                ? addMethodMatch.Groups["returns"].Value
                : "void";

            var (parameterText, order) = addMethodMatch.Groups["cparams"].Success
                ? (addMethodMatch.Groups["cparams"].Value, MethodParameterParser.Order.TypeFirst)
                : (addMethodMatch.Groups["eparams"].Value, MethodParameterParser.Order.NameFirst);

            // A list that cannot be read is carried to the handler, which refuses the method by
            // that reason: dropping the list would write a method the user did not ask for.
            if (MethodParameterParser.TryParse(parameterText, order, out var parameters, out var error))
                addMethodCmd.Parameters = parameters;
            else
                addMethodCmd.Metadata[ParsedCommand.ParameterErrorKey] = error!;

            return addMethodCmd;
        }

        // `add property <name> [<type>] to <Class>`, ahead of the mapper, which needs a capitalised
        // name to see a property: "add property note string to Circle" was not recognised, and a
        // name that is no identifier ("9x") never reached the handler to be refused by name.
        if (AddPropertyStartRe.IsMatch(text) && CommandEntityExtractor.AddPropertyRe.IsMatch(text))
        {
            var addPropertyCmd = CommandEntityExtractor.Extract(text, IntentType.CreateProperty, workspace);
            addPropertyCmd.OriginalSentence = input ?? string.Empty;
            addPropertyCmd.Confidence = 1.0;
            return addPropertyCmd;
        }

        var renamePropertyMatch = RenamePropertyRe.Match(text);
        if (renamePropertyMatch.Success)
        {
            var renameCmd = new ParsedCommand
            {
                Intent = IntentType.RenameProperty,
                PropertyName = renamePropertyMatch.Groups["old"].Value.Trim(),
                NewPropertyName = renamePropertyMatch.Groups["new"].Value.Trim(),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

            renameCmd.ClassName = renamePropertyMatch.Groups["class"].Success
                ? renamePropertyMatch.Groups["class"].Value.Trim()
                : SessionContext.CurrentClass ?? string.Empty;
            renameCmd.ClassFromSession = FromSession(renamePropertyMatch);

            return renameCmd;
        }

        var deleteMethodMatch = DeleteMethodRe.Match(text);
        if (deleteMethodMatch.Success)
            return new ParsedCommand
            {
                Intent = IntentType.DeleteMethod,
                MethodName = deleteMethodMatch.Groups["method"].Value.Trim(),
                ClassName = ClassOrCurrent(deleteMethodMatch),
                ClassFromSession = FromSession(deleteMethodMatch),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

        var deletePropertyMatch = DeletePropertyRe.Match(text);
        if (deletePropertyMatch.Success)
            return new ParsedCommand
            {
                Intent = IntentType.DeleteProperty,
                PropertyName = deletePropertyMatch.Groups["property"].Value.Trim(),
                ClassName = deletePropertyMatch.Groups["class"].Value.Trim(),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

        var renameMethodMatch = RenameMethodRe.Match(text);
        if (renameMethodMatch.Success)
            return new ParsedCommand
            {
                Intent = IntentType.RenameMethod,
                MethodName = renameMethodMatch.Groups["old"].Value.Trim(),
                NewMethodName = renameMethodMatch.Groups["new"].Value.Trim(),
                ClassName = ClassOrCurrent(renameMethodMatch),
                ClassFromSession = FromSession(renameMethodMatch),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

        var changeTypeMatch = ChangePropertyTypeRe.Match(text);
        if (changeTypeMatch.Success)
            return new ParsedCommand
            {
                Intent = IntentType.ChangePropertyType,
                PropertyName = changeTypeMatch.Groups["property"].Value.Trim(),
                PropertyType = changeTypeMatch.Groups["type"].Value.Trim(),
                ClassName = ClassOrCurrent(changeTypeMatch),
                ClassFromSession = FromSession(changeTypeMatch),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

        var removeValidationMatch = RemoveValidationRe.Match(text);
        if (removeValidationMatch.Success)
            return new ParsedCommand
            {
                Intent = removeValidationMatch.Groups["business"].Success
                    ? IntentType.RemoveBusinessValidation
                    : IntentType.RemoveValidation,
                ClassName = removeValidationMatch.Groups["class"].Value.Trim(),
                PropertyName = removeValidationMatch.Groups["property"].Value.Trim(),
                RuleNumber = removeValidationMatch.Groups["number"].Success
                    && int.TryParse(removeValidationMatch.Groups["number"].Value, out var number)
                        ? number
                        : null,
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

        if (AddBusinessValidationRe.IsMatch(text))
        {
            var cmd = CommandEntityExtractor.Extract(text, IntentType.AddBusinessValidation, workspace);
            cmd.Confidence = 1.0;
            return cmd;
        }

        if (ShowBusinessValidationRe.IsMatch(text))
        {
            var cmd = CommandEntityExtractor.Extract(text, IntentType.ShowBusinessValidation, workspace);
            cmd.Confidence = 1.0;
            return cmd;
        }

        if (AddValidationRe.IsMatch(text))
        {
            var addValidationCmd = CommandEntityExtractor.Extract(text, IntentType.AddValidation, workspace);
            addValidationCmd.Confidence = 1.0;
            return addValidationCmd;
        }

        if (ShowValidationRe.IsMatch(text))
        {
            var showValidationCmd = CommandEntityExtractor.Extract(text, IntentType.ShowValidation, workspace);
            showValidationCmd.Confidence = 1.0;
            return showValidationCmd;
        }

        if (Validator.IsMatch(text))
        {
            var validatorCmd = CommandEntityExtractor.Extract(text, IntentType.GenerateValidator, workspace);
            // The extractor's switch has no GenerateValidator case, so fill the
            // class name from the sentence here — exactly what HandleGenerateValidator
            // used to do with the raw input, now done in the bridge.
            CommandEntityExtractor.ExtractClassName(text, validatorCmd);
            validatorCmd.Confidence = 1.0;
            return validatorCmd;
        }

        SemanticResult? analysis = NlpActionProvider.Analyze(input ?? string.Empty);
        var intent = NlpCommandMapper.ResolveIntent(analysis);
        var command = CommandEntityExtractor.Extract(input ?? string.Empty, intent, workspace);
        command.Confidence = analysis?.Confidence ?? 0d;
        return command;
    }

    // A command on one class, named as typed in any case.
    private static ParsedCommand ClassCommand(IntentType intent, string typed, string? input, string workspace) =>
        new()
        {
            Intent = intent,
            ClassName = DeclaredClassName(workspace, typed),
            OriginalSentence = input ?? string.Empty,
            Confidence = 1.0
        };

    /// <summary>
    /// The class name as the workspace declares it - its file is named after it - so "orderline"
    /// finds OrderLine; else the typed name PascalCased, as create class makes it. A deleted class
    /// is not in the workspace, and restore class matches its name without regard to case.
    /// </summary>
    internal static string DeclaredClassName(string workspace, string typed)
    {
        try
        {
            if (ClassFiles.FileOf(workspace, typed) is { } file)
                return Path.GetFileNameWithoutExtension(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return CommandEntityExtractor.ToPascalCase(typed);
    }

    // A guard's optional `class` group, or the class the session is on.
    private static string ClassOrCurrent(Match match) =>
        match.Groups["class"].Success
            ? match.Groups["class"].Value.Trim()
            : SessionContext.CurrentClass ?? string.Empty;

    /// <summary>Whether <see cref="ClassOrCurrent"/> took the class from the session.</summary>
    private static bool FromSession(Match match) =>
        !match.Groups["class"].Success && !string.IsNullOrEmpty(SessionContext.CurrentClass);

    // "remove property Name from Item and add property Title string to Item": a second command
    // after "and", "then" or a comma. One line runs one command, so the line is refused whole.
    private static readonly Regex SecondCommandRe = new(
        @"(?:\s+(?:and|then)|\s*[,;])\s+(?:then\s+)?(?<second>(?:add|create|remove|delete|drop|rename|change|retype)\s+"
        + @"(?:(?:a|an|the|new)\s+)?(?:property|field|method|class|interface|project|validation)\b.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CreatesAType = new(
        @"\bcreate\s+(?:(?:a|an|new)\s+)?(?:(?:abstract|sealed|static)\s+)*(?:class|record|struct|enum|interface)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MemberAddition = new(
        @"^(?:add|create)\s+(?:(?:a|an|new)\s+)?(?:property|field|method)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The two commands in <paramref name="text"/>, when it holds two; a build app description is
    /// prose, and is never split.
    /// </summary>
    internal static (string First, string Second)? TwoCommands(string text)
    {
        if (text.StartsWith("build app", StringComparison.OrdinalIgnoreCase))
            return null;

        var match = SecondCommandRe.Match(text);
        if (!match.Success || match.Index == 0)
            return null;

        // "create class Employee and add method AddEmployee": the members of the class being
        // created, one command that the create-class parser reads whole.
        if (CreatesAType.IsMatch(text[..match.Index])
            && MemberAddition.IsMatch(match.Groups["second"].Value))
            return null;

        return (text[..match.Index].Trim(), match.Groups["second"].Value.Trim());
    }

    /// <summary>Outcome of running one command: whether the action succeeded
    /// (<paramref name="Handled"/>) and, distinctly, whether the input was a
    /// command we understood at all (<paramref name="Recognized"/>). A recognized
    /// command whose handler fails for a real reason ("No project selected") is
    /// Handled=false but Recognized=true, so the CLI must not offer it the
    /// "did you mean" help meant for genuinely unintelligible input.</summary>
    /// <param name="NeededAi">True when the rules could not read the input and the AI's reading was
    /// accepted: a script, which never asks the AI, could not replay it as typed.</param>
    public readonly record struct RunResult(bool Handled, bool Recognized, bool NeededAi = false);

    /// <summary>Parse then execute one command. The demo shortcut and the NLE
    /// analysis callback (for display) live here so callers need only one entry.</summary>
    public static bool Run(
        string input,
        ref ProjectMetaData? project,
        List<PropertyRule> configuredRules,
        string workspace,
        Action<ProjectMetaData?> setLastCreatedProject,
        Action<SemanticResult?>? onAnalyzed = null)
        => RunDetailed(input, ref project, configuredRules, workspace, setLastCreatedProject, onAnalyzed).Handled;

    /// <summary>As <see cref="Run"/>, but also reports whether the input was
    /// recognized as a command (see <see cref="RunResult"/>).
    ///
    /// <paramref name="aiResolver"/> is consulted ONLY when rule-based parsing yields
    /// Unknown, and only when <paramref name="confirmAi"/> is also supplied — so the AI
    /// can never change, delay or degrade a command that already works today. With both
    /// null this method behaves exactly as it did before the AI layer existed.</summary>
    public static RunResult RunDetailed(
        string input,
        ref ProjectMetaData? project,
        List<PropertyRule> configuredRules,
        string workspace,
        Action<ProjectMetaData?> setLastCreatedProject,
        Action<SemanticResult?>? onAnalyzed = null,
        IIntentResolver? aiResolver = null,
        Func<AiResolution, bool>? confirmAi = null,
        CancellationToken ct = default)
    {
        // `demo` used to be intercepted here, printing CommandHandlingService.BuildDemo()'s canned
        // Customer class with its comment markers stripped — creating nothing, explaining nothing,
        // and running no workflow while the catalog called it one. It is now DemoCommand in the CLI,
        // which runs real commands back through this very method, so the demo cannot drift from
        // what the tool actually does.

        // Parsing moves the current class before the handler runs ("create class Class1" sets it
        // even when Class1 exists and the create is refused). A command that fails must leave the
        // session as it found it: otherwise the undo journal sees a changed state, records an empty
        // entry for the refusal and clears redo.
        var before = ParsingStateSnapshot.Capture();

        // Refused before it is parsed: a parse reads one half, and the half it read was not
        // always the first one.
        if (TwoCommands((input ?? string.Empty).Trim()) is var (first, second))
        {
            Output.Error("This line holds two commands. Type them one at a time:");
            Output.Detail($"  {first}");
            Output.Detail($"  {second}");
            return new RunResult(Handled: false, Recognized: true, NeededAi: false);
        }

        if (onAnalyzed is not null)
            onAnalyzed(NlpActionProvider.Analyze(input ?? string.Empty));

        var command = Parse(input ?? string.Empty, workspace);

        var neededAi = false;

        if (command.Intent == IntentType.Unknown && aiResolver is not null && confirmAi is not null)
        {
            var resolution = aiResolver.Resolve(input ?? string.Empty, ct);

            // The Unknown check is not redundant with the resolver contract: a resolver
            // may legally return a non-null resolution, and nothing stops that
            // resolution being Unknown. Confirming it would ask the user to approve
            // "I don't know", and accepting would mark the command Recognized while
            // executing nothing.
            if (resolution is not null &&
                resolution.Intent != IntentType.Unknown &&
                confirmAi(resolution))
            {
                command = CommandEntityExtractor.Extract(
                    input ?? string.Empty, resolution.Intent, workspace);
                command.Confidence = resolution.Confidence;
                neededAi = true;
            }
        }

        var recognized = command.Intent != IntentType.Unknown;
        var handled = CommandHandlingService.Execute(command, ref project, configuredRules, workspace, setLastCreatedProject);
        if (!handled)
            before.Restore();
        return new RunResult(handled, recognized, neededAi);
    }
}
