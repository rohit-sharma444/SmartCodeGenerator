using System.Runtime.CompilerServices;

// Lets NaturalLanguageEngine.Tests unit-test internal implementation
// details (e.g. the individual ITokenRecognizer classes under
// Lexical/Tokenizers, which are intentionally not part of the public API)
// without widening the public surface of this assembly.
[assembly: InternalsVisibleTo("NaturalLanguageEngine.Tests")]
