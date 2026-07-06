using NaturalLanguageEngine.Models.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace NaturalLanguageEngine.Models.Common;

public sealed class ParseTree
{
    public Phrase Root { get; init; } = default!;
}
