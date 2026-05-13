using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BokWorks.AppConfigGenerator;

[Generator]
public class AppConfigGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidJson = new(
        id: "BWCFG001",
        title: "Invalid JSON in settings file",
        messageFormat: "Failed to parse '{0}': {1}",
        category: "BokWorks.AppConfigGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var isRelease = context.AnalyzerConfigOptionsProvider.Select((opts, _) =>
        {
            opts.GlobalOptions.TryGetValue("build_property.Configuration", out var config);
            return string.Equals(config, "Release", StringComparison.OrdinalIgnoreCase);
        });

        var assemblyName = context.CompilationProvider
            .Select((c, _) => c.AssemblyName ?? "App");

        var settingsFiles = context.AdditionalTextsProvider
            .Where(f =>
            {
                var name = Path.GetFileName(f.Path);
                return name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("appsettings.Loc.json", StringComparison.OrdinalIgnoreCase);
            });

        var combined = settingsFiles.Collect()
            .Combine(isRelease)
            .Combine(assemblyName);

        context.RegisterSourceOutput(combined, (ctx, data) =>
        {
            var ((files, release), asmName) = data;

            AdditionalText? baseFile = null, locFile = null;
            foreach (var f in files)
            {
                var name = Path.GetFileName(f.Path);
                if (name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)) baseFile = f;
                if (name.Equals("appsettings.Loc.json", StringComparison.OrdinalIgnoreCase)) locFile = f;
            }

            if (baseFile == null) return;

            var baseJson = baseFile.GetText()?.ToString() ?? "{}";

            var locJson = (!release && locFile != null)
                ? locFile.GetText()?.ToString() ?? "{}"
                : "{}";

            Dictionary<string, object?> merged;
            try
            {
                merged = Merge(locJson, baseJson);
            }
            catch (JsonException ex)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(InvalidJson,
                    Location.None, Path.GetFileName(baseFile.Path), ex.Message));
                return;
            }

            var source = GenerateClass(merged, asmName);
            ctx.AddSource("AppConfig.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    /// Loc supplies fallback values; base overwrites on any key collision (deep).
    private static Dictionary<string, object?> Merge(string fallbackJson, string dominantJson)
    {
        Dictionary<string, object?> fallback, dominant;

        using (var fallbackDoc = JsonDocument.Parse(fallbackJson))
            fallback = ParseObject(fallbackDoc.RootElement);

        using (var dominantDoc = JsonDocument.Parse(dominantJson))
            dominant = ParseObject(dominantDoc.RootElement);

        return DeepMerge(fallback, dominant);
    }

    private static Dictionary<string, object?> DeepMerge(
        Dictionary<string, object?> fallback,
        Dictionary<string, object?> dominant)
    {
        var result = new Dictionary<string, object?>(fallback);
        foreach (var kvp in dominant)
        {
            if (kvp.Value is Dictionary<string, object?> dominantNested
             && result.TryGetValue(kvp.Key, out var existing)
             && existing is Dictionary<string, object?> fallbackNested)
            {
                result[kvp.Key] = DeepMerge(fallbackNested, dominantNested);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
    }

    private static Dictionary<string, object?> ParseObject(JsonElement el)
    {
        var dict = new Dictionary<string, object?>();
        if (el.ValueKind != JsonValueKind.Object) return dict;
        foreach (var prop in el.EnumerateObject())
            dict[prop.Name] = ParseValue(prop.Value);
        return dict;
    }

    private static object? ParseValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Object => ParseObject(el),
        JsonValueKind.String => el.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when el.TryGetInt64(out var i) => i,
        JsonValueKind.Number => el.GetDouble(),
        _ => null
    };

    private static string GenerateClass(Dictionary<string, object?> values, string assemblyName)
    {
        var ns = SanitizeIdentifier(assemblyName);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine($"// Source: appsettings.json (assembly: {assemblyName})");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}.Config");
        sb.AppendLine("{");
        sb.AppendLine("    public static partial class AppConfig");
        sb.AppendLine("    {");
        WriteMembers(sb, values, indent: 2);
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void WriteMembers(StringBuilder sb, Dictionary<string, object?> dict, int indent)
    {
        var pad = new string(' ', indent * 4);
        foreach (var kvp in dict)
        {
            var name = ToPascalCase(kvp.Key);
            switch (kvp.Value)
            {
                case Dictionary<string, object?> nested:
                    var className = name + "Config";
                    // Property backed by a nested config class instance
                    sb.AppendLine($"{pad}public static {className} {name} {{ get; }} = new {className}();");
                    sb.AppendLine();
                    sb.AppendLine($"{pad}public partial class {className}");
                    sb.AppendLine($"{pad}{{");
                    WriteNestedMembers(sb, nested, indent + 1);
                    sb.AppendLine($"{pad}}}");
                    break;
                case string s:
                    sb.AppendLine($"{pad}public const string {name} = \"{Escape(s)}\";");
                    break;
                case bool b:
                    sb.AppendLine($"{pad}public const bool {name} = {(b ? "true" : "false")};");
                    break;
                case long i:
                    sb.AppendLine($"{pad}public const long {name} = {i}L;");
                    break;
                case double d:
                    sb.AppendLine($"{pad}public const double {name} = {d};");
                    break;
            }
        }
    }

    private static void WriteNestedMembers(StringBuilder sb, Dictionary<string, object?> dict, int indent)
    {
        var pad = new string(' ', indent * 4);
        foreach (var kvp in dict)
        {
            var name = ToPascalCase(kvp.Key);
            switch (kvp.Value)
            {
                case Dictionary<string, object?> nested:
                    var className = name + "Config";
                    sb.AppendLine($"{pad}public {className} {name} {{ get; }} = new {className}();");
                    sb.AppendLine();
                    sb.AppendLine($"{pad}public class {className}");
                    sb.AppendLine($"{pad}{{");
                    WriteNestedMembers(sb, nested, indent + 1);
                    sb.AppendLine($"{pad}}}");
                    break;
                case string s:
                    sb.AppendLine($"{pad}public string {name} {{ get; }} = \"{Escape(s)}\";");
                    break;
                case bool b:
                    sb.AppendLine($"{pad}public bool {name} {{ get; }} = {(b ? "true" : "false")};");
                    break;
                case long i:
                    sb.AppendLine($"{pad}public long {name} {{ get; }} = {i}L;");
                    break;
                case double d:
                    sb.AppendLine($"{pad}public double {name} {{ get; }} = {d};");
                    break;
            }
        }
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    private static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        var sb = new StringBuilder(s.Length);
        var capitalizeNext = true;
        foreach (var c in s)
        {
            if (c == '-' || c == '_' || c == '.')
            {
                capitalizeNext = true;
                continue;
            }
            sb.Append(capitalizeNext ? char.ToUpper(c) : c);
            capitalizeNext = false;
        }
        return sb.ToString();
    }

    private static string SanitizeIdentifier(string s) =>
        s.Replace('-', '_').Replace(' ', '_');
}}