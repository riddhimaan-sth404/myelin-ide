use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use parking_lot::RwLock;
use serde::{Deserialize, Serialize};

use crate::document::{Document, DocumentError};

#[derive(Debug, Serialize, Deserialize)]
pub struct FileNode {
    pub name: String,
    pub path: PathBuf,
    pub is_dir: bool,
    pub children: Vec<FileNode>,
}

/// Central workspace managing active documents and filesystem state.
pub struct Workspace {
    root_path: Option<PathBuf>,
    documents: RwLock<HashMap<u64, Document>>,
    next_doc_id: AtomicU64,
}

impl Default for Workspace {
    fn default() -> Self {
        Self {
            root_path: None,
            documents: RwLock::new(HashMap::new()),
            next_doc_id: AtomicU64::new(1),
        }
    }
}

impl Workspace {
    pub fn new(root_path: Option<PathBuf>) -> Self {
        Self {
            root_path,
            documents: RwLock::new(HashMap::new()),
            next_doc_id: AtomicU64::new(1),
        }
    }

    pub fn root_path(&self) -> Option<&Path> {
        self.root_path.as_deref()
    }

    pub fn open_scratch_document(&self, initial_text: &str) -> u64 {
        let id = self.next_doc_id.fetch_add(1, Ordering::SeqCst);
        let doc = Document::new(id, initial_text, None);
        self.documents.write().insert(id, doc);
        id
    }

    pub fn open_file<P: AsRef<Path>>(&self, path: P) -> Result<u64, DocumentError> {
        let path_ref = path.as_ref();
        // Check if already open
        {
            let docs = self.documents.read();
            for doc in docs.values() {
                if let Some(doc_path) = doc.path() {
                    if doc_path == path_ref {
                        return Ok(doc.id());
                    }
                }
            }
        }

        let id = self.next_doc_id.fetch_add(1, Ordering::SeqCst);
        let doc = Document::from_file(id, path_ref)?;
        self.documents.write().insert(id, doc);
        Ok(id)
    }

    pub fn close_document(&self, id: u64) -> bool {
        self.documents.write().remove(&id).is_some()
    }

    pub fn with_document<F, R>(&self, id: u64, f: F) -> Option<R>
    where
        F: FnOnce(&Document) -> R,
    {
        let docs = self.documents.read();
        docs.get(&id).map(f)
    }

    pub fn with_document_mut<F, R>(&self, id: u64, f: F) -> Option<R>
    where
        F: FnOnce(&mut Document) -> R,
    {
        let mut docs = self.documents.write();
        docs.get_mut(&id).map(f)
    }

    /// Enumerates directory contents for the explorer tree safely.
    pub fn scan_directory<P: AsRef<Path>>(dir: P, max_depth: usize) -> std::io::Result<FileNode> {
        let max_depth = max_depth.min(20);
        let path = dir.as_ref().to_path_buf();
        let name = path
            .file_name()
            .map(|n| n.to_string_lossy().to_string())
            .unwrap_or_else(|| path.to_string_lossy().to_string());

        let mut node = FileNode {
            name,
            path: path.clone(),
            is_dir: true,
            children: Vec::new(),
        };

        if max_depth == 0 {
            return Ok(node);
        }

        if let Ok(entries) = std::fs::read_dir(&path) {
            let mut entries: Vec<_> = entries.filter_map(|e| e.ok()).collect();
            // Sort directories first, then alphabetical
            entries.sort_by_key(|e| {
                let is_file = e.file_type().map(|t| t.is_file()).unwrap_or(false);
                (is_file, e.file_name())
            });

            for entry in entries {
                let file_type = match entry.file_type() {
                    Ok(ft) => ft,
                    Err(_) => continue,
                };
                let file_name = entry.file_name().to_string_lossy().to_string();

                // Skip hidden files/directories and common heavy build output
                if file_name.starts_with('.') || file_name == "target" || file_name == "bin" || file_name == "obj" || file_name == "node_modules" {
                    continue;
                }

                if file_type.is_dir() && !file_type.is_symlink() {
                    if let Ok(child_node) = Self::scan_directory(entry.path(), max_depth - 1) {
                        node.children.push(child_node);
                    }
                } else {
                    node.children.push(FileNode {
                        name: file_name,
                        path: entry.path(),
                        is_dir: false,
                        children: Vec::new(),
                    });
                }
            }
        }

        Ok(node)
    }
}
