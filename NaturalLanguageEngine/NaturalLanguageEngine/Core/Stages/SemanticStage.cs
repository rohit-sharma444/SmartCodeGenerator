using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Core.Diagnostics;
using NaturalLanguageEngine.Models.Diagnostics;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Core.Stages;

/// <summary>
/// Final stage: recognizes entities and intent, extracts relationships,
/// and writes them (plus an aggregate confidence) onto the context.
/// </summary>
public sealed class SemanticStage : PipelineStageBase
{
    private readonly IEntityRecognizer _entityRecognizer;
    private readonly IIntentRecognizer _intentRecognizer;
    private readonly IRelationshipExtractor _relationshipExtractor;

    public SemanticStage(
        IEntityRecognizer entityRecognizer,
        IIntentRecognizer intentRecognizer,
        IRelationshipExtractor relationshipExtractor)
    {
        _entityRecognizer = entityRecognizer;
        _intentRecognizer = intentRecognizer;
        _relationshipExtractor = relationshipExtractor;
    }

    public override string Name => "Semantics";

    public override int Order => 50;

    public override AnalysisContext Execute(AnalysisContext context)
    {
        var phrases = context.Document.Sentences
            .SelectMany(s => s.Phrases)
            .ToList();

        var entities = _entityRecognizer.Recognize(phrases);
        foreach (var entity in entities)
            context.Entities.Add(entity);

        if (entities.Count == 0)
        {
            context.AddDiagnostic(
                code: "NLE.Semantics.NoEntities",
                severity: DiagnosticSeverity.Warning,
                message: "No entities were recognized; intent and relationships will be empty.");
        }

        context.Intent = _intentRecognizer.Recognize(entities);

        var relationships = _relationshipExtractor.Extract(
            entities,
            context.Dependencies.ToList());

        if (relationships.Count > 0)
        {
            // context.Dependencies.Count > 0 is a good proxy (not a
            // guarantee - see RelationshipExtractor's remarks) for whether
            // the dependency-parse path or the positional fallback path
            // actually produced these relationships.
            if (context.Dependencies.Count > 0)
            {
                context.AddDiagnostic(
                    code: "NLE.Semantics.SyntacticRelationships",
                    severity: DiagnosticSeverity.Info,
                    message: "Relationships were derived from a dependency parse of the sentence's " +
                              "phrase structure; accuracy is still limited on passive voice, " +
                              "coordinated subjects/objects, negation, and questions.");
            }
            else
            {
                context.AddDiagnostic(
                    code: "NLE.Semantics.HeuristicRelationships",
                    severity: DiagnosticSeverity.Info,
                    message: "Relationships were derived from entity order, not a syntactic parse " +
                              "(no usable dependency edges for this sentence); accuracy is limited on " +
                              "passive voice, multiple clauses, negation, and questions.");
            }
        }

        foreach (var relationship in relationships)
            context.Relationships.Add(relationship);

        context.Confidence = ComputeConfidence(context, entities.Count);

        return context;
    }

    private static double ComputeConfidence(AnalysisContext context, int entityCount)
    {
        if (entityCount == 0)
            return 0d;

        double entityAvg = context.Entities.Average(e => (double)e.Confidence);
        double intentScore = context.Intent is null ? 0.5 : (double)context.Intent.Confidence;

        // Blend entity quality with intent quality.
        return Math.Clamp((entityAvg * 0.6) + (intentScore * 0.4), 0d, 1d);
    }
}
