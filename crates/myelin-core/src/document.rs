use std::path::{Path, PathBuf};
use myelin_text::{ByteRange, CursorSet, History, Point, Selection, TextBuffer, TextError, Transaction};
use thiserror::Error;

#[derive(Error, Debug)]
pub enum DocumentError {
    #[error("Text error: {0}")]
    Text(#[from] TextError),
    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),
}

/// An open document in the IDE core.
#[derive(Debug)]
pub struct Document {
    id: u64,
    path: Option<PathBuf>,
    buffer: TextBuffer,
    cursors: CursorSet,
    history: History,
    version: u64,
    is_dirty: bool,
    clean_version: u64,
}

impl Document {
    pub fn new(id: u64, initial_text: &str, path: Option<PathBuf>) -> Self {
        Self {
            id,
            path,
            buffer: TextBuffer::from_str(initial_text),
            cursors: CursorSet::default(),
            history: History::new(),
            version: 0,
            is_dirty: false,
            clean_version: 0,
        }
    }

    pub fn from_file<P: AsRef<Path>>(id: u64, path: P) -> Result<Self, DocumentError> {
        let bytes = std::fs::read(path.as_ref())?;
        let content = String::from_utf8_lossy(&bytes).to_string();
        Ok(Self {
            id,
            path: Some(path.as_ref().to_path_buf()),
            buffer: TextBuffer::from_str(&content),
            cursors: CursorSet::default(),
            history: History::new(),
            version: 0,
            is_dirty: false,
            clean_version: 0,
        })
    }

    pub fn id(&self) -> u64 {
        self.id
    }

    pub fn path(&self) -> Option<&Path> {
        self.path.as_deref()
    }

    pub fn version(&self) -> u64 {
        self.version
    }

    pub fn is_dirty(&self) -> bool {
        self.is_dirty
    }

    pub fn line_count(&self) -> usize {
        self.buffer.line_count()
    }

    pub fn len_bytes(&self) -> usize {
        self.buffer.len_bytes()
    }

    pub fn text(&self) -> String {
        self.buffer.to_string()
    }

    pub fn line_text(&self, line_idx: usize) -> Result<String, DocumentError> {
        Ok(self.buffer.line_text(line_idx)?)
    }

    pub fn lines_text(&self, start: usize, end: usize) -> Result<Vec<String>, DocumentError> {
        Ok(self.buffer.lines_text(start, end)?)
    }

    pub fn cursors(&self) -> &CursorSet {
        &self.cursors
    }

    pub fn set_cursor(&mut self, point: Point) {
        self.cursors.set_single(Selection::point(point));
    }

    pub fn set_selection(&mut self, anchor: Point, head: Point) {
        self.cursors.set_single(Selection::new(anchor, head));
    }

    pub fn insert_at_cursor(&mut self, text: &str) -> Result<(), DocumentError> {
        let primary = self.cursors.primary();
        if !primary.is_collapsed() {
            self.delete_selection()?;
        }
        let pos_before = self.cursors.primary().head;
        let offset = self.buffer.point_to_byte(pos_before)?;
        self.buffer.insert_bytes(offset, text)?;
        
        let new_offset = offset + text.len();
        let new_point = self.buffer.byte_to_point(new_offset)?;
        self.set_cursor(new_point);

        let tx = Transaction::insert(offset, text.to_string())
            .with_cursors(Some(pos_before), Some(new_point));
        self.history.push(tx);

        self.version += 1;
        self.is_dirty = self.version != self.clean_version;
        Ok(())
    }

    pub fn delete_selection(&mut self) -> Result<String, DocumentError> {
        let sel = self.cursors.primary();
        if sel.is_collapsed() {
            return Ok(String::new());
        }
        let start_pos = sel.start();
        let start_offset = self.buffer.point_to_byte(start_pos)?;
        let end_offset = self.buffer.point_to_byte(sel.end())?;
        let deleted = self.buffer.delete_bytes(ByteRange::new(start_offset, end_offset))?;
        
        self.set_cursor(start_pos);
        let tx = Transaction::delete(start_offset, deleted.clone())
            .with_cursors(Some(sel.head), Some(start_pos));
        self.history.push(tx);

        self.version += 1;
        self.is_dirty = self.version != self.clean_version;
        Ok(deleted)
    }

