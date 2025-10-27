using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Comfizen;

/// <summary>
/// Provides a suite of tests to verify the functionality of the WildcardProcessor.
/// Creates a temporary directory with mock wildcard files to ensure isolated testing.
/// </summary>
public class WildcardSystemTester
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "wildcards_test");
    private readonly Random _random = new Random(0); // Use fixed seed for predictable "random" choices

    /// <summary>
    /// Runs all wildcard tests and returns a formatted report.
    /// </summary>
    public string RunAllTests()
    {
#if DEBUG
        var report = new StringBuilder();
        report.AppendLine("--- Running Wildcard System Tests ---");

        try
        {
            SetupMockFiles();
            
            // Change: Use the new, safe methods to set the test directory.
            
            WildcardFileHandler.SetTestDirectory(_testDir);
            
            
            // Clear caches before running tests
            var contentCacheField = typeof(WildcardFileHandler).GetField("_contentCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            ((System.Collections.Concurrent.ConcurrentDictionary<string, string[]>)contentCacheField.GetValue(null)).Clear();
            var listCacheField = typeof(WildcardFileHandler).GetField("_allWildcardNamesCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            listCacheField.SetValue(null, null);


            RunTest(report, "Simple Wildcard", "__color__", new[] { "red", "green", "blue" });
            RunTest(report, "Simple Non-Existent Wildcard", "__animal__", new[] { "__animal__" });
            RunTest(report, "Glob Wildcard", "__poses/style_*__", new[] { "action pose", "dynamic pose" });
            RunTest(report, "Strict Glob Matching", "__pose*__", new[] { "action pose", "dynamic pose", "sitting", "standing" });
            RunTest(report, "Combined List", "{art|__color__}", new[] { "art", "red", "green", "blue" });
            RunTest(report, "Quantifier Exact", "{2$$a|b|c}", res => res.Split(new[] { ", " }, StringSplitOptions.None).Length == 2);
            RunTest(report, "Quantifier Range", "{1-2$$a|b|c}", res => { var p = res.Split(new[] { ", " }, StringSplitOptions.None); return p.Length >= 1 && p.Length <= 2; });
            RunTest(report, "Quantifier with Custom Separator", "{2$$ :: $$a|b|c}", res => res.Contains(" :: ") && res.Split(new[] { " :: " }, StringSplitOptions.None).Length == 2);
            RunTest(report, "Nested Wildcard in Braces", "A {detailed|{__quality__}} picture", new[] { "A detailed picture", "A beautiful picture", "A stunning picture" });
            RunTest(report, "Nested Wildcard in Wildcard Path", "__nested/{__part__}__", new[] { "final_a", "final_b" });
            
            // --- MODIFIED AND NEW TESTS ---
            // This test's expected outcome changes because now __recursive__ -> __color__ -> red/green/blue
            RunTest(report, "Recursive Wildcard (Now fully resolves)", "__recursive__", new[] { "red", "green", "blue" });
            
            // New test for multi-level expansion
            RunTest(report, "Multi-level Wildcard Expansion", "A __toplevel__ story.", new[] { "A very exciting story." });
            
            // New test for infinite loop safety
            RunTest(report, "Infinite Loop Safety", "__loop_a__", res => !string.IsNullOrEmpty(res)); // Checks that it terminates without crashing

        }
        catch (Exception ex)
        {
            report.AppendLine($"\nFATAL ERROR DURING TEST: {ex.Message}");
            report.AppendLine(ex.StackTrace);
        }
        finally
        {
            // Change: Use the new, safe method to restore the directory.
            WildcardFileHandler.ResetDirectory();
            CleanupMockFiles();
        }

        report.AppendLine("\n--- Tests Finished ---");
        return report.ToString();
#else
        return "";
#endif
    }

    private void RunTest(StringBuilder report, string testName, string input, Func<string, bool> validationFunc)
    {
        try
        {
            var processor = new WildcardProcessor(0); // Use fixed seed for deterministic results
            string result = processor.Process(input);
            bool success = validationFunc(result);
            report.AppendLine($"[{(success ? "PASS" : "FAIL")}] {testName}: '{input}' -> '{result}'");
            if (!success) Debug.WriteLine($"FAIL: {testName} -> {result}");
        }
        catch (Exception ex)
        {
            report.AppendLine($"[ERROR] {testName}: {ex.Message}");
        }
    }

    private void RunTest(StringBuilder report, string testName, string input, string[] expectedOutcomes)
    {
        RunTest(report, testName, input, result => expectedOutcomes.Contains(result));
    }

    private void SetupMockFiles()
    {
        if (Directory.Exists(_testDir)) CleanupMockFiles();
        Directory.CreateDirectory(_testDir);
        
        // Create nested directories
        Directory.CreateDirectory(Path.Combine(_testDir, "poses"));
        Directory.CreateDirectory(Path.Combine(_testDir, "nested"));

        File.WriteAllLines(Path.Combine(_testDir, "color.txt"), new[] { "red", "green", "blue" });
        File.WriteAllLines(Path.Combine(_testDir, "quality.txt"), new[] { "beautiful", "stunning" });
        File.WriteAllLines(Path.Combine(_testDir, "poses", "style_action.txt"), new[] { "action pose" });
        File.WriteAllLines(Path.Combine(_testDir, "poses", "style_dynamic.txt"), new[] { "dynamic pose" });
        File.WriteAllLines(Path.Combine(_testDir, "poses.txt"), new[] { "sitting", "standing" });
        
        // For nested path test
        File.WriteAllLines(Path.Combine(_testDir, "part.txt"), new[] { "a", "b" });
        File.WriteAllLines(Path.Combine(_testDir, "nested", "a.txt"), new[] { "final_a" });
        File.WriteAllLines(Path.Combine(_testDir, "nested", "b.txt"), new[] { "final_b" });
        
        // For recursion/safety test
        File.WriteAllLines(Path.Combine(_testDir, "recursive.txt"), new[] { "__color__" });
        
        // --- NEW MOCK FILES ---
        // For multi-level expansion test
        File.WriteAllLines(Path.Combine(_testDir, "toplevel.txt"), new[] { "very __midlevel__" });
        File.WriteAllLines(Path.Combine(_testDir, "midlevel.txt"), new[] { "exciting" });

        // For infinite loop test
        File.WriteAllLines(Path.Combine(_testDir, "loop_a.txt"), new[] { "loop __loop_b__" });
        File.WriteAllLines(Path.Combine(_testDir, "loop_b.txt"), new[] { "chain __loop_a__" });
    }

    private void CleanupMockFiles()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }
}