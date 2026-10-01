namespace DomainDiscovery.SeedExtraction;

/// <summary>
/// The verbs a description names its transaction with, and the record each one makes: a clinic
/// that BOOKS patients keeps Bookings, a library that LENDS books keeps Loans.
/// </summary>
/// <remarks>
/// Its own file so it can be read and extended without reading the extractor. Every verb here is
/// one whose record is almost always a class the app needs; a verb whose record is not ("runs",
/// "teaches") stays out, since an entry costs a class in every app whose description uses it.
/// "sell" is out for that reason: a gym that sells memberships has no Sale, and measured on the
/// online Wikipedia path the extra seed pulled that gym's topic towards schools and colleges.
/// Only the extractor's verb test decides whether a word is being used as a verb at all - "books"
/// in "lends books" is the thing lent, and still a class.
/// </remarks>
public static class TransactionVerbs
{
    private static readonly (string Record, string[] Verbs, string[] Irregular)[] Table =
    [
        ("Booking", ["book"], []),
        ("Reservation", ["reserve"], []),
        ("Loan", ["lend", "loan", "borrow"], ["lent"]),
        ("Rental", ["rent", "hire"], []),
        ("Enrolment", ["enrol", "enroll"], []),
        ("Order", ["order"], []),
        ("Purchase", ["buy", "purchase"], ["bought"]),
        ("Payment", ["pay"], ["paid"]),
        ("Shipment", ["ship"], []),
        ("Delivery", ["deliver"], []),
        ("Subscription", ["subscribe"], []),
    ];

    /// <summary>
    /// Verbs whose record exists only when what they act on is a thing. A van hire firm that
    /// hires vans keeps Rentals; a company that hires staff is employing people, and gave a Rental
    /// class nobody wanted. Kept rather than dropped from the table because without it "that hires
    /// vans" has no record at all and the verb phrase itself becomes a class.
    /// </summary>
    private static readonly string[] ThingsOnly = ["hire"];

    private static readonly Dictionary<string, (string Record, string Verb)> ByForm = BuildForms();

    private static readonly HashSet<string> BaseForms =
        Table.SelectMany(t => t.Verbs).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The record <paramref name="word"/> makes, or null when it is not a form of a verb here.
    /// Matches the base form and its -s, -es, -d, -ed and doubled-consonant -ed forms ("enrolled",
    /// "shipped"), plus the irregular past tenses listed with each verb. Not -ing: "booking" is the
    /// record's own name, and a noun.
    /// </summary>
    public static string? RecordFor(string word)
        => ByForm.TryGetValue((word ?? string.Empty).Trim().ToLowerInvariant(), out var found) ? found.Record : null;

    /// <summary>
    /// Whether <paramref name="word"/> is a verb whose record exists only when its object is a
    /// thing, not a person ("hires vans", not "hires staff").
    /// </summary>
    public static bool MakesNoRecordForAPerson(string word)
        => ByForm.TryGetValue((word ?? string.Empty).Trim().ToLowerInvariant(), out var found)
           && ThingsOnly.Contains(found.Verb);

    /// <summary>
    /// As <see cref="RecordFor"/>, but null for the base form itself: "books" and "enrolled" can
    /// only be verbs where a noun stands before them, while "book" and "order" are nouns as often.
    /// </summary>
    public static string? InflectedRecordFor(string word)
    {
        var form = (word ?? string.Empty).Trim().ToLowerInvariant();
        return BaseForms.Contains(form) ? null : RecordFor(form);
    }

    private static Dictionary<string, (string Record, string Verb)> BuildForms()
    {
        var forms = new Dictionary<string, (string Record, string Verb)>(StringComparer.Ordinal);

        foreach (var (record, verbs, irregular) in Table)
        {
            foreach (var verb in verbs)
            {
                forms[verb] = (record, verb);
                forms[verb + "s"] = (record, verb);
                forms[verb + "es"] = (record, verb);
                forms[verb + "d"] = (record, verb);
                forms[verb + "ed"] = (record, verb);
                forms[verb + verb[^1] + "ed"] = (record, verb);
            }

            foreach (var form in irregular)
                forms[form] = (record, verbs[0]);
        }

        return forms;
    }
}
