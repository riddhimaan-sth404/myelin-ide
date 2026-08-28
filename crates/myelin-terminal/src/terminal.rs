use std::io::{Read, Write};
use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::thread;
use parking_lot::Mutex;
use portable_pty::{native_pty_system, Child, CommandBuilder, MasterPty, PtySize};
use thiserror::Error;

#[derive(Error, Debug)]
pub enum TerminalError {
    #[error("PTY error: {0}")]
    Pty(#[from] anyhow::Error),
    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),
}

/// Byte-level VT/ANSI escape sequence stripper.
///
/// Keeps its state across calls so sequences split across read chunks are
/// handled correctly. Handles CSI, OSC, DCS/PM/APC strings, and two-char
/// escapes; strips them entirely without leaking intermediate bytes.
pub struct AnsiStripper {
    mode: EscapeMode,
    had_escape: bool,
}

#[derive(PartialEq)]
enum EscapeMode {
    Normal,
    Escape,
    Csi,
    StringMode,
    Charset,
}

impl Default for AnsiStripper {
    fn default() -> Self {
        Self::new()
    }
}

impl AnsiStripper {
    pub fn new() -> Self {
        Self {
            mode: EscapeMode::Normal,
            had_escape: false,
        }
    }

    fn feed(&mut self, b: u8) -> bool {
        match self.mode {
            EscapeMode::Normal => {
                if b == 0x1b {
                    self.mode = EscapeMode::Escape;
                    self.had_escape = true;
                    false
                } else {
                    b != b'\r'
                }
            }
            EscapeMode::Escape => {
                match b {
                    0x5b => self.mode = EscapeMode::Csi,          // ESC [
                    0x5d => self.mode = EscapeMode::StringMode,   // ESC ] (OSC)
                    0x50 | 0x5e | 0x5f => self.mode = EscapeMode::StringMode, // DCS / PM / APC
                    0x28 | 0x29 | 0x2a | 0x2b => self.mode = EscapeMode::Charset,
                    0x20..=0x7e => self.mode = EscapeMode::Normal, // single-char escape
                    _ => self.mode = EscapeMode::Normal,
                }
                false
            }
            EscapeMode::Csi => {
                // Final byte of a CSI sequence is in 0x40..=0x7E.
                if (0x40..=0x7e).contains(&b) {
                    self.mode = EscapeMode::Normal;
                }
                false
            }
            EscapeMode::StringMode => {
                // OSC/DCS/PM/APC terminate at BEL or ST (ESC \).
                if b == 0x07 {
                    self.mode = EscapeMode::Normal;
                } else if b == 0x1b {
                    self.mode = EscapeMode::Escape;
                    self.had_escape = true;
                }
                false
            }
            EscapeMode::Charset => {
                // ESC ( X  – ends after the next byte.
                self.mode = EscapeMode::Normal;
                false
            }
        }
    }

    /// Strips ANSI escapes from a chunk of raw bytes, keeping internal state
    /// for sequences that continue in the next chunk. Returns the cleaned text.
    pub fn strip(&mut self, bytes: &[u8]) -> String {
        let mut output: Vec<u8> = Vec::with_capacity(bytes.len());
        for &b in bytes {
            if self.feed(b) {
                // Keep UTF-8 multibyte sequences intact: pass non-ASCII bytes through.
                output.push(b);
            }
        }
        String::from_utf8_lossy(&output).to_string()
    }

    /// True when an escape sequence is currently in progress.
    pub fn in_escape(&self) -> bool {
        self.mode != EscapeMode::Normal
    }

    /// True when this stripper has seen an escape since the last reset.
    pub fn has_seen_escape(&self) -> bool {
        self.had_escape
    }
}

/// Number of trailing bytes that form an incomplete UTF-8 sequence.
fn incomplete_trailing_len(buf: &[u8]) -> usize {
    let n = buf.len();
    if n == 0 {
        return 0;
    }
    let mut i = n;
    let mut continuation_count = 0usize;
    while i > 0 {
        let b = buf[i - 1];
        if b & 0b1100_0000 == 0b1000_0000 {
            continuation_count += 1;
            i -= 1;
            continue;
        }
        let expected = if b & 0b1110_0000 == 0b1100_0000 {
            2
        } else if b & 0b1111_0000 == 0b1110_0000 {
            3
        } else if b & 0b1111_1000 == 0b1111_0000 {
            4
        } else {
            1
        };
        let have = 1 + continuation_count;
        return if have < expected { have } else { 0 };
    }
    0
}

