namespace AppScaffolding;

/// <summary>The copyright banner prepended to generated C#.</summary>
public static class FileHeader
{
    /// <summary>
    /// The banner for <paramref name="company"/>, or empty when there is none.
    /// </summary>
    /// <remarks>
    /// The year is a parameter, never <c>DateTime.Now</c>: a template reading the clock would
    /// turn every golden-output test red on 1 January. "(c)" rather than the symbol keeps
    /// emitted source free of any encoding dependency.
    /// </remarks>
    public static string For(string company, int year)
        => company.Length == 0 ? string.Empty : $"// Copyright (c) {year} {company}.\n";
}
