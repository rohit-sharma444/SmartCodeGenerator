using NaturalLanguageEngine.Knowledge.Models;

namespace NaturalLanguageEngine.Knowledge.Providers;

/// <summary>
/// A self-contained, in-memory English language provider.
/// Ships a compact lexicon, stop-word list, contraction map,
/// abbreviation set, synonym sets and a small irregular-form table
/// so the engine works out of the box with no external resources.
/// </summary>
public sealed class EnglishLanguageProvider : ILanguageProvider
{
    public IEnumerable<WordEntry> LoadWords()
    {
        // Determiners
        foreach (var w in new[] { "the", "a", "an", "this", "that", "these", "those", "my", "your", "his", "her", "its", "our", "their", "some", "any", "each", "every", "no" })
            yield return Make(w, "determiner");

        // Pronouns
        foreach (var w in new[] { "i", "you", "he", "she", "it", "we", "they", "me", "him", "us", "them", "who", "whom", "which", "what", "myself", "yourself" })
            yield return Make(w, "pronoun");

        // Prepositions
        foreach (var w in new[] { "in", "on", "at", "by", "for", "with", "about", "against", "between", "into", "through", "during", "before", "after", "above", "below", "to", "from", "up", "down", "of", "off", "over", "under", "near" })
            yield return Make(w, "preposition");

        // Conjunctions
        foreach (var w in new[] { "and", "or", "but", "nor", "so", "yet", "because", "although", "while", "if", "unless", "since", "than", "whereas" })
            yield return Make(w, "conjunction");

        // Auxiliary / modal verbs
        foreach (var w in new[] { "is", "am", "are", "was", "were", "be", "been", "being", "do", "does", "did", "have", "has", "had", "will", "would", "shall", "should", "can", "could", "may", "might", "must", "jumps" })
            yield return Make(w, "verb");

        // Common verbs
        foreach (var w in new[] { "go", "goes", "went", "make", "makes", "made", "take", "takes", "took", "see", "saw", "come", "came", "want", "wants", "wanted", "send", "sends", "sent", "give", "gives", "gave", "book", "books", "booked", "buy", "buys", "bought", "find", "found", "show", "shows", "tell", "told", "ask", "asks", "asked", "need", "needs", "needed", "like", "likes", "liked", "call", "calls", "called", "open", "opens", "opened", "close", "closes", "set", "schedule", "schedules", "scheduled", "play", "plays", "played", "create", "creates", "created", "build", "builds", "built", "run", "runs", "ran", "jumps",
            "add", "adds", "added", "list", "lists", "listed", "generate", "generates", "generated",
            "delete", "deletes", "deleted", "remove", "removes", "removed", "rename", "renames", "renamed",
            "analyze", "analyzes", "analyzed", "validate", "validates", "validated", "use", "uses", "used",
            "restore", "restores", "restored" })
            yield return Make(w, "verb");

        // Common nouns
        foreach (var w in new[] { "meeting", "flight", "report", "email", "message", "table", "car", "house", "city", "country", "person", "people", "company", "team", "project", "task", "time", "day", "week", "month", "year", "morning", "afternoon", "evening", "night", "ticket", "order", "price", "money", "phone", "computer", "engine", "language", "word", "sentence", "document", "file", "name", "place", "thing", "way", "dog", "cat", "ball", "fox" })
            yield return Make(w, "noun");

        // Business-domain nouns (so build app: descriptions extract entities).
        // Includes -ing gerund forms that the POS verb-suffix heuristic would
        // otherwise mis-tag as verbs (billing, shipping, accounting, ...).
        foreach (var w in new[] { "payroll", "application", "system", "employee", "employer", "salary", "wage", "invoice", "payment", "customer", "client", "supplier", "vendor", "product", "account", "transaction", "department", "manager", "timesheet", "tax", "deduction", "bonus", "benefit", "expense", "budget", "ledger", "inventory", "warehouse", "shipment", "user", "role", "permission", "contract", "subscription", "refund", "receipt", "balance", "record", "dashboard", "notification", "billing", "shipping", "accounting", "invoicing", "onboarding", "booking", "reporting", "scheduling", "pricing", "listing", "training" })
            yield return Make(w, "noun");

        // Adjectives
        foreach (var w in new[] { "big", "small", "red", "blue", "green", "fast", "slow", "good", "bad", "new", "old", "high", "low", "happy", "sad", "important", "urgent", "quick", "great", "nice", "early", "late", "next", "last", "first", "second" })
            yield return Make(w, "adjective");

        // Adverbs
        foreach (var w in new[] { "quickly", "slowly", "very", "really", "soon", "now", "later", "today", "tomorrow", "yesterday", "here", "there", "always", "never", "often", "sometimes", "well", "badly" })
            yield return Make(w, "adverb");
    }

