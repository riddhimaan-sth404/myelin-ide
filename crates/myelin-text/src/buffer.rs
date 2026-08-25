use crop::Rope;
use thiserror::Error;

use crate::point::{ByteRange, Point, TextRange};

#[derive(Error, Debug, PartialEq)]
pub enum TextError {
    #[error("Index out of bounds: byte {offset} exceeds buffer length {len}")]
    ByteOutOfBounds { offset: usize, len: usize },
    #[error("Line index out of bounds: line {line} exceeds total lines {total}")]
    LineOutOfBounds { line: usize, total: usize },
    #[error("Invalid UTF-8 sequence")]
    InvalidUtf8,
}

/// A high-performance text buffer backed by a Crop Rope.
#[derive(Clone, Debug, Default)]
pub struct TextBuffer {
    rope: Rope,
}

impl TextBuffer {
    pub fn new() -> Self {
        Self { rope: Rope::new() }
    }

    pub fn from_str(text: &str) -> Self {
        Self {
            rope: Rope::from(text),
        }
    }

    pub fn len_bytes(&self) -> usize {
        self.rope.byte_len()
    }

    pub fn is_empty(&self) -> bool {
        self.rope.is_empty()
    }

    pub fn line_count(&self) -> usize {
        self.rope.line_len().max(1)
    }

    /// Converts a (line, column) 0-indexed coordinate into a byte offset.
    pub fn point_to_byte(&self, point: Point) -> Result<usize, TextError> {
        let total_lines = self.rope.line_len();
        if total_lines == 0 {
            return Ok(0);
        }

        if point.line >= total_lines {
            return Ok(self.len_bytes());
        }

        let line_byte_start = self.rope.byte_of_line(point.line);
        let line_chunk = self.rope.line(point.line);

        let mut current_col = 0;
        let mut byte_offset = 0;

        for ch in line_chunk.chars() {
            if ch == '\r' || ch == '\n' {
                break;
            }
            if current_col == point.column {
                break;
            }
            byte_offset += ch.len_utf8();
            current_col += 1;
        }

        Ok(line_byte_start + byte_offset)
    }

    /// Converts a byte offset into a (line, column) coordinate.
    pub fn byte_to_point(&self, byte_offset: usize) -> Result<Point, TextError> {
        let total_bytes = self.len_bytes();
        if byte_offset > total_bytes {
            return Err(TextError::ByteOutOfBounds {
                offset: byte_offset,
                len: total_bytes,
            });
        }
        if total_bytes == 0 || byte_offset == 0 {
            return Ok(Point::ZERO);
        }

        if byte_offset == total_bytes {
            // End of buffer: position after the last character of the last line.
            let line_idx = self.rope.line_len().saturating_sub(1);
            let line_chunk = self.rope.line(line_idx);
            let mut column = 0;
            for ch in line_chunk.chars() {
                if ch == '\r' || ch == '\n' {
                    break;
                }
                column += 1;
            }
            return Ok(Point::new(line_idx, column));
        }

        let line_idx = self.rope.line_of_byte(byte_offset);
        let line_byte_start = self.rope.byte_of_line(line_idx);
        let col_byte_offset = byte_offset.saturating_sub(line_byte_start);

        let line_chunk = self.rope.line(line_idx);
        let mut column = 0;
        let mut accumulated_bytes = 0;

        for ch in line_chunk.chars() {
            if ch == '\r' || ch == '\n' {
                break;
            }
            if accumulated_bytes >= col_byte_offset {
                break;
            }
            accumulated_bytes += ch.len_utf8();
            column += 1;
        }

        Ok(Point::new(line_idx, column))
    }

    /// Returns the text content of a single line (excluding trailing newline).
    pub fn line_text(&self, line_idx: usize) -> Result<String, TextError> {
        let total_lines = self.rope.line_len();
        if line_idx >= total_lines {
            if line_idx == 0 && total_lines == 0 {
                return Ok(String::new());
            }
            return Err(TextError::LineOutOfBounds {
                line: line_idx,
                total: total_lines,
            });
        }
        let chunk = self.rope.line(line_idx);
        let mut text = chunk.to_string();
        if text.ends_with("\r\n") {
            text.truncate(text.len() - 2);
        } else if text.ends_with('\n') || text.ends_with('\r') {
            text.truncate(text.len() - 1);
        }
        Ok(text)
    }

    /// Returns lines in a range [start_line, end_line) as a list of strings.
    pub fn lines_text(&self, start_line: usize, end_line: usize) -> Result<Vec<String>, TextError> {
        let total = self.line_count();
        let end = end_line.min(total);
        if start_line >= total {
            return Ok(Vec::new());
        }
        let mut result = Vec::with_capacity(end.saturating_sub(start_line));
        for l in start_line..end {
            result.push(self.line_text(l).unwrap_or_default());
        }
        Ok(result)
    }

    /// Returns the byte offset of the newline terminating the given line
    /// (i.e., the offset just past its last content character). For the last
    /// line this equals the end of the buffer.
    pub fn line_end_byte(&self, line_idx: usize) -> Result<usize, TextError> {
        let total_lines = self.rope.line_len();
        if total_lines == 0 {
            return Ok(0);
        }
        if line_idx >= total_lines {
            return Err(TextError::LineOutOfBounds {
                line: line_idx,
                total: total_lines,
            });
        }
        let line_byte_start = self.rope.byte_of_line(line_idx);
        let line_chunk = self.rope.line(line_idx);
        let mut content_len = 0;
        for ch in line_chunk.chars() {
            if ch == '\r' || ch == '\n' {
                break;
            }
            content_len += ch.len_utf8();
        }
        Ok(line_byte_start + content_len)
    }

    /// Inserts text at a specific byte offset.
    pub fn insert_bytes(&mut self, byte_offset: usize, text: &str) -> Result<(), TextError> {
        let total_bytes = self.len_bytes();
        let offset = byte_offset.min(total_bytes);
        self.rope.insert(offset, text);
        Ok(())
    }

    /// Deletes text in the specified byte range.
    pub fn delete_bytes(&mut self, range: ByteRange) -> Result<String, TextError> {
        let total_bytes = self.len_bytes();
        let start = range.start.min(total_bytes);
        let end = range.end.min(total_bytes);
        if start >= end {
            return Ok(String::new());
        }
        let deleted_text = self.rope.byte_slice(start..end).to_string();
        self.rope.delete(start..end);
        Ok(deleted_text)
    }

    /// Inserts text at a Point coordinate.
    pub fn insert(&mut self, point: Point, text: &str) -> Result<(), TextError> {
        let offset = self.point_to_byte(point)?;
        self.insert_bytes(offset, text)
    }

    /// Deletes text within a 2D TextRange.
    pub fn delete(&mut self, range: TextRange) -> Result<String, TextError> {
        let start_offset = self.point_to_byte(range.start)?;
        let end_offset = self.point_to_byte(range.end)?;
        self.delete_bytes(ByteRange::new(start_offset, end_offset))
    }

    pub fn to_string(&self) -> String {
        self.rope.to_string()
    }
}
