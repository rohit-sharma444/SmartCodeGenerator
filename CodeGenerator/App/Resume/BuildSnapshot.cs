using AppScaffolding;
using AppScaffolding.Layouts;
using CodeGenerationEngine.Models;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DomainDiscovery.Model;

namespace CodeGenerator.App.Resume;

/// <summary>
/// An unfinished build, as it is written to disk.
/// </summary>
/// <remarks>
/// A DTO, not the session. Nullable throughout so a file written by an older build fills a missing
/// field from the default rather than with a null or a zero — the shape
/// <see cref="CustomizationDefaults"/>'s profile already takes, for the same reason.
/// <para>
/// <see cref="CandidateDomainModel"/> cannot be serialized directly: every collection on it is
/// get-only, so <c>System.Text.Json</c> has nothing to deserialize into, and its leaf types use
/// <c>required</c> members. Hence the parallel shapes below.
/// </para>
/// <para>
/// Two things are deliberately absent, and both are asserted by tests rather than left as
/// intentions: the ROWS that were read — re-read on resume, so the file stays small whatever the
/// data size — and the CONNECTION STRING, which the manual promises is never written to
/// <c>.codegen/</c>.
/// </para>
/// </remarks>
internal sealed class BuildSnapshot
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? Description { get; set; }

    /// <summary>The target's KEY ("wpf"), not a type name — a type name in a file would break the
    /// moment a class moved.</summary>
    public string? AppType { get; set; }

    public string? Stage { get; set; }

    public string? Company { get; set; }
    public string? Theme { get; set; }
    public string? Accent { get; set; }
    public string? TitlePattern { get; set; }
    public string? IconPath { get; set; }
    public bool? Validation { get; set; }
    public bool? Logging { get; set; }
    public bool? SampleSeedData { get; set; }
    public bool? Login { get; set; }
    public string? Transactions { get; set; }
    public string? Masters { get; set; }
    public string? NamespacePrefix { get; set; }
    public string? Layout { get; set; }

    public string? SourceKind { get; set; }

    /// <summary>Set for every kind but SQL.</summary>
    public string? SourcePath { get; set; }

    /// <summary>Set for SQL alone, and the ONLY thing kept about a SQL connection.</summary>
    public string? SourceDatabase { get; set; }

    public List<Entity>? Entities { get; set; }
    public List<Entity>? Suggestions { get; set; }
    public List<string>? Rejected { get; set; }
    public List<Decision>? Decisions { get; set; }

    internal sealed class Entity
    {
        public string? Name { get; set; }
        public string? Provenance { get; set; }
        public double? Confidence { get; set; }
        public string? SourceTitle { get; set; }
        public List<Field>? Fields { get; set; }
        public List<Relationship>? Relationships { get; set; }
    }

    internal sealed class Field
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Provenance { get; set; }

        /// <summary>
        /// What the source declared about this field's column.
        /// </summary>
        /// <remarks>
        /// Absent before schema version 2, which meant a resumed build came back factless and
        /// inferred none of the field rules the same build inferred without a resume. The rows
        /// are re-read on resume; the FACTS are not, because nothing re-runs the reader's
        /// schema pass once a model has been restored.
        /// </remarks>
        public Facts? Facts { get; set; }

        /// <summary>
        /// The proposed rules and, the point of keeping them, which were accepted.
        /// </summary>
        /// <remarks>
        /// The scan re-runs from the re-read rows, so the proposals themselves could be
        /// recomputed — the DECISION could not, and that is the user's work.
        /// </remarks>
        public List<Proposal>? Proposals { get; set; }
    }

    internal sealed class Facts
    {
        public bool? IsNullable { get; set; }
        public int? MaxLength { get; set; }
        public bool? IsUnbounded { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public string? DeclaredType { get; set; }
    }

    internal sealed class Proposal
    {
        public string? Kind { get; set; }
        public string? Evidence { get; set; }
        public bool? Accepted { get; set; }
        public string? RuleName { get; set; }
        public List<string>? AllowedValues { get; set; }
        public string? RegexPattern { get; set; }
        public string? CustomRule { get; set; }
    }

    internal sealed class Relationship
    {
        public string? Target { get; set; }
        public string? Role { get; set; }
    }

    internal sealed class Decision
    {
        public string? Name { get; set; }
        public string? Stage { get; set; }
        public string? Outcome { get; set; }
        public string? Reason { get; set; }
        public string? Evidence { get; set; }
    }

    public static BuildSnapshot Of(BuildSession session)
    {
        var customization = session.Customization;

        var snapshot = new BuildSnapshot
        {
            Description = session.Description,
            AppType = session.Target?.Key,
            Stage = session.Current.ToString(),

            Company = customization.Company,
            Theme = customization.Theme.ToString(),
            Accent = customization.Accent,
            TitlePattern = customization.TitlePattern,
            IconPath = customization.IconPath,
            Validation = customization.Validation,
            Logging = customization.Logging,
            SampleSeedData = customization.SampleSeedData,
            Login = customization.Login,
            Transactions = customization.Transactions,
            Masters = customization.Masters,
            NamespacePrefix = customization.NamespacePrefix,
            Layout = customization.Layout?.ToString(),
        };

        if (session.Source is { } source)
        {
            snapshot.SourceKind = source.Kind.ToString();

            // The one branch that matters. A SQL location IS a connection string, so only the
            // database name survives — see the class remarks.
            if (source.Kind == DataSourceKind.Sql)
                snapshot.SourceDatabase =
                    source.SqlDatabase ?? SqlConnectionInfo.DatabaseOf(source.Location);
            else
                snapshot.SourcePath = source.Location;
        }

        if (session.Model is { } model)
        {
            snapshot.Entities = [.. model.Entities.Select(ToDto)];
            snapshot.Suggestions = [.. model.Suggestions.Select(ToDto)];
            snapshot.Rejected = [.. model.Rejected];
            snapshot.Decisions = [.. model.Decisions.Select(d => new Decision
            {
                Name = d.Name,
                Stage = d.Stage.ToString(),
                Outcome = d.Outcome.ToString(),
                Reason = d.Reason,
                Evidence = d.Evidence,
            })];
        }

        return snapshot;
    }

    /// <summary>The customization this snapshot holds, with anything missing taken from the
    /// defaults.</summary>
    public AppCustomization ToCustomization()
    {
        var fallback = AppCustomization.Default;

        return new AppCustomization(
            Company: Company ?? fallback.Company,
            Theme: Enum.TryParse<AppTheme>(Theme, out var theme) ? theme : fallback.Theme,
            Accent: Accent ?? fallback.Accent,
            TitlePattern: TitlePattern ?? fallback.TitlePattern,
            IconPath: IconPath ?? fallback.IconPath,
            Validation: Validation ?? fallback.Validation,
            Logging: Logging ?? fallback.Logging,
            SampleSeedData: SampleSeedData ?? fallback.SampleSeedData,
            Login: Login ?? fallback.Login,
            Transactions: Transactions ?? fallback.Transactions,
            Masters: Masters ?? fallback.Masters,
            NamespacePrefix: NamespacePrefix ?? fallback.NamespacePrefix,
            Layout: Enum.TryParse<AppLayout>(Layout, out var layout) ? layout : fallback.Layout);
    }

    /// <summary>The model this snapshot holds, or null when it was taken before discovery ran.</summary>
    public CandidateDomainModel? ToModel()
    {
        if (Entities is null)
            return null;

        var model = new CandidateDomainModel();

        foreach (var entity in Entities)
            if (FromDto(entity) is { } candidate)
                model.Entities.Add(candidate);

        foreach (var suggestion in Suggestions ?? [])
            if (FromDto(suggestion) is { } candidate)
                model.Suggestions.Add(candidate);

        foreach (var name in Rejected ?? [])
            model.Rejected.Add(name);

        foreach (var decision in Decisions ?? [])
        {
            if (decision.Name is null || decision.Reason is null)
                continue;

            model.Record(
                decision.Name,
                Enum.TryParse<DecisionStage>(decision.Stage, out var stage)
                    ? stage
                    : DecisionStage.SeedExtraction,
                Enum.TryParse<DecisionOutcome>(decision.Outcome, out var outcome)
                    ? outcome
                    : DecisionOutcome.Kept,
                decision.Reason,
                decision.Evidence);
        }

        return model;
    }

    private static Entity ToDto(CandidateEntity entity) => new()
    {
        Name = entity.Name,
        Provenance = entity.Provenance.ToString(),
        Confidence = entity.Confidence,
        SourceTitle = entity.SourceTitle,
        Fields = [.. entity.Fields.Select(f => new Field
        {
            Name = f.Name,
            Type = f.Type,
            Provenance = f.Provenance.ToString(),
            Facts = f.Facts is { } facts ? ToDto(facts) : null,
            Proposals = f.Proposals.Count > 0 ? [.. f.Proposals.Select(ToDto)] : null,
        })],
        Relationships = [.. entity.Relationships.Select(r => new Relationship
        {
            Target = r.Target, Role = r.Role
        })],
    };

    /// <summary>
    /// One entity back, or null when the file did not name it.
    /// </summary>
    /// <remarks>
    /// A nameless entity is dropped rather than defaulted: <see cref="CandidateEntity.Name"/> is
    /// <c>required</c>, and inventing a name would put a class in the model the user never had.
    /// </remarks>
    private static CandidateEntity? FromDto(Entity dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return null;

        var entity = new CandidateEntity
        {
            Name = dto.Name,
            Provenance = Enum.TryParse<Provenance>(dto.Provenance, out var provenance)
                ? provenance
                : Provenance.Wiki,
            Confidence = dto.Confidence ?? 0,
            SourceTitle = dto.SourceTitle,
        };

        foreach (var field in dto.Fields ?? [])
        {
            if (field.Name is null || field.Type is null)
                continue;

            var candidateField = new CandidateField
            {
                Name = field.Name,
                Type = field.Type,
                Provenance = Enum.TryParse<Provenance>(field.Provenance, out var p)
                    ? p
                    : Provenance.Wiki,
                Facts = field.Facts is { } facts ? FromDto(facts) : null,
            };

            foreach (var proposal in field.Proposals ?? [])
                if (FromDto(proposal) is { } restored)
                    candidateField.Proposals.Add(restored);

            entity.Fields.Add(candidateField);
        }

        foreach (var relationship in dto.Relationships ?? [])
        {
            if (relationship.Target is null || relationship.Role is null)
                continue;

            entity.Relationships.Add(new CandidateRelationship
            {
                Target = relationship.Target, Role = relationship.Role
            });
        }

        return entity;
    }

    private static Facts ToDto(TypeFacts facts) => new()
    {
        IsNullable = facts.IsNullable,
        MaxLength = facts.MaxLength,
        IsUnbounded = facts.IsUnbounded,
        Precision = facts.Precision,
        Scale = facts.Scale,
        DeclaredType = facts.DeclaredType,
    };

    /// <summary>
    /// The real <see cref="TypeFacts.IsUnbounded"/> cannot be null, so a DTO with no value for it
    /// (an older file, or a field this build never scanned) comes back <c>false</c> — "unknown"
    /// and "known to be bounded" collapse to the same starting point a fresh discovery would give.
    /// </summary>
    private static TypeFacts FromDto(Facts dto) => new()
    {
        IsNullable = dto.IsNullable,
        MaxLength = dto.MaxLength,
        IsUnbounded = dto.IsUnbounded ?? false,
        Precision = dto.Precision,
        Scale = dto.Scale,
        DeclaredType = dto.DeclaredType,
    };

    private static Proposal ToDto(RuleProposal proposal) => new()
    {
        Kind = proposal.Kind.ToString(),
        Evidence = proposal.Evidence,
        Accepted = proposal.Accepted,
        RuleName = proposal.Rule.Name,
        AllowedValues = proposal.Rule.AllowedValues.Count > 0 ? [.. proposal.Rule.AllowedValues] : null,
        RegexPattern = proposal.Rule.RegexPattern,
        CustomRule = proposal.Rule.CustomRule,
    };

    /// <summary>
    /// One proposal back, or null when its <see cref="Proposal.Kind"/> does not parse. Deliberate:
    /// restoring it with some default kind would tick a rule the user never actually saw.
    /// </summary>
    private static RuleProposal? FromDto(Proposal dto)
    {
        if (!Enum.TryParse<RuleProposalKind>(dto.Kind, out var kind))
            return null;

        return new RuleProposal
        {
            Kind = kind,
            Rule = new PropertyRule
            {
                Name = dto.RuleName ?? string.Empty,
                AllowedValues = dto.AllowedValues ?? [],
                RegexPattern = dto.RegexPattern,
                CustomRule = dto.CustomRule,
            },
            Evidence = dto.Evidence ?? string.Empty,
            Accepted = dto.Accepted ?? false,
        };
    }
}