    public IEnumerable<SynonymSet> LoadSynonyms()
    {
        yield return new SynonymSet { Canonical = "buy", Words = new[] { "buy", "purchase", "acquire" } };
        yield return new SynonymSet { Canonical = "big", Words = new[] { "big", "large", "huge", "enormous" } };
        yield return new SynonymSet { Canonical = "small", Words = new[] { "small", "tiny", "little" } };
        yield return new SynonymSet { Canonical = "fast", Words = new[] { "fast", "quick", "rapid", "speedy" } };
        yield return new SynonymSet { Canonical = "happy", Words = new[] { "happy", "glad", "joyful", "pleased" } };
        yield return new SynonymSet { Canonical = "schedule", Words = new[] { "schedule", "book", "arrange" } };
    }

    public IDictionary<string, string> LoadContractions()
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["can't"] = "cannot",
            ["won't"] = "will not",
            ["don't"] = "do not",
            ["doesn't"] = "does not",
            ["didn't"] = "did not",
            ["isn't"] = "is not",
            ["aren't"] = "are not",
            ["wasn't"] = "was not",
            ["weren't"] = "were not",
            ["i'm"] = "i am",
            ["you're"] = "you are",
            ["we're"] = "we are",
            ["they're"] = "they are",
            ["it's"] = "it is",
            ["i've"] = "i have",
            ["you've"] = "you have",
            ["we've"] = "we have",
            ["i'll"] = "i will",
            ["you'll"] = "you will",
            ["we'll"] = "we will",
            ["let's"] = "let us",
            ["that's"] = "that is",
            ["there's"] = "there is",
            ["what's"] = "what is",
            ["who's"] = "who is",
        };

    public ISet<string> LoadStopWords()
        => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "if", "then", "of", "to", "in",
            "on", "at", "by", "for", "with", "as", "is", "are", "was", "were",
            "be", "been", "being", "this", "that", "these", "those", "it", "its",
            "i", "you", "he", "she", "we", "they", "me", "him", "her", "us", "them",
            "do", "does", "did", "have", "has", "had", "not", "no", "so", "up", "out",
            "about", "into", "over", "again", "from", "down"
        };

    public ISet<string> LoadAbbreviations()
        => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Dr", "Mr", "Mrs", "Ms", "Prof", "Sr", "Jr", "St", "vs", "etc",
            "Inc", "Ltd", "Corp", "Co", "e.g", "i.e", "a.m", "p.m", "U.S", "U.K"
        };

    public IDictionary<string, string[]> LoadIrregularWords()
        => new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["be"] = new[] { "am", "is", "are", "was", "were", "been", "being" },
            ["go"] = new[] { "goes", "went", "gone", "going" },
            ["have"] = new[] { "has", "had", "having" },
            ["do"] = new[] { "does", "did", "done", "doing" },
            ["make"] = new[] { "makes", "made", "making" },
            ["take"] = new[] { "takes", "took", "taken", "taking" },
            ["see"] = new[] { "sees", "saw", "seen", "seeing" },
            ["come"] = new[] { "comes", "came", "coming" },
            ["give"] = new[] { "gives", "gave", "given", "giving" },
            ["buy"] = new[] { "buys", "bought", "buying" },
            ["find"] = new[] { "finds", "found", "finding" },
            ["run"] = new[] { "runs", "ran", "running" },
            ["child"] = new[] { "children" },
            ["person"] = new[] { "people" },
            ["mouse"] = new[] { "mice" },
            ["foot"] = new[] { "feet" },
        };

    public ISet<string> LoadKnownLocations()
        => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Cities
            "London", "Paris", "Berlin", "Madrid", "Rome", "Tokyo", "Beijing",
            "Moscow", "Dublin", "Amsterdam", "Vienna", "Brussels", "Lisbon",
            "Toronto", "Vancouver", "Sydney", "Melbourne", "Singapore",
            "Chicago", "Boston", "Seattle", "Denver", "Miami", "Austin",
            "York", "Angeles", "Francisco", "Delhi", "Mumbai", "Shanghai",
            "Cairo", "Nairobi", "Lagos", "Dubai",
            // Countries / regions
            "England", "France", "Germany", "Spain", "Italy", "Japan",
            "China", "Russia", "Ireland", "Canada", "Australia", "India",
            "Brazil", "Mexico", "Egypt", "Kenya", "Nigeria", "Scotland",
            "Wales", "Europe", "Asia", "Africa", "America",
        };

    public ISet<string> LoadKnownOrganizations()
        => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Well-known organization names
            "Google", "Microsoft", "Amazon", "Apple", "Facebook", "Meta",
            "Netflix", "Tesla", "IBM", "Intel", "Oracle", "Samsung", "Sony",
            "NASA", "FBI", "CIA", "WHO", "UN", "UNESCO", "NATO",
            // Suffix tokens that mark a preceding proper-noun phrase as an org
            "Inc", "Corp", "Ltd", "LLC", "Co", "Company", "Group",
            "University", "Institute", "Foundation", "Association",
        };

    private static WordEntry Make(string word, string pos)
        => new()
        {
            Word = word,
            Lemma = word,
            PartOfSpeech = pos,
            Tags = Array.Empty<string>()
        };
}
