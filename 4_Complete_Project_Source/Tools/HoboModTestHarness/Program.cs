using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HoboModTestHarness.Runners;
using HoboModTestHarness.Reports;
using HoboModTestHarness.Utils;
using Spectre.Console;

namespace HoboModTestHarness;

class Program
{
    static void Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("HoboMod Harness")
                .Color(Color.Cyan1));

        // Base paths
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var projectPath = Path.Combine(basePath, "HoboModPlugin-release/HoboModFramework.csproj");
        var testProjectPath = Path.Combine(basePath, "HoboModPlugin.Tests/HoboModPlugin.Tests.csproj");
        var schemasPath = Path.Combine(AppContext.BaseDirectory, "Schemas");
        var testModsPath = Path.Combine(basePath, "test_mods");
        var gamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Hobo Tough Life";
        var logPath = Path.Combine(gamePath, "BepInEx", "LogOutput.log");
        var reportsPath = Path.Combine(basePath, "reports");
        
        // Parse command line args
        var command = args.Length > 0 ? args[0].ToLower() : "all";
        var generateReport = args.Contains("--report") || args.Contains("-r");
        
        switch (command)
        {
            case "build":
                RunBuild(projectPath);
                break;
                
            case "generate-schemas":
            case "schemas":
                GenerateSchemas(schemasPath);
                break;
                
            case "validate":
            case "validate-mods":
                ValidateMods(schemasPath, testModsPath);
                break;
                
            case "unit":
            case "unit-tests":
            case "tests":
                RunUnitTests(testProjectPath);
                break;
                
            case "ingame":
            case "in-game":
            case "surity":
                RunInGameTests(gamePath);
                break;
                
            case "logs":
            case "verify-logs":
                RunLogVerification(logPath);
                break;
                
            case "all":
            default:
                RunAll(projectPath, testProjectPath, schemasPath, testModsPath, gamePath, logPath, generateReport, reportsPath);
                break;
        }
    }
    
    static void RunBuild(string projectPath)
    {
        AnsiConsole.MarkupLine($"[bold]Building project:[/] {projectPath}");

        var runner = new BuildRunner();
        var result = runner.Run(projectPath);

        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]✓ Build Succeeded[/] ({result.Duration.TotalSeconds:F2}s)");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗ Build Failed[/] ({result.Duration.TotalSeconds:F2}s)");
            AnsiConsole.WriteLine(result.Errors);
            AnsiConsole.WriteLine(result.Output);
        }
    }
    
    static void GenerateSchemas(string outputPath)
    {
        AnsiConsole.MarkupLine($"[bold]Generating schemas to:[/] {outputPath}");
        
        try
        {
            SchemaGenerator.GenerateAll(outputPath);
            AnsiConsole.MarkupLine("[green]✓ Schemas generated successfully[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Schema generation failed:[/] {ex.Message}");
        }
    }
    
    static (int total, int valid, int invalid) ValidateMods(string schemasPath, string modsPath)
    {
        AnsiConsole.MarkupLine($"[bold]Validating mods in:[/] {modsPath}");
        
        // Check if schemas exist, generate if not
        if (!Directory.Exists(schemasPath) || !Directory.GetFiles(schemasPath, "*.schema.json").Any())
        {
            AnsiConsole.MarkupLine("[yellow]Schemas not found, generating...[/]");
            SchemaGenerator.GenerateAll(schemasPath);
        }
        
        // Step 1: Detect all mod types
        var detector = new ModTypeDetector();
        var mods = detector.ScanDirectory(modsPath);
        
        if (mods.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No mods found in {modsPath}[/]");
            return (0, 0, 0);
        }
        
        // Prepare validators
        var schemaValidator = new SchemaValidator(schemasPath);
        var csharpValidator = new CSharpPluginValidator();
        
        // Track results
        var results = new List<(ModInfo mod, bool valid, string status, int errors, List<string> errorMessages)>();
        
        foreach (var mod in mods)
        {
            switch (mod.Type)
            {
                case ModType.JsonMod:
                    var jsonResult = schemaValidator.ValidateMod(mod.Path);
                    var jsonErrors = jsonResult.FileResults.SelectMany(f => f.Errors).ToList();
                    results.Add((mod, jsonResult.IsValid, 
                        jsonResult.IsValid ? $"[green]✓ Valid ({jsonResult.ValidFiles}/{jsonResult.TotalFiles})[/]" : $"[red]✗ Invalid ({jsonResult.ValidFiles}/{jsonResult.TotalFiles})[/]",
                        jsonErrors.Count, jsonErrors));
                    break;
                    
                case ModType.CSharpPlugin:
                    var csharpResult = csharpValidator.Validate(mod);
                    var csharpErrors = csharpResult.Builds ? new List<string>() : new List<string> { csharpResult.Errors };
                    results.Add((mod, csharpResult.Builds,
                        csharpResult.Builds ? $"[green]✓ Builds ({csharpResult.Duration.TotalSeconds:F1}s)[/]" : "[red]✗ Build Failed[/]",
                        csharpResult.Builds ? 0 : 1, csharpErrors));
                    break;
                    
                case ModType.IncompleteJsonMod:
                    var missing = mod.ContentFolders.Any() 
                        ? $"Has {string.Join(", ", mod.ContentFolders)}/ but missing mod.json" 
                        : "Missing mod.json";
                    results.Add((mod, false, "[red]✗ Incomplete[/]", 1, new List<string> { missing }));
                    break;
                    
                case ModType.CompiledPlugin:
                    var dllCount = mod.DllPaths.Count;
                    results.Add((mod, true, $"[cyan]○ DLL ({dllCount} file(s))[/]", 0, new List<string>()));
                    break;
                    
                case ModType.Unknown:
                default:
                    results.Add((mod, true, "[dim]? Unknown[/]", 0, new List<string>()));
                    break;
            }
        }
        
        // Display results in a table with Type column
        var table = new Table();
        table.AddColumn("Mod");
        table.AddColumn("Type");
        table.AddColumn("Status");
        table.AddColumn("Errors");
        
        foreach (var (mod, valid, status, errors, _) in results)
        {
            var errorsCol = errors > 0 ? $"[red]{errors}[/]" : "[dim]0[/]";
            table.AddRow(mod.Name, $"[dim]{mod.TypeName}[/]", status, errorsCol);
        }
        
        AnsiConsole.Write(table);
        
        // Show details for failed mods
        var failedMods = results.Where(r => !r.valid && r.errorMessages.Any()).ToList();
        foreach (var (mod, _, _, _, errorMessages) in failedMods)
        {
            AnsiConsole.MarkupLine($"\n[bold red]Errors in {mod.Name} ({mod.TypeName}):[/]");
            foreach (var error in errorMessages.Take(5))
            {
                AnsiConsole.MarkupLine($"  [dim]- {Markup.Escape(error)}[/]");
            }
            if (errorMessages.Count > 5)
            {
                AnsiConsole.MarkupLine($"  [dim]... and {errorMessages.Count - 5} more[/]");
            }
        }
        
        // Summary
        AnsiConsole.WriteLine();
        var validCount = results.Count(r => r.valid);
        var invalidCount = results.Count(r => !r.valid);
        
        var jsonModCount = results.Count(r => r.mod.Type == ModType.JsonMod);
        var csharpCount = results.Count(r => r.mod.Type == ModType.CSharpPlugin);
        var summary = $"[dim]{jsonModCount} JSON mod(s), {csharpCount} C# plugin(s)[/]";
        
        if (invalidCount == 0)
        {
            AnsiConsole.MarkupLine($"[green]✓ All {validCount} mod(s) validated successfully[/] {summary}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗ {invalidCount}/{results.Count} mod(s) have issues[/] {summary}");
        }
        
        return (results.Count, validCount, invalidCount);
    }
    
    static void RunUnitTests(string testProjectPath)
    {
        AnsiConsole.MarkupLine($"[bold]Running unit tests:[/] {testProjectPath}");
        
        var runner = new UnitTestRunner();
        var result = runner.Run(testProjectPath);
        
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]✓ All {result.Passed} tests passed[/] ({result.Duration.TotalSeconds:F2}s)");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗ {result.Failed}/{result.TotalTests} tests failed[/] ({result.Duration.TotalSeconds:F2}s)");
            
            foreach (var failed in result.FailedTests.Take(10))
            {
                AnsiConsole.MarkupLine($"  [dim]- {Markup.Escape(failed)}[/]");
            }
            
            if (result.FailedTests.Count > 10)
            {
                AnsiConsole.MarkupLine($"  [dim]... and {result.FailedTests.Count - 10} more[/]");
            }
        }
    }
    
    static void RunInGameTests(string gamePath)
    {
        AnsiConsole.MarkupLine($"[bold]Running in-game tests with Surity:[/] {gamePath}");
        
        var tester = new InGameTester(gamePath);
        
        // Validate game setup first
        var (valid, message) = tester.ValidateGameSetup();
        if (!valid)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ {message}[/]");
            AnsiConsole.MarkupLine("[dim]In-game tests require:[/]");
            AnsiConsole.MarkupLine("[dim]  1. Game installed at the specified path[/]");
            AnsiConsole.MarkupLine("[dim]  2. BepInEx installed in game folder[/]");
            AnsiConsole.MarkupLine("[dim]  3. HoboModPlugin.dll in BepInEx/plugins[/]");
            AnsiConsole.MarkupLine("[dim]  4. In-game test DLL in BepInEx/plugins[/]");
            return;
        }
        
        AnsiConsole.MarkupLine($"[green]✓ {message}[/]");
        
        // Check/install Surity CLI
        if (!tester.IsSurityInstalled())
        {
            AnsiConsole.MarkupLine("[yellow]Installing Surity CLI...[/]");
            if (!tester.InstallSurity())
            {
                AnsiConsole.MarkupLine("[red]✗ Failed to install Surity CLI[/]");
                return;
            }
        }
        AnsiConsole.MarkupLine("[green]✓ Surity CLI available[/]");
        
        // Run tests
        AnsiConsole.MarkupLine("[dim]Launching game in batch mode for testing...[/]");
        var result = tester.Run(timeoutSeconds: 180);
        
        if (result.TotalTests == 0)
        {
            // No tests found/ran
            AnsiConsole.MarkupLine($"[yellow]⚠ No in-game tests found or executed[/] ({result.Duration.TotalSeconds:F2}s)");
            AnsiConsole.MarkupLine("[dim]  (In-game tests require the game to launch with Surity)[/]");
        }
        else if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]✓ All {result.Passed} in-game tests passed[/] ({result.Duration.TotalSeconds:F2}s)");
        }
        else if (!result.GameLaunched)
        {
            AnsiConsole.MarkupLine($"[red]✗ Game failed to launch: {Markup.Escape(result.Errors)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗ {result.Failed}/{result.TotalTests} in-game tests failed[/] ({result.Duration.TotalSeconds:F2}s)");
            
            foreach (var failed in result.FailedTests.Take(10))
            {
                AnsiConsole.MarkupLine($"  [dim]- {Markup.Escape(failed)}[/]");
            }
        }
    }
    
    static void RunLogVerification(string logPath)
    {
        AnsiConsole.MarkupLine($"[bold]Verifying BepInEx log:[/] {logPath}");
        
        var verifier = new LogVerifier();
        
        // First, get summary
        var summary = verifier.GetSummary(logPath);
        
        if (!summary.Exists)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ Log file not found[/]");
            AnsiConsole.MarkupLine("[dim]Log verification requires the game to have been run with BepInEx[/]");
            return;
        }
        
        AnsiConsole.MarkupLine($"[dim]Log entries: {summary.TotalEntries} | HoboMod entries: {summary.HoboModEntries}[/]");
        
        // Create basic expectations
        var expectations = new LogExpectations
        {
            ExpectedMods = new List<string> { "testsuite" },
            ExpectedItems = new List<string> { "super_apple" },
            FailOnErrors = true,
            IgnoreErrorsFromSources = new List<string> { "Unity", "Steamworks" }
        };
        
        var result = verifier.Verify(logPath, expectations);
        
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]✓ Log verification passed[/]");
            foreach (var check in result.PassedChecks.Take(5))
            {
                AnsiConsole.MarkupLine($"  [dim]✓ {check}[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗ Log verification failed ({result.Failures.Count} issues)[/]");
            foreach (var failure in result.Failures.Take(10))
            {
                AnsiConsole.MarkupLine($"  [red]- {Markup.Escape(failure)}[/]");
            }
        }
        
        if (result.Warnings.Any())
        {
            AnsiConsole.MarkupLine($"[yellow]  Warnings: {result.Warnings.Count}[/]");
        }
        
        if (result.ErrorCount > 0)
        {
            AnsiConsole.MarkupLine($"[dim]  Errors in log: {result.ErrorCount}[/]");
        }
    }
    
    static void RunAll(string projectPath, string testProjectPath, string schemasPath, string modsPath, string gamePath, string logPath, bool generateReport, string reportsPath)
    {
        var startTime = DateTime.Now;
        var report = new TestReport();
        
        AnsiConsole.MarkupLine("[bold]Running full test suite...[/]\n");
        
        // Step 1: Build
        AnsiConsole.Write(new Rule("[cyan]Step 1: Build Verification[/]"));
        var buildRunner = new BuildRunner();
        var buildResult = buildRunner.Run(projectPath);
        report.Build = new BuildReport
        {
            Success = buildResult.Success,
            DurationSeconds = buildResult.Duration.TotalSeconds,
            ErrorMessage = buildResult.Success ? null : buildResult.Errors
        };
        if (buildResult.Success)
            AnsiConsole.MarkupLine($"[green]✓ Build Succeeded[/] ({buildResult.Duration.TotalSeconds:F2}s)");
        else
            AnsiConsole.MarkupLine($"[red]✗ Build Failed[/]");
        AnsiConsole.WriteLine();
        
        // Step 2: Unit Tests
        AnsiConsole.Write(new Rule("[cyan]Step 2: Unit Tests[/]"));
        var unitRunner = new UnitTestRunner();
        var unitResult = unitRunner.Run(testProjectPath);
        report.UnitTests = new UnitTestReport
        {
            Success = unitResult.Success,
            Total = unitResult.TotalTests,
            Passed = unitResult.Passed,
            Failed = unitResult.Failed,
            Skipped = unitResult.Skipped,
            DurationSeconds = unitResult.Duration.TotalSeconds,
            FailedTests = unitResult.FailedTests
        };
        if (unitResult.Success)
            AnsiConsole.MarkupLine($"[green]✓ All {unitResult.Passed} tests passed[/] ({unitResult.Duration.TotalSeconds:F2}s)");
        else
            AnsiConsole.MarkupLine($"[red]✗ {unitResult.Failed}/{unitResult.TotalTests} tests failed[/]");
        AnsiConsole.WriteLine();
        
        // Step 3: Generate Schemas
        AnsiConsole.Write(new Rule("[cyan]Step 3: Schema Generation[/]"));
        GenerateSchemas(schemasPath);
        report.Schemas = new SchemaReport
        {
            Success = true,
            GeneratedSchemas = new List<string> { "mod.schema.json", "item.schema.json", "recipe.schema.json", "quest.schema.json" }
        };
        AnsiConsole.WriteLine();
        
        // Step 4: Validate Mods
        AnsiConsole.Write(new Rule("[cyan]Step 4: Mod Validation[/]"));
        var (totalMods, validMods, invalidMods) = ValidateMods(schemasPath, modsPath);
        report.ModValidation = new ModValidationReport 
        { 
            Success = invalidMods == 0, 
            TotalMods = totalMods, 
            ValidMods = validMods, 
            InvalidMods = invalidMods 
        };
        AnsiConsole.WriteLine();
        
        // Step 5: In-Game Tests (optional)
        AnsiConsole.Write(new Rule("[cyan]Step 5: In-Game Tests (Optional)[/]"));
        var tester = new InGameTester(gamePath);
        var (valid, msg) = tester.ValidateGameSetup();
        if (valid)
        {
            RunInGameTests(gamePath);
            report.InGameTests = new InGameReport { Skipped = false };
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]⚠ Skipping in-game tests (game not configured)[/]");
            report.InGameTests = new InGameReport { Skipped = true, SkipReason = msg };
        }
        AnsiConsole.WriteLine();
        
        // Step 6: Log Verification (optional)
        AnsiConsole.Write(new Rule("[cyan]Step 6: Log Verification (Optional)[/]"));
        if (File.Exists(logPath))
        {
            var verifier = new LogVerifier();
            var logResult = verifier.Verify(logPath, new LogExpectations
            {
                ExpectedMods = new List<string> { "testsuite" },
                FailOnErrors = true,
                IgnoreErrorsFromSources = new List<string> { "Unity", "Steamworks" }
            });
            report.LogVerification = new LogReport
            {
                Success = logResult.Success,
                Skipped = false,
                TotalEntries = logResult.TotalEntries,
                ErrorCount = logResult.ErrorCount,
                WarningCount = logResult.WarningCount,
                PassedChecks = logResult.PassedChecks,
                Failures = logResult.Failures
            };
            if (logResult.Success)
                AnsiConsole.MarkupLine($"[green]✓ Log verification passed[/]");
            else
                AnsiConsole.MarkupLine($"[red]✗ Log verification failed ({logResult.Failures.Count} issues)[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]⚠ Skipping log verification (log not found)[/]");
            report.LogVerification = new LogReport { Skipped = true, SkipReason = "Log file not found" };
        }
        AnsiConsole.WriteLine();
        
        // Final summary
        report.TotalDuration = DateTime.Now - startTime;
        report.OverallSuccess = (report.Build?.Success ?? false) && (report.UnitTests?.Success ?? false);
        
        AnsiConsole.Write(new Rule("[bold green]Test Suite Complete[/]"));
        
        // Generate reports if requested
        if (generateReport)
        {
            Directory.CreateDirectory(reportsPath);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var jsonPath = Path.Combine(reportsPath, $"report_{timestamp}.json");
            var htmlPath = Path.Combine(reportsPath, $"report_{timestamp}.html");
            
            var generator = new ReportGenerator();
            generator.SaveJson(report, jsonPath);
            generator.SaveHtml(report, htmlPath);
            
            AnsiConsole.MarkupLine($"\n[dim]Reports saved:[/]");
            AnsiConsole.MarkupLine($"  [cyan]{jsonPath}[/]");
            AnsiConsole.MarkupLine($"  [cyan]{htmlPath}[/]");
        }
    }
}



