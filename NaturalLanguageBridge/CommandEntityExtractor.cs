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
        //
        // Nor may anything else in the normaliser touch them: it turns , ; : . ? ! into spaces and
        // rewrites new -> create, manager -> service, and so on. A regex ^(a|b)?$ was stored as
        // ^(a|b) $ with "Validation added", 0.5 became "0 5", and "Draft, Sent, Paid" lost the
        // commas that make it a list. The command words of these two are fixed ("add validation",
        // "on", "in") and are matched as typed, so the sentence is only trimmed and its runs of
        // whitespace collapsed.
        //
        // A project name is the same: "A" is a stop word, Data.Core lost its dot and a quoted
        // name lost everything, so the project was made as NewProject or under another name; and
        // net8.0 and C:\path lost their punctuation too.
        //
        // And add/remove property: a quoted name, the class A, string?, System.Double and
        // Dictionary<string, int> (see ExtractPropertyInfo).
        var verbatim = intent is IntentType.AddValidation or IntentType.AddBusinessValidation
            or IntentType.CreateProject or IntentType.CreateProperty or IntentType.DeleteProperty;
        var sentence = verbatim
            ? Verbatim(rawSentence)
            : CommandParser.Normalize(rawSentence ?? string.Empty);

        var command = new ParsedCommand
        {
            Intent = intent,
            OriginalSentence = rawSentence ?? string.Empty
        };

        var tokens = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        switch (intent)
        {
            case IntentType.CreateClass:
            {
                // The members are read from the sentence as typed, not the normalised one: that
                // rewrote a property called New to Create, manager to Service and entity to Class,
                // and removed the commas the ignored-entry report splits the list on.
                var members = MemberListText(rawSentence);
                ExtractClassName(WithoutAddMethodClause(sentence), command);
                ExtractProperties(members, command);
                if (command.Properties.Count == 0)
                {
                    ExtractEnglishProperties(members, command);
                }
                ExtractMethods(members, command);
                if (!string.IsNullOrWhiteSpace(command.ClassName))
                    SessionContext.CurrentClass = command.ClassName;
                // A base-type word inside a quoted default ("extends the base") is not a clause.
                ExtractInheritanceInfo(WithoutQuotedText(members), command);
                ExtractInterfaceInfo(WithoutQuotedText(members), command);
                break;
            }

            case IntentType.ClassInherit:
                ExtractClassName(sentence, command);
                ExtractInheritanceInfo(sentence, command);
                break;

            case IntentType.CreateInterface:
            {
                var members = MemberListText(rawSentence);
                ExtractInterfaceName(WithoutAddMethodClause(sentence), command);
                ExtractProperties(members, command);
                if (command.Properties.Count == 0)
                    ExtractEnglishProperties(members, command);
                ExtractMethods(members, command);
                if (!string.IsNullOrWhiteSpace(command.InterfaceName))
                    SessionContext.CurrentClass = command.InterfaceName;
                break;
            }

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
        {
            command.ClassName = SessionContext.CurrentClass ?? string.Empty;
            command.ClassFromSession = command.ClassName.Length > 0;
        }
        return command;
    }

    /// <summary>The sentence as typed, trimmed and with each run of whitespace made one space.</summary>
    internal static string Verbatim(string? rawSentence) =>
        Regex.Replace((rawSentence ?? string.Empty).Trim(), @"\s+", " ");

    /// <summary>
    /// The sentence a member list is read from: as typed, without filler words ("with a Name and
    /// an Email") and without the full stop that ends it.
    /// </summary>
    /// <remarks>
    /// The text inside parentheses is a method's parameter list and is kept whole: filler removal
    /// took the parameter a out of <c>Add(int a, int b)</c>, as it takes the article "a". Quoted
    /// text is a default value (<c>default "a, b"</c>) and is kept whole for the same reason.
    /// </remarks>
    internal static string MemberListText(string? rawSentence)
    {
        var text = Verbatim(rawSentence);
        var kept = new List<string>();
        string Keep(Match m)
        {
            kept.Add(m.Value);
            return $"\u0001{kept.Count - 1}\u0001";
        }

        text = QuotedText.Replace(text, Keep);

        // The one-word value after default or =: "default A" lost the A as the article.
        text = Regex.Replace(text, @"(?<=(?:^|\s)(?:default|=)\s+)[^\s""\u0001]\S*", Keep, RegexOptions.IgnoreCase);

        // An English parameter list too, up to a property list or a base-type clause: in
        // "Ship with parameters count int, a string" the parameter a was taken as the article.
        var shielded = Regex.Replace(text,
            $@"\b{MethodParameterParser.EnglishLead}\s.*?(?=\s+(?:(?:and\s+)?(?:with\s+)?propert(?:y|ies)|{BaseTypeClause})\b|[.!]?$)",
            Keep, RegexOptions.IgnoreCase);
        shielded = Regex.Replace(shielded, @"\([^()]*\)", Keep);

        var cleaned = CommandParser.RemoveFillerWords(shielded);
        for (var pass = 0; pass < 3 && cleaned.Contains('\u0001'); pass++)
            cleaned = Regex.Replace(cleaned, "\u0001(\\d+)\u0001", m => kept[int.Parse(m.Groups[1].Value)]);

        return cleaned.TrimEnd('.', '!').TrimEnd();
    }

    /// <summary>The words that start a base class or interface clause in a create command.</summary>
    private const string BaseTypeClause = @"implements?|inherits?|extends|derived\s+from|base\s+class";

    /// <summary>
    /// A quoted default value: <c>"..."</c>, in which <c>\"</c> is a quote of the text, or
    /// <c>'...'</c> starting a word (an apostrophe inside a word, as in O'Brien, opens nothing).
    /// </summary>
    internal static readonly Regex QuotedText = new(
        "\"(?:[^\"\\\\]|\\\\.)*\"|(?<!\\S)'[^']*'(?!\\w)", RegexOptions.Compiled);

    /// <summary>The text with each quoted value blanked out, at the same length: a word inside one
    /// is a value, not a clause.</summary>
    internal static string WithoutQuotedText(string text) =>
        QuotedText.Replace(text, m => new string(' ', m.Length));

    /// <summary>A member name as typed with its first letter capitalised: new becomes New.</summary>
    internal static string Capitalised(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];

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

            var name = InterfaceNaming.NameFor(match.Groups[1].Value);

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

    // "add [business] validation to|for|on <class>: <rule>" — the class first, the rule after a
    // colon. It matched none of the other shapes, so the rule was read as empty. The rule ends the
    // sentence, so a full stop at the very end is the sentence's (quote a literal that ends in one).
    private static bool TryClassFirst(string sentence, string verb, ParsedCommand command)
    {
        var m = Regex.Match(sentence,
            $@"^\s*add\s+{verb}\s+(?:to|for|on)\s+(?<class>[A-Za-z_]\w*)\s*:\s*(?<rule>.+?)\s*$",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return false;

        var rule = m.Groups["rule"].Value.Trim();
        if (rule.EndsWith('.') && !rule.EndsWith(".."))
            rule = rule[..^1].TrimEnd();

        command.ClassName = ToPascalCase(m.Groups["class"].Value);
        command.ValidationText = rule;
        return true;
    }

    public static void ExtractValidationInfo(string sentence, ParsedCommand command)
    {
        if (TryClassFirst(sentence, "validation", command))
        {
            command.PropertyName = ToPascalCase(command.ValidationText.Split(' ', 2)[0]);
            return;
        }

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
            @"add\s+validation\s+(.+?)\s+in\s+([A-Za-z_]\w*)\s*[.!]?\s*$",
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
            @"add\s+validation\s+(.+?)\s+to\s+([A-Za-z_]\w*)\s*[.!]?\s*$",
            RegexOptions.IgnoreCase);

        if (to.Success)
        {
            command.ValidationText = to.Groups[1].Value.Trim();
            command.ClassName = ToPascalCase(to.Groups[2].Value);
        }
    }

    public static void ExtractBusinessValidationInfo(string sentence, ParsedCommand command)
    {
        if (TryClassFirst(sentence, @"business\s+validation", command))
            return;

        // "add business validation <rule> in|for|on <class>"
        var withClass = Regex.Match(sentence,
            @"add\s+business\s+validation\s+(?<rule>.+?)\s+(?:in|for|on)\s+(?<class>[A-Za-z_]\w*)\s*[.!]?\s*$",
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
        // A business rule names properties, never a literal, so a full stop typed at the end of
        // the sentence is not part of it.
        if (noClass.Success)
            command.ValidationText = noClass.Groups["rule"].Value.Trim().TrimEnd('.', '!').TrimEnd();
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
    // A property type as typed: any run of non-spaces, so string?, System.Double and List<string>
    // arrive whole; inside angle brackets (one level of nesting) spaces are allowed too, because
    // that is how Dictionary<string, int> is written. ChangePropertyTypeRe reads it the same way.
    private const string TypedPropertyType = @"(?:[^\s<]|<[^<>]*(?:<[^<>]*>[^<>]*)*>)+";

    internal static readonly Regex AddPropertyRe = new(
        @"\b(?:add|create)\s+(?:(?:a|an|new)\s+)?(?<modifiers>(?:(?:readonly|private|static|required)\s+)*)(?:property|field)\s+"
        + @"(?<name>""[^""]*""|'[^']*'|\S+)"
        + $@"(?:\s+(?<type>{TypedPropertyType}))?"
        // `Name string required`: a modifier after the type, as a create class list takes it.
        + @"(?<late>(?:\s+(?:readonly|private|static|required)(?=\s))*)"
        // `default 5`, `default "New"`, `default 'New York'`, `= 5`: one word or one quoted text
        // (in which \" is a quote), read from the raw text so a filler word (A, the) given as the
        // value is kept.
        + @"(?:\s+(?:default\s+|=\s*)(?<default>""(?:[^""\\]|\\.)*""|'[^']*'|[^\s""']\S*))?"
        + @"\s+(?:to|in|on|into)\s+(?:class\s+(?=\S))?(?<class>[A-Za-z_]\w*)\s*[.!]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// add property and remove property, read from the sentence as typed.
    /// </summary>
    /// <remarks>
    /// It was read from the normalised sentence, which drops the stop words a, an, the, removes
    /// , . ? and quotes nothing out: add property Y string to A lost its class, a quoted name, a
    /// Dictionary&lt;string, int&gt; or a System.Double matched no pattern at all, string? was
    /// stored as string, and a property called New was written as Create. With no name matched
    /// the add went ahead with an empty one and said "Property '' already exists."
    /// A name that is not an identifier is passed on as typed, to be refused by name.
    /// </remarks>
    public static void ExtractPropertyInfo(string sentence, ParsedCommand command)
    {
        // A delete reads the delete pattern only: "remove property Name from Item and add
        // property Title string to Item" matched the add pattern first, and so named Title as
        // the property to remove.
        var add = command.Intent == IntentType.DeleteProperty ? Match.Empty : AddPropertyRe.Match(sentence);
        if (add.Success)
        {
            var name = add.Groups["name"].Value.Trim('"', '\'');
            command.PropertyName = Regex.IsMatch(name, @"^[A-Za-z_]\w*$") ? ToPascalCase(name) : name;
            if (add.Groups["type"].Success)
                command.PropertyType = add.Groups["type"].Value.Trim();
            command.ClassName = ToPascalCase(add.Groups["class"].Value);
            foreach (var word in (add.Groups["modifiers"].Value + " " + add.Groups["late"].Value)
                         .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                command.PropertyModifiers |= ModifierWord(word);
            if (add.Groups["default"].Success)
            {
                command.PropertyDefault = add.Groups["default"].Value;

                // `add property Qty = 5 to Order`: with no type, the value's own type, not string.
                if (!add.Groups["type"].Success)
                    command.PropertyType = TypeOfValue(command.PropertyDefault);
            }
            return;
        }

        // remove/delete property <name> from <class>
        var remove = Regex.Match(sentence,
            @"\b(?:remove|delete|drop)\s+(?:the\s+)?(?:(?:property|field)\s+(?<property>[A-Za-z_]\w*)|(?<property>[A-Za-z_]\w*)\s+(?:property|field))"
            + @"\s+(?:from|of|in|on)\s+(?:the\s+)?(?:class\s+)?(?<class>[A-Za-z_]\w*)",
            RegexOptions.IgnoreCase);
        if (remove.Success)
        {
            command.PropertyName = ToPascalCase(remove.Groups["property"].Value);
            command.ClassName = ToPascalCase(remove.Groups["class"].Value);
        }
    }

    /// <summary>The type a default value typed with no type is: a whole number is an int, a number
    /// with a decimal point a decimal, true or false a bool, and anything else (quoted or not) a string.</summary>
    internal static string TypeOfValue(string value)
    {
        if (Regex.IsMatch(value, @"^[+-]?\d+$"))
            return "int";
        if (Regex.IsMatch(value, @"^[+-]?\d+\.\d+$"))
            return "decimal";
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            return "bool";
        return "string";
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

    // "create class Order ... and add method Ship to Customer": the name patterns read the "to
    // Customer" as the class, so the clause is cut off before the name is read.
    private static string WithoutAddMethodClause(string sentence)
    {
        var clause = Regex.Match(sentence,
            @"\s+and\s+(?:add|create|generate)\s+(?:(?:an?|new)\s+)?(?:abstract\s+)?(?:method|function)\b",
            RegexOptions.IgnoreCase);
        return clause.Success ? sentence[..clause.Index] : sentence;
    }

    public static void ExtractMethods(string sentence, ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return;

        var match = Regex.Match(
            sentence,
            // functions/operations too: the member list is read as typed, so the normaliser no
            // longer rewrites them to methods first.
            @"\b(?:methods?|functions?|operations?)\b\s+(?<methods>.+)$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return;

        var methodsText = match.Groups["methods"].Value.Trim();

        // "methods Save, Load implements IEntity": the clause after the last method belongs to
        // the class. Left in, the last token was "Load implements IEntity" and Load was dropped.
        var clause = Regex.Match(methodsText,
            @"\s+(?:implements?|inherits?|extends|derived\s+from|base\s+class)\b",
            RegexOptions.IgnoreCase);
        if (clause.Success)
            methodsText = methodsText[..clause.Index];

        // Methods can be separated by commas, semicolons, or the word "and"
        // (e.g. "AddEmployee and EmployeeList method").
        foreach (var token in MethodEntries(methodsText))
        {
            var cleaned = CleanMethodToken(token);
            if (!TryParseMethod(cleaned, out var method, out var error))
            {
                // "add method Ship to Order" inside create class Order: the "to Order" names the
                // class being created, or another one, which this command cannot change.
                var target = Regex.Match(cleaned,
                    @"^(?<entry>.+?)\s+(?:to|for|in|on)\s+(?:the\s+)?(?:class\s+)?(?<name>\w+)$",
                    RegexOptions.IgnoreCase);
                if (target.Success
                    && TryParseMethod(target.Groups["entry"].Value, out method, out error) && method != null)
                {
                    var name = target.Groups["name"].Value;
                    var own = !string.IsNullOrWhiteSpace(command.ClassName) ? command.ClassName : command.InterfaceName;
                    if (!name.Equals(own, StringComparison.OrdinalIgnoreCase))
                    {
                        command.IgnoredMethods.Add((cleaned,
                            $"this command creates '{own}', not '{name}'; create it first, then type 'add method {method.Name} to {name}'."));
                        continue;
                    }
                }
                else
                {
                    command.IgnoredMethods.Add((cleaned, error!));
                    continue;
                }
            }

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

        // Leading verb ("add EmployeeList" -> "EmployeeList"), but not when the verb is the
        // method's own name: "Add with parameters x int" and "Add returns int" lost their method.
        token = Regex.Replace(
            token,
            $@"^(?:add|create|generate)\s+(?!(?:{MethodParameterParser.EnglishLead}|returns?)\b)",
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

    /// <summary>
    /// The entries of a method list: split on commas, semicolons and "and", but not inside
    /// parentheses or angle brackets (<c>Apply(decimal rate, string code)</c>,
    /// <c>Map returns Dictionary&lt;string, int&gt;</c>), and with the entries of an English parameter
    /// list kept on their method: in <c>Ship with parameters address string, express bool</c> the
    /// second entry is a parameter, as it is two words with no parentheses and no "returns".
    /// </summary>
    internal static List<string> MethodEntries(string methodsText)
    {
        var raw = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < methodsText.Length; i++)
        {
            var c = methodsText[i];
            if (c is '(' or '<' or '[') depth++;
            else if (c is ')' or '>' or ']') depth--;
            else if (depth == 0 && (c is ',' or ';' || IsAndAt(methodsText, i)))
            {
                raw.Add(methodsText[start..i]);
                start = c is ',' or ';' ? i + 1 : i + 3;
            }
        }
        raw.Add(methodsText[start..]);

        var entries = new List<string>();
        foreach (var entry in raw.Select(e => e.Trim()).Where(e => e.Length > 0))
        {
            var continuesParameters = entries.Count > 0
                && Regex.IsMatch(entries[^1], $@"\s{MethodParameterParser.EnglishLead}\s", RegexOptions.IgnoreCase)
                && Regex.IsMatch(entry, EnglishParameterEntry, RegexOptions.IgnoreCase)
                && !Regex.IsMatch(entry, @"\s+methods?$", RegexOptions.IgnoreCase);

            if (continuesParameters)
                entries[^1] += ", " + entry;
            else
                entries.Add(entry);
        }

        return entries;
    }

    // One entry of an English parameter list, "name type", the last one perhaps with the method's
    // return type: "map Dictionary<string, int>", "express bool returns bool". "Print method" is a
    // method, not a parameter called Print of the type method.
    private static readonly string EnglishParameterEntry =
        $@"^[A-Za-z_]\w*\s+{MethodParameterParser.TypePattern}(?:\s+returns?\s+{MethodParameterParser.TypePattern})?$";

    // " and " as a word, at i.
    private static bool IsAndAt(string text, int i) =>
        i + 3 <= text.Length
        && string.Compare(text, i, "and", 0, 3, StringComparison.OrdinalIgnoreCase) == 0
        && (i == 0 || char.IsWhiteSpace(text[i - 1]))
        && (i + 3 == text.Length || char.IsWhiteSpace(text[i + 3]));

    private const string ReturnTypePattern = MethodParameterParser.TypePattern;

    public static MethodDefinition? ParseMethod(string token) =>
        TryParseMethod(token, out var method, out _) ? method : null;

    /// <summary>
    /// A method entry: <c>Name</c>, <c>Name returns T</c>, <c>Name(type name, ...) [returns T]</c> or
    /// <c>Name [returns T] with parameters name type, ...</c>. False, with the reason, when the
    /// entry cannot be read (a parameter list that does not parse, or words that fit no form); true
    /// with a null method only for an empty entry.
    /// </summary>
    public static bool TryParseMethod(string token, out MethodDefinition? method, out string? error)
    {
        method = null;
        error = null;

        if (string.IsNullOrWhiteSpace(token))
            return true;

        token = token.Trim();

        // Method(params) returns Type
        var fullMatch = Regex.Match(
            token,
            $@"^(?<name>\w+)\s*\((?<params>.*)\)\s*(?:returns?\s+(?<type>{ReturnTypePattern}))?$",
            RegexOptions.IgnoreCase);

        if (fullMatch.Success)
            return WithParameters(fullMatch, fullMatch.Groups["params"].Value,
                MethodParameterParser.Order.TypeFirst, token, out method, out error);

        // Method [returns Type] with parameters name type, ... [returns Type]
        var englishMatch = Regex.Match(
            token,
            $@"^(?<name>\w+)(?:\s+returns?\s+(?<type>{ReturnTypePattern}))?\s+{MethodParameterParser.EnglishLead}\s+(?<params>.+?)"
                + $@"(?:\s+returns?\s+(?<type>{ReturnTypePattern}))?$",
            RegexOptions.IgnoreCase);

        if (englishMatch.Success)
            return WithParameters(englishMatch, englishMatch.Groups["params"].Value,
                MethodParameterParser.Order.NameFirst, token, out method, out error);

        // Method returns Type
        var returnMatch = Regex.Match(
            token,
            $@"^(?<name>\w+)\s+returns?\s+(?<type>{ReturnTypePattern})$",
            RegexOptions.IgnoreCase);

        if (returnMatch.Success)
        {
            method = new MethodDefinition
            {
                Name = returnMatch.Groups["name"].Value,
                ReturnType = returnMatch.Groups["type"].Value.Trim()
            };
            return true;
        }

        // Just method name
        var nameMatch = Regex.Match(
            token,
            @"^(?<name>\w+)$");

        if (nameMatch.Success)
        {
            method = new MethodDefinition
            {
                Name = nameMatch.Groups["name"].Value,
                ReturnType = "void"
            };
            return true;
        }

        error = $"'{token}' is not a method; type a name, Name(type param), or Name returns Type.";
        return false;
    }

    private static bool WithParameters(
        Match match, string parameterText, MethodParameterParser.Order order, string token,
        out MethodDefinition? method, out string? error)
    {
        method = null;
        if (!MethodParameterParser.TryParse(parameterText, order, out var parameters, out error))
            return false;

        method = new MethodDefinition
        {
            Name = match.Groups["name"].Value,
            ReturnType = match.Groups["type"].Success ? match.Groups["type"].Value.Trim() : "void",
            Parameters = parameters,
        };
        return true;
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
            // The name is never "class" itself: "create class Model" names Model.
            @"i\s+need\s+(?:an?\s+)?(?!class\b)(\w+)\s+(?:class|entity|model|dto)",
            @"create\s+(?:an?\s+)?(?!class\b)(\w+)\s+(?:class|entity|model|dto)",
            @"generate\s+(?:an?\s+)?(?!class\b)(\w+)\s+(?:class|entity|model|dto)",
            @"build\s+(?:an?\s+)?(?!class\b)(\w+)\s+(?:class|entity|model|dto)",
            @"make\s+(?:an?\s+)?(?!class\b)(\w+)\s+(?:class|entity|model|dto)",

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
            @"(?:with(?!\s+parameters?\b)|having|containing|contains)\s+(.+)",
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

        var entries = TypedEntries(propertySection, out var quoted);

        // Any entry read as a name and a type - built in or not - means the list is typed, as
        // "Name string, Email" or "Name, Kind Buyer" are: an untyped entry beside one is reported.
        // Only a list with no typed entry at all goes to the fallback parser, which infers types.
        if (entries.Any(e => e.Matches.Count > 0))
        {
            foreach (var (entry, matches) in entries)
            {
                for (var i = 0; i < matches.Count; i++)
                {
                    var match = matches[i];
                    var property = new PropertyDefinition
                    {
                        Name = Capitalised(match.Groups["name"].Value),
                        Type = match.Groups["type"].Value
                    };

                    // The words before the first name in the entry ("readonly Id int"): the
                    // modifiers apply to it, any other word is reported.
                    if (i == 0 && ReadWordsBeforeName(entry[..match.Groups["name"].Index], property) is { Length: > 0 } before)
                        command.IgnoredWordsBefore.Add((property.Name, before));

                    // The words up to the next property in the entry, or to its end.
                    var end = i + 1 < matches.Count ? matches[i + 1].Index : entry.Length;
                    var ignored = ReadWordsAfterType(entry[(match.Index + match.Length)..end], property, quoted);
                    command.Properties.Add(property);
                    if (ignored.Length > 0)
                        command.IgnoredWords.Add((property.Name, ignored));
                }
            }

            // A typed entry was read, so the fallback parser below never runs: an entry that is not
            // a name and a type goes nowhere. It is recorded so the handler can say so instead of
            // the user finding out by looking at the file.
            command.IgnoredProperties.AddRange(Ignored(entries, quoted));

            return;
        }

        // fallback to old logic
        var tokens = SplitEntries(propertySection);

        foreach (var token in tokens)
        {
            var property = ParseProperty(token);
            property.Name = Capitalised(property.Name);

            if (!string.IsNullOrWhiteSpace(property.Name))
                command.Properties.Add(property);
        }
    }

    /// <summary>
    /// The entries in a property list that the built-in-matching path could not read: neither a
    /// built-in typed property nor a <c>Name Type</c> one.
    /// </summary>
    public static IReadOnlyList<string> IgnoredPropertyEntries(string sentence)
    {
        var section = ExtractPropertySection(sentence);

        if (string.IsNullOrWhiteSpace(section))
            return [];

        var entries = TypedEntries(section, out var quoted);

        // No typed entry means the fallback parser ran and read the whole list, so nothing was
        // skipped over.
        return entries.Any(e => e.Matches.Count > 0) ? Ignored(entries, quoted) : [];
    }

    /// <summary>The built-in types a create class list reads: the ones <c>TypeValidator</c> supports
    /// that name a type on their own (a bare <c>list</c> or <c>array</c> names no element type).</summary>
    /// <remarks>
    /// <c>byte[]</c> was reported as "only built-in types (... also with ?, [] or in List&lt;&gt;)",
    /// because byte was not in this list while add property accepted it.
    /// </remarks>
    private const string BuiltInType =
        "(?:string|int|long|short|byte|sbyte|ushort|uint|ulong|char|bool|boolean|decimal|double|float"
        + "|datetime|date|timespan|time|guid|uuid)";

    /// <summary>
    /// A type that is not built in, as a create class list may give it: a name, qualified or not
    /// (<c>Shape</c>, <c>System.Uri</c>), generic (<c>Dictionary&lt;string, int&gt;</c>, two levels
    /// deep at most), then any <c>?</c> and <c>[]</c> (<c>Shape?[]</c>). Whether it names a type is
    /// the handler's to check, by binding it in the project as add property does: the extractor
    /// does not know the project.
    /// </summary>
    private const string NamedType = @"(?:[A-Za-z_][\w.]*(?:<(?:[^<>]|<[^<>]*>)*>)?(?:\?|\[,*\])*)";

    /// <summary>
    /// An entry with no built-in type that is <c>Name Type</c>, after any modifiers: a property of
    /// a type add property also takes (<c>Kind Buyer</c>, <c>Home Shape</c>,
    /// <c>Created DateTimeOffset</c>).
    /// </summary>
    /// <remarks>
    /// A name that is itself a type word (<c>int Id</c>, <c>datetime Created</c>) is the other
    /// order, which the fallback parser reads; see <see cref="IsTypeWord"/>.
    /// </remarks>
    private static readonly Regex NamedTypeEntry = new(
        $@"^(?:(?:propert(?:y|ies)|fields?|attributes?):?\s+)?(?:(?:readonly|private|static|required)\s+)*(?<name>[A-Za-z_]\w*)\s+(?<type>{NamedType})(?!\S)",
        RegexOptions.IgnoreCase);

    private static readonly Regex BuiltInTypeWord = new($"^{BuiltInType}$", RegexOptions.IgnoreCase);

    /// <summary>Whether <paramref name="word"/> is a built-in type word or a C# keyword, and so
    /// cannot be a property's name.</summary>
    private static bool IsTypeWord(string word) =>
        BuiltInTypeWord.IsMatch(word)
        || Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(word) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None;

    /// <summary>
    /// The entries of a property list: split at a comma, a semicolon or <c>and</c>, but not at a
    /// comma inside angle brackets, which belongs to a generic type (<c>Dictionary&lt;string, int&gt;</c>).
    /// </summary>
    private static string[] SplitEntries(string section)
    {
        var shielded = new System.Text.StringBuilder(section.Length);
        var depth = 0;
        foreach (var c in section)
        {
            depth = c switch { '<' => depth + 1, '>' when depth > 0 => depth - 1, _ => depth };
            shielded.Append(c == ',' && depth > 0 ? '\u0003' : c);
        }

        return
        [
            .. Regex.Split(shielded.ToString(), @"\s*(?:,|;|\band\b)\s*", RegexOptions.IgnoreCase)
                .Select(t => t.Replace('\u0003', ','))
        ];
    }

    /// <summary>
    /// <c>Name type</c>, where the type is built in and may be nullable (<c>string?</c>), an array
    /// (<c>int[]</c>) or a list (<c>List&lt;int&gt;</c>).
    /// </summary>
    /// <remarks>
    /// The name must start the entry or follow a space, and the type must end it or come before a
    /// space. Without both, <c>Email string?</c> matched as <c>Email string</c> and lost its
    /// <c>?</c>, and <c>2x int</c> matched as <c>x int</c> and made a property X nobody typed.
    /// </remarks>
    private static readonly Regex TypedEntry = new(
        $@"(?<!\S)(?<name>[A-Za-z_]\w*)\s+(?<type>{BuiltInType}(?:\?|\[\])?|List<\s*{BuiltInType}\??\s*>)(?!\S)",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// The entries of a property list, each with the built-in typed properties in it - or, for an
    /// entry with none, its <c>Name Type</c> property (<see cref="NamedTypeEntry"/>).
    /// </summary>
    /// <remarks>
    /// A separator inside quoted text is part of a default value (<c>default "Black and White"</c>),
    /// not the end of an entry, and a name and type inside it are not a property. So each quoted
    /// value (<see cref="QuotedText"/>) is replaced by a marker before the list is split; the
    /// entries keep the markers, and <paramref name="quoted"/> holds the values they stand for
    /// (<see cref="Unquoted"/> puts them back). A quote left open matches nothing and shields nothing.
    /// </remarks>
    private static List<(string Entry, IReadOnlyList<Match> Matches)> TypedEntries(
        string section, out List<string> quoted)
    {
        var kept = new List<string>();
        var shielded = QuotedText.Replace(section, m =>
        {
            kept.Add(m.Value);
            return $"\u0002{kept.Count - 1}\u0002";
        });
        quoted = kept;

        return
        [
            .. SplitEntries(shielded)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Select(t => (t, (IReadOnlyList<Match>)[.. TypedEntry.Matches(t).Cast<Match>()]))
                .Select(e => e.Item2.Count > 0
                    || NamedTypeEntry.Match(e.t) is not { Success: true } named
                    || IsTypeWord(named.Groups["name"].Value)
                    ? e
                    : (e.t, (IReadOnlyList<Match>)[named]))
        ];
    }

    /// <summary>The text with each marker <see cref="TypedEntries"/> put in replaced by its quoted value.</summary>
    private static string Unquoted(string text, List<string> quoted) =>
        Regex.Replace(text, "\u0002(\\d+)\u0002", m => quoted[int.Parse(m.Groups[1].Value)]);

    /// <summary>A word (a quoted value is one word: <see cref="TypedEntries"/> made it a marker).</summary>
    private static readonly Regex WordAfterType = new("\\S+");

    /// <summary>The label words a list may repeat before a name ("property Name string"), which
    /// say nothing more.</summary>
    private static readonly Regex ListLabel = new(
        @"^(?:propert(?:y|ies)|fields?|attributes?)?:?$", RegexOptions.IgnoreCase);

    /// <summary>
    /// Reads the words before a property's name in a create class list: <c>readonly</c>,
    /// <c>private</c>, <c>static</c> and <c>required</c> apply to it. Returns the other words,
    /// which nothing reads.
    /// </summary>
    private static string ReadWordsBeforeName(string text, PropertyDefinition property)
    {
        var ignored = new List<string>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var modifier = ModifierWord(word);
            if (modifier != PropertyModifiers.None)
                property.Modifiers |= modifier;
            else if (!ListLabel.IsMatch(word))
                ignored.Add(word);
        }

        return string.Join(' ', ignored);
    }

    /// <summary>The modifier <paramref name="word"/> names, or None.</summary>
    private static PropertyModifiers ModifierWord(string word) => word.ToLowerInvariant() switch
    {
        "readonly" => PropertyModifiers.ReadOnly,
        "private" => PropertyModifiers.Private,
        "static" => PropertyModifiers.Static,
        "required" => PropertyModifiers.Required,
        _ => PropertyModifiers.None
    };

    /// <summary>
    /// Reads the words after a property's type: <c>readonly</c>, <c>private</c>, <c>static</c>,
    /// <c>required</c>, and <c>default &lt;value&gt;</c> or <c>= &lt;value&gt;</c>, where the value is
    /// one word or one quoted text. Returns the other words, which nothing reads.
    /// </summary>
    /// <remarks>
    /// The value is kept as typed on <see cref="PropertyDefinition.Initializer"/>; the create class
    /// handler checks it against the type and turns it into a C# expression, or refuses it.
    /// </remarks>
    private static string ReadWordsAfterType(string text, PropertyDefinition property, List<string> quoted)
    {
        var words = WordAfterType.Matches(text).Select(m => Unquoted(m.Value, quoted)).ToList();
        var ignored = new List<string>();

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var modifier = ModifierWord(word);

            if (modifier != PropertyModifiers.None)
            {
                property.Modifiers |= modifier;
                continue;
            }

            if ((word.Equals("default", StringComparison.OrdinalIgnoreCase) || word == "=")
                && i + 1 < words.Count && property.Initializer is null)
            {
                property.Initializer = words[++i];
                continue;
            }

            ignored.Add(word);
        }

        return string.Join(' ', ignored);
    }

    private static IReadOnlyList<string> Ignored(
        List<(string Entry, IReadOnlyList<Match> Matches)> entries, List<string> quoted) =>
        [.. entries.Where(e => e.Matches.Count == 0).Select(e => Unquoted(e.Entry, quoted))];

    public static string ExtractPropertySection(string sentence)
    {
        var match = Regex.Match(sentence,
                // "with parameters" starts a method's parameter list, not the properties.
                @"(?:properties|with properties|with(?!\s+parameters?\b)|having|contains)\s+(.+)", RegexOptions.IgnoreCase);

        if (!match.Success)
            return string.Empty;

        var section = match.Groups[1].Value;

        // "with property Name string" and "with fields ...": the leading noun is the list's
        // label, not its first entry. It was read as one ("property Name string" ignored, as a
        // non-primitive) - but only when a name and a type follow, so a property that is itself
        // called Property is kept.
        section = Regex.Replace(section, @"^(?:propert(?:y|ies)|fields?|attributes?)\s+(?=\S+\s+\S)",
            string.Empty, RegexOptions.IgnoreCase);

        // The list ends where a methods clause or a base-type clause starts: those are read by
        // ExtractMethods and ExtractInheritanceInfo, and their words were reported as ignored.
        // A clause word inside a quoted default ("extends the base") is part of the value.
        var stopMatch = Regex.Match(WithoutQuotedText(section),
            $@"\b(?:(?:and\s+)?(?:with\s+)?(?:having\s+)?(?:methods|functions)|(?:with|and)\s+(?:method|function)|and\s+(?:add|create|generate)\s+(?:(?:an?|new)\s+)?(?:abstract\s+)?(?:method|function)|{BaseTypeClause})\b",
            RegexOptions.IgnoreCase);
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

    internal static string ToPascalCase(string value)
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

    // The name after "project", or after the type noun of a type phrased without the word
    // ("create worker service MyWorker", "create class library MyLib"). Quoted, or a dotted .NET
    // name; never one of the words that start the folder or framework clause, nor another of the
    // nouns ("create classlib project Shop" is Shop, not "Project").
    private static readonly Regex ProjectNameRe = new(
        @"\b(?:project|service|library|classlib|lib|app|application)(?:\s+(?:named|called))?\s+"
        + @"(?!(?:in|at|for|with|targeting|using|as|type|on|to)\b)(?!net\d)"
        + @"(?!(?:project|service|library|classlib|lib|app|application)\b)"
        + @"(?<name>""[^""]+""|'[^']+'|[A-Za-z0-9_][A-Za-z0-9_\-\.]*)",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// A project name as typed, unquoted, with a sentence's full stop dropped and its first letter
    /// capitalised - "shop" becomes Shop, as "create class shop" makes Shop - and nothing else
    /// changed: Data.Core and my_lib keep their dots and underscores, which are part of the name.
    /// </summary>
    internal static string ProjectNameAsTyped(string raw)
    {
        var name = raw.Trim().Trim('"', '\'').Trim().TrimEnd('.');
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }

    public static void ExtractProjectInfo(string sentence, ParsedCommand command)
    {
        var nameMatch = ProjectNameRe.Match(sentence);
        if (nameMatch.Success)
            command.ProjectName = ProjectNameAsTyped(nameMatch.Groups["name"].Value);

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
