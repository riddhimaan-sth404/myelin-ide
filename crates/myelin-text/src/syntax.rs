use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum TokenType {
    Keyword,
    Type,
    Function,
    String,
    Number,
    Comment,
    Punctuation,
    Plain,
}

impl TokenType {
    /// Returns the standard VS Code Dark+ hex color for the token type.
    pub fn hex_color(&self) -> &'static str {
        match self {
            TokenType::Keyword => "#C586C0",    // Pink/Purple
            TokenType::Type => "#4EC9B0",       // Teal
            TokenType::Function => "#DCDCAA",   // Pale Yellow
            TokenType::String => "#CE9178",     // Salmon / Orange-Brown
            TokenType::Number => "#B5CEA8",     // Mint Green
            TokenType::Comment => "#6A9955",    // Green
            TokenType::Punctuation => "#D4D4D4",// Light Gray
            TokenType::Plain => "#D4D4D4",      // Light Gray
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StyledSpan {
    pub text: String,
    pub color: String,
}

/// Lexer state for cross-line highlighting (block comments, multi-line strings).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LexerState {
    Normal,
    InBlockComment,
}

/// Ultra-fast lexer for real-time syntax highlighting.
pub struct SimpleLexer;

impl SimpleLexer {
    pub fn highlight_line(line: &str) -> Vec<StyledSpan> {
        let (spans, _state) = Self::highlight_line_with_state(line, LexerState::Normal);
        spans
    }

    /// Stateful per-line highlight. Returns (spans, ending_state).
    pub fn highlight_line_with_state(line: &str, mut state: LexerState) -> (Vec<StyledSpan>, LexerState) {
        let mut spans = Vec::new();
        let chars: Vec<char> = line.chars().collect();
        let n = chars.len();
        let mut i = 0;

        // If we're inside a block comment from a previous line
        if state == LexerState::InBlockComment {
            let mut end_found = false;
            while i + 1 < n {
                if chars[i] == '*' && chars[i + 1] == '/' {
                    let before = chars[0..i].iter().collect::<String>();
                    if !before.is_empty() {
                        spans.push(StyledSpan { text: before, color: TokenType::Comment.hex_color().to_string() });
                    }
                    spans.push(StyledSpan { text: "*/".to_string(), color: TokenType::Comment.hex_color().to_string() });
                    i += 2;
                    state = LexerState::Normal;
                    end_found = true;
                    break;
                }
                i += 1;
            }
            if !end_found {
                spans.push(StyledSpan {
                    text: line.to_string(),
                    color: TokenType::Comment.hex_color().to_string(),
                });
                return (spans, LexerState::InBlockComment);
            }
        }

        while i < n {
            let ch = chars[i];

            // Whitespace
            if ch.is_whitespace() {
                let start = i;
                while i < n && chars[i].is_whitespace() {
                    i += 1;
                }
                spans.push(StyledSpan {
                    text: chars[start..i].iter().collect(),
                    color: TokenType::Plain.hex_color().to_string(),
                });
                continue;
            }

            // Single-line comment starting mid-line
            if ch == '/' && i + 1 < n && chars[i + 1] == '/' {
                spans.push(StyledSpan {
                    text: chars[i..n].iter().collect(),
                    color: TokenType::Comment.hex_color().to_string(),
                });
                i = n;
                continue;
            }

            // Block comment start
            if ch == '/' && i + 1 < n && chars[i + 1] == '*' {
                let start = i;
                i += 2;
                let mut found_end = false;
                while i + 1 < n {
                    if chars[i] == '*' && chars[i + 1] == '/' {
                        i += 2;
                        found_end = true;
                        break;
                    }
                    i += 1;
                }
                if !found_end {
                    // Block comment extends to next line
                    spans.push(StyledSpan {
                        text: chars[start..n].iter().collect(),
                        color: TokenType::Comment.hex_color().to_string(),
                    });
                    return (spans, LexerState::InBlockComment);
                }
                spans.push(StyledSpan {
                    text: chars[start..i].iter().collect(),
                    color: TokenType::Comment.hex_color().to_string(),
                });
                continue;
            }

            // Raw string literal r#"..."# or r"..."
            if ch == 'r' && i + 1 < n && (chars[i + 1] == '"' || chars[i + 1] == '#') {
                let start = i;
                i += 1;
                let mut hash_count = 0;
                while i < n && chars[i] == '#' {
                    hash_count += 1;
                    i += 1;
                }
                if i < n && chars[i] == '"' {
                    i += 1;
                    while i < n {
                        if chars[i] == '"' {
                            let mut end_hashes = 0;
                            let mut peek = i + 1;
                            while peek < n && chars[peek] == '#' && end_hashes < hash_count {
                                end_hashes += 1;
                                peek += 1;
                            }
                            if end_hashes == hash_count {
                                i = peek;
                                break;
                            }
                        }
                        i += 1;
                    }
                    spans.push(StyledSpan {
                        text: chars[start..i.min(n)].iter().collect(),
                        color: TokenType::String.hex_color().to_string(),
                    });
                    continue;
                } else {
                    i = start;
                }
            }

            // Standard string literal
            if ch == '"' {
                let start = i;
                i += 1;
                let mut escaped = false;
                while i < n {
                    let cur = chars[i];
                    if escaped {
                        escaped = false;
                    } else if cur == '\\' {
                        escaped = true;
                    } else if cur == '"' {
                        i += 1;
                        break;
                    }
                    i += 1;
                }
                spans.push(StyledSpan {
                    text: chars[start..i.min(n)].iter().collect(),
                    color: TokenType::String.hex_color().to_string(),
                });
                continue;
            }

            // Char literal 'c'
            if ch == '\'' {
                let start = i;
                i += 1;
                let mut escaped = false;
                while i < n {
                    let cur = chars[i];
                    if escaped {
                        escaped = false;
                    } else if cur == '\\' {
                        escaped = true;
                    } else if cur == '\'' {
                        i += 1;
                        break;
                    } else if cur == '\n' || cur == '\r' {
                        break;
                    }
                    i += 1;
                }
                spans.push(StyledSpan {
                    text: chars[start..i.min(n)].iter().collect(),
                    color: TokenType::String.hex_color().to_string(),
                });
                continue;
            }

            // Number literal
            if ch.is_ascii_digit() {
                let start = i;
                while i < n && (chars[i].is_ascii_alphanumeric() || chars[i] == '.' || chars[i] == '_') {
                    i += 1;
                }
                spans.push(StyledSpan {
                    text: chars[start..i].iter().collect(),
                    color: TokenType::Number.hex_color().to_string(),
                });
                continue;
            }

            // Identifier or Keyword
            if ch.is_alphabetic() || ch == '_' {
                let start = i;
                while i < n && (chars[i].is_alphanumeric() || chars[i] == '_') {
                    i += 1;
                }
                let word: String = chars[start..i].iter().collect();

                let mut peek = i;
                while peek < n && chars[peek].is_whitespace() {
                    peek += 1;
                }
                let is_func = peek < n && chars[peek] == '(';

                let color = Self::classify_word(&word, is_func);

                spans.push(StyledSpan {
                    text: word,
                    color: color.to_string(),
                });
                continue;
            }

            // Punctuation / Operator
            spans.push(StyledSpan {
                text: ch.to_string(),
                color: TokenType::Punctuation.hex_color().to_string(),
            });
            i += 1;
        }

        (spans, state)
    }

