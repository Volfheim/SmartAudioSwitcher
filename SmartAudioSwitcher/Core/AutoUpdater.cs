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
                
                var latestVersionStr = tagElement.GetString();
                if (string.IsNullOrEmpty(latestVersionStr)) return;

                var latestVersion = ParseNormalizedVersion(latestVersionStr);
                var currentVersion = ParseNormalizedVersion(Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0");

                if (latestVersion > currentVersion)
                {
                    var downloadUrl = GetDownloadUrl(root);
                    if (string.IsNullOrEmpty(downloadUrl))
                    {
                        Application.Current.Dispatcher.Invoke(() => AppDialog.ShowWarning(null, "Ошибка", "Найдена новая версия, но файл для скачивания (Asset) не найден."));
                        return;
                    }

                    var result = Application.Current.Dispatcher.Invoke(() => AppDialog.ShowQuestion(
                        null, "Обновление", $"Доступна новая версия: v{latestVersionStr}\nТекущая версия: v{currentVersion}\n\nХотите обновить приложение прямо сейчас?"));

                    if (result)
                    {
                        await PerformUpdateAsync(downloadUrl, client);
                    }
                }
                else if (showUpToDateMessage)
                {
                    Application.Current.Dispatcher.Invoke(() => AppDialog.ShowInfo(null, "Обновление", "У вас установлена самая последняя версия приложения!"));
                }
            }
            catch (Exception ex)
            {
                if (showUpToDateMessage)
                    Application.Current.Dispatcher.Invoke(() => AppDialog.ShowError(null, "Ошибка", $"Ошибка проверки обновлений:\n{ex.Message}"));
            }
        }

        private static Version ParseNormalizedVersion(string versionStr)
        {
            var clean = versionStr.TrimStart('v', 'V').Trim();
            if (Version.TryParse(clean, out var v))
            {
                return new Version(
                    v.Major,
                    v.Minor,
                    v.Build >= 0 ? v.Build : 0,
                    v.Revision >= 0 ? v.Revision : 0
                );
            }
            return new Version(0, 0, 0, 0);
        }

        private static string? GetDownloadUrl(JsonElement root)
        {
            var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExePath)) return null;

            bool isFull = new FileInfo(currentExePath).Length > 10 * 1024 * 1024;
            string expectedAssetName = isFull ? "SmartAudioSwitcher_Full.exe" : "SmartAudioSwitcher.exe";

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameElement) && 
                        nameElement.GetString()?.Equals(expectedAssetName, StringComparison.OrdinalIgnoreCase) == true)
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

                var currentPid = Process.GetCurrentProcess().Id;
                var batFile = Path.Combine(Path.GetTempPath(), "update_sas.bat");
                var logFile = Path.Combine(Path.GetTempPath(), "sas_update.log");

                var batContent = $@"@echo off
setlocal enableextensions
set ""PID={currentPid}""
set ""DOWNLOADED={tempExeFile}""
set ""FINAL={currentExe}""
set ""LOG={logFile}""

echo [%date% %time%] Updater script started >> ""%LOG%""

:: Ожидаем завершения процесса приложения (до 10 секунд)
for /L %%A in (1,1,10) do (
  tasklist /FI ""PID eq %PID%"" 2>NUL | find ""%PID%"" >NUL
  if errorlevel 1 goto wait_done
  timeout /t 1 /nobreak >NUL
)

:: Если процесс все еще запущен, принудительно убиваем его
tasklist /FI ""PID eq %PID%"" 2>NUL | find ""%PID%"" >NUL
if not errorlevel 1 (
  echo [%date% %time%] Killing process %PID% >> ""%LOG%""
  taskkill /PID %PID% /F >NUL 2>&1
  timeout /t 1 /nobreak >NUL
)

:wait_done
if not exist ""%DOWNLOADED%"" (
  echo [%date% %time%] Downloaded file not found >> ""%LOG%""
  goto cleanup
)

:: Копируем новый файл с повторными попытками в случае блокировок
for /L %%C in (1,1,5) do (
  copy /Y ""%DOWNLOADED%"" ""%FINAL%"" >NUL
  if not errorlevel 1 goto copy_success
  timeout /t 1 /nobreak >NUL
)

echo [%date% %time%] Copy to final location failed >> ""%LOG%""
goto cleanup

:copy_success
echo [%date% %time%] Successfully updated, starting application >> ""%LOG%""
start """" ""%FINAL%""
del ""%DOWNLOADED%"" >NUL 2>&1

:cleanup
echo [%date% %time%] Updater finished >> ""%LOG%""
(goto) 2>NUL & del ""%~f0""
endlocal
exit /b 0
";
                File.WriteAllText(batFile, batContent);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);

                Application.Current.Dispatcher.Invoke(() => Environment.Exit(0));
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => AppDialog.ShowError(null, "Ошибка", $"Ошибка при загрузке обновления:\n{ex.Message}"));
            }
        }
    }
}
