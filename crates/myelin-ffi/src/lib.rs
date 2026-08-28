use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_int};
use std::path::PathBuf;
use std::sync::Arc;

use myelin_core::Workspace;
use myelin_text::Point;

pub struct WorkspaceHandle {
    pub inner: Arc<Workspace>,
}

#[no_mangle]
pub unsafe extern "C" fn myelin_workspace_create(root_path: *const c_char) -> *mut WorkspaceHandle {
    let root = if !root_path.is_null() {
        let c_str = CStr::from_ptr(root_path);
        if let Ok(s) = c_str.to_str() {
            Some(PathBuf::from(s))
        } else {
            None
        }
    } else {
        None
    };

    let ws = Arc::new(Workspace::new(root));
    Box::into_raw(Box::new(WorkspaceHandle { inner: ws }))
}

#[no_mangle]
pub unsafe extern "C" fn myelin_workspace_destroy(handle: *mut WorkspaceHandle) {
    if !handle.is_null() {
        drop(Box::from_raw(handle));
    }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_workspace_open_scratch(
    handle: *mut WorkspaceHandle,
    initial_text: *const c_char,
) -> u64 {
    if handle.is_null() {
        return 0;
    }
    let ws = &(*handle).inner;
    let text = if !initial_text.is_null() {
        CStr::from_ptr(initial_text).to_str().unwrap_or("")
    } else {
        ""
    };
    ws.open_scratch_document(text)
}

#[no_mangle]
pub unsafe extern "C" fn myelin_workspace_open_file(
    handle: *mut WorkspaceHandle,
    path_utf8: *const c_char,
) -> u64 {
    if handle.is_null() || path_utf8.is_null() {
        return 0;
    }
    let ws = &(*handle).inner;
    let path_str = match CStr::from_ptr(path_utf8).to_str() {
        Ok(s) => s,
        Err(_) => return 0,
    };

    ws.open_file(path_str).unwrap_or(0)
}

#[no_mangle]
pub unsafe extern "C" fn myelin_workspace_close_doc(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
) -> c_int {
    if handle.is_null() {
        return 0;
    }
    let ws = &(*handle).inner;
    if ws.close_document(doc_id) { 1 } else { 0 }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_get_line_count(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
) -> usize {
    if handle.is_null() {
        return 0;
    }
    let ws = &(*handle).inner;
    ws.with_document(doc_id, |doc| doc.line_count()).unwrap_or(0)
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_get_line(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
    line_idx: usize,
) -> *mut c_char {
    if handle.is_null() {
        return std::ptr::null_mut();
    }
    let ws = &(*handle).inner;
    let line_opt = ws.with_document(doc_id, |doc| doc.line_text(line_idx).ok());
    if let Some(Some(line)) = line_opt {
        if let Ok(c_str) = CString::new(line) {
            return c_str.into_raw();
        }
    }
    std::ptr::null_mut()
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_get_visible_lines_json(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
    start_line: usize,
    end_line: usize,
) -> *mut c_char {
    if handle.is_null() {
        return std::ptr::null_mut();
    }
    let ws = &(*handle).inner;
    let lines_opt = ws.with_document(doc_id, |doc| doc.lines_text(start_line, end_line).ok());
    if let Some(Some(lines)) = lines_opt {
        if let Ok(json_str) = serde_json::to_string(&lines) {
            if let Ok(c_str) = CString::new(json_str) {
                return c_str.into_raw();
            }
        }
    }
    std::ptr::null_mut()
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_get_styled_lines_json(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
    start_line: usize,
    end_line: usize,
) -> *mut c_char {
    if handle.is_null() {
        return std::ptr::null_mut();
    }
    let ws = &(*handle).inner;
    let lines_opt = ws.with_document(doc_id, |doc| doc.lines_text(start_line, end_line).ok());
    if let Some(Some(lines)) = lines_opt {
        let line_refs: Vec<&str> = lines.iter().map(|l| l.as_str()).collect();
        let (styled, _state) = myelin_text::SimpleLexer::highlight_lines(
            &line_refs,
            myelin_text::LexerState::Normal,
        );
        if let Ok(json_str) = serde_json::to_string(&styled) {
            if let Ok(c_str) = CString::new(json_str) {
                return c_str.into_raw();
            }
        }
    }
    std::ptr::null_mut()
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_insert_at_cursor(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
    text_utf8: *const c_char,
) -> c_int {
    if handle.is_null() || text_utf8.is_null() {
        return -1;
    }
    let ws = &(*handle).inner;
    let text = match CStr::from_ptr(text_utf8).to_str() {
        Ok(s) => s,
        Err(_) => return -1,
    };

    let res = ws.with_document_mut(doc_id, |doc| doc.insert_at_cursor(text));
    match res {
        Some(Ok(())) => 0,
        _ => -1,
    }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_backspace(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
) -> c_int {
    if handle.is_null() {
        return -1;
    }
    let ws = &(*handle).inner;
    let res = ws.with_document_mut(doc_id, |doc| doc.backspace());
    match res {
        Some(Ok(())) => 0,
        _ => -1,
    }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_delete_forward(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
) -> c_int {
    if handle.is_null() {
        return -1;
    }
    let ws = &(*handle).inner;
    let res = ws.with_document_mut(doc_id, |doc| doc.delete_forward());
    match res {
        Some(Ok(())) => 0,
        _ => -1,
    }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_version(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
) -> u64 {
    if handle.is_null() {
        return 0;
    }
    let ws = &(*handle).inner;
    ws.with_document(doc_id, |doc| doc.version()).unwrap_or(0)
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_undo(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
) -> c_int {
    if handle.is_null() {
        return -1;
    }
    let ws = &(*handle).inner;
    let res = ws.with_document_mut(doc_id, |doc| doc.undo());
    match res {
        Some(Ok(true)) => 1,
        Some(Ok(false)) => 0,
        _ => -1,
    }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_redo(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
) -> c_int {
    if handle.is_null() {
        return -1;
    }
    let ws = &(*handle).inner;
    let res = ws.with_document_mut(doc_id, |doc| doc.redo());
    match res {
        Some(Ok(true)) => 1,
        Some(Ok(false)) => 0,
        _ => -1,
    }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_get_cursor(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
    out_line: *mut usize,
    out_col: *mut usize,
) -> c_int {
    if handle.is_null() || out_line.is_null() || out_col.is_null() {
        return -1;
    }
    let ws = &(*handle).inner;
    let res = ws.with_document(doc_id, |doc| {
        let pos = doc.cursors().primary().head;
        *out_line = pos.line;
        *out_col = pos.column;
    });
    if res.is_some() { 0 } else { -1 }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_set_cursor(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
    line: usize,
    col: usize,
) -> c_int {
    if handle.is_null() {
        return -1;
    }
    let ws = &(*handle).inner;
    let res = ws.with_document_mut(doc_id, |doc| {
        doc.set_cursor(Point::new(line, col));
    });
    if res.is_some() { 0 } else { -1 }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_set_selection(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
    anchor_line: usize,
    anchor_col: usize,
    head_line: usize,
    head_col: usize,
) -> c_int {
    if handle.is_null() {
        return -1;
    }
    let ws = &(*handle).inner;
    let res = ws.with_document_mut(doc_id, |doc| {
        doc.set_selection(Point::new(anchor_line, anchor_col), Point::new(head_line, head_col));
    });
    if res.is_some() { 0 } else { -1 }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_get_selection(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
    out_anchor_line: *mut usize,
    out_anchor_col: *mut usize,
    out_head_line: *mut usize,
    out_head_col: *mut usize,
) -> c_int {
    if handle.is_null() || out_anchor_line.is_null() || out_anchor_col.is_null()
        || out_head_line.is_null() || out_head_col.is_null() {
        return -1;
    }
    let ws = &(*handle).inner;
    let res = ws.with_document(doc_id, |doc| {
        let sel = doc.cursors().primary();
        *out_anchor_line = sel.anchor.line;
        *out_anchor_col = sel.anchor.column;
        *out_head_line = sel.head.line;
        *out_head_col = sel.head.column;
    });
    if res.is_some() { 0 } else { -1 }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_is_dirty(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
) -> c_int {
    if handle.is_null() {
        return 0;
    }
    let ws = &(*handle).inner;
    ws.with_document(doc_id, |doc| if doc.is_dirty() { 1 } else { 0 }).unwrap_or(0)
}

#[no_mangle]
pub unsafe extern "C" fn myelin_doc_save(
    handle: *mut WorkspaceHandle,
    doc_id: u64,
) -> c_int {
    if handle.is_null() {
        return -1;
    }
    let ws = &(*handle).inner;
    let res = ws.with_document_mut(doc_id, |doc| doc.save());
    match res {
        Some(Ok(())) => 0,
        _ => -1,
    }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_workspace_scan_dir_json(
    dir_path_utf8: *const c_char,
    max_depth: usize,
) -> *mut c_char {
    if dir_path_utf8.is_null() {
        return std::ptr::null_mut();
    }
    let dir_str = match CStr::from_ptr(dir_path_utf8).to_str() {
        Ok(s) => s,
        Err(_) => return std::ptr::null_mut(),
    };

    if let Ok(tree) = Workspace::scan_directory(dir_str, max_depth) {
        if let Ok(json) = serde_json::to_string(&tree) {
            if let Ok(c_str) = CString::new(json) {
                return c_str.into_raw();
            }
        }
    }
    std::ptr::null_mut()
}

#[no_mangle]
pub unsafe extern "C" fn myelin_free_string(ptr: *mut c_char) {
    if !ptr.is_null() {
        drop(CString::from_raw(ptr));
    }
}

pub struct TerminalHandle {
    pub inner: parking_lot::Mutex<myelin_terminal::TerminalSession>,
}

#[no_mangle]
pub unsafe extern "C" fn myelin_terminal_create(
    cols: u16,
    rows: u16,
    working_dir: *const c_char,
) -> *mut TerminalHandle {
    myelin_terminal_create_profile(cols, rows, working_dir, std::ptr::null(), std::ptr::null())
}

#[no_mangle]
pub unsafe extern "C" fn myelin_terminal_create_profile(
    cols: u16,
    rows: u16,
    working_dir: *const c_char,
    shell_path_utf8: *const c_char,
    shell_args_utf8: *const c_char,
) -> *mut TerminalHandle {
    let dir = if !working_dir.is_null() {
        let c_str = CStr::from_ptr(working_dir);
        c_str.to_str().ok().map(PathBuf::from)
    } else {
        None
    };

    let shell = if !shell_path_utf8.is_null() {
        let c_str = CStr::from_ptr(shell_path_utf8);
        c_str.to_str().ok()
    } else {
        None
    };

    let args_str = if !shell_args_utf8.is_null() {
        let c_str = CStr::from_ptr(shell_args_utf8);
        c_str.to_str().ok()
    } else {
        None
    };

    let args_vec: Option<Vec<&str>> = args_str.map(|s| s.split_whitespace().collect());

    match myelin_terminal::TerminalSession::spawn_with_shell(
        cols,
        rows,
        dir.as_deref(),
        shell,
        args_vec.as_deref(),
    ) {
        Ok(session) => Box::into_raw(Box::new(TerminalHandle {
            inner: parking_lot::Mutex::new(session),
        })),
        Err(_) => std::ptr::null_mut(),
    }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_terminal_destroy(handle: *mut TerminalHandle) {
    if !handle.is_null() {
        drop(Box::from_raw(handle));
    }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_terminal_write(
    handle: *mut TerminalHandle,
    input_utf8: *const c_char,
) -> c_int {
    if handle.is_null() || input_utf8.is_null() {
        return -1;
    }
    let text = match CStr::from_ptr(input_utf8).to_str() {
        Ok(s) => s,
        Err(_) => return -1,
    };
    let session = (*handle).inner.lock();
    if session.write_input(text).is_ok() { 0 } else { -1 }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_terminal_read_available(
    handle: *mut TerminalHandle,
) -> *mut c_char {
    if handle.is_null() {
        return std::ptr::null_mut();
    }
    let session = (*handle).inner.lock();
    let output = session.read_available_output();
    if output.is_empty() {
        return std::ptr::null_mut();
    }
    if let Ok(c_str) = CString::new(output) {
        return c_str.into_raw();
    }
    std::ptr::null_mut()
}

#[no_mangle]
pub unsafe extern "C" fn myelin_terminal_read_raw(
    handle: *mut TerminalHandle,
) -> *mut c_char {
    if handle.is_null() {
        return std::ptr::null_mut();
    }
    let session = (*handle).inner.lock();
    let output = session.read_available_raw_str();
    if output.is_empty() {
        return std::ptr::null_mut();
    }
    if let Ok(c_str) = CString::new(output) {
        return c_str.into_raw();
    }
    std::ptr::null_mut()
}

#[no_mangle]
pub unsafe extern "C" fn myelin_terminal_resize(
    handle: *mut TerminalHandle,
    cols: u16,
    rows: u16,
) -> c_int {
    if handle.is_null() {
        return -1;
    }
    let mut session = (*handle).inner.lock();
    if session.resize(cols, rows).is_ok() { 0 } else { -1 }
}

#[no_mangle]
pub unsafe extern "C" fn myelin_terminal_is_alive(handle: *mut TerminalHandle) -> c_int {
    if handle.is_null() {
        return 0;
    }
    let session = (*handle).inner.lock();
    if session.is_alive() { 1 } else { 0 }
}

