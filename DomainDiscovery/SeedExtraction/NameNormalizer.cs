namespace DomainDiscovery.SeedExtraction;

/// <summary>Turns a raw entity phrase into a PascalCase C# identifier stub.</summary>
public static class NameNormalizer
{
    public static string ToEntityName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Take the head noun (last word) of a phrase like "library member".
        var word = text.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? string.Empty;

        // Keep only letters/digits.
        var cleaned = new string(word.Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length == 0)
            return string.Empty;

        // Prose mentions "employee" and "employees" as one concept, so fold them onto
        // one name — otherwise both become separate entities, and separate classes.
        var singular = Singularize(cleaned.ToLowerInvariant());

        return char.ToUpperInvariant(singular[0]) + singular[1..];
    }

    /// <summary>
    /// A comparison key under which a UK and a US spelling of one name are equal: Enrolment and
    /// Enrollment, Instalment and Installment, Fulfilment and Fulfillment. Only the -lment/-llment
    /// pair, the one discovery has actually produced both ways at once; a key, never a rename.
    /// </summary>
    public static string SpellingKey(string name)
        => (name ?? string.Empty).ToLowerInvariant().Replace("llment", "lment", StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="name"/> is something C# can declare as a type or member name.
    /// The normalizers strip punctuation but cannot stop a name starting with a digit
    /// ("2nd course" -> "2ndCourse"), which emits CS1519 in the generated class.
    /// </summary>
    public static bool IsValidIdentifier(string name)
        => !string.IsNullOrEmpty(name)
           && (char.IsLetter(name[0]) || name[0] == '_')
           && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// Turns <paramref name="text"/> into a PascalCase property name: every word is kept and
    /// capitalized ("prep time" -> "PrepTime"). Unlike <see cref="ToEntityName"/> this keeps
    /// plurals, since a field may legitimately be one ("Servings", "Ingredients"), and keeps
    /// every word, since a field name is not a head noun.
    /// </summary>
    public static string ToFieldName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Split on anything that cannot appear in an identifier, so "due-date" and
        // "prep time" both yield two words rather than one run-on.
        var words = text.Split(
            text.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(),
            StringSplitOptions.RemoveEmptyEntries);

        return string.Concat(words.Select(
            word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    /// <summary>
    /// As <see cref="ToFieldName"/>, for a name read from the user's data rather than typed: a
    /// leading number is moved behind the words that follow it ("2ndRef" as "Ref2nd",
    /// "2024 Sales" as "Sales2024", "3D model" as "Model3D"), and a name that is only a number,
    /// or whose number cannot be told apart from the word after it, gets "Field" in front.
    /// </summary>
    /// <remarks>
    /// A C# identifier cannot start with a digit, and a column whose name did was dropped along
    /// with every value it held. A name the user TYPES is refused instead, so they can choose
    /// the name; a column has nobody to ask. Moving the number keeps the name readable as the
    /// source spelled it, where a prefix would start every such field with the same word.
    /// </remarks>
    public static string ToFieldNameFromData(string text)
        => StartingWithALetter(ToFieldName(text));

    /// <summary>See <see cref="ToFieldNameFromData"/>.</summary>
    private static string StartingWithALetter(string name)
    {
        if (name.Length == 0 || !char.IsDigit(name[0]))
            return name;

        var number = System.Text.RegularExpressions.Regex.Match(name, @"^\d+[A-Za-z]{0,2}(?=[A-Z]|$)").Value;
        var rest = name[number.Length..];

        return number.Length > 0 && rest.Length > 0 && char.IsLetter(rest[0])
            ? rest + number
            : "Field" + name;
    }

    /// <summary>
    /// Turns a multi-word concept into a PascalCase type name, keeping every word and
    /// singularizing only the last ("Cash register" -> "CashRegister"). Unlike
    /// <see cref="ToEntityName"/>, which reduces a phrase to its head noun, this preserves
    /// compounds — a wikilink title names one concept, not a noun with modifiers.
    /// </summary>
    public static string ToCompoundEntityName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var words = text
            .Split(text.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(),
                   StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        if (words.Length == 0)
            return string.Empty;

        words[^1] = Singularize(words[^1].ToLowerInvariant());

        return string.Concat(words.Select(
            w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    /// <summary>
    /// Turns text a person typed at the review loop into a class name, keeping the shape they
    /// typed ("CashRegister" -> "CashRegister", "cash register" -> "CashRegister") and
    /// singularizing only the last segment ("BookCategories" -> "BookCategory").
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ToEntityName"/>, which reduces prose to its head noun, and
    /// <see cref="ToCompoundEntityName"/>, which rebuilds a wikilink title, this one is for input
    /// that is ALREADY the name the user wants. Both of the others lowercase a word after its
    /// first character, which silently destroys a compound the user typed themselves — the whole
    /// reason this exists.
    /// </remarks>
    public static string ToUserTypedEntityName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var segments = Segments(text);
        if (segments.Count == 0)
            return string.Empty;

        segments[^1] = SingularizePreservingCase(segments[^1]);

        return string.Concat(segments.Select(
            s => char.ToUpperInvariant(s[0]) + s[1..]));
    }

    /// <summary>
    /// Splits on anything an identifier cannot contain, and again at each PascalCase boundary.
    /// A capital following another capital does not start a segment, so an acronym stays whole.
    /// </summary>
    private static List<string> Segments(string text)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var character in text)
        {
            if (!char.IsLetterOrDigit(character))
            {
                Flush();
                continue;
            }

            // "CashRegister" breaks before R; "CashREGISTER" does not break inside the run.
            if (char.IsUpper(character) && current.Length > 0 && !char.IsUpper(current[^1]))
                Flush();

            current.Append(character);
        }

        Flush();
        return segments;

        void Flush()
        {
            if (current.Length > 0)
                segments.Add(current.ToString());
            current.Clear();
        }
    }

    /// <summary>
    /// <see cref="Singularize"/> works in lowercase, so its answer cannot simply be pasted over
    /// the original. Where it only stripped a suffix the original is truncated ("Books" -> "Book");
    /// the "ies" -> "y" rule is the one rewrite that is not a prefix, so the original's leading
    /// characters are kept and the "y" appended ("Categories" -> "Categor" + "y").
    /// </summary>
    private static string SingularizePreservingCase(string segment)
    {
        var singular = Singularize(segment.ToLowerInvariant());

        if (singular.Length == segment.Length)
            return segment;

        return segment.ToLowerInvariant().StartsWith(singular, StringComparison.Ordinal)
            ? segment[..singular.Length]
            : segment[..(singular.Length - 1)] + singular[^1];
    }

    /// <summary>
    /// A deliberately small English plural-stripper for the lowercase <paramref name="word"/>.
    /// It errs towards leaving a word alone: a missed plural costs a duplicate entity, while
    /// a wrongly stripped one ("address" -> "addres") corrupts a class name.
    /// </summary>
    private static string Singularize(string word)
    {
        // Too short to carry a plural suffix meaningfully ("tax" vs "as", "gas", "bus").
        if (word.Length <= 3)
            return word;

        // Not plurals at all: address, business, gross / status, campus / analysis, basis.
        if (word.EndsWith("ss", StringComparison.Ordinal)
            || word.EndsWith("us", StringComparison.Ordinal)
            || word.EndsWith("is", StringComparison.Ordinal))
            return word;

        // companies -> company, policies -> policy
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal))
            return string.Concat(word.AsSpan(0, word.Length - 3), "y");

        // taxes -> tax, boxes -> box, matches -> match, businesses -> business
        foreach (var suffix in EsSuffixes)
        {
            if (word.EndsWith(suffix, StringComparison.Ordinal))
                return word[..^2];
        }

        // employees -> employee, accounts -> account
        if (word.EndsWith("s", StringComparison.Ordinal))
            return word[..^1];

        return word;
    }

    private static readonly string[] EsSuffixes = { "sses", "shes", "ches", "xes", "zes" };
}