    /// Highlight multiple lines with cross-line state tracking.
    pub fn highlight_lines(lines: &[&str], start_state: LexerState) -> (Vec<Vec<StyledSpan>>, LexerState) {
        let mut all_spans = Vec::with_capacity(lines.len());
        let mut state = start_state;
        for line in lines {
            let (line_spans, new_state) = Self::highlight_line_with_state(line, state);
            all_spans.push(line_spans);
            state = new_state;
        }
        (all_spans, state)
    }

    fn classify_word(word: &str, is_func: bool) -> &'static str {
        match word {
            "fn" | "let" | "mut" | "pub" | "struct" | "enum" | "impl" | "trait" | "for" |
            "in" | "while" | "loop" | "if" | "else" | "match" | "return" | "use" | "mod" |
            "const" | "static" | "type" | "as" | "break" | "continue" | "crate" | "self" |
            "super" | "where" | "async" | "await" | "unsafe" | "class" | "public" | "private" |
            "protected" | "internal" | "void" | "new" | "this" | "namespace" | "var" |
            "override" | "virtual" | "interface" | "using" | "readonly" |
            "import" | "export" | "from" | "default" | "try" | "catch" | "finally" | "throw" |
            "switch" | "case" | "do" | "typedef" | "define" | "macro" | "template" => {
                TokenType::Keyword.hex_color()
            }
            "String" | "str" | "u8" | "u16" | "u32" | "u64" | "u128" | "usize" | "i8" | "i16" |
            "i32" | "i64" | "i128" | "isize" | "f32" | "f64" | "bool" | "char" | "Option" |
            "Result" | "Some" | "None" | "Ok" | "Err" | "Vec" | "HashMap" | "HashSet" |
            "Arc" | "Rc" | "Box" | "int" | "string" | "object" | "List" | "Dictionary" | "Task" => {
                TokenType::Type.hex_color()
            }
            _ if is_func => TokenType::Function.hex_color(),
            _ => TokenType::Plain.hex_color(),
        }
    }
}
