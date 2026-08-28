using System;
using Avalonia.Media;
using Myelin.UI.Views;
using Xunit;

namespace Myelin.Core.Tests
{
    public class TerminalCanvasLogicTests
    {
        [Fact]
        public void Test_Terminal_BasicInputAndCursor()
        {
            var canvas = new TerminalCanvas();
            canvas.ProcessAnsiStream("Hello World");

            Assert.Equal(11, canvas.CursorCol);
            Assert.True(canvas.CursorRow >= 0);
        }

        [Fact]
        public void Test_Terminal_CursorPositioning_Relative_To_Screen()
        {
            var canvas = new TerminalCanvas();

            // Feed 40 lines of text (more than default 30 rows)
            for (int i = 0; i < 40; i++)
            {
                canvas.ProcessAnsiStream($"Line {i}\r\n");
            }

            int screenTop = canvas.ScreenTopRow;
            Assert.True(screenTop > 0, "ScreenTopRow should be greater than 0 after scrolling");

            // Send CUP: row 1, col 5 (ESC [ 1 ; 5 H)
            canvas.ProcessAnsiStream("\x1b[1;5H");

            // CursorRow must be at ScreenTopRow, NOT row 0 of scrollback!
            Assert.Equal(screenTop, canvas.CursorRow);
            Assert.Equal(4, canvas.CursorCol); // 0-based col 4 = 1-based col 5

            // Send CUP: row 10, col 1 (ESC [ 10 ; 1 H)
            canvas.ProcessAnsiStream("\x1b[10;1H");
            Assert.Equal(screenTop + 9, canvas.CursorRow);
            Assert.Equal(0, canvas.CursorCol);
        }

        [Fact]
        public void Test_Terminal_CursorMovement_Commands()
        {
            var canvas = new TerminalCanvas();
            canvas.ProcessAnsiStream("\x1b[5;10H"); // Row 5, Col 10 (0-indexed: 4, 9)
            int initialRow = canvas.CursorRow;

            // Cursor Up 2 rows (ESC [ 2 A)
            canvas.ProcessAnsiStream("\x1b[2A");
            Assert.Equal(initialRow - 2, canvas.CursorRow);

            // Cursor Down 3 rows (ESC [ 3 B)
            canvas.ProcessAnsiStream("\x1b[3B");
            Assert.Equal(initialRow + 1, canvas.CursorRow);

            // Cursor Forward 4 cols (ESC [ 4 C)
            canvas.ProcessAnsiStream("\x1b[4C");
            Assert.Equal(9 + 4, canvas.CursorCol);

            // Cursor Back 5 cols (ESC [ 5 D)
            canvas.ProcessAnsiStream("\x1b[5D");
            Assert.Equal(9 + 4 - 5, canvas.CursorCol);

            // CHA - Column Absolute (ESC [ 20 G)
            canvas.ProcessAnsiStream("\x1b[20G");
            Assert.Equal(19, canvas.CursorCol);

            // VPA - Line Position Absolute (ESC [ 3 d)
            canvas.ProcessAnsiStream("\x1b[3d");
            Assert.Equal(canvas.ScreenTopRow + 2, canvas.CursorRow);
        }

        [Fact]
        public void Test_Terminal_EraseInLine_Modes()
        {
            var canvas = new TerminalCanvas();
            canvas.ProcessAnsiStream("ABCDEFGH");

            // Move cursor to col 4 ('D') and erase to end of line (ESC [ K or ESC [ 0 K)
            canvas.ProcessAnsiStream("\x1b[4G\x1b[K");

            // Write new text
            canvas.ProcessAnsiStream("123");
            Assert.Equal(6, canvas.CursorCol);
        }

        [Fact]
        public void Test_Terminal_EraseInDisplay_Modes()
        {
            var canvas = new TerminalCanvas();

            // Fill multiple lines
            for (int i = 0; i < 10; i++)
            {
                canvas.ProcessAnsiStream($"Row {i}\r\n");
            }

            // Move to row 2 and erase from cursor to end of screen (ESC [ J)
            canvas.ProcessAnsiStream("\x1b[2;1H\x1b[J");

            // The cursor row should be preserved
            Assert.Equal(canvas.ScreenTopRow + 1, canvas.CursorRow);

            // Erase entire screen (ESC [ 2 J)
            canvas.ProcessAnsiStream("\x1b[2J");
            Assert.True(canvas.CursorRow >= canvas.ScreenTopRow);
        }

