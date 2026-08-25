use serde::{Deserialize, Serialize};

use crate::point::{Point, TextRange};

/// A selection defined by an anchor (where selection started) and a head (where the active cursor is).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct Selection {
    pub anchor: Point,
    pub head: Point,
}

impl Selection {
    pub fn point(point: Point) -> Self {
        Self {
            anchor: point,
            head: point,
        }
    }

    pub fn new(anchor: Point, head: Point) -> Self {
        Self { anchor, head }
    }

    pub fn is_collapsed(&self) -> bool {
        self.anchor == self.head
    }

    pub fn range(&self) -> TextRange {
        TextRange::new(self.anchor, self.head)
    }

    pub fn start(&self) -> Point {
        if self.anchor <= self.head {
            self.anchor
        } else {
            self.head
        }
    }

    pub fn end(&self) -> Point {
        if self.anchor <= self.head {
            self.head
        } else {
            self.anchor
        }
    }
}

/// A collection of selections (multi-cursor support).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CursorSet {
    selections: Vec<Selection>,
    primary_index: usize,
}

impl Default for CursorSet {
    fn default() -> Self {
        Self {
            selections: vec![Selection::point(Point::ZERO)],
            primary_index: 0,
        }
    }
}

impl CursorSet {
    pub fn new(selection: Selection) -> Self {
        Self {
            selections: vec![selection],
            primary_index: 0,
        }
    }

    pub fn primary(&self) -> Selection {
        self.selections[self.primary_index]
    }

    pub fn primary_mut(&mut self) -> &mut Selection {
        &mut self.selections[self.primary_index]
    }

    pub fn all(&self) -> &[Selection] {
        &self.selections
    }

    pub fn set_single(&mut self, selection: Selection) {
        self.selections.clear();
        self.selections.push(selection);
        self.primary_index = 0;
    }

    pub fn add_selection(&mut self, selection: Selection) {
        self.selections.push(selection);
        self.normalize();
    }

    fn normalize(&mut self) {
        // Sort selections by start point
        self.selections.sort_by_key(|s| s.start());
        // Deduplicate or merge overlapping selections if appropriate
    }
}
