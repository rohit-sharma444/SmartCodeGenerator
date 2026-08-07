using System.Runtime.CompilerServices;

// Lets CodeIntelligence.Tests unit-test internal implementation details (e.g.
// CorpusHarvester.StripPlaceholders' delimiter handling) without widening the
// public surface of this assembly.
[assembly: InternalsVisibleTo("CodeIntelligence.Tests")]
