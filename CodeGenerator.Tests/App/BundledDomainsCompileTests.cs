using AppScaffolding;
using CodeGenerationEngine.Core;
using DomainDiscovery.Bundled;
using DomainDiscovery.Llm;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Every built-in domain's classes, generated the way `build app` generates them, compile.
/// </summary>
/// <remarks>
/// One test over all of them rather than one per domain, so a failure lists every broken domain at
/// once. A class name that collides with a type the generated code uses (Task, Application) is the
/// failure this exists to catch before a customer does.
/// </remarks>
[TestClass]
public class BundledDomainsCompileTests
{
    [TestMethod]
    public void EveryDomainsClassesCompile()
    {
        var failures = new List<string>();

        foreach (var domain in BundledDomainCatalog.Default.Domains)
        {
            var model = LlmModelNormalizer.Normalize(domain.Model, ModelOrigin.BuiltIn(domain.Name));

            var sources = new List<(string Path, string Code)>
            {
                ("GlobalUsings.cs", "global using System;\nglobal using System.Collections.Generic;")
            };
            foreach (var definition in DomainModelMapper.ToClassDefinitions(model))
                sources.Add(("Models/" + definition.Name + ".cs", RoslynClassGenerator.Generate(definition, "Acme.Models")));

            var errors = Compiler.CompileSources(sources).Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();

            if (errors.Count > 0)
                failures.Add(domain.Name + ":\n  " + string.Join("\n  ", errors));
        }

        Assert.AreEqual(25, BundledDomainCatalog.Default.Domains.Count);
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }
}