    pub fn backspace(&mut self) -> Result<(), DocumentError> {
        let sel = self.cursors.primary();
        if !sel.is_collapsed() {
            self.delete_selection()?;
            return Ok(());
        }

        let head = sel.head;
        if head.line == 0 && head.column == 0 {
            return Ok(());
        }

        let offset = self.buffer.point_to_byte(head)?;
        if offset == 0 {
            return Ok(());
        }

        let prev_offset = if head.column > 0 {
            self.buffer.point_to_byte(Point::new(head.line, head.column - 1))?
        } else if head.line > 0 {
            self.buffer.line_end_byte(head.line - 1)?
        } else {
            return Ok(());
        };

        if prev_offset >= offset {
            return Ok(());
        }

        let deleted = self.buffer.delete_bytes(ByteRange::new(prev_offset, offset))?;
        let new_point = self.buffer.byte_to_point(prev_offset)?;
        self.set_cursor(new_point);

        let tx = Transaction::delete(prev_offset, deleted)
            .with_cursors(Some(head), Some(new_point));
        self.history.push(tx);

        self.version += 1;
        self.is_dirty = self.version != self.clean_version;
        Ok(())
    }

    pub fn delete_forward(&mut self) -> Result<(), DocumentError> {
        let sel = self.cursors.primary();
        if !sel.is_collapsed() {
            self.delete_selection()?;
            return Ok(());
        }

        let head = sel.head;
        let offset = self.buffer.point_to_byte(head)?;
        let total = self.buffer.len_bytes();

        if offset >= total {
            return Ok(());
        }

        let full_text = self.buffer.to_string();
        let bytes = full_text.as_bytes();

        let mut end = offset + 1;
        while end < bytes.len() && (bytes[end] & 0xC0) == 0x80 {
            end += 1;
        }

        let deleted = self.buffer.delete_bytes(ByteRange::new(offset, end))?;
        let tx = Transaction::delete(offset, deleted)
            .with_cursors(Some(head), Some(head));
        self.history.push(tx);

        self.version += 1;
        self.is_dirty = self.version != self.clean_version;
        Ok(())
    }

    pub fn undo(&mut self) -> Result<bool, DocumentError> {
        if let Some(cursor_pos) = self.history.undo(&mut self.buffer)? {
            self.set_cursor(cursor_pos);
            self.version = self.version.saturating_sub(1);
            self.is_dirty = self.version != self.clean_version;
            Ok(true)
        } else {
            Ok(false)
        }
    }

    pub fn redo(&mut self) -> Result<bool, DocumentError> {
        if let Some(cursor_pos) = self.history.redo(&mut self.buffer)? {
            self.set_cursor(cursor_pos);
            self.version += 1;
            self.is_dirty = self.version != self.clean_version;
            Ok(true)
        } else {
            Ok(false)
        }
    }

    pub fn clean_version(&self) -> u64 {
        self.clean_version
    }

