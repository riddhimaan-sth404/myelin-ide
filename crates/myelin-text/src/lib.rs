pub mod buffer;
pub mod cursor;
pub mod history;
pub mod point;
pub mod syntax;

pub use buffer::{TextBuffer, TextError};
pub use cursor::{CursorSet, Selection};
pub use history::{EditOperation, History, Transaction};
pub use point::{ByteRange, Point, TextRange};
pub use syntax::{LexerState, SimpleLexer, StyledSpan, TokenType};

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_buffer_basic_edits() {
        let mut buf = TextBuffer::from_str("Hello World\nLine 2\nLine 3");
        assert_eq!(buf.line_count(), 3);
        assert_eq!(buf.line_text(0).unwrap(), "Hello World");
        assert_eq!(buf.line_text(1).unwrap(), "Line 2");
        assert_eq!(buf.line_text(2).unwrap(), "Line 3");

        // Insert at point
        buf.insert(Point::new(0, 5), " Beautiful").unwrap();
        assert_eq!(buf.line_text(0).unwrap(), "Hello Beautiful World");

        // Convert Point <-> Byte
        let p = Point::new(1, 4);
        let byte = buf.point_to_byte(p).unwrap();
        let p_back = buf.byte_to_point(byte).unwrap();
        assert_eq!(p, p_back);

        // Delete range
        let deleted = buf.delete(TextRange::new(Point::new(0, 5), Point::new(0, 15))).unwrap();
        assert_eq!(deleted, " Beautiful");
        assert_eq!(buf.line_text(0).unwrap(), "Hello World");
    }

    #[test]
    fn test_history_undo_redo() {
        let mut buf = TextBuffer::from_str("Initial");
        let mut history = History::new();

        // Transaction 1: Append text
        let insert_text = " Text";
        let offset = buf.len_bytes();
        buf.insert_bytes(offset, insert_text).unwrap();
        history.push(Transaction::insert(offset, insert_text.to_string()));

        assert_eq!(buf.to_string(), "Initial Text");

        // Undo
        assert!(history.undo(&mut buf).unwrap().is_some());
        assert_eq!(buf.to_string(), "Initial");

        // Redo
        assert!(history.redo(&mut buf).unwrap().is_some());
        assert_eq!(buf.to_string(), "Initial Text");
    }

    #[test]
    fn test_syntax_highlighting() {
        let line = "fn main() { let x: u32 = 42; // comment }";
        let spans = SimpleLexer::highlight_line(line);
        assert!(!spans.is_empty());
        assert_eq!(spans[0].text, "fn");
        assert_eq!(spans[0].color, "#C586C0");

        // Test string with double backslash
        let escaped_str_line = r#"let s = "a\\"; let y = 1;"#;
        let spans2 = SimpleLexer::highlight_line(escaped_str_line);
        assert!(spans2.iter().any(|s| s.text == r#""a\\""# && s.color == "#CE9178"));
        assert!(spans2.iter().any(|s| s.text == "let" && s.color == "#C586C0"));
    }

    #[test]
    fn test_buffer_spaces_and_indentation() {
        let mut buf = TextBuffer::from_str("fn main() {\n}");
        // Insert space at end of line 0
        buf.insert(Point::new(0, 11), " ").unwrap();
        assert_eq!(buf.line_text(0).unwrap(), "fn main() { ");
        // Insert 4 spaces at line 1
        buf.insert(Point::new(1, 0), "    let a = 10;").unwrap();
        assert_eq!(buf.line_text(1).unwrap(), "    let a = 10;}");
    }

    #[test]
    fn test_stateful_block_comment_across_lines() {
        use syntax::{SimpleLexer, LexerState};

        let lines = vec![
            "fn foo() {",
            "    /* start comment",
            "    still in comment",
            "    end */",
            "    let x = 1;",
        ];
        let line_refs: Vec<&str> = lines.iter().map(|s| *s).collect();
        let (spans, final_state) = SimpleLexer::highlight_lines(&line_refs, LexerState::Normal);

        // Line 0: normal
        assert_eq!(spans[0][0].text, "fn");
        assert_eq!(spans[0][0].color, "#C586C0");

        // Line 1: enters block comment
        assert!(spans[1].iter().any(|s| s.color == "#6A9955"));

        // Line 2: all comment
        assert!(spans[2].iter().all(|s| s.color == "#6A9955"));

        // Line 3: block comment ends
        assert!(spans[3].iter().any(|s| s.text.contains("*/") && s.color == "#6A9955"));

        // Line 4: back to normal
        assert!(spans[4].iter().any(|s| s.text == "let" && s.color == "#C586C0"));

        assert_eq!(final_state, LexerState::Normal);
    }

    #[test]
    fn test_block_comment_single_line() {
        use syntax::{SimpleLexer, LexerState};

        let (spans, state) = SimpleLexer::highlight_line_with_state("/* hello */", LexerState::Normal);
        assert!(spans.iter().any(|s| s.text.contains("/*")));
        assert!(spans.iter().any(|s| s.text.contains("*/")));
        assert_eq!(state, LexerState::Normal);
    }

    #[test]
    fn test_block_comment_unclosed() {
        use syntax::{SimpleLexer, LexerState};

        let (spans, state) = SimpleLexer::highlight_line_with_state("/* not closed", LexerState::Normal);
        assert_eq!(spans.len(), 1);
        assert_eq!(spans[0].color, "#6A9955");
        assert_eq!(state, LexerState::InBlockComment);
    }

    #[test]
    fn test_block_comment_close_in_middle() {
        use syntax::{SimpleLexer, LexerState};

        // Start in a block comment, close it mid-line
        let (spans, state) = SimpleLexer::highlight_line_with_state("end of comment */ let x = 1;", LexerState::InBlockComment);
        assert_eq!(state, LexerState::Normal);
        assert!(spans.iter().any(|s| s.text.contains("*/")));
        assert!(spans.iter().any(|s| s.text == "let" && s.color == "#C586C0"));
    }
}
