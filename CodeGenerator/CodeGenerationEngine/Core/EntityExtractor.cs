using CodeGenerationEngine.Models;
using CodeGenerationEngine.Semantics.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;
using static Microsoft.CodeAnalysis.CSharp.SyntaxTokenParser;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Extracts entities from natural language input based on detected intent.
/// Improved tokenization and modifier parsing to be more forgiving of punctuation and phrasing.
/// </summary>
public static class EntityExtractor
{
    public static ParsedCommand Extract(SemanticModel semantic,
        IntentType intent, string workspace)
    {
        var sentence = semantic.NormalizedText;

        var command = new ParsedCommand
        {
            Intent = intent
        };

        var tokens = TextTokenizer.Tokenize(sentence);

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
                if (tokens.Count > 2)
                    command.ClassName = tokens[2].Normalized;
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

            case IntentType.CreateProject:
                ExtractProjectInfo(sentence, command);
                break;
            case IntentType.RestoreLastClass:
                break;

            case IntentType.ListProperties:
                ExtractClassName(sentence, command);
                break;
        }
        if (string.IsNullOrWhiteSpace(command.ClassName))
            command.ClassName = SessionContext.CurrentClass;
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

    public static bool GenerateValidator(ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ClassName))
        {
            Console.WriteLine("Class name is required.");
            return false;
        }

        var rules = ValidationRegistry.GetRules(command.ClassName);

        if (rules.Count == 0)
        {
            Console.WriteLine($"No validation rules found for {command.ClassName}.");
            return false;
        }

        var project = ProjectManager.CurrentProject;
        if (project == null)
        {
            Console.WriteLine("No project selected.");
            return false;
        }

        var classFile = Directory
            .EnumerateFiles(project.ProjectPath,
                $"{command.ClassName}.cs",
                SearchOption.AllDirectories).FirstOrDefault();

        if (classFile == null)
        {
            Console.WriteLine($"Class '{command.ClassName}' not found.");
            return false;
        }

        var properties = ExtractProperties(classFile);

        var namespaceName = ExtractNamespace(classFile);

        var validationCode = ValidationEngine.GenerateValidationPartial(
                namespaceName, command.ClassName, properties, rules);

        var validatorFile = Path.Combine(Path.GetDirectoryName(classFile)!,
                $"{command.ClassName}.Validation.cs");

        File.WriteAllText(validatorFile, validationCode);

        Console.WriteLine($"Validator generated: {validatorFile}");
        return true;
    }

    private static string ExtractNamespace(string classFile)
    {
        var source = File.ReadAllText(classFile);

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var fileScoped = root.Members
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        if (fileScoped != null)
            return fileScoped.Name.ToString();

        var traditional = root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();

        return traditional?.Name.ToString() ?? "GeneratedProject";
    }


    public static List<PropertyDefinition> ExtractProperties(string classFile)
    {
        var code = File.ReadAllText(classFile);

        var matches = Regex.Matches(code,
            @"public\s+([\w<>?]+)\s+(\w+)\s*\{\s*get;\s*set;\s*\}");

        var properties = new List<PropertyDefinition>();

        foreach (Match match in matches)
        {
            properties.Add(new PropertyDefinition
            {
                Type = match.Groups[1].Value,
                Name = match.Groups[2].Value
            });
        }

        return properties;
    }

    public static void ExtractValidationInfo(string sentence, ParsedCommand command)
    {
        var match = Regex.Match(sentence,
            @"add\s+validation\s+(.+?)\s+to\s+(\w+)$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return;

        command.ValidationText = match.Groups[1].Value.Trim();

        command.ClassName = match.Groups[2].Value.Trim();
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

        var methodTokens = Regex.Split(
                methodsText,
                @"\s*(?:,|;)\s*")
            .Where(t => !string.IsNullOrWhiteSpace(t));

        foreach (var token in methodTokens)
        {
            var method = ParseMethod(token.Trim());

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
            // Create Class
            @"i\s+need\s+(?:a|an)?\s*(\w+)\s+(?:class|entity|model|dto)",
            @"create\s+(?:a|an)?\s*(\w+)\s+(?:class|entity|model|dto)",
            @"generate\s+(?:a|an)?\s*(\w+)\s+(?:class|entity|model|dto)",
            @"build\s+(?:a|an)?\s*(\w+)\s+(?:class|entity|model|dto)",
            @"make\s+(?:a|an)?\s*(\w+)\s+(?:class|entity|model|dto)",

            // Named class
            @"(?:class|entity|model|dto)\s+named\s+([A-Za-z_]\w*)",

            // Show/Delete/Restore/Rename
            @"show\s+class\s+([A-Za-z_]\w*)",
            @"delete\s+class\s+([A-Za-z_]\w*)",
            @"restore\s+class\s+([A-Za-z_]\w*)",
            @"rename\s+class\s+([A-Za-z_]\w*)",

            // Property operations
            @"show\s+properties\s+of\s+([A-Za-z_]\w*)",
            @"list\s+properties\s+of\s+([A-Za-z_]\w*)",
            @"add\s+property\s+\w+\s+to\s+([A-Za-z_]\w*)",
            @"remove\s+property\s+\w+\s+from\s+([A-Za-z_]\w*)",

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

    //public static void ExtractClassName(string sentence, ParsedCommand command)
    //{
    //    if (string.IsNullOrWhiteSpace(sentence))
    //        return;

    //    var englishPatterns = new[]
    //        {
    //            @"i\s+need\s+(?:a|an)?\s*(\w+)\s+(?:class|entity|model|dto)",
    //            @"create\s+(?:a|an)?\s*(\w+)\s+(?:class|entity|model|dto)",
    //            @"generate\s+(?:a|an)?\s*(\w+)\s+(?:class|entity|model|dto)",
    //            @"build\s+(?:a|an)?\s*(\w+)\s+(?:class|entity|model|dto)",
    //            @"make\s+(?:a|an)?\s*(\w+)\s+(?:class|entity|model|dto)",
    //            @"(?:class|entity|model|dto)\s+named\s+(\w+)"
    //        };

    //    foreach (var pattern in englishPatterns)
    //    {
    //        var match = Regex.Match(sentence, pattern, RegexOptions.IgnoreCase);
    //        if (match.Success)
    //        {
    //            command.ClassName = ToPascalCase(match.Groups[1].Value);
    //            return;
    //        }
    //    }

    //    var showClassMatch = Regex.Match(sentence,
    //        @"show\s+class\s+([A-Za-z_]\w*)", RegexOptions.IgnoreCase);
    //    if (showClassMatch.Success)
    //    {
    //        command.ClassName = showClassMatch.Groups[1].Value;
    //        return;
    //    }

    //    // 2) Fallback: "create <Token>" but avoid capturing common keywords or the misspelled 'classs'
    //    var fallback = Regex.Match(sentence, @"\bcreate\s+([A-Za-z_]\w*)", RegexOptions.IgnoreCase);
    //    if (fallback.Success)
    //    {
    //        var candidate = fallback.Groups[1].Value;
    //        var lower = candidate.ToLowerInvariant();

    //        // ignore if candidate looks like a keyword or a plural/misspelling of 'class' or 'project' etc.
    //        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    //        {
    //            "class", "classs", "classes", "project", "method", "property", "generate", "create", "new"
    //        };

    //        if (!reserved.Contains(lower))
    //        {
    //            command.ClassName = candidate;
    //            return;
    //        }

    //        // if candidate is a reserved-like token, try to pick the next word as class name
    //        var nextWordMatch = Regex.Match(sentence, @"\bcreate\s+[A-Za-z_]\w*\s+([A-Za-z_]\w*)", RegexOptions.IgnoreCase);
    //        if (nextWordMatch.Success)
    //        {
    //            var nextCandidate = nextWordMatch.Groups[1].Value;
    //            if (!reserved.Contains(nextCandidate.ToLowerInvariant()))
    //            {
    //                command.ClassName = nextCandidate;
    //                return;
    //            }
    //        }
    //    }

    //    // 3) As a last resort, try to find any stand-alone "class <Name>" (already tried but be extra tolerant)
    //    var anyClass = Regex.Match(sentence, @"\bclasss*\s+([A-Za-z_]\w*)", RegexOptions.IgnoreCase);
    //    if (anyClass.Success)
    //    {
    //        command.ClassName = anyClass.Groups[1].Value;
    //    }
    //}

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

        var parts = Regex.Split(value.Trim(), @"[\s_\-]+");

        return string.Concat(parts.Select(p =>
        char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
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