using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace BokWorks.AppConfigGenerator.Tst;

internal static class GeneratorTestHelper
{
    /// <summary>
    /// Runs the AppConfigGenerator against the given JSON files and returns the results.
    /// </summary>
    public static GeneratorRunResult RunGenerator(
        string? appSettingsJson,
        string? appSettingsLocJson = null,
        string? appSettingsLocalJson = null,
        string? appSettingsDevelopmentJson = null,
        string assemblyName = "TestApp",
        string configuration = "Debug")
    {
        var syntaxTrees = new[] { CSharpSyntaxTree.ParseText("") };

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var additionalTexts = new List<AdditionalText>();
        if (appSettingsJson is not null)
            additionalTexts.Add(new InMemoryAdditionalText("appsettings.json", appSettingsJson));
        if (appSettingsLocJson is not null)
            additionalTexts.Add(new InMemoryAdditionalText("appsettings.Loc.json", appSettingsLocJson));
        if (appSettingsLocalJson is not null)
            additionalTexts.Add(new InMemoryAdditionalText("appsettings.Local.json", appSettingsLocalJson));
        if (appSettingsDevelopmentJson is not null)
            additionalTexts.Add(new InMemoryAdditionalText("appsettings.Development.json", appSettingsDevelopmentJson));

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(configuration);

        var generator = new AppConfigGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .AddAdditionalTexts([.. additionalTexts])
            .WithUpdatedAnalyzerConfigOptions(optionsProvider)
            .RunGeneratorsAndUpdateCompilation(compilation, out var _, out var _);

        return driver.GetRunResult().Results[0];
    }

    /// <summary>
    /// Convenience: runs the generator and returns the single generated source text.
    /// </summary>
    public static string GetGeneratedSource(
        string appSettingsJson,
        string? appSettingsLocJson = null,
        string? appSettingsLocalJson = null,
        string? appSettingsDevelopmentJson = null,
        string assemblyName = "TestApp",
        string configuration = "Debug")
    {
        var result = RunGenerator(appSettingsJson, appSettingsLocJson, appSettingsLocalJson, appSettingsDevelopmentJson, assemblyName, configuration);
        Assert.Single(result.GeneratedSources);
        return result.GeneratedSources[0].SourceText.ToString();
    }
}

internal sealed class InMemoryAdditionalText : AdditionalText
{
    private readonly SourceText _text;

    public InMemoryAdditionalText(string path, string content)
    {
        Path = path;
        _text = SourceText.From(content);
    }

    public override string Path { get; }
    public override SourceText? GetText(CancellationToken cancellationToken = default) => _text;
}

internal sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly TestGlobalOptions _globalOptions;

    public TestAnalyzerConfigOptionsProvider(string configuration)
    {
        _globalOptions = new TestGlobalOptions(configuration);
    }

    public override AnalyzerConfigOptions GlobalOptions => _globalOptions;
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyOptions.Instance;
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => EmptyOptions.Instance;

    private sealed class TestGlobalOptions : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> _values;

        public TestGlobalOptions(string configuration)
        {
            _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["build_property.Configuration"] = configuration
            };
        }

        public override bool TryGetValue(string key, out string value) =>
            _values.TryGetValue(key, out value!);
    }

    private sealed class EmptyOptions : AnalyzerConfigOptions
    {
        public static EmptyOptions Instance { get; } = new();
        public override bool TryGetValue(string key, out string value) { value = null!; return false; }
    }
}
