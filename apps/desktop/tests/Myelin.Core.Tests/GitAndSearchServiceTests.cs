using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Myelin.Core.Models;
using Myelin.Core.Services;
using Xunit;

namespace Myelin.Core.Tests
{
    public class GitAndSearchServiceTests
    {
        [Fact]
        public void ParsePorcelainStatus_ParsesStagedAndWorkingChangesCorrectly()
        {
            var service = new GitService();
            var result = new GitStatusResult();
            string mockOutput = 
                "M  src/Myelin.Core/Services/GitService.cs\n" +
                "A  src/Myelin.Core/Models/GitModels.cs\n" +
                " M src/Myelin.UI/Views/MainWindow.axaml\n" +
                "?? tests/new_file.txt\n" +
                " D deleted_file.rs\n";

            service.ParsePorcelainStatus(@"D:\Projects\myelin-ide", mockOutput, result);

            // Verify Staged
            Assert.Equal(2, result.StagedFiles.Count);
            Assert.Contains(result.StagedFiles, f => f.RelativePath.Contains("GitService.cs") && f.Status == GitFileStatus.Modified);
            Assert.Contains(result.StagedFiles, f => f.RelativePath.Contains("GitModels.cs") && f.Status == GitFileStatus.Added);

            // Verify Working
            Assert.Equal(3, result.WorkingFiles.Count);
            Assert.Contains(result.WorkingFiles, f => f.RelativePath.Contains("MainWindow.axaml") && f.Status == GitFileStatus.Modified);
            Assert.Contains(result.WorkingFiles, f => f.RelativePath.Contains("new_file.txt") && f.Status == GitFileStatus.Untracked);
            Assert.Contains(result.WorkingFiles, f => f.RelativePath.Contains("deleted_file.rs") && f.Status == GitFileStatus.Deleted);
        }

        [Fact]
        public async Task WorkspaceSearchService_FindsMatchesInDirectory()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "myelin_search_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string file1 = Path.Combine(tempDir, "test1.rs");
                string file2 = Path.Combine(tempDir, "test2.cs");

                File.WriteAllText(file1, "fn main() {\n    let test_var = 42;\n    println!(\"test_var: {}\", test_var);\n}\n");
                File.WriteAllText(file2, "namespace Test {\n    public class Runner {\n        int count = 100;\n    }\n}\n");

                var searchService = new WorkspaceSearchService();
                var options = new SearchOptions
                {
                    Query = "test_var",
                    MatchCase = true,
                    MatchWholeWord = true
                };

                var results = await searchService.SearchAsync(tempDir, options);

                Assert.Single(results);
                Assert.Equal("test1.rs", results[0].FileName);
                Assert.Equal(3, results[0].MatchCount);
                Assert.Equal(2, results[0].Matches[0].LineNumber);
                Assert.Equal(3, results[0].Matches[1].LineNumber);
                Assert.Equal(3, results[0].Matches[2].LineNumber);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void GitFileItem_StatusCharAndTooltip_AreAccurate()
        {
            var untracked = new GitFileItem { RelativePath = "test.txt", Status = GitFileStatus.Untracked };
            Assert.Equal("U", untracked.StatusChar);
            Assert.Equal("Untracked", untracked.StatusTooltip);

            var added = new GitFileItem { RelativePath = "test.txt", Status = GitFileStatus.Added };
            Assert.Equal("A", added.StatusChar);
            Assert.Equal("Index Added", added.StatusTooltip);

            var modified = new GitFileItem { RelativePath = "test.txt", Status = GitFileStatus.Modified };
            Assert.Equal("M", modified.StatusChar);
            Assert.Equal("Modified", modified.StatusTooltip);
        }
    }
}