        [Fact]
        public void Test_Terminal_ScrollUpAndDown()
        {
            var canvas = new TerminalCanvas();
            for (int i = 0; i < 20; i++)
            {
                canvas.ProcessAnsiStream($"Line {i}\r\n");
            }
            int topBefore = canvas.ScreenTopRow;

            // Scroll Up by 3 lines (ESC [ 3 S)
            canvas.ProcessAnsiStream("\x1b[3S");
            Assert.True(canvas.ScreenTopRow >= topBefore);

            // Scroll Down by 2 lines (ESC [ 2 T)
            canvas.ProcessAnsiStream("\x1b[2T");
            Assert.True(canvas.ScreenTopRow >= 0);
        }

        [Fact]
        public void Test_Terminal_InsertAndDeleteLines()
        {
            var canvas = new TerminalCanvas();
            canvas.ProcessAnsiStream("Line 1\r\nLine 2\r\nLine 3\r\n");

            // Position on Line 2 (ESC [ 2 ; 1 H) and insert 1 line (ESC [ 1 L)
            canvas.ProcessAnsiStream("\x1b[2;1H\x1b[1L");
            Assert.Equal(canvas.ScreenTopRow + 1, canvas.CursorRow);

            // Delete 1 line (ESC [ 1 M)
            canvas.ProcessAnsiStream("\x1b[1M");
            Assert.Equal(canvas.ScreenTopRow + 1, canvas.CursorRow);
        }

        [Fact]
        public void Test_Terminal_AlternateScreenBuffer()
        {
            var canvas = new TerminalCanvas();
            canvas.ProcessAnsiStream("Main Screen Content\r\n");

            // Enter alternate screen buffer (like vim/nano: ESC [ ? 1049 h)
            canvas.ProcessAnsiStream("\x1b[?1049h");
            canvas.ProcessAnsiStream("Vim Screen Content");

            Assert.Equal(18, canvas.CursorCol);

            // Exit alternate screen buffer (ESC [ ? 1049 l)
            canvas.ProcessAnsiStream("\x1b[?1049l");

            // Returned to main screen
            Assert.True(canvas.CursorRow >= 0);
        }

        [Fact]
        public void Test_Terminal_TabExpansion()
        {
            var canvas = new TerminalCanvas();
            canvas.ProcessAnsiStream("A\tB");

            // 'A' at col 0, tab advances to col 8, 'B' at col 8 -> cursor at 9
            Assert.Equal(9, canvas.CursorCol);
        }

        [Fact]
        public void Test_Terminal_SaveAndRestoreCursor()
        {
            var canvas = new TerminalCanvas();
            canvas.ProcessAnsiStream("\x1b[5;10H"); // Row 5, Col 10
            int expectedRow = canvas.CursorRow;
            int expectedCol = canvas.CursorCol;

            // Save cursor (ESC [ s)
            canvas.ProcessAnsiStream("\x1b[s");

            // Move somewhere else
            canvas.ProcessAnsiStream("\x1b[1;1H");
            Assert.NotEqual(expectedRow, canvas.CursorRow);

            // Restore cursor (ESC [ u)
            canvas.ProcessAnsiStream("\x1b[u");
            Assert.Equal(expectedRow, canvas.CursorRow);
            Assert.Equal(expectedCol, canvas.CursorCol);
        }

        [Fact]
        public void Test_Terminal_SgrColorParsing()
        {
            var canvas = new TerminalCanvas();

            // Set Red Foreground (ESC [ 31 m), Bold (ESC [ 1 m), Green Background (ESC [ 42 m)
            canvas.ProcessAnsiStream("\x1b[31;1;42mColorText\x1b[0mNormalText");
            Assert.Equal(19, canvas.CursorCol);

            // 24-bit TrueColor Foreground: ESC [ 38 ; 2 ; 100 ; 150 ; 200 m
            canvas.ProcessAnsiStream("\x1b[38;2;100;150;200mTrueColor\x1b[0m");
            Assert.Equal(28, canvas.CursorCol);
        }
    }
}
