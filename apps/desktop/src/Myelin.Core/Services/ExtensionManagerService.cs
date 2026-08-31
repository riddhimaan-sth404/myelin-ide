using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Myelin.Core.Commands;
using Myelin.Core.Models;

namespace Myelin.Core.Services
{
    public class ExtensionManagerService
    {
        private static ExtensionManagerService? _instance;
        public static ExtensionManagerService Instance => _instance ??= new ExtensionManagerService();

        private readonly string _extensionsDirectory;
        private readonly ConcurrentDictionary<string, InstalledExtension> _installed = new(StringComparer.OrdinalIgnoreCase);

        public string ExtensionsDirectory => _extensionsDirectory;
        public IReadOnlyCollection<InstalledExtension> InstalledExtensions => _installed.Values.ToList();

        public event Action<InstalledExtension>? ExtensionInstalled;
        public event Action<string>? ExtensionUninstalled;
        public event Action<InstalledExtension>? ExtensionStateChanged;

        public ExtensionManagerService()
        {
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _extensionsDirectory = Path.Combine(userHome, ".myelin", "extensions");

            if (!Directory.Exists(_extensionsDirectory))
            {
                Directory.CreateDirectory(_extensionsDirectory);
            }

            ScanInstalledExtensions();
        }

        public void ScanInstalledExtensions()
        {
            _installed.Clear();

            if (!Directory.Exists(_extensionsDirectory)) return;

            foreach (var dir in Directory.GetDirectories(_extensionsDirectory))
            {
                try
                {
                    string pkgJsonPath = Path.Combine(dir, "package.json");
                    if (!File.Exists(pkgJsonPath))
                    {
                        // Some VSIX extract into "extension/package.json"
                        pkgJsonPath = Path.Combine(dir, "extension", "package.json");
                    }

                    if (File.Exists(pkgJsonPath))
                    {
                        string json = File.ReadAllText(pkgJsonPath);
                        var manifest = JsonSerializer.Deserialize<ExtensionPackageJson>(json);
                        if (manifest != null && !string.IsNullOrEmpty(manifest.Name))
                        {
                            string installDir = Path.GetDirectoryName(pkgJsonPath)!;
                            string? iconPath = null;
                            if (!string.IsNullOrEmpty(manifest.Icon))
                            {
                                string candidate = Path.Combine(installDir, manifest.Icon);
                                if (File.Exists(candidate)) iconPath = candidate;
                            }

                            var ext = new InstalledExtension
                            {
                                Manifest = manifest,
                                InstallDirectory = installDir,
                                IsEnabled = true,
                                IconPath = iconPath
                            };

                            _installed[ext.Id] = ext;
                            RegisterContributions(ext);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExtensionManager] Error loading extension in {dir}: {ex.Message}");
                }
            }
        }

        public bool IsInstalled(string extensionId)
        {
            return _installed.ContainsKey(extensionId);
        }

        public InstalledExtension? GetInstalled(string extensionId)
        {
            return _installed.TryGetValue(extensionId, out var ext) ? ext : null;
        }

        public async Task<InstalledExtension?> InstallFromMarketplaceAsync(OpenVsxExtensionItem item, IProgress<double>? progress = null)
        {
            if (string.IsNullOrEmpty(item.DownloadUrl)) return null;

            string tempVsix = Path.Combine(Path.GetTempPath(), $"{item.Namespace}.{item.Name}-{item.Version}.vsix");

            try
            {
                bool downloaded = await OpenVsxClient.Instance.DownloadVsixAsync(item.DownloadUrl, tempVsix, progress).ConfigureAwait(false);
                if (!downloaded || !File.Exists(tempVsix)) return null;

                return await InstallVsixAsync(tempVsix).ConfigureAwait(false);
            }
            finally
            {
                if (File.Exists(tempVsix))
                {
                    try { File.Delete(tempVsix); } catch { }
                }
            }
        }

        public Task<InstalledExtension?> InstallVsixAsync(string vsixPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    using var archive = ZipFile.OpenRead(vsixPath);

                    // Find package.json inside VSIX
                    var packageEntry = archive.Entries.FirstOrDefault(e => e.FullName.Equals("extension/package.json", StringComparison.OrdinalIgnoreCase) || e.FullName.Equals("package.json", StringComparison.OrdinalIgnoreCase));
                    if (packageEntry == null) return null;

                    string json;
                    using (var reader = new StreamReader(packageEntry.Open()))
                    {
                        json = reader.ReadToEnd();
                    }

                    var manifest = JsonSerializer.Deserialize<ExtensionPackageJson>(json);
                    if (manifest == null || string.IsNullOrEmpty(manifest.Name)) return null;

                    string targetFolder = Path.Combine(_extensionsDirectory, $"{manifest.Publisher}.{manifest.Name}-{manifest.Version}");

                    if (Directory.Exists(targetFolder))
                    {
                        Directory.Delete(targetFolder, true);
                    }
                    Directory.CreateDirectory(targetFolder);

                    // Extract all files under extension/
                    foreach (var entry in archive.Entries)
                    {
                        string relativePath = entry.FullName;
                        if (relativePath.StartsWith("extension/", StringComparison.OrdinalIgnoreCase))
                        {
                            relativePath = relativePath.Substring("extension/".Length);
                        }

                        if (string.IsNullOrEmpty(relativePath) || relativePath.EndsWith("/")) continue;

                        string destFile = Path.Combine(targetFolder, relativePath);
                        string? destDir = Path.GetDirectoryName(destFile);
                        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }

                        entry.ExtractToFile(destFile, true);
                    }

                    string? iconPath = null;
                    if (!string.IsNullOrEmpty(manifest.Icon))
                    {
                        string candidate = Path.Combine(targetFolder, manifest.Icon);
                        if (File.Exists(candidate)) iconPath = candidate;
                    }

                    var installed = new InstalledExtension
                    {
                        Manifest = manifest,
                        InstallDirectory = targetFolder,
                        IsEnabled = true,
                        IconPath = iconPath
                    };

                    _installed[installed.Id] = installed;
                    RegisterContributions(installed);
                    ExtensionInstalled?.Invoke(installed);

                    return installed;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExtensionManager] Install VSIX error: {ex.Message}");
                    return null;
                }
            });
        }

        public Task<bool> UninstallExtensionAsync(string extensionId)
        {
            return Task.Run(() =>
            {
                if (_installed.TryRemove(extensionId, out var ext))
                {
                    try
                    {
                        UnregisterContributions(ext);
                        if (Directory.Exists(ext.InstallDirectory))
                        {
                            Directory.Delete(ext.InstallDirectory, true);
                        }
                        ExtensionUninstalled?.Invoke(extensionId);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExtensionManager] Uninstall error: {ex.Message}");
                    }
                }
                return false;
            });
        }

        public void EnableExtension(string extensionId)
        {
            if (_installed.TryGetValue(extensionId, out var ext))
            {
                ext.IsEnabled = true;
                RegisterContributions(ext);
                ExtensionStateChanged?.Invoke(ext);
            }
        }

        public void DisableExtension(string extensionId)
        {
            if (_installed.TryGetValue(extensionId, out var ext))
            {
                ext.IsEnabled = false;
                UnregisterContributions(ext);
                ExtensionStateChanged?.Invoke(ext);
            }
        }

        private void RegisterContributions(InstalledExtension ext)
        {
            if (!ext.IsEnabled || ext.Manifest.Contributes == null) return;

            var reg = CommandRegistry.Instance;
            if (ext.Manifest.Contributes.Commands != null)
            {
                foreach (var cmd in ext.Manifest.Contributes.Commands)
                {
                    string category = cmd.Category ?? ext.DisplayName;
                    string title = cmd.Title;
                    string commandId = cmd.Command;

                    reg.Register(commandId, category, title, "", () =>
                    {
                        // Dispatch command to Node Extension Host or command executor
                        NodeExtensionHostService.Instance.ExecuteCommand(commandId);
                    });
                }
            }
        }

        private void UnregisterContributions(InstalledExtension ext)
        {
            // Unregister contributed commands if any
        }
    }
}
