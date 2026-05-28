using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using SmartAudioSwitcher.UI;

namespace SmartAudioSwitcher.Core
{
    public static class AutoUpdater
    {
        private const string GithubOwner = "Volfheim";
        private const string GithubRepo = "SmartAudioSwitcher";
        private const string UserAgent = "SmartAudioSwitcher-Updater";

        public static async Task CheckForUpdatesAsync(bool showUpToDateMessage = false)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "3.0"));
                
                var url = $"https://api.github.com/repos/{GithubOwner}/{GithubRepo}/releases/latest";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    if (showUpToDateMessage)
                        Application.Current.Dispatcher.Invoke(() => AppDialog.ShowError(null, "Ошибка", "Ошибка проверки обновлений. Возможно, репозиторий недоступен."));
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (!root.TryGetProperty("tag_name", out var tagElement)) return;
                
                var latestVersionStr = tagElement.GetString()?.TrimStart('v', 'V');
                if (string.IsNullOrEmpty(latestVersionStr)) return;

                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion == null) return;

                if (Version.TryParse(latestVersionStr, out var latestVersion))
                {
                    if (latestVersion > currentVersion)
                    {
                        var downloadUrl = GetDownloadUrl(root);
                        if (string.IsNullOrEmpty(downloadUrl))
                        {
                            Application.Current.Dispatcher.Invoke(() => AppDialog.ShowWarning(null, "Ошибка", "Найдена новая версия, но файл для скачивания (Asset) не найден."));
                            return;
                        }

                        var result = Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                            $"Доступна новая версия: v{latestVersionStr}\nТекущая версия: v{currentVersion}\n\nХотите обновить приложение прямо сейчас?",
                            "Обновление", MessageBoxButton.YesNo, MessageBoxImage.Question));

                        if (result == MessageBoxResult.Yes)
                        {
                            await PerformUpdateAsync(downloadUrl, client);
                        }
                    }
                    else if (showUpToDateMessage)
                    {
                        Application.Current.Dispatcher.Invoke(() => AppDialog.ShowInfo(null, "Обновление", "У вас установлена самая последняя версия приложения!"));
                    }
                }
            }
            catch (Exception ex)
            {
                if (showUpToDateMessage)
                    Application.Current.Dispatcher.Invoke(() => AppDialog.ShowError(null, "Ошибка", $"Ошибка проверки обновлений:\n{ex.Message}"));
            }
        }

        private static string? GetDownloadUrl(JsonElement root)
        {
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameElement) && 
                        nameElement.GetString()?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        if (asset.TryGetProperty("browser_download_url", out var urlElement))
                        {
                            return urlElement.GetString();
                        }
                    }
                }
            }
            return null;
        }

        private static async Task PerformUpdateAsync(string downloadUrl, HttpClient client)
        {
            try
            {
                var tempExeFile = Path.Combine(Path.GetTempPath(), "SmartAudioSwitcher_Update.exe");
                
                var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                using (var fs = new FileStream(tempExeFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }

                var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExe)) return;

                var batFile = Path.Combine(Path.GetTempPath(), "update_sas.bat");
                var batContent = $@"@echo off
timeout /t 2 /nobreak > nul
move /Y ""{tempExeFile}"" ""{currentExe}""
start """" ""{currentExe}""
del ""%~f0""
";
                File.WriteAllText(batFile, batContent);

                var psi = new ProcessStartInfo
                {
                    FileName = batFile,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);

                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => AppDialog.ShowError(null, "Ошибка", $"Ошибка при загрузке обновления:\n{ex.Message}"));
            }
        }
    }
}