pub struct TerminalSession {
    master: Box<dyn MasterPty + Send>,
    writer: Mutex<Box<dyn Write + Send>>,
    output_buffer: Arc<Mutex<Vec<u8>>>,
    is_running: Arc<AtomicBool>,
    child: Mutex<Option<Box<dyn Child + Send + Sync>>>,
    ansi: Mutex<AnsiStripper>,
}

impl TerminalSession {
    pub fn spawn<P: AsRef<Path>>(cols: u16, rows: u16, working_dir: Option<P>) -> Result<Self, TerminalError> {
        Self::spawn_with_shell(cols, rows, working_dir, None, None)
    }

    pub fn spawn_with_shell<P: AsRef<Path>>(
        cols: u16,
        rows: u16,
        working_dir: Option<P>,
        shell_path: Option<&str>,
        shell_args: Option<&[&str]>,
    ) -> Result<Self, TerminalError> {
        let pty_system = native_pty_system();
        let size = PtySize {
            rows,
            cols,
            pixel_width: 0,
            pixel_height: 0,
        };

        let pair = pty_system.openpty(size).map_err(|e| TerminalError::Pty(e.into()))?;

        // Determine shell command
        let mut cmd = if let Some(shell) = shell_path {
            let mut builder = CommandBuilder::new(shell);
            if let Some(args) = shell_args {
                for arg in args {
                    builder.arg(*arg);
                }
            }
            builder
        } else {
            #[cfg(target_os = "windows")]
            let builder = CommandBuilder::new("powershell.exe");
            #[cfg(not(target_os = "windows"))]
            let builder = CommandBuilder::new(std::env::var("SHELL").unwrap_or_else(|_| "/bin/bash".to_string()));
            builder
        };

        if let Some(dir) = working_dir {
            cmd.cwd(dir.as_ref());
        }

        let child = pair.slave.spawn_command(cmd).map_err(|e| TerminalError::Pty(e.into()))?;

        let mut reader = pair.master.try_clone_reader().map_err(|e| TerminalError::Pty(e.into()))?;
        let writer = pair.master.take_writer().map_err(|e| TerminalError::Pty(e.into()))?;

        let output_buffer = Arc::new(Mutex::new(Vec::new()));
        let is_running = Arc::new(AtomicBool::new(true));

        let buffer_clone = Arc::clone(&output_buffer);
        let running_clone = Arc::clone(&is_running);

        thread::spawn(move || {
            let mut temp_buf = [0u8; 4096];
            while running_clone.load(Ordering::SeqCst) {
                match reader.read(&mut temp_buf) {
                    Ok(0) => break, // EOF
                    Ok(n) => {
                        let mut buf = buffer_clone.lock();
                        buf.extend_from_slice(&temp_buf[..n]);
                    }
                    Err(_) => break,
                }
            }
            running_clone.store(false, Ordering::SeqCst);
        });

        Ok(Self {
            master: pair.master,
            writer: Mutex::new(writer),
            output_buffer,
            is_running,
            child: Mutex::new(Some(child)),
            ansi: Mutex::new(AnsiStripper::new()),
        })
    }

    pub fn write_input(&self, text: &str) -> Result<(), TerminalError> {
        let mut w = self.writer.lock();
        w.write_all(text.as_bytes())?;
        w.flush()?;
        Ok(())
    }

    pub fn read_available_output(&self) -> String {
        let mut buf = self.output_buffer.lock();
        if buf.is_empty() {
            return String::new();
        }

        // Retain any trailing incomplete UTF-8 sequence so a multi-byte
        // character split across polls is never corrupted or lost.
        let keep = incomplete_trailing_len(&buf);
        let take = buf.len() - keep;
        if take == 0 {
            return String::new();
        }

        let mut ansi = self.ansi.lock();
        let cleaned = ansi.strip(&buf[..take]);
        buf.drain(..take);
        cleaned
    }

