using System.Text.RegularExpressions;
using DomainDiscovery.Bundled;
using DomainDiscovery.Enrichment;
using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Bundled;

/// <summary>Holds every built-in domain file to the rules a hand-written file can quietly break.</summary>
public class CatalogContentTests
{
    // What LlmModelNormalizer.ToCsharpType recognises; anything else silently becomes string.
    private static readonly string[] Types = ["string", "int", "long", "decimal", "double", "bool", "DateTime", "Guid"];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var d in BundledDomainCatalog.Default.Domains)
            data.Add(d.Name);
        return data;
    }

    private static BundledDomain Get(string name) => BundledDomainCatalog.Default.Domains.Single(d => d.Name == name);

    [Fact]
    public void ThereAreTwentyFive() => Assert.Equal(25, BundledDomainCatalog.Default.Domains.Count);

    [Theory, MemberData(nameof(Names))]
    public void NormalisesWithNothingRejected(string name)
    {
        var model = LlmModelNormalizer.Normalize(Get(name).Model, ModelOrigin.BuiltIn(name));

        Assert.Empty(model.Rejected);
        Assert.Equal(Get(name).Model.Classes.Count, model.Entities.Count);
    }

    [Theory, MemberData(nameof(Names))]
    public void EveryTypeIsOneTheNormaliserKnows(string name)   // Review Focus 4
    {
        foreach (var c in Get(name).Model.Classes)
            foreach (var p in c.Properties)
                Assert.True(Types.Contains(p.Type), $"{name}.{c.Name}.{p.Name}: type \"{p.Type}\"");
    }

    [Theory, MemberData(nameof(Names))]
    public void EveryIdFieldBecomesARelationship(string name)   // Review Focus 5
    {
        var raw = Get(name).Model;
        var model = LlmModelNormalizer.Normalize(raw, ModelOrigin.BuiltIn(name));
        var classNames = raw.Classes.Select(c => c.Name).ToHashSet();

        foreach (var c in raw.Classes)
            foreach (var p in c.Properties.Where(p => p.Name.EndsWith("Id") && classNames.Contains(p.Name[..^2])))
                Assert.True(
                    model.Entities.Single(e => e.Name == c.Name).Relationships.Any(r => r.Target == p.Name[..^2]),
                    $"{name}.{c.Name}.{p.Name} did not become a link: role {c.Role} cannot hold one");
    }

    [Theory, MemberData(nameof(Names))]
    public void RolesAreRealAndRelationshipsAreLeftEmpty(string name)
    {
        foreach (var c in Get(name).Model.Classes)
        {
            Assert.True(Enum.TryParse<EntityRole>(c.Role, out _), $"{name}.{c.Name}: role \"{c.Role}\"");
            Assert.Empty(c.Relationships);
        }
    }

    [Fact]
    public void NoTwoDomainsShareAWeightThreeTerm()
    {
        var shared = BundledDomainCatalog.Default.Domains
            .SelectMany(d => d.Terms.Where(t => t.Value == 3).Select(t => (Stem: t.Key, d.Name)))
            .GroupBy(x => x.Stem)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Name))}");

        Assert.Empty(shared);
    }

    [Fact]
    public void EveryFileIsNamedAfterItsDomain()
    {
        const string prefix = "DomainDiscovery.Bundled.Domains.";
        var resources = typeof(BundledDomainCatalog).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => n[prefix.Length..^".json".Length].ToLowerInvariant());

        // PointOfSale.json holds "Point of sale": spaces removed, case ignored.
        Assert.Equal(resources,
            BundledDomainCatalog.Default.Domains.Select(d => Regex.Replace(d.Name, @"\s", "").ToLowerInvariant()));
    }

    [Theory]
    [InlineData("a library that lends books to members", "Library")]
    [InlineData("a clinic that books patients with doctors", "Clinic")]
    [InlineData("a school with students enrolled on courses", "School")]
    [InlineData("an online shop selling products to customers", "Online shop")]
    [InlineData("a till for a corner shop cashier", "Point of sale")]
    [InlineData("invoicing for my clients", "Invoicing")]
    [InlineData("a warehouse stock system", "Inventory")]
    [InlineData("a hotel booking system", "Hotel")]
    [InlineData("a restaurant with tables and a menu", "Restaurant")]
    [InlineData("an HR system for employees and leave", "HR")]
    [InlineData("payroll for our staff", "Payroll")]
    [InlineData("a CRM for leads and deals", "CRM")]
    [InlineData("a helpdesk where customers raise tickets", "Helpdesk")]
    [InlineData("track projects and tasks", "Projects and tasks")]
    [InlineData("a conference with speakers and attendees", "Events")]
    [InlineData("a gym with members and trainers", "Gym")]
    [InlineData("a car hire business", "Car rental")]
    [InlineData("a landlord managing tenants", "Property rental")]
    [InlineData("manage a fleet of vans and drivers", "Fleet")]
    [InlineData("a bank with accounts and deposits", "Banking")]
    [InlineData("staff expense claims with receipts", "Expenses")]
    [InlineData("recruitment of candidates for vacancies", "Recruitment")]
    [InlineData("purchasing from suppliers", "Purchasing")]
    [InlineData("a blog with posts and comments", "Blog")]
    [InlineData("a vet practice for pets and their owners", "Veterinary")]
    public void EachDomainIsFoundByAnOrdinaryDescription(string description, string domain)
        => Assert.Equal(domain, BundledDomainCatalog.Default.Match(description)?.Domain.Name);

    [Theory]
    [InlineData("a book club")]
    [InlineData("a knitting pattern tracker")]
    [InlineData("an app that lets staff track tasks")]           // "lets" is not "letting"
    [InlineData("a tool that lets us manage our work")]
    [InlineData("split the bill between friends")]               // "bill" is not "billing"
    [InlineData("a coffee shop")]                                // one word is not a domain
    [InlineData("a food bank")]
    [InlineData("count the days till the deadline")]
    public void NearMissesDoNotMatch(string description) => Assert.Null(BundledDomainCatalog.Default.Match(description));

    // Found by the final review: each of these picked a wrong domain, or none.
    [Theory]
    [InlineData("a veterinary clinic", "Veterinary")]
    [InlineData("a vet clinic", "Veterinary")]
    [InlineData("an animal hospital", "Veterinary")]
    [InlineData("a shop that rents cars", "Car rental")]
    [InlineData("a car rental shop", "Car rental")]
    [InlineData("a point of sale system", "Point of sale")]
    [InlineData("a help desk for support tickets", "Helpdesk")]
    [InlineData("an e-commerce site", "Online shop")]
    [InlineData("a human resources system", "HR")]
    [InlineData("a customer relationship management tool", "CRM")]
    [InlineData("a project management app", "Projects and tasks")]
    [InlineData("students enroll in courses", "School")]
    [InlineData("online banking", "Banking")]
    public void CommonPhrasingsFindTheRightDomain(string description, string domain)
        => Assert.Equal(domain, BundledDomainCatalog.Default.Match(description)?.Domain.Name);

    [Theory]
    [InlineData("an online store where customers purchase products", "Purchasing")]
    [InlineData("a bike rental shop", "Online shop")]
    public void CommonPhrasingsDoNotFindTheWrongDomain(string description, string wrong)
        => Assert.NotEqual(wrong, BundledDomainCatalog.Default.Match(description)?.Domain.Name);

    // Found by the gaps audit, each reproduced live with Ollama unreachable.
    [Theory]
    [InlineData("a pet clinic", "Veterinary")]              // clinic alone beat clinic + pet
    [InlineData("a banking system", "Banking")]             // banking and bank share one stem
    [InlineData("an online store", "Online shop")]          // "store" was only a self word
    [InlineData("a doctors surgery", "Clinic")]             // "surgery" was only a self word
    [InlineData("a vet surgery", "Veterinary")]
    [InlineData("a car hire business", "Car rental")]
    [InlineData("a shop that rents cars", "Car rental")]
    public void AuditedPhrasingsFindTheRightDomain(string description, string domain)
        => Assert.Equal(domain, BundledDomainCatalog.Default.Match(description)?.Domain.Name);

    // "rent", "rental" and "hire" are not about cars: two of them used to pass the threshold.
    [Theory]
    [InlineData("equipment rental where customers rent tools")]
    [InlineData("a bike hire and rental shop")]
    public void RentingSomethingElseIsNotCarRental(string description)
        => Assert.NotEqual("Car rental", BundledDomainCatalog.Default.Match(description)?.Domain.Name);

    [Fact]
    public void ABankIsStillNotEnoughOnItsOwn() => Assert.Null(BundledDomainCatalog.Default.Match("a food bank"));
}
