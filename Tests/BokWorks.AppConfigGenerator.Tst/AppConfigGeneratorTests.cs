namespace BokWorks.AppConfigGenerator.Tst;

public class AppConfigGeneratorTests
{
    [Fact]
    public void NoAppSettings_ProducesNoOutput()
    {
        var result = GeneratorTestHelper.RunGenerator(appSettingsJson: null);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void EmptyJson_ProducesEmptyClass()
    {
        var source = GeneratorTestHelper.GetGeneratedSource("{}");
        Assert.Contains("public static partial class AppConfig", source);
    }

    [Fact]
    public void StringProperty_GeneratesConst()
    {
        var json = """{ "stage": "Dev" }""";
        var source = GeneratorTestHelper.GetGeneratedSource(json);
        Assert.Contains("""public const string Stage = "Dev";""", source);
    }

    [Fact]
    public void BoolProperty_GeneratesConst()
    {
        var json = """{ "enabled": true }""";
        var source = GeneratorTestHelper.GetGeneratedSource(json);
        Assert.Contains("public const bool Enabled = true;", source);
    }

    [Fact]
    public void IntegerProperty_GeneratesLongConst()
    {
        var json = """{ "port": 8080 }""";
        var source = GeneratorTestHelper.GetGeneratedSource(json);
        Assert.Contains("public const long Port = 8080L;", source);
    }

    [Fact]
    public void DoubleProperty_GeneratesDoubleConst()
    {
        var json = """{ "rate": 1.5 }""";
        var source = GeneratorTestHelper.GetGeneratedSource(json);
        Assert.Contains("public const double Rate = 1.5;", source);
    }

    [Fact]
    public void NestedObject_GeneratesPartialClassWithInstanceProperties()
    {
        var json = """
        {
            "Database": {
                "ConnectionString": "Server=localhost",
                "MaxRetries": 3
            }
        }
        """;
        var source = GeneratorTestHelper.GetGeneratedSource(json);

        Assert.Contains("public static DatabaseConfig Database { get; } = new DatabaseConfig();", source);
        Assert.Contains("public partial class DatabaseConfig", source);
        Assert.Contains("""public string ConnectionString { get; } = "Server=localhost";""", source);
        Assert.Contains("public long MaxRetries { get; } = 3L;", source);
    }

    [Fact]
    public void DeeplyNestedObject_GeneratesNestedClasses()
    {
        var json = """
        {
            "Services": {
                "Auth": {
                    "Endpoint": "https://auth.example.com"
                }
            }
        }
        """;
        var source = GeneratorTestHelper.GetGeneratedSource(json);

        Assert.Contains("public static ServicesConfig Services { get; } = new ServicesConfig();", source);
        Assert.Contains("public partial class ServicesConfig", source);
        Assert.Contains("public AuthConfig Auth { get; } = new AuthConfig();", source);
        Assert.Contains("public partial class AuthConfig", source);
        Assert.Contains("""public string Endpoint { get; } = "https://auth.example.com";""", source);
    }

    [Fact]
    public void AssemblyName_UsedAsNamespace()
    {
        var json = """{ "key": "val" }""";
        var source = GeneratorTestHelper.GetGeneratedSource(json, assemblyName: "MyApp");
        Assert.Contains("namespace MyApp.Config", source);
    }

    [Fact]
    public void AssemblyNameWithHyphen_SanitizedInNamespace()
    {
        var json = """{ "key": "val" }""";
        var source = GeneratorTestHelper.GetGeneratedSource(json, assemblyName: "My-App");
        Assert.Contains("namespace My_App.Config", source);
    }

    [Fact]
    public void ToPascalCase_HandlesSeparators()
    {
        var json = """{ "api-key": "secret", "user_name": "bob", "base.url": "http://x" }""";
        var source = GeneratorTestHelper.GetGeneratedSource(json);

        Assert.Contains("""public const string ApiKey = "secret";""", source);
        Assert.Contains("""public const string UserName = "bob";""", source);
        Assert.Contains("""public const string BaseUrl = "http://x";""", source);
    }

    [Fact]
    public void SpecialCharactersInString_AreEscaped()
    {
        var json = """{ "msg": "line1\nline2" }""";
        var source = GeneratorTestHelper.GetGeneratedSource(json);
        Assert.Contains("""public const string Msg = "line1\nline2";""", source);
    }

    [Fact]
    public void LocFile_MergesInDebug()
    {
        var baseJson = """{ "stage": "Prod", "region": "us-east-1" }""";
        var locJson = """{ "stage": "Dev", "extra": "local-only" }""";

        var source = GeneratorTestHelper.GetGeneratedSource(baseJson, locJson, configuration: "Debug");

        // base wins on conflict
        Assert.Contains("""public const string Stage = "Prod";""", source);
        // loc adds new keys
        Assert.Contains("""public const string Extra = "local-only";""", source);
        // base key preserved
        Assert.Contains("""public const string Region = "us-east-1";""", source);
    }

    [Fact]
    public void LocFile_MergesInDebugWithBaseFilePlaceholders()
    {
        var baseJson = """
        {
            "stage": null,
            "services": {}            
        }
        """;

        var locJson = """
        {
            "stage": "Dev",
            "services": 
            {            
                "populated": true
            }            
        }
        """;

        var source = GeneratorTestHelper.GetGeneratedSource(baseJson, locJson, configuration: "Debug");
        
        // placeholders (empty value) in baseJson SHOULD be overridden by local file
        Assert.Contains("""public const string Stage = "Dev";""", source);
        // placeholders (empty object) in baseJson SHOULD be overridden by local file
        Assert.Contains("public static ServicesConfig Services { get; } = new ServicesConfig();", source);
        Assert.Contains("public partial class ServicesConfig", source);      
        Assert.Contains("""public bool Populated { get; } = true;""", source);
    }

    [Fact]
    public void LocFile_IgnoredInRelease()
    {
        var baseJson = """{ "stage": "Prod" }""";
        var locJson = """{ "extra": "local-only" }""";

        var source = GeneratorTestHelper.GetGeneratedSource(baseJson, locJson, configuration: "Release");

        Assert.Contains("""public const string Stage = "Prod";""", source);
        Assert.DoesNotContain("Extra", source);
    }

    [Fact]
    public void InvalidJson_ProducesDiagnostic()
    {
        var result = GeneratorTestHelper.RunGenerator(appSettingsJson: "NOT JSON");

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, d => d.Id == "BWCFG001");
    }

