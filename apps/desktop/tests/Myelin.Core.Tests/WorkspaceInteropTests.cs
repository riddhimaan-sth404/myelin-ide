using System;
using System.IO;
using Myelin.Core;
using Xunit;

namespace Myelin.Core.Tests
{
    public class WorkspaceInteropTests
    {
        [Fact]
        public void Test_Workspace_ScratchDocument_Lifecycle()
        {
            using var ws = new NativeWorkspace();
            ulong docId = ws.OpenScratch("fn main() {\n    println!(\"Hello\");\n}");
            Assert.True(docId > 0);

            // Verify lines
            nuint lineCount = ws.GetLineCount(docId);
            Assert.Equal((nuint)3, lineCount);

            string line0 = ws.GetLine(docId, 0);
            Assert.Equal("fn main() {", line0);

            // Insert text
            ws.SetCursor(docId, 1, 19); // after println!("Hello
            ws.InsertAtCursor(docId, ", World!");

            string line1 = ws.GetLine(docId, 1);
            Assert.Equal("    println!(\"Hello, World!\");", line1);
            Assert.True(ws.IsDirty(docId));

            // Undo
            Assert.True(ws.Undo(docId));
            string line1Undone = ws.GetLine(docId, 1);
            Assert.Equal("    println!(\"Hello\");", line1Undone);

            // Redo
            Assert.True(ws.Redo(docId));
            string line1Redone = ws.GetLine(docId, 1);
            Assert.Equal("    println!(\"Hello, World!\");", line1Redone);

            // Close document
            Assert.True(ws.CloseDocument(docId));
        }

        [Fact]
        public void Test_Workspace_DirectoryScanning()
        {
            string currentDir = Directory.GetCurrentDirectory();
            var tree = NativeWorkspace.ScanDirectory(currentDir, 2);
            Assert.NotNull(tree);
            Assert.True(tree.IsDirectory);
        }

        [Fact]
        public void Test_NativeTerminal_SpawnAndWrite()
        {
            using var term = new NativeTerminal(80, 24);
            Assert.True(term.IsAlive);

            // Write a simple command
            bool wrote = term.Write("echo 'Myelin Terminal'\r\n");
            Assert.True(wrote);

            System.Threading.Thread.Sleep(200);
            string output = term.ReadAvailable();
            Assert.NotNull(output);
        }
    }
}