    pub fn read_available_raw_str(&self) -> String {
        let mut buf = self.output_buffer.lock();
        if buf.is_empty() {
            return String::new();
        }

        let keep = incomplete_trailing_len(&buf);
        let take = buf.len() - keep;
        if take == 0 {
            return String::new();
        }

        let raw = String::from_utf8_lossy(&buf[..take]);
        let sanitized = raw.replace('\0', "");
        buf.drain(..take);
        sanitized
    }

    pub fn read_raw_output(&self) -> Vec<u8> {
        let mut buf = self.output_buffer.lock();
        let bytes = buf.clone();
        buf.clear();
        bytes
    }

    pub fn resize(&mut self, cols: u16, rows: u16) -> Result<(), TerminalError> {
        let size = PtySize {
            rows,
            cols,
            pixel_width: 0,
            pixel_height: 0,
        };
        self.master.resize(size).map_err(|e| TerminalError::Pty(e.into()))?;
        Ok(())
    }

    pub fn is_alive(&self) -> bool {
        if !self.is_running.load(Ordering::SeqCst) {
            return false;
        }
        let mut child = self.child.lock();
        if let Some(child_proc) = child.as_mut() {
            match child_proc.try_wait() {
                Ok(Some(_)) => {
                    *child = None;
                    false
                }
                Ok(None) => true,
                Err(_) => {
                    *child = None;
                    false
                }
            }
        } else {
            false
        }
    }
}

impl Drop for TerminalSession {
    fn drop(&mut self) {
        self.is_running.store(false, Ordering::SeqCst);
        if let Some(mut child) = self.child.lock().take() {
            let _ = child.kill();
            let _ = child.wait();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn strips_simple_csi_sequences() {
        let mut s = AnsiStripper::new();
        assert_eq!(s.strip(b"hello\x1b[31mworld"), "helloworld");
    }

    #[test]
    fn strips_osc_sequences_with_bel() {
        let mut s = AnsiStripper::new();
        assert_eq!(s.strip(b"a\x1b]0;My Title\x07b"), "ab");
    }

    #[test]
    fn strips_osc_sequences_with_st() {
        let mut s = AnsiStripper::new();
        assert_eq!(s.strip(b"a\x1b]0;My Title\x1b\\b"), "ab");
    }

    #[test]
    fn strips_csi_ending_in_tilde() {
        let mut s = AnsiStripper::new();
        assert_eq!(s.strip(b"\x1b[3~x"), "x");
    }

    #[test]
    fn strips_two_char_escapes() {
        let mut s = AnsiStripper::new();
        assert_eq!(s.strip(b"\x1b(Bx"), "x");
        assert_eq!(s.strip(b"\x1b7x\x1b8"), "x");
    }

    #[test]
    fn keeps_text_across_chunk_boundaries() {
        let mut s = AnsiStripper::new();
        assert_eq!(s.strip(b"wor"), "wor");
        // Escape sequence split across chunks
        assert_eq!(s.strip(b"\x1b["), "");
        assert_eq!(s.strip(b"31m"), "");
        assert_eq!(s.strip(b"done"), "done");
        assert!(!s.in_escape());
    }

    #[test]
    fn strips_cr_but_keeps_newlines() {
        let mut s = AnsiStripper::new();
        assert_eq!(s.strip(b"a\r\nb\r"), "a\nb");
    }

    #[test]
    fn keeps_utf8_bytes_untouched() {
        let mut s = AnsiStripper::new();
        assert_eq!(s.strip("héllo中".as_bytes()), "héllo中");
    }

    #[test]
    fn incomplete_trailing_utf8_detection() {
        assert_eq!(incomplete_trailing_len(b"abc"), 0);
        // é = 0xC3 0xA9, complete
        assert_eq!(incomplete_trailing_len(&[0x61, 0xC3, 0xA9]), 0);
        // truncated é: only lead byte
        assert_eq!(incomplete_trailing_len(&[0x61, 0xC3]), 1);
        // truncated 4-byte emoji: lead + 2 continuations of 3
        assert_eq!(incomplete_trailing_len(&[0x61, 0xF0, 0x9F, 0x98]), 3);
        // complete emoji
        assert_eq!(incomplete_trailing_len(&[0xF0, 0x9F, 0x98, 0x80]), 0);
        // lone continuation byte is treated as complete (invalid but not split)
        assert_eq!(incomplete_trailing_len(&[0x61, 0x80]), 0);
    }
}