    [Fact]
    public void NullJsonValue_IsSkipped()
    {
        var json = """{ "key": null, "name": "test" }""";
        var source = GeneratorTestHelper.GetGeneratedSource(json);

        Assert.DoesNotContain("Key", source);
        Assert.Contains("""public const string Name = "test";""", source);
    }

    [Fact]
    public void MultipleTopLevelProperties_AllGenerated()
    {
        var json = """
        {
            "AppName": "BokWorks",
            "Version": "1.0",
            "Debug": false,
            "MaxItems": 100,
            "Ratio": 0.75
        }
        """;
        var source = GeneratorTestHelper.GetGeneratedSource(json);

        Assert.Contains("""public const string AppName = "BokWorks";""", source);
        Assert.Contains("""public const string Version = "1.0";""", source);
        Assert.Contains("public const bool Debug = false;", source);
        Assert.Contains("public const long MaxItems = 100L;", source);
        Assert.Contains("public const double Ratio = 0.75;", source);
    }

    [Fact]
    public void LocalFile_MergesInDebug()
    {
        var baseJson = """{ "stage": "Prod" }""";
        var localJson = """{ "extra": "local-only" }""";

        var result = GeneratorTestHelper.RunGenerator(baseJson, appSettingsLocalJson: localJson, configuration: "Debug");
        var source = result.GeneratedSources[0].SourceText.ToString();

        Assert.Contains("""public const string Stage = "Prod";""", source);
        Assert.Contains("""public const string Extra = "local-only";""", source);
    }

    [Fact]
    public void LocalFile_IgnoredInRelease()
    {
        var baseJson = """{ "stage": "Prod" }""";
        var localJson = """{ "extra": "local-only" }""";

        var result = GeneratorTestHelper.RunGenerator(baseJson, appSettingsLocalJson: localJson, configuration: "Release");
        var source = result.GeneratedSources[0].SourceText.ToString();

        Assert.DoesNotContain("Extra", source);
    }

    [Fact]
    public void LocFile_TakesPrecedenceOverLocalFile()
    {
        var baseJson = """{ "stage": "Prod" }""";
        var locJson = """{ "from": "loc" }""";
        var localJson = """{ "from": "local" }""";

        var result = GeneratorTestHelper.RunGenerator(baseJson, locJson, localJson, configuration: "Debug");
        var source = result.GeneratedSources[0].SourceText.ToString();

        // .Loc wins over .Local
        Assert.Contains("""public const string From = "loc";""", source);
    }

    [Fact]
    public void DevelopmentFile_MergesInDebug()
    {
        var baseJson = """{ "stage": "Prod" }""";
        var devJson = """{ "extra": "dev-only" }""";

        var result = GeneratorTestHelper.RunGenerator(baseJson, appSettingsDevelopmentJson: devJson, configuration: "Debug");
        var source = result.GeneratedSources[0].SourceText.ToString();

        Assert.Contains("""public const string Stage = "Prod";""", source);
        Assert.Contains("""public const string Extra = "dev-only";""", source);
    }

    [Fact]
    public void DevelopmentFile_IgnoredInRelease()
    {
        var baseJson = """{ "stage": "Prod" }""";
        var devJson = """{ "extra": "dev-only" }""";

        var result = GeneratorTestHelper.RunGenerator(baseJson, appSettingsDevelopmentJson: devJson, configuration: "Release");
        var source = result.GeneratedSources[0].SourceText.ToString();

        Assert.DoesNotContain("Extra", source);
    }

    [Fact]
    public void LocFile_TakesPrecedenceOverDevelopmentFile()
    {
        var baseJson = """{ "stage": "Prod" }""";
        var locJson = """{ "from": "loc" }""";
        var devJson = """{ "from": "dev" }""";

        var result = GeneratorTestHelper.RunGenerator(baseJson, locJson, appSettingsDevelopmentJson: devJson, configuration: "Debug");
        var source = result.GeneratedSources[0].SourceText.ToString();

        Assert.Contains("""public const string From = "loc";""", source);
    }
}