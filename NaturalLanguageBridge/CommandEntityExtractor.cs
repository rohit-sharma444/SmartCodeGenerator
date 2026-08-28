using CodeGenerationEngine.Core;      // CommandParser
using CodeGenerationEngine.Models;    // ParsedCommand, IntentType, SessionContext
using System.Text.RegularExpressions;

namespace NaturalLanguageBridge;

/// <summary>
/// Prose-to-<see cref="ParsedCommand"/> field extraction for code-generation
/// commands. Moved out of CodeGenerationEngine so raw user text is parsed in the
/// bridge, never in the code generator. Still regex-over-sentence: NLE's entity
/// output is too lossy for structured extraction (see the Plan 1 exit report),
/// so it parses the normalized raw sentence, not NLE entities. Stateless.
/// </summary>
public static class CommandEntityExtractor
{
    public static ParsedCommand Extract(string rawSentence,
        IntentType intent, string workspace)
    {
        // "add validation"/"add business validation" carry a rule fragment the user typed
        // on purpose ("must be in the past", "must be a valid date", "must be the same
        // as ..."); stripping "the"/"a"/"an" as English filler corrupts that fragment
        // before ComposeRuleFragment or ExtractBusinessValidationInfo ever see it, so
        // those two intents keep their stop words while every other intent still gets
        // the friendlier, stopword-stripped prose.
        var keepStopWords = intent is IntentType.AddValidation or IntentType.AddBusinessValidation;
        var sentence = CommandParser.Normalize(rawSentence, removeStopWords: !keepStopWords);

        var command = new ParsedCommand
        {
            Intent = intent,
            OriginalSentence = rawSentence
        };

        var tokens = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        switch (intent)
        {
            case IntentType.CreateClass:
                ExtractClassName(sentence, command);
                ExtractProperties(sentence, command);
                if (command.Properties.Count == 0)
                {
                    ExtractEnglishProperties(sentence, command);
                }
                ExtractMethods(sentence, command);
                if (!string.IsNullOrWhiteSpace(command.ClassName))
                    SessionContext.CurrentClass = command.ClassName;
                ExtractInheritanceInfo(sentence, command);
                ExtractInterfaceInfo(sentence, command);
                break;

            case IntentType.ClassInherit:
                ExtractClassName(sentence, command);
                ExtractInheritanceInfo(sentence, command);
                break;

            case IntentType.CreateInterface:
                ExtractInterfaceName(sentence, command);
                ExtractProperties(sentence, command);
                if (command.Properties.Count == 0)
                    ExtractEnglishProperties(sentence, command);
                ExtractMethods(sentence, command);
                if (!string.IsNullOrWhiteSpace(command.InterfaceName))
                    SessionContext.CurrentClass = command.InterfaceName;
                break;

            case IntentType.DeleteClass:

            case IntentType.RestoreClass:

            case IntentType.ShowClass:

            case IntentType.AnalyzeClass:
                ExtractClassName(sentence, command);
                break;

            case IntentType.ListMethods:
                if (tokens.Length > 2)
                    command.ClassName = tokens[2];
                ExtractClassName(sentence, command);
                break;

            case IntentType.RenameClass:
                ExtractRenameInfo(sentence, command);
                break;

            case IntentType.CreateMethod:
                ExtractMethodInfo(sentence, command);
                break;

            case IntentType.AddValidation:
                ExtractValidationInfo(sentence, command);
                break;

            case IntentType.ShowValidation:
                ExtractClassName(sentence, command);
                break;

            case IntentType.AddBusinessValidation:
                ExtractBusinessValidationInfo(sentence, command);
                break;

            case IntentType.ShowBusinessValidation:
                ExtractBusinessShowTarget(sentence, command);
                break;

            case IntentType.CreateProject:
                ExtractProjectInfo(sentence, command);
                break;
            case IntentType.RestoreLastClass:
                break;

            case IntentType.ListProperties:
                ExtractClassName(sentence, command);
                break;

            case IntentType.CreateProperty:
            case IntentType.DeleteProperty:
                ExtractPropertyInfo(sentence, command);
                break;
        }
        if (string.IsNullOrWhiteSpace(command.ClassName))
            command.ClassName = SessionContext.CurrentClass ?? string.Empty;
        return command;
    }

