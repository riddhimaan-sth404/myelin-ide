use crate::point::{ByteRange, Point};
use crate::buffer::{TextBuffer, TextError};

/// A single atomic edit in the document.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum EditOperation {
    Insert { offset: usize, text: String },
    Delete { offset: usize, text: String },
}

/// A transaction containing one or more atomic edits that should be undone/redone together.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct Transaction {
    pub ops: Vec<EditOperation>,
    pub cursor_before: Option<Point>,
    pub cursor_after: Option<Point>,
}

impl Transaction {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn insert(offset: usize, text: String) -> Self {
        Self {
            ops: vec![EditOperation::Insert { offset, text }],
            cursor_before: None,
            cursor_after: None,
        }
    }

    pub fn delete(offset: usize, text: String) -> Self {
        Self {
            ops: vec![EditOperation::Delete { offset, text }],
            cursor_before: None,
            cursor_after: None,
        }
    }

    pub fn with_cursors(mut self, before: Option<Point>, after: Option<Point>) -> Self {
        self.cursor_before = before;
        self.cursor_after = after;
        self
    }

    /// Inverts the transaction so it can be applied in reverse for undo/redo.
    pub fn invert(&self) -> Self {
        let mut inverted_ops = Vec::with_capacity(self.ops.len());
        for op in self.ops.iter().rev() {
            match op {
                EditOperation::Insert { offset, text } => {
                    inverted_ops.push(EditOperation::Delete {
                        offset: *offset,
                        text: text.clone(),
                    });
                }
                EditOperation::Delete { offset, text } => {
                    inverted_ops.push(EditOperation::Insert {
                        offset: *offset,
                        text: text.clone(),
                    });
                }
            }
        }
        Self {
            ops: inverted_ops,
            cursor_before: self.cursor_after,
            cursor_after: self.cursor_before,
        }
    }

    /// Applies this transaction to a TextBuffer.
    pub fn apply(&self, buffer: &mut TextBuffer) -> Result<(), TextError> {
        for op in &self.ops {
            match op {
                EditOperation::Insert { offset, text } => {
                    buffer.insert_bytes(*offset, text)?;
                }
                EditOperation::Delete { offset, text } => {
                    let range = ByteRange::new(*offset, *offset + text.len());
                    buffer.delete_bytes(range)?;
                }
            }
        }
        Ok(())
    }
}

/// History managing the undo and redo stacks.
#[derive(Debug, Default)]
pub struct History {
    undo_stack: Vec<Transaction>,
    redo_stack: Vec<Transaction>,
}

impl History {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn push(&mut self, transaction: Transaction) {
        if !transaction.ops.is_empty() {
            self.undo_stack.push(transaction);
            self.redo_stack.clear();
        }
    }

    pub fn undo(&mut self, buffer: &mut TextBuffer) -> Result<Option<Point>, TextError> {
        if let Some(tx) = self.undo_stack.pop() {
            let inverted = tx.invert();
            inverted.apply(buffer)?;
            let restored_cursor = tx.cursor_before;
            self.redo_stack.push(tx);
            Ok(restored_cursor.or(Some(Point::ZERO)))
        } else {
            Ok(None)
        }
    }

    pub fn redo(&mut self, buffer: &mut TextBuffer) -> Result<Option<Point>, TextError> {
        if let Some(tx) = self.redo_stack.pop() {
            tx.apply(buffer)?;
            let restored_cursor = tx.cursor_after;
            self.undo_stack.push(tx);
            Ok(restored_cursor.or(Some(Point::ZERO)))
        } else {
            Ok(None)
        }
    }

    pub fn can_undo(&self) -> bool {
        !self.undo_stack.is_empty()
    }

    pub fn can_redo(&self) -> bool {
        !self.redo_stack.is_empty()
    }
}