    /// Saves the document atomically using a temporary file.
    pub fn save(&mut self) -> Result<(), DocumentError> {
        if let Some(path) = &self.path {
            let tmp_path = path.with_extension(format!("tmp.{}", self.id));
            let content = self.buffer.to_string();
            std::fs::write(&tmp_path, content)?;
            
            if let Err(e) = std::fs::rename(&tmp_path, path) {
                // If rename fails (e.g. across filesystems), try copy + remove
                if std::fs::copy(&tmp_path, path).is_ok() {
                    let _ = std::fs::remove_file(&tmp_path);
                } else {
                    let _ = std::fs::remove_file(&tmp_path);
                    return Err(DocumentError::Io(e));
                }
            }

            self.is_dirty = false;
            self.clean_version = self.version;
            Ok(())
        } else {
            Err(DocumentError::Io(std::io::Error::new(
                std::io::ErrorKind::Other,
                "No file path to save to",
            )))
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn doc(text: &str) -> Document {
        Document::new(1, text, None)
    }

    #[test]
    fn backspace_deletes_full_utf8_characters() {
        let mut d = doc("héllo");
        d.set_cursor(Point::new(0, 5));
        d.backspace().unwrap();
        assert_eq!(d.text(), "héll");
        d.backspace().unwrap();
        assert_eq!(d.text(), "hél");
        d.backspace().unwrap();
        assert_eq!(d.text(), "hé");
        d.backspace().unwrap();
        assert_eq!(d.text(), "h");
        d.backspace().unwrap();
        assert_eq!(d.text(), "");
    }

    #[test]
    fn backspace_handles_wide_chars_and_emoji() {
        let mut d = doc("中🙂x");
        d.set_cursor(Point::new(0, 3));
        d.backspace().unwrap();
        assert_eq!(d.text(), "中🙂");
        d.backspace().unwrap();
        assert_eq!(d.text(), "中");
        d.backspace().unwrap();
        assert_eq!(d.text(), "");
        assert_eq!(d.line_text(0).unwrap(), "");
    }

    #[test]
    fn backspace_at_line_start_joins_previous_line() {
        let mut d = doc("first line\nsecond line");
        d.set_cursor(Point::new(1, 0));
        d.backspace().unwrap();
        assert_eq!(d.text(), "first linesecond line");
        let pos = d.cursors().primary().head;
        assert_eq!((pos.line, pos.column), (0, 10));
    }

    #[test]
    fn backspace_at_document_start_is_noop() {
        let mut d = doc("abc");
        d.set_cursor(Point::ZERO);
        d.backspace().unwrap();
        assert_eq!(d.text(), "abc");
    }

    #[test]
    fn undo_restores_multibyte_text_after_backspace() {
        let mut d = doc("caf\u{e9}"); // "café"
        d.set_cursor(Point::new(0, 4));
        d.backspace().unwrap();
        assert_eq!(d.text(), "caf");
        assert!(d.undo().unwrap());
        assert_eq!(d.text(), "caf\u{e9}");
        assert_eq!(d.cursors().primary().head, Point::new(0, 4));
        assert!(d.redo().unwrap());
        assert_eq!(d.text(), "caf");
    }

    #[test]
    fn delete_forward_removes_next_character() {
        let mut d = doc("abcdef");
        d.set_cursor(Point::new(0, 0));
        d.delete_forward().unwrap();
        assert_eq!(d.text(), "bcdef");
        assert_eq!(d.cursors().primary().head, Point::new(0, 0));
    }

    #[test]
    fn delete_forward_multibyte_utf8() {
        let mut d = doc("héllo");
        d.set_cursor(Point::new(0, 1));
        d.delete_forward().unwrap();
        assert_eq!(d.text(), "hllo");
    }

    #[test]
    fn delete_forward_at_end_is_noop() {
        let mut d = doc("abc");
        d.set_cursor(Point::new(0, 3));
        d.delete_forward().unwrap();
        assert_eq!(d.text(), "abc");
    }

    #[test]
    fn delete_forward_joins_next_line() {
        let mut d = doc("first\nsecond");
        d.set_cursor(Point::new(0, 5));
        d.delete_forward().unwrap();
        assert_eq!(d.text(), "firstsecond");
    }

    #[test]
    fn delete_forward_with_selection_deletes_selection() {
        let mut d = doc("abcdef");
        d.set_selection(Point::new(0, 1), Point::new(0, 4));
        d.delete_forward().unwrap();
        assert_eq!(d.text(), "aef");
        assert_eq!(d.cursors().primary().head, Point::new(0, 1));
    }
}
