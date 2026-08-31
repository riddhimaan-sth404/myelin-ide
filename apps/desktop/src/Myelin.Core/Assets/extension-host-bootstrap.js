/**
 * Myelin IDE - Lightweight Node.js Extension Host (Eclipse Theia-inspired Architecture)
 * Provides VS Code Extension API emulation and JSON-RPC stdio IPC bridge.
 */

const fs = require('fs');
const path = require('path');
const readline = require('readline');
const EventEmitter = require('events');

// Global Registries
const registeredCommands = new Map();
const activeWebviewPanels = new Map();
const activeExtensions = new Map();
const documents = new Map();

let workspaceRoot = process.cwd();
let rpcIdCounter = 1;

// Stdio JSON-RPC Bridge
const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
    terminal: false
});

function sendRpc(msg) {
    try {
        process.stdout.write(JSON.stringify(msg) + '\n');
    } catch (e) {
        // Output stream error
    }
}

function sendNotification(method, params) {
    sendRpc({ jsonrpc: '2.0', method, params });
}

function sendResponse(id, result, error) {
    sendRpc({ jsonrpc: '2.0', id, result, error });
}

// ---------------------------------------------------------------------------
// VS Code API Emulation Shim
// ---------------------------------------------------------------------------
class Disposable {
    constructor(func) {
        this._func = func;
    }
    dispose() {
        if (typeof this._func === 'function') {
            this._func();
            this._func = null;
        }
    }
    static from(...disposables) {
        return new Disposable(() => {
            for (const d of disposables) {
                if (d && typeof d.dispose === 'function') d.dispose();
            }
        });
    }
}

class TypedEventEmitter extends EventEmitter {
    get event() {
        return (listener, thisArgs) => {
            const bound = thisArgs ? listener.bind(thisArgs) : listener;
            this.on('event', bound);
            return new Disposable(() => this.off('event', bound));
        };
    }
    fire(data) {
        this.emit('event', data);
    }
}

class Uri {
    constructor(scheme, authority, pathStr, query, fragment) {
        this.scheme = scheme || 'file';
        this.authority = authority || '';
        this.path = pathStr || '';
        this.query = query || '';
        this.fragment = fragment || '';
        this.fsPath = this.path;
    }
    static file(filePath) {
        return new Uri('file', '', path.normalize(filePath));
    }
    static parse(str) {
        return new Uri('file', '', str);
    }
    toString() {
        return `${this.scheme}://${this.path}`;
    }
}

class Position {
    constructor(line, character) {
        this.line = line;
        this.character = character;
    }
}

class Range {
    constructor(startLine, startChar, endLine, endChar) {
        this.start = startLine instanceof Position ? startLine : new Position(startLine, startChar);
        this.end = endLine instanceof Position ? endLine : new Position(endLine, endChar);
    }
}

class WebviewPanel {
    constructor(viewType, title, showOptions, options) {
        this.viewType = viewType;
        this.title = title;
        this.options = options || {};
        this.id = 'webview_' + Date.now() + '_' + Math.random().toString(36).substr(2, 6);
        this._onDidReceiveMessageEmitter = new TypedEventEmitter();
        this.onDidReceiveMessage = this._onDidReceiveMessageEmitter.event;

        this.webview = {
            html: '',
            options: this.options,
            postMessage: (message) => {
                sendNotification('webview.postMessage', { panelId: this.id, message });
                return Promise.resolve(true);
            },
            onDidReceiveMessage: this.onDidReceiveMessage,
            asWebviewUri: (localUri) => {
                return Uri.parse(`myelin-webview://${localUri.fsPath}`);
            }
        };

        activeWebviewPanels.set(this.id, this);

        sendNotification('window.createWebviewPanel', {
            panelId: this.id,
            viewType: this.viewType,
            title: this.title,
            options: this.options
        });
    }

    dispose() {
        activeWebviewPanels.delete(this.id);
        sendNotification('window.disposeWebviewPanel', { panelId: this.id });
    }
}

