use std::process::Stdio;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;
use serde_json::Value;
use thiserror::Error;
use tokio::io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader};
use tokio::process::{Child, ChildStdin, ChildStdout, Command};
use tokio::sync::{mpsc, oneshot, Mutex};

use crate::protocol::*;

#[derive(Error, Debug)]
pub enum LspError {
    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),
    #[error("Serialization error: {0}")]
    Json(#[from] serde_json::Error),
    #[error("RPC error: {code} - {message}")]
    Rpc { code: i64, message: String },
    #[error("Process not running")]
    NotRunning,
    #[error("Channel closed")]
    ChannelClosed,
    #[error("Request timed out")]
    Timeout,
}

pub struct LspClient {
    next_id: AtomicU64,
    stdin: Mutex<ChildStdin>,
    pending_requests: Arc<Mutex<std::collections::HashMap<u64, oneshot::Sender<Result<Value, LspError>>>>>,
    child: Mutex<Option<Child>>,
}

impl LspClient {
    pub async fn spawn(command: &str, args: &[&str]) -> Result<(Arc<Self>, mpsc::UnboundedReceiver<Value>), LspError> {
        let mut child = Command::new(command)
            .args(args)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()?;

        let stdin = child.stdin.take().ok_or(LspError::NotRunning)?;
        let stdout = child.stdout.take().ok_or(LspError::NotRunning)?;

        let pending_requests = Arc::new(Mutex::new(std::collections::HashMap::new()));
        let (notification_tx, notification_rx) = mpsc::unbounded_channel();

        let client = Arc::new(Self {
            next_id: AtomicU64::new(1),
            stdin: Mutex::new(stdin),
            pending_requests: Arc::clone(&pending_requests),
            child: Mutex::new(Some(child)),
        });

        // Spawn background reader loop
        tokio::spawn(Self::read_loop(stdout, pending_requests, notification_tx));

        Ok((client, notification_rx))
    }

    pub async fn send_request<P: serde::Serialize>(&self, method: &str, params: P) -> Result<Value, LspError> {
        let id = self.next_id.fetch_add(1, Ordering::Relaxed);
        let req = JsonRpcRequest {
            jsonrpc: "2.0".to_string(),
            id,
            method: method.to_string(),
            params: serde_json::to_value(params)?,
        };

        let payload = serde_json::to_string(&req)?;
        let message = format!("Content-Length: {}\r\n\r\n{}", payload.len(), payload);

        let (tx, rx) = oneshot::channel();
        {
            let mut pending = self.pending_requests.lock().await;
            pending.insert(id, tx);
        }

        let write_res = {
            let mut stdin = self.stdin.lock().await;
            async {
                stdin.write_all(message.as_bytes()).await?;
                stdin.flush().await?;
                Ok::<(), std::io::Error>(())
            }.await
        };

        if let Err(e) = write_res {
            let mut pending = self.pending_requests.lock().await;
            pending.remove(&id);
            return Err(LspError::Io(e));
        }

        match tokio::time::timeout(Duration::from_secs(15), rx).await {
            Ok(Ok(result)) => result,
            Ok(Err(_)) => Err(LspError::ChannelClosed),
            Err(_) => {
                let mut pending = self.pending_requests.lock().await;
                pending.remove(&id);
                Err(LspError::Timeout)
            }
        }
    }

    pub async fn send_notification<P: serde::Serialize>(&self, method: &str, params: P) -> Result<(), LspError> {
        let notif = JsonRpcNotification {
            jsonrpc: "2.0".to_string(),
            method: method.to_string(),
            params: serde_json::to_value(params)?,
        };

        let payload = serde_json::to_string(&notif)?;
        let message = format!("Content-Length: {}\r\n\r\n{}", payload.len(), payload);

        let mut stdin = self.stdin.lock().await;
        stdin.write_all(message.as_bytes()).await?;
        stdin.flush().await?;
        Ok(())
    }

    async fn read_loop(
        stdout: ChildStdout,
        pending: Arc<Mutex<std::collections::HashMap<u64, oneshot::Sender<Result<Value, LspError>>>>>,
        notification_tx: mpsc::UnboundedSender<Value>,
    ) {
        let mut reader = BufReader::new(stdout);
        let mut line = String::new();

        loop {
            line.clear();
            let mut content_length: Option<usize> = None;

            // Read headers
            loop {
                line.clear();
                if reader.read_line(&mut line).await.unwrap_or(0) == 0 {
                    return; // EOF
                }
                let trimmed = line.trim();
                if trimmed.is_empty() {
                    break;
                }
                if let Some(len_str) = trimmed.strip_prefix("Content-Length:") {
                    if let Ok(len) = len_str.trim().parse::<usize>() {
                        content_length = Some(len);
                    }
                }
            }

            if let Some(len) = content_length {
                let mut body = vec![0u8; len];
                if reader.read_exact(&mut body).await.is_err() {
                    return;
                }

                if let Ok(value) = serde_json::from_slice::<Value>(&body) {
                    if let Some(id) = value.get("id").and_then(|v| v.as_u64()) {
                        let mut p = pending.lock().await;
                        if let Some(sender) = p.remove(&id) {
                            if let Some(err) = value.get("error") {
                                let code = err.get("code").and_then(|c| c.as_i64()).unwrap_or(-1);
                                let msg = err.get("message").and_then(|m| m.as_str()).unwrap_or("Unknown error");
                                let _ = sender.send(Err(LspError::Rpc {
                                    code,
                                    message: msg.to_string(),
                                }));
                            } else {
                                let result = value.get("result").cloned().unwrap_or(Value::Null);
                                let _ = sender.send(Ok(result));
                            }
                        }
                    } else {
                        let _ = notification_tx.send(value);
                    }
                }
            }
        }
    }
}

impl Drop for LspClient {
    fn drop(&mut self) {
        if let Ok(mut lock) = self.child.try_lock() {
            if let Some(mut child) = lock.take() {
                let _ = child.start_kill();
            }
        }
    }
}