    public static void ExtractInheritanceInfo(
        string sentence,
        ParsedCommand command)
    {
        var patterns = new[]
        {
            @"inherits?\s+([A-Za-z_]\w*)",
            @"extends\s+([A-Za-z_]\w*)",
            @"derived\s+from\s+([A-Za-z_]\w*)",
            @"base\s+class\s+([A-Za-z_]\w*)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(
                sentence,
                pattern,
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                command.BaseClass =
                    ToPascalCase(match.Groups[1].Value);

                return;
            }
        }
    }

    public static void ExtractInterfaceInfo(string sentence, ParsedCommand command)
    {
        var match = Regex.Match(
            sentence,
            @"implements?\s+(.+)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return;

        var interfaces =
            Regex.Split(
                    match.Groups[1].Value,
                    @"\s*(?:,|and)\s*",
                    RegexOptions.IgnoreCase)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));

        foreach (var item in interfaces)
            command.Interfaces.Add(item);
    }

    public static void ExtractInterfaceName(string input,
        ParsedCommand command)
    {
        var patterns = new[]
        {
        @"create\s+interface\s+(\w+)",
        @"generate\s+interface\s+(\w+)",
        @"build\s+interface\s+(\w+)",
        @"interface\s+named\s+(\w+)"
    };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;

            var name = ToPascalCase(match.Groups[1].Value);
            if (!name.StartsWith("I", StringComparison.Ordinal))
                name = "I" + name;

            command.InterfaceName = name;
            command.ClassName = name;
            return;
        }
    }

    public static void ExtractServiceName(string input, ParsedCommand command)
    {
        var patterns = new[]
        {
            @"create\s+service\s+(\w+)",
            @"generate\s+service\s+(\w+)",
            @"service\s+for\s+(\w+)",
            @"manager\s+for\s+(\w+)",
            @"handler\s+for\s+(\w+)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);

            if (!match.Success)
                continue;

            var entity = ToPascalCase(match.Groups[1].Value);

            command.ServiceName =
                entity.EndsWith("Service")
                    ? entity
                    : entity + "Service";

            return;
        }
    }

    public static void ExtractRepositoryName(string input, ParsedCommand command)
    {
        var patterns = new[]
        {
            @"create\s+repository\s+(\w+)",
            @"repository\s+for\s+(\w+)",
            @"storage\s+for\s+(\w+)",
            @"data\s+access\s+for\s+(\w+)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);

            if (!match.Success)
                continue;

            var entity = ToPascalCase(match.Groups[1].Value);

            command.RepositoryName =
                entity.EndsWith("Repository")
                    ? entity
                    : entity + "Repository";

            return;
        }
    }

    public static void ExtractControllerName(string input, ParsedCommand command)
    {
        var match = Regex.Match(input, @"create controller\s+(\w+)", RegexOptions.IgnoreCase);

        if (match.Success)
            command.ControllerName = match.Groups[1].Value;
    }

    // Rule fragments are "<property> <predicate>". The predicate the user types may
    // already carry its own verb ("must be between 18 and 65", "length must be
    // between 2 and 50"), in which case only the property name is prepended. A bare
    // predicate ("required") needs an "is" to become a sentence. Injecting "is"
    // unconditionally — as this used to — produced "Age is must be between 18 and 65",
    // which no parser but RequiredRule's could ever match, leaving every other data
    // rule type registered but unreachable from the command.
    private static readonly string[] SelfVerbedPrefixes =
        ["is ", "are ", "must ", "should ", "shall ", "cannot ", "can ", "has ", "have ", "length "];

    private static string ComposeRuleFragment(string propertyName, string rule)
    {
        // "add validation Age must be between 18 and 65 on Age ..." — the property is
        // already the subject; prepending it again would give "Age Age must be ...".
        var firstWord = rule.Split(' ', 2)[0];
        if (firstWord.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            return rule;

        foreach (var prefix in SelfVerbedPrefixes)
            if (rule.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return $"{propertyName} {rule}";

        return $"{propertyName} is {rule}";
    }

    public static void ExtractValidationInfo(string sentence, ParsedCommand command)
    {
        // "add validation <rule> on <property> in <class>" (documented form).
        var onIn = Regex.Match(sentence,
            @"add\s+validation\s+(.+?)\s+on\s+([A-Za-z_]\w*)\s+in\s+([A-Za-z_]\w*)",
            RegexOptions.IgnoreCase);

        if (onIn.Success)
        {
            command.PropertyName = ToPascalCase(onIn.Groups[2].Value);
            command.ClassName = ToPascalCase(onIn.Groups[3].Value);

            // Reshape "<rule> on <property>" into the "<property> <predicate>"
            // phrasing the rule parsers understand (e.g. "Name is required").
            command.ValidationText = ComposeRuleFragment(
                command.PropertyName, onIn.Groups[1].Value.Trim());
            return;
        }

        // "add validation <rule> in <class>" — the "on <property>" clause omitted
        // because the rule text already names the property (e.g. "Number should not
        // be blank in Bill"). The fragment is passed through as-is: the rule parsers
        // take the property from its first token, exactly as they do for the "on"
        // form after it composes "<property> <predicate>". Tried after the "on ... in"
        // form so the documented shape wins; the non-greedy rule keeps the class as
        // the final token even when the rule itself contains "in".
        var inClass = Regex.Match(sentence,
            @"add\s+validation\s+(.+?)\s+in\s+([A-Za-z_]\w*)\s*$",
            RegexOptions.IgnoreCase);

        if (inClass.Success)
        {
            var ruleText = inClass.Groups[1].Value.Trim();
            command.ClassName = ToPascalCase(inClass.Groups[2].Value);
            command.PropertyName = ToPascalCase(ruleText.Split(' ', 2)[0]);
            command.ValidationText = ruleText;
            return;
        }

        // "add validation <rule> to <class>" (legacy form).
        var to = Regex.Match(sentence,
            @"add\s+validation\s+(.+?)\s+to\s+([A-Za-z_]\w*)\s*$",
            RegexOptions.IgnoreCase);

        if (to.Success)
        {
            command.ValidationText = to.Groups[1].Value.Trim();
            command.ClassName = ToPascalCase(to.Groups[2].Value);
        }
    }

    public static void ExtractBusinessValidationInfo(string sentence, ParsedCommand command)
    {
        // "add business validation <rule> in|for|on <class>"
        var withClass = Regex.Match(sentence,
            @"add\s+business\s+validation\s+(?<rule>.+?)\s+(?:in|for|on)\s+(?<class>[A-Za-z_]\w*)\s*$",
            RegexOptions.IgnoreCase);
        if (withClass.Success)
        {
            command.ValidationText = withClass.Groups["rule"].Value.Trim();
            command.ClassName = ToPascalCase(withClass.Groups["class"].Value);
            return;
        }

        // "add business validation <rule>" (class comes from session context)
        var noClass = Regex.Match(sentence,
            @"add\s+business\s+validation\s+(?<rule>.+)$", RegexOptions.IgnoreCase);
        if (noClass.Success)
            command.ValidationText = noClass.Groups["rule"].Value.Trim();
    }

    public static void ExtractBusinessShowTarget(string sentence, ParsedCommand command)
    {
        var m = Regex.Match(sentence,
            @"(?:show|list|view)\s+business\s+validation\s+(?:rules?\s+)?(?:for\s+)?(?<class>[A-Za-z_]\w*)",
            RegexOptions.IgnoreCase);
        if (m.Success)
            command.ClassName = ToPascalCase(m.Groups["class"].Value);
    }

    // Parses single-property member commands:
    //   "add property <name> [<type>] to <class>"
    //   "remove property <name> from <class>"
    public static void ExtractPropertyInfo(string sentence, ParsedCommand command)
    {
        // add/create property <name> <type> to/in <class>
        var addTyped = Regex.Match(sentence,
            @"\b(?:add|create)\s+property\s+([A-Za-z_]\w*)\s+([A-Za-z_][\w<>\[\],\.\?]*)\s+(?:to|in|on)\s+([A-Za-z_]\w*)",
            RegexOptions.IgnoreCase);
        if (addTyped.Success)
        {
            command.PropertyName = ToPascalCase(addTyped.Groups[1].Value);
            command.PropertyType = addTyped.Groups[2].Value.Trim();
            command.ClassName = ToPascalCase(addTyped.Groups[3].Value);
            return;
        }

        // add/create property <name> to/in <class> (type defaults to string)
        var addUntyped = Regex.Match(sentence,
            @"\b(?:add|create)\s+property\s+([A-Za-z_]\w*)\s+(?:to|in|on)\s+([A-Za-z_]\w*)",
            RegexOptions.IgnoreCase);
        if (addUntyped.Success)
        {
            command.PropertyName = ToPascalCase(addUntyped.Groups[1].Value);
            command.ClassName = ToPascalCase(addUntyped.Groups[2].Value);
            return;
        }

        // remove/delete property <name> from <class>
        var remove = Regex.Match(sentence,
            @"\b(?:remove|delete|drop)\s+property\s+([A-Za-z_]\w*)\s+(?:from|of|in|on)\s+([A-Za-z_]\w*)",
            RegexOptions.IgnoreCase);
        if (remove.Success)
        {
            command.PropertyName = ToPascalCase(remove.Groups[1].Value);
            command.ClassName = ToPascalCase(remove.Groups[2].Value);
        }
    }

    public static void ExtractClassInfo(string sentence, ParsedCommand command)
    {
        ExtractClassName(sentence, command);
        ExtractProperties(sentence, command);
        ExtractMethods(sentence, command);
    }

    public static void ExtractMethodInfo(string sentence, ParsedCommand command)
    {
        var patterns = new[]
        {
        @"(?:add|create|generate)\s+method\s+(\w+)(?:\s+returns\s+(\w+))?\s+(?:to|for|in)\s+(\w+)",
        @"(?:add|create|generate)\s+(\w+)\s+method(?:\s+returns\s+(\w+))?\s+(?:to|for|in)\s+(\w+)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(sentence, pattern, RegexOptions.IgnoreCase);

            if (!match.Success)
                continue;

            command.MethodName = match.Groups[1].Value;

            command.ReturnType = match.Groups[2].Success
                    ? match.Groups[2].Value
                    : "void";

            command.ClassName = match.Groups[3].Value;

            return;
        }
    }

    public static void ExtractMethods(string sentence, ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return;

        var match = Regex.Match(
            sentence,
            @"\bmethods?\b\s+(?<methods>.+)$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return;

        var methodsText = match.Groups["methods"].Value.Trim();

        // Methods can be separated by commas, semicolons, or the word "and"
        // (e.g. "AddEmployee and EmployeeList method").
        var methodTokens = Regex.Split(
                methodsText,
                @"\s*(?:,|;|\band\b)\s*",
                RegexOptions.IgnoreCase)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        foreach (var token in methodTokens)
        {
            var method = ParseMethod(CleanMethodToken(token));

            if (method == null)
                continue;

            if (command.Methods.Any(m =>
                m.Name.Equals(method.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            command.Methods.Add(method);
        }
    }

    /// <summary>
    /// Strips method-related noise words from a single method token so that
    /// phrasings like "add EmployeeList method" or "method GetName" reduce to
    /// the bare method name/signature that <see cref="ParseMethod"/> understands.
    /// </summary>
    private static string CleanMethodToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        token = token.Trim();

        // Leading verb ("add EmployeeList" -> "EmployeeList").
        token = Regex.Replace(
            token,
            @"^(?:add|create|generate)\s+",
            string.Empty,
            RegexOptions.IgnoreCase);

        // Leading "method"/"methods" keyword ("method GetName" -> "GetName").
        token = Regex.Replace(
            token,
            @"^methods?\s+",
            string.Empty,
            RegexOptions.IgnoreCase);

        // Trailing "method"/"methods" keyword ("EmployeeList method" -> "EmployeeList").
        token = Regex.Replace(
            token,
            @"\s+methods?$",
            string.Empty,
            RegexOptions.IgnoreCase);

        return token.Trim();
    }

    public static MethodDefinition? ParseMethod(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        token = token.Trim();

        // Method(params) returns Type
        var fullMatch = Regex.Match(
            token,
            @"^(?<name>\w+)\((?<params>.*?)\)\s*(?:returns?\s+(?<type>\w+))?$",
            RegexOptions.IgnoreCase);

        if (fullMatch.Success)
        {
            return new MethodDefinition
            {
                Name = fullMatch.Groups["name"].Value,
                ReturnType = fullMatch.Groups["type"].Success
                    ? fullMatch.Groups["type"].Value
                    : "void"
            };
        }

        // Method returns Type
        var returnMatch = Regex.Match(
            token,
            @"^(?<name>\w+)\s+returns?\s+(?<type>\w+)$",
            RegexOptions.IgnoreCase);

        if (returnMatch.Success)
        {
            return new MethodDefinition
            {
                Name = returnMatch.Groups["name"].Value,
                ReturnType = returnMatch.Groups["type"].Value
            };
        }

        // Just method name
        var nameMatch = Regex.Match(
            token,
            @"^(?<name>\w+)$");

        if (nameMatch.Success)
        {
            return new MethodDefinition
            {
                Name = nameMatch.Groups["name"].Value,
                ReturnType = "void"
            };
        }

        return null;
    }

    public static void ExtractClassName(string sentence, ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return;

        sentence = sentence.Trim();

        var inheritancePatterns = new[]
        {
            @"create\s+([A-Za-z_]\w*)\s+class\s+inherits?\s+([A-Za-z_]\w*)",
            @"create\s+([A-Za-z_]\w*)\s+class\s+extends\s+([A-Za-z_]\w*)",
            @"([A-Za-z_]\w*)\s+inherits?\s+([A-Za-z_]\w*)",
            @"([A-Za-z_]\w*)\s+extends\s+([A-Za-z_]\w*)"
        };

        foreach (var pattern in inheritancePatterns)
        {
            var match = Regex.Match(
                sentence,
                pattern,
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                command.ClassName = ToPascalCase(match.Groups[1].Value);
                command.BaseClass = ToPascalCase(match.Groups[2].Value);
                return;
            }
        }

        var patterns = new[]
        {
            // Create Class. The article group requires trailing whitespace so it can
            // only match a standalone "a"/"an": unanchored, it matched the first
            // letter of the name itself, turning "create Alpha class" into "Lpha".
            @"i\s+need\s+(?:an?\s+)?(\w+)\s+(?:class|entity|model|dto)",
            @"create\s+(?:an?\s+)?(\w+)\s+(?:class|entity|model|dto)",
            @"generate\s+(?:an?\s+)?(\w+)\s+(?:class|entity|model|dto)",
            @"build\s+(?:an?\s+)?(\w+)\s+(?:class|entity|model|dto)",
            @"make\s+(?:an?\s+)?(\w+)\s+(?:class|entity|model|dto)",

            // Named class
            @"(?:class|entity|model|dto)\s+(?:named|called)\s+([A-Za-z_]\w*)",

            // Show/Delete/Restore/Rename
            @"show\s+class\s+([A-Za-z_]\w*)",
            @"delete\s+class\s+([A-Za-z_]\w*)",
            @"restore\s+class\s+([A-Za-z_]\w*)",
            @"rename\s+class\s+([A-Za-z_]\w*)",

            // Property operations
            @"show\s+properties\s+of\s+([A-Za-z_]\w*)",
            @"list\s+properties\s+of\s+([A-Za-z_]\w*)",
            @"(?:list|show|view)\s+propert(?:y|ies)\s+([A-Za-z_]\w*)",
            @"add\s+property\s+\w+\s+to\s+([A-Za-z_]\w*)",
            @"remove\s+property\s+\w+\s+from\s+([A-Za-z_]\w*)",

            // Validation
            @"(?:show|list|view)\s+validation\s+(?:for\s+|of\s+|on\s+)?([A-Za-z_]\w*)",
            @"validator\s+(?:for\s+|of\s+)?([A-Za-z_]\w*)",

            // Method operations
            @"add\s+method\s+\w+\s+to\s+([A-Za-z_]\w*)",
            @"remove\s+method\s+\w+\s+from\s+([A-Za-z_]\w*)",

            // Generic fallback
            @"\bclasss*\s+([A-Za-z_]\w*)",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(sentence, pattern, RegexOptions.IgnoreCase);

            if (!match.Success)
                continue;

            command.ClassName = ToPascalCase(match.Groups[1].Value);

            return;
        }

        var createMatch = Regex.Match(sentence,
            @"\bcreate\s+([A-Za-z_]\w*)",
            RegexOptions.IgnoreCase);

        if (!createMatch.Success)
            return;

        var candidate = createMatch.Groups[1].Value;

        var reserved = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
    {
        "class",
        "classs",
        "classes",
        "project",
        "method",
        "property",
        "generate",
        "create",
        "new"
    };

        if (!reserved.Contains(candidate))
        {
            command.ClassName = ToPascalCase(candidate);

            return;
        }

        var nextWordMatch = Regex.Match(
            sentence,
            @"\bcreate\s+[A-Za-z_]\w*\s+([A-Za-z_]\w*)",
            RegexOptions.IgnoreCase);

        if (nextWordMatch.Success)
        {
            var nextCandidate =
                nextWordMatch.Groups[1].Value;

            if (!reserved.Contains(nextCandidate))
            {
                command.ClassName =
                    ToPascalCase(nextCandidate);
            }
        }
    }

    public static void ExtractEnglishProperties(string sentence, ParsedCommand command)
    {
        var match = Regex.Match(sentence,
            @"(?:with|having|containing|contains)\s+(.+)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return;

        var text = match.Groups[1].Value;

        text = Regex.Replace(text, @"\b(and|plus)\b", ",",
            RegexOptions.IgnoreCase);

        foreach (var token in text.Split(','))
        {
            var name = token.Trim();

            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (name.Contains("method", StringComparison.OrdinalIgnoreCase))
                break;

            command.Properties.Add(new PropertyDefinition
            {
                Name = ToPascalCase(name),
                Type = InferType(name)
            });
        }
    }

    public static void ExtractProperties(string sentence, ParsedCommand command)
    {
        var propertySection = ExtractPropertySection(sentence);

        if (string.IsNullOrWhiteSpace(propertySection))
            return;

        var matches = Regex.Matches(
            propertySection,
            @"([A-Za-z_]\w*)\s+(string|int|long|bool|decimal|double|float|datetime|guid)\b",
            RegexOptions.IgnoreCase);

        if (matches.Count > 0)
        {
            foreach (Match match in matches)
            {
                command.Properties.Add(
                    new PropertyDefinition
                    {
                        Name = match.Groups[1].Value,
                        Type = match.Groups[2].Value
                    });
            }

            // Returning here is what drops `Customer Customer` out of
            // `CustomerId int, Customer Customer`: one primitive matched, so the fallback parser
            // below never runs and the rest of the list is never looked at again. The entries
            // that go nowhere are recorded so the handler can say so instead of the user finding
            // out by looking at the file.
            command.IgnoredProperties.AddRange(Ignored(propertySection, matches));

            return;
        }

        // fallback to old logic
        var tokens = Regex.Split(
            propertySection,
            @"\s*(?:,|;|\band\b)\s*",
            RegexOptions.IgnoreCase);

        foreach (var token in tokens)
        {
            var property = ParseProperty(token);

            if (!string.IsNullOrWhiteSpace(property.Name))
                command.Properties.Add(property);
        }
    }

    /// <summary>
    /// The entries in a property list that the primitive-matching path threw away.
    /// </summary>
    /// <remarks>
    /// Reporting one is not the same as emitting it. A property typed as another class cannot
    /// compile in the single-file check the create-class path runs, so it stays dropped - the
    /// relationship offer is where the two classes are joined up, and the message says so.
    /// </remarks>
    public static IReadOnlyList<string> IgnoredPropertyEntries(string sentence)
    {
        var section = ExtractPropertySection(sentence);

        if (string.IsNullOrWhiteSpace(section))
            return [];

        var matched = Regex.Matches(
            section,
            @"([A-Za-z_]\w*)\s+(string|int|long|bool|decimal|double|float|datetime|guid)\b",
            RegexOptions.IgnoreCase);

        // No primitive matched means the fallback parser ran and read the whole list, so nothing
        // was skipped over.
        return matched.Count == 0 ? [] : Ignored(section, matched);
    }

    private static IReadOnlyList<string> Ignored(string section, MatchCollection matched)
    {
        var kept = matched.Select(m => m.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. Regex
                .Split(section, @"\s*(?:,|;|\band\b)\s*", RegexOptions.IgnoreCase)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0
                            && !kept.Any(k => t.StartsWith(k, StringComparison.OrdinalIgnoreCase)))
        ];
    }

    public static string ExtractPropertySection(string sentence)
    {
        var match = Regex.Match(sentence,
                @"(?:properties|with properties|with|having|contains)\s+(.+)", RegexOptions.IgnoreCase);

        if (!match.Success)
            return string.Empty;

        var section = match.Groups[1].Value;

        // If the properties section contains a methods/functions clause, cut it off
        var stopMatch = Regex.Match(section, @"\b(?:and\s+)?(?:with\s+)?(?:having\s+)?(?:methods|functions)\b", RegexOptions.IgnoreCase);
        if (stopMatch.Success)
        {
            section = section.Substring(0, stopMatch.Index);
        }

        return section.Trim().TrimEnd('.', ';');
    }

    public static PropertyDefinition ParseProperty(string token)
    {
        var property = new PropertyDefinition
        {
            Name = string.Empty,
            Type = "string"
        };

        if (string.IsNullOrWhiteSpace(token))
            return property;

        // remove wrapping characters
        token = token.Trim().Trim(',', ';');

        // split into parts: allow "Name:Type modifiers" or "Name Type modifiers" or "Type Name modifiers"
        string modifiersPart = string.Empty;

        if (token.Contains(':'))
        {
            var parts = token.Split(':', 2);
            property.Name = parts[0].Trim();
            var right = parts[1].Trim();
            var rightWords = Regex.Split(right, @"\s+").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (rightWords.Length > 0)
                property.Type = rightWords[0].Trim();
            if (rightWords.Length > 1)
                modifiersPart = string.Join(' ', rightWords.Skip(1));
        }
        else
        {
            var words = Regex.Split(token, @"\s+").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (words.Length == 1)
            {
                property.Name = words[0];
            }
            else
            {
                // try to find known type token position
                int typeIdx = -1;
                for (int i = 0; i < words.Length; i++)
                {
                    if (IsKnownType(words[i]))
                    {
                        typeIdx = i;
                        break;
                    }
                }

                if (typeIdx == 0)
                {
                    // "int Id ..." style
                    property.Type = words[0];
                    property.Name = words.Length > 1 ? words[1] : string.Empty;
                    if (words.Length > 2)
                        modifiersPart = string.Join(' ', words.Skip(2));
                }
                else if (typeIdx > 0)
                {
                    // type found somewhere after index 0: prefer nearest name before type
                    property.Type = words[typeIdx];
                    property.Name = words[typeIdx - 1];
                    if (typeIdx + 1 < words.Length)
                        modifiersPart = string.Join(' ', words.Skip(typeIdx + 1));
                }
                else
                {
                    // fallback: assume "Name Type ..." or "Name modifiers..."
                    property.Name = words[0];
                    property.Type = words.Length > 1 ? words[1] : "string";
                    if (words.Length > 2)
                        modifiersPart = string.Join(' ', words.Skip(2));
                }
            }
        }

        ParseModifiers(modifiersPart, property);

        return property;
    }

    private static void ParseModifiers(string modifiers, PropertyDefinition property)
    {
        if (string.IsNullOrWhiteSpace(modifiers))
            return;

        var text = modifiers.Trim();
        var lower = text.ToLowerInvariant();

        var rule = new PropertyRule
        {
            PropertyName = property.Name ?? string.Empty,
            Name = property.Name ?? string.Empty
        };

        if (Regex.IsMatch(lower, @"\brequired\b"))
        {
            rule.Required = true;
            rule.IsRequired = true;
        }

        // min/max length: support "min length 3", "minlength 3", "min 3"
        var minLen = Regex.Match(text, @"(?:min(?:imum)?\s*length|minlength|min)\s*(?:[:=]|\s)?\s*(\d+)", RegexOptions.IgnoreCase);
        if (minLen.Success && int.TryParse(minLen.Groups[1].Value, out var mn))
            rule.MinLength = mn;

        var maxLen = Regex.Match(text, @"(?:max(?:imum)?\s*length|maxlength|max)\s*(?:[:=]|\s)?\s*(\d+)", RegexOptions.IgnoreCase);
        if (maxLen.Success && int.TryParse(maxLen.Groups[1].Value, out var mx))
            rule.MaxLength = mx;

        // numeric min (supports "greater than", ">", ">=", "min value")
        var minVal = Regex.Match(text, @"(?:(?:greater\s+than)|>=|>|\bmin(?:imum)?\s*value\b|\bmin\b)\s*(?:[:=]|\s)?\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        if (minVal.Success && decimal.TryParse(minVal.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var minV))
            rule.MinValue = minV;

        // numeric max (supports "less than", "<=", "<", "max value")
        var maxVal = Regex.Match(text, @"(?:(?:less\s+than)|<=|<|\bmax(?:imum)?\s*value\b|\bmax\b)\s*(?:[:=]|\s)?\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        if (maxVal.Success && decimal.TryParse(maxVal.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var maxV))
            rule.MaxValue = maxV;

        // allowed values
        var allowed = Regex.Match(text, @"allowed\s*(?:\:|\[)?\s*([^\]]+)\]?", RegexOptions.IgnoreCase);
        if (allowed.Success)
        {
            var group = allowed.Groups[1].Value;
            var parts = group.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(p => p.Trim())
                             .Where(p => !string.IsNullOrWhiteSpace(p))
                             .ToList();
            if (parts.Any())
                rule.AllowedValues = parts;
        }

        // unique
        if (Regex.IsMatch(lower, @"\bunique\b"))
            rule.IsUnique = true;

        // email shorthand or contains '@'
        if (Regex.IsMatch(lower, @"\bemail\b") || modifiers.Contains("@"))
        {
            rule.RegexPattern = @"^\S+@\S+\.\S+$";
        }

        // explicit regex /pattern/ or pattern: ...
        var rxSlash = Regex.Match(text, @"\/([^\/]+)\/");
        if (rxSlash.Success)
        {
            rule.RegexPattern = rxSlash.Groups[1].Value.Trim();
        }
        else
        {
            var pat = Regex.Match(text, @"(?:pattern|regex)\s*(?:\:)?\s*(.+)$", RegexOptions.IgnoreCase);
            if (pat.Success)
                rule.RegexPattern = pat.Groups[1].Value.Trim().Trim('\'', '"');
        }

        // custom rule
        var custom = Regex.Match(text, @"custom\s*[:=]\s*(.+)$", RegexOptions.IgnoreCase);
        if (custom.Success)
            rule.CustomRule = custom.Groups[1].Value.Trim();

        var hasConstraints = rule.Required || rule.MinLength.HasValue || rule.MaxLength.HasValue
                             || rule.MinValue.HasValue || rule.MaxValue.HasValue || (rule.AllowedValues?.Any() == true)
                             || !string.IsNullOrWhiteSpace(rule.RegexPattern) || rule.IsUnique || !string.IsNullOrWhiteSpace(rule.CustomRule);

        if (hasConstraints)
            property.Rules.Add(rule);
    }

    private static readonly Dictionary<string, string> TypeHints =
    new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = "int",
        ["email"] = "string",
        ["phone"] = "string",
        ["mobile"] = "string",
        ["telephone"] = "string",
        ["name"] = "string",
        ["firstname"] = "string",
        ["lastname"] = "string",
        ["address"] = "string",
        ["city"] = "string",
        ["country"] = "string",
        ["zipcode"] = "string",
        ["price"] = "decimal",
        ["amount"] = "decimal",
        ["cost"] = "decimal",
        ["total"] = "decimal",
        ["quantity"] = "int",
        ["count"] = "int",
        ["date"] = "DateTime",
        ["createddate"] = "DateTime",
        ["updateddate"] = "DateTime",
        ["birthdate"] = "DateTime",
        ["isactive"] = "bool",
        ["enabled"] = "bool",
        ["deleted"] = "bool"
    };

    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string",
        "int",
        "long",
        "short",
        "bool",
        "datetime",
        "decimal",
        "double",
        "float",
        "guid",
        "decimal?"
    };

    private static bool IsKnownType(string value)
        => !string.IsNullOrWhiteSpace(value) && KnownTypes.Contains(value.Trim().TrimEnd('?'));

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var parts = Regex.Split(value.Trim(), @"[\s_\-]+")
            .Where(p => p.Length > 0);

        // Capitalize the first letter of each part but preserve the rest, so
        // already-cased identifiers like "IRepository" or "BaseEntity" survive
        // instead of being flattened to "Irepository" / "Baseentity".
        return string.Concat(parts.Select(p =>
        char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }

    private static string InferType(string propertyName)
    {
        var normalized = propertyName.Replace(" ", "")
                        .Replace("_", "").ToLowerInvariant();

        foreach (var hint in TypeHints)
        {
            if (normalized.Contains(hint.Key, StringComparison.OrdinalIgnoreCase))
                return hint.Value;
        }
        return "string";
    }

    // Project extraction (unchanged)
    public static void ExtractProjectInfo(string sentence, ParsedCommand command)
    {
        var nameMatch = Regex.Match(sentence, @"project(?:\s+named|\s+called)?\s+([A-Za-z0-9_\-\.]+)", RegexOptions.IgnoreCase);
        if (nameMatch.Success)
            command.ProjectName = nameMatch.Groups[1].Value.Trim();
        else
        {
            // Project types phrased without the word "project" (e.g. "create
            // worker service MyWorker", "create class library MyLib") — take the
            // identifier that follows the type noun so the name isn't lost.
            var altName = Regex.Match(sentence,
                @"\b(?:service|library|lib|app|application)\s+(?:named\s+|called\s+)?([A-Za-z_][A-Za-z0-9_\.\-]*)",
                RegexOptions.IgnoreCase);
            if (altName.Success)
                command.ProjectName = altName.Groups[1].Value.Trim();
        }

        var folderMatch = Regex.Match(sentence, @"\bin\s+(""[^""]+""|'[^']+'|[A-Za-z]:\\[^\s]+)", RegexOptions.IgnoreCase);
        if (folderMatch.Success)
        {
            var val = folderMatch.Groups[1].Value.Trim().Trim('"').Trim('\'');
            command.OutputFolder = val;
        }
        else
        {
            folderMatch = Regex.Match(sentence, @"in\s+(""[^""]+""|'[^']+'|[A-Za-z]:\\[^\s]+)", RegexOptions.IgnoreCase);
            if (folderMatch.Success)
            {
                var val = folderMatch.Groups[1].Value.Trim().Trim('"').Trim('\'');
                command.OutputFolder = val;
            }
        }

        var fwMatch = Regex.Match(sentence, @"\bnet\d+(\.\d+)?\b", RegexOptions.IgnoreCase);
        if (fwMatch.Success)
            command.Framework = fwMatch.Value;

        var typeMap = new Dictionary<string, DotNetProjectType>(StringComparer.OrdinalIgnoreCase)
        {
            ["console"] = DotNetProjectType.Console,
            ["classlib"] = DotNetProjectType.ClassLibrary,
            ["class library"] = DotNetProjectType.ClassLibrary,
            ["class"] = DotNetProjectType.ClassLibrary,
            ["webapi"] = DotNetProjectType.WebApi,
            ["web api"] = DotNetProjectType.WebApi,
            ["api"] = DotNetProjectType.WebApi,
            ["blazor"] = DotNetProjectType.BlazorServer,
            ["blazorserver"] = DotNetProjectType.BlazorServer,
            ["blazorwasm"] = DotNetProjectType.BlazorWebAssembly,
            ["wasm"] = DotNetProjectType.BlazorWebAssembly,
            ["wpf"] = DotNetProjectType.Wpf,
            ["winforms"] = DotNetProjectType.WinForms,
            ["worker"] = DotNetProjectType.WorkerService,
            ["mvc"] = DotNetProjectType.Mvc
        };

        foreach (var kv in typeMap)
        {
            if (Regex.IsMatch(sentence, $@"\b{Regex.Escape(kv.Key)}\b", RegexOptions.IgnoreCase))
            {
                command.ProjectType = kv.Value;
                break;
            }
        }

        if (!command.ProjectType.HasValue)
        {
            var typeMatch = Regex.Match(sentence, @"\b(?:as|type)\s+([A-Za-z0-9]+)\b", RegexOptions.IgnoreCase);
            if (typeMatch.Success)
            {
                var candidate = typeMatch.Groups[1].Value;
                if (typeMap.TryGetValue(candidate, out var pt))
                    command.ProjectType = pt;
                else if (Enum.TryParse<DotNetProjectType>(candidate, true, out var parsed))
                    command.ProjectType = parsed;
            }
        }
    }

    public static void ExtractRenameInfo(string sentence, ParsedCommand command)
    {
        var match = Regex.Match(sentence,
            @"rename\s+class\s+([A-Za-z_]\w*)\s+to\s+([A-Za-z_]\w*)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return;

        command.ClassName = match.Groups[1].Value;
        command.NewClassName = match.Groups[2].Value;
    }

    public static List<ParameterDefinition> ParseParameters(string text)
    {
        List<ParameterDefinition> parameters = [];

        if (string.IsNullOrWhiteSpace(text))
            return parameters;

        foreach (var part in text.Split(','))
        {
            var pieces = part.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (pieces.Length != 2)
                continue;

            parameters.Add(new ParameterDefinition
            {
                Type = pieces[0],
                Name = pieces[1]
            });
        }
        return parameters;
    }

}