const vscode = {
    Disposable,
    EventEmitter: TypedEventEmitter,
    Uri,
    Position,
    Range,
    ViewColumn: { One: 1, Two: 2, Three: 3, Active: -1, Beside: -2 },
    StatusBarAlignment: { Left: 1, Right: 2 },

    commands: {
        registerCommand(commandId, handler, thisArg) {
            const func = thisArg ? handler.bind(thisArg) : handler;
            registeredCommands.set(commandId, func);
            sendNotification('commands.registerCommand', { command: commandId });
            return new Disposable(() => {
                registeredCommands.delete(commandId);
            });
        },
        executeCommand(commandId, ...args) {
            if (registeredCommands.has(commandId)) {
                try {
                    return Promise.resolve(registeredCommands.get(commandId)(...args));
                } catch (err) {
                    return Promise.reject(err);
                }
            }
            sendNotification('commands.executeCommand', { command: commandId, args });
            return Promise.resolve();
        },
        getCommands() {
            return Promise.resolve(Array.from(registeredCommands.keys()));
        }
    },

    window: {
        showInformationMessage(message, ...items) {
            sendNotification('window.showInformationMessage', { message, items });
            return Promise.resolve(items[0] || undefined);
        },
        showWarningMessage(message, ...items) {
            sendNotification('window.showWarningMessage', { message, items });
            return Promise.resolve(items[0] || undefined);
        },
        showErrorMessage(message, ...items) {
            sendNotification('window.showErrorMessage', { message, items });
            return Promise.resolve(items[0] || undefined);
        },
        showQuickPick(items, options) {
            sendNotification('window.showQuickPick', { items, options });
            return Promise.resolve(Array.isArray(items) ? items[0] : undefined);
        },
        showInputBox(options) {
            sendNotification('window.showInputBox', { options });
            return Promise.resolve(options && options.value ? options.value : '');
        },
        createWebviewPanel(viewType, title, showOptions, options) {
            return new WebviewPanel(viewType, title, showOptions, options);
        },
        createOutputChannel(name) {
            return {
                name,
                append: (val) => sendNotification('window.appendOutput', { name, text: String(val) }),
                appendLine: (val) => sendNotification('window.appendOutput', { name, text: String(val) + '\n' }),
                clear: () => sendNotification('window.clearOutput', { name }),
                show: () => sendNotification('window.showOutput', { name }),
                hide: () => {},
                dispose: () => {}
            };
        },
        createStatusBarItem(alignment, priority) {
            let _text = '';
            let _tooltip = '';
            let _command = '';
            return {
                alignment,
                priority,
                get text() { return _text; },
                set text(val) { _text = val; sendNotification('window.setStatusBar', { text: _text, tooltip: _tooltip }); },
                get tooltip() { return _tooltip; },
                set tooltip(val) { _tooltip = val; },
                get command() { return _command; },
                set command(val) { _command = val; },
                show: () => sendNotification('window.setStatusBar', { text: _text, tooltip: _tooltip }),
                hide: () => sendNotification('window.setStatusBar', { text: '' }),
                dispose: () => {}
            };
        },
        setStatusBarMessage(text, hideAfterTimeout) {
            sendNotification('window.setStatusBar', { text });
            return new Disposable(() => {});
        }
    },

    workspace: {
        get rootPath() { return workspaceRoot; },
        get workspaceFolders() {
            return workspaceRoot ? [{ uri: Uri.file(workspaceRoot), name: path.basename(workspaceRoot), index: 0 }] : undefined;
        },
        getConfiguration(section) {
            return {
                get: (key, defaultValue) => defaultValue,
                has: () => true,
                inspect: () => undefined,
                update: () => Promise.resolve()
            };
        },
        openTextDocument(uriOrPath) {
            const filePath = typeof uriOrPath === 'string' ? uriOrPath : uriOrPath.fsPath;
            const content = fs.existsSync(filePath) ? fs.readFileSync(filePath, 'utf8') : '';
            return Promise.resolve({
                uri: Uri.file(filePath),
                fileName: filePath,
                getText: () => content,
                lineCount: content.split('\n').length
            });
        },
        fs: {
            readFile: (uri) => fs.promises.readFile(uri.fsPath),
            writeFile: (uri, content) => fs.promises.writeFile(uri.fsPath, content),
            delete: (uri) => fs.promises.unlink(uri.fsPath),
            stat: (uri) => fs.promises.stat(uri.fsPath)
        }
    },

    languages: {
        registerCompletionItemProvider: () => new Disposable(() => {}),
        registerHoverProvider: () => new Disposable(() => {}),
        registerDefinitionProvider: () => new Disposable(() => {})
    },

    env: {
        appName: 'Myelin IDE',
        language: 'en',
        clipboard: {
            readText: () => Promise.resolve(''),
            writeText: (t) => { sendNotification('env.clipboardWrite', { text: t }); return Promise.resolve(); }
        },
        openExternal: (uri) => {
            sendNotification('env.openExternal', { uri: uri.toString() });
            return Promise.resolve(true);
        }
    }
};

// Hook require('vscode')
const Module = require('module');
const originalRequire = Module.prototype.require;
Module.prototype.require = function (id) {
    if (id === 'vscode') {
        return vscode;
    }
    return originalRequire.apply(this, arguments);
};

// ---------------------------------------------------------------------------
// RPC Message Dispatcher
// ---------------------------------------------------------------------------
async function handleMessage(msg) {
    if (!msg || typeof msg !== 'object') return;

    const { id, method, params } = msg;

    try {
        switch (method) {
            case 'init':
                if (params && params.workspaceRoot) {
                    workspaceRoot = params.workspaceRoot;
                }
                if (id) sendResponse(id, { status: 'ready', version: '1.0.0' });
                break;

            case 'activateExtension': {
                const { extensionId, entrypointPath, extensionPath } = params;
                if (!entrypointPath || !fs.existsSync(entrypointPath)) {
                    if (id) sendResponse(id, null, { code: -32602, message: `Entrypoint not found: ${entrypointPath}` });
                    return;
                }

                const extContext = {
                    subscriptions: [],
                    extensionPath: extensionPath || path.dirname(entrypointPath),
                    extensionUri: Uri.file(extensionPath || path.dirname(entrypointPath)),
                    globalState: { get: () => undefined, update: () => Promise.resolve() },
                    workspaceState: { get: () => undefined, update: () => Promise.resolve() },
                    asAbsolutePath: (rel) => path.join(extensionPath || path.dirname(entrypointPath), rel)
                };

                const extModule = require(entrypointPath);
                if (extModule && typeof extModule.activate === 'function') {
                    await Promise.resolve(extModule.activate(extContext));
                }

                activeExtensions.set(extensionId, { module: extModule, context: extContext });
                if (id) sendResponse(id, { status: 'activated', extensionId });
                break;
            }

            case 'deactivateExtension': {
                const { extensionId } = params;
                if (activeExtensions.has(extensionId)) {
                    const ext = activeExtensions.get(extensionId);
                    if (ext.module && typeof ext.module.deactivate === 'function') {
                        await Promise.resolve(ext.module.deactivate());
                    }
                    if (ext.context && Array.isArray(ext.context.subscriptions)) {
                        for (const sub of ext.context.subscriptions) {
                            if (sub && typeof sub.dispose === 'function') sub.dispose();
                        }
                    }
                    activeExtensions.delete(extensionId);
                }
                if (id) sendResponse(id, { status: 'deactivated', extensionId });
                break;
            }

            case 'executeCommand': {
                const { command, args } = params;
                if (registeredCommands.has(command)) {
                    const res = await Promise.resolve(registeredCommands.get(command)(...(args || [])));
                    if (id) sendResponse(id, res);
                } else {
                    if (id) sendResponse(id, null, { code: -32601, message: `Command not found: ${command}` });
                }
                break;
            }

            case 'webview.onMessage': {
                const { panelId, message } = params;
                if (activeWebviewPanels.has(panelId)) {
                    const panel = activeWebviewPanels.get(panelId);
                    panel._onDidReceiveMessageEmitter.fire(message);
                }
                if (id) sendResponse(id, { ok: true });
                break;
            }

            case 'webview.setHtml': {
                const { panelId, html } = params;
                if (activeWebviewPanels.has(panelId)) {
                    activeWebviewPanels.get(panelId).webview.html = html;
                }
                if (id) sendResponse(id, { ok: true });
                break;
            }

            default:
                if (id) sendResponse(id, null, { code: -32601, message: `Method not found: ${method}` });
                break;
        }
    } catch (err) {
        if (id) sendResponse(id, null, { code: -32000, message: err.message || String(err) });
    }
}

rl.on('line', (line) => {
    if (!line || !line.trim()) return;
    try {
        const msg = JSON.parse(line.trim());
        handleMessage(msg);
    } catch (e) {
        // Invalid JSON
    }
});

// Notify IDE that Extension Host runtime process is initialized
sendNotification('host.ready', { pid: process.pid, nodeVersion: process.version });
