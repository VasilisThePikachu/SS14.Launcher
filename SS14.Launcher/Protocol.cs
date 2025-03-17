using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using Serilog;
using SS14.Launcher.Localization;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Views;

namespace SS14.Launcher;

public abstract class Protocol
{
    private static ProtocolsCheckResultCode CheckExisting()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key1 = Registry.ClassesRoot.OpenSubKey("ss14s", false);
            using var key2 = Registry.ClassesRoot.OpenSubKey("ss14", false);
            using var key3 = Registry.ClassesRoot.OpenSubKey("RobustToolbox", false);
            using var key4 = Registry.ClassesRoot.OpenSubKey(".rtreplay", false);
            using var key5 = Registry.ClassesRoot.OpenSubKey(".rtbundle", false);

            if (key1 != null && key2 != null && key3 != null && key4 != null && key5 != null)
            {
                return ProtocolsCheckResultCode.Exists;
            }

            if (key1 == null && key2 == null && key3 == null && key4 == null && key5 == null)
            {
                return ProtocolsCheckResultCode.NonExistent;
            }

            return ProtocolsCheckResultCode.NeedsUpdate;
        }

        if (OperatingSystem.IsMacOS())
        {
            var appBundlePath = GetMacAppBundlePath();
            if (string.IsNullOrEmpty(appBundlePath))
            {
                // Can't find app bundle, assume none registered
                return ProtocolsCheckResultCode.NonExistent;
            }

            // Check if protocols are registered by querying Launch Services database
            var ss14Exists = CheckMacProtocol("ss14");
            var ss14SExists = CheckMacProtocol("ss14s");
            var rtbundleExists = CheckMacFileType("rtbundle");
            var rtreplayExists = CheckMacFileType("rtreplay");

            if (ss14Exists && ss14SExists && rtbundleExists && rtreplayExists)
            {
                return ProtocolsCheckResultCode.Exists;
            }

            if (!ss14Exists && !ss14SExists && !rtbundleExists && !rtreplayExists)
            {
                return ProtocolsCheckResultCode.NonExistent;
            }

            return ProtocolsCheckResultCode.NeedsUpdate;
        }

        if (OperatingSystem.IsLinux())
        {
            var ss14SchemeExists = CheckLinuxSchemeHandler("ss14");
            var ss14SSchemeExists = CheckLinuxSchemeHandler("ss14s");
            var rtbundleTypeExists = CheckLinuxMimeType("application/rtbundle");
            var rtreplayTypeExists = CheckLinuxMimeType("application/rtreplay");

            if (ss14SchemeExists && ss14SSchemeExists && rtbundleTypeExists && rtreplayTypeExists)
            {
                return ProtocolsCheckResultCode.Exists;
            }

            if (!ss14SchemeExists && !ss14SSchemeExists && !rtbundleTypeExists && !rtreplayTypeExists)
            {
                return ProtocolsCheckResultCode.NonExistent;
            }

            return ProtocolsCheckResultCode.NeedsUpdate;
        }

        return ProtocolsCheckResultCode.NonExistent;
    }

    private static string GetMacAppBundlePath()
    {
        var path = AppDomain.CurrentDomain.BaseDirectory;
        var appIndex = path.IndexOf(".app", StringComparison.Ordinal);
        if (appIndex >= 0)
        {
            return path.Substring(0, appIndex + 4);
        }
        return string.Empty;
    }

    private static bool CheckMacProtocol(string scheme)
    {
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "defaults",
                    Arguments = $"read com.apple.LaunchServices/com.apple.launchservices.secure LSHandlers",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            // Check if the app bundle is registered as a handler for this scheme
            // This is a basic check that looks for both the scheme and our app identifier in the output
            return output.Contains($"LSHandlerURLScheme = {scheme}") &&
                   output.Contains("com.spacestation14.launcher");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check macOS protocol handler for {Scheme}", scheme);
            return false;
        }
    }

    private static bool CheckMacFileType(string extension)
    {
        try
        {
            // Create a temporary file to check, this is the most reliable way
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"test.{extension}");
            File.WriteAllText(tempFilePath, "test");

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "mdls",
                    Arguments = $"-name kMDItemContentTypeTree \"{tempFilePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            // Delete temp file
            try { File.Delete(tempFilePath); } catch { /* Ignore */ }

            // Check if our app identifier is linked to this file type
            return output.Contains($"dyn.ah62d4rv4ge81e7") || // General dynamic type pattern
                   output.Contains($"com.spacestation14"); // Our app identifier
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check macOS file type for {Extension}", extension);
            return false;
        }
    }

    private static bool CheckLinuxSchemeHandler(string scheme)
    {
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xdg-mime",
                    Arguments = $"query default x-scheme-handler/{scheme}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            return !string.IsNullOrEmpty(output) && output.Contains("SS14.desktop");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check Linux scheme handler for {Scheme}", scheme);
            return false;
        }
    }

    private static bool CheckLinuxMimeType(string mimeType)
    {
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xdg-mime",
                    Arguments = $"query default {mimeType}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            return !string.IsNullOrEmpty(output) && output.Contains("SS14.desktop");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check Linux mime type for {MimeType}", mimeType);
            return false;
        }
    }

    public static async Task<ProtocolsResultCode> RegisterProtocol()
    {
        try
        {
            // Windows registration
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var proc = new Process();
                    proc.StartInfo.FileName = "Space Station 14 Launcher.exe";
                    proc.StartInfo.Arguments = "--register-protocol";
                    proc.StartInfo.UseShellExecute = true;
                    proc.StartInfo.Verb = "runas";
                    proc.Start();
                    await proc.WaitForExitAsync();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Do nothing, the user either declined UAC, they don't have administrator rights or something else went wrong.
                    Log.Warning("User declined UAC or doesn't have admin rights.");
                    return ProtocolsResultCode.ErrorWindowsUac;
                }
            }

            // macOS registration
            if (OperatingSystem.IsMacOS())
            {
                var path = AppDomain.CurrentDomain.BaseDirectory;

                // User needs to move the app manually to get this sandbox restriction lifted.
                // This can be done "automated" by making one of those installer dmg stuff
                if (path.Contains("AppTranslocation"))
                {
                    Log.Error(
                        "I have been put in apple jail (Gatekeeper path randomisation)... move me to your application folder");
                    return ProtocolsResultCode.ErrorMacOSTranslocation;
                }

                var appBundlePath = GetMacAppBundlePath();
                if (string.IsNullOrEmpty(appBundlePath))
                {
                    Log.Error("Could not determine app bundle path");
                    return ProtocolsResultCode.ErrorUnknown;
                }

                var proc = new Process();
                // Use lsregister to register app capabilities with Launch Services
                proc.StartInfo.FileName =
                    "/System/Library/Frameworks/CoreServices.framework/Versions/A/Frameworks/LaunchServices.framework/Versions/A/Support/lsregister";
                proc.StartInfo.Arguments = $"-R -f {appBundlePath}";
                proc.Start();
                await proc.WaitForExitAsync();

                // Refresh the Launch Services database to ensure changes take effect
                var refreshProc = new Process();
                refreshProc.StartInfo.FileName = "/usr/bin/killall";
                refreshProc.StartInfo.Arguments = "-KILL Finder";
                refreshProc.Start();
                await refreshProc.WaitForExitAsync();
            }

            // Linux registration
            if (OperatingSystem.IsLinux())
            {
                // Get the path to the launcher's desktop file
                var launcherPath = AppDomain.CurrentDomain.BaseDirectory;
                var desktopFilePath = Path.Combine(launcherPath, "SS14.desktop");

                if (!File.Exists(desktopFilePath))
                {
                    Log.Error("Desktop file does not exist at path: {Path}", desktopFilePath);
                    return ProtocolsResultCode.ErrorUnknown;
                }

                // Create .local directories if they don't exist
                var localShareDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share");
                var applicationsDir = Path.Combine(localShareDir, "applications");
                var mimeDir = Path.Combine(localShareDir, "mime");
                var packagesDir = Path.Combine(mimeDir, "packages");

                try
                {
                    Directory.CreateDirectory(applicationsDir);
                    Directory.CreateDirectory(packagesDir);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to create necessary directories");
                    return ProtocolsResultCode.ErrorUnknown;
                }

                // Copy desktop file to applications directory
                var targetDesktopPath = Path.Combine(applicationsDir, "SS14.desktop");
                File.Copy(desktopFilePath, targetDesktopPath, true);

                // Make desktop file executable
                try
                {
                    Helpers.ChmodPlusX(targetDesktopPath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to make desktop file executable");
                    // Continue anyway, as this might still work
                }

                // Copy mime type file
                var mimeTypePath = Path.Combine(launcherPath, "ss14-mime-type.xml");
                var targetMimePath = Path.Combine(packagesDir, "ss14-mime-type.xml");

                if (!File.Exists(mimeTypePath))
                {
                    Log.Error("MIME type file does not exist at path: {Path}", mimeTypePath);
                    return ProtocolsResultCode.ErrorUnknown;
                }

                File.Copy(mimeTypePath, targetMimePath, true);

                // Update databases
                RunLinuxProcess("update-mime-database", localShareDir + "/mime");
                RunLinuxProcess("update-desktop-database", localShareDir + "/applications");

                // Register protocol handlers and file types
                RunLinuxProcess("xdg-mime", $"default SS14.desktop x-scheme-handler/ss14");
                RunLinuxProcess("xdg-mime", $"default SS14.desktop x-scheme-handler/ss14s");
                RunLinuxProcess("xdg-mime", $"default SS14.desktop application/rtbundle");
                RunLinuxProcess("xdg-mime", $"default SS14.desktop application/rtreplay");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register protocol, and we did not catch it");
            return ProtocolsResultCode.ErrorUnknown;
        }

        Log.Information("Successfully registered protocol");
        return ProtocolsResultCode.Success;
    }

    public static async Task<ProtocolsResultCode> UnregisterProtocol()
    {
        try
        {
            // Windows unregistration
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var proc = new Process();
                    proc.StartInfo.FileName = "Space Station 14 Launcher.exe";
                    proc.StartInfo.Arguments = "--unregister-protocol";
                    proc.StartInfo.UseShellExecute = true;
                    proc.StartInfo.Verb = "runas";
                    proc.Start();
                    await proc.WaitForExitAsync();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Do nothing, the user either declined UAC, they don't have administrator rights or something else went wrong.
                    Log.Warning("User declined UAC or doesn't have admin rights.");
                    return ProtocolsResultCode.ErrorWindowsUac;
                }
            }

            // macOS unregistration
            if (OperatingSystem.IsMacOS())
            {
                var appBundlePath = GetMacAppBundlePath();
                if (string.IsNullOrEmpty(appBundlePath))
                {
                    Log.Error("Could not determine app bundle path for unregistration");
                    return ProtocolsResultCode.ErrorUnknown;
                }

                var proc = new Process();
                proc.StartInfo.FileName =
                    "/System/Library/Frameworks/CoreServices.framework/Versions/A/Frameworks/LaunchServices.framework/Versions/A/Support/lsregister";
                proc.StartInfo.Arguments = $"-R -f -u {appBundlePath}";
                proc.Start();
                await proc.WaitForExitAsync();

                // Refresh the Launch Services database
                var refreshProc = new Process();
                refreshProc.StartInfo.FileName = "/usr/bin/killall";
                refreshProc.StartInfo.Arguments = "-KILL Finder";
                refreshProc.Start();
                await refreshProc.WaitForExitAsync();

                // Clear Launch Services cache as a fallback
                try
                {
                    var clearProc = new Process();
                    clearProc.StartInfo.FileName = "/bin/rm";
                    clearProc.StartInfo.Arguments = "-rf ~/Library/Caches/com.apple.LaunchServices-*.csstore";
                    clearProc.StartInfo.UseShellExecute = true;
                    clearProc.Start();
                    await clearProc.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to clear Launch Services cache, but continuing");
                    // Continue anyway as this is just a fallback
                }
            }

            // Linux unregistration
            if (OperatingSystem.IsLinux())
            {
                var localShareDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share");
                var applicationsDir = Path.Combine(localShareDir, "applications");
                var mimeDir = Path.Combine(localShareDir, "mime");
                var packagesDir = Path.Combine(mimeDir, "packages");

                // Remove desktop file
                var targetDesktopPath = Path.Combine(applicationsDir, "SS14.desktop");
                if (File.Exists(targetDesktopPath))
                {
                    try
                    {
                        File.Delete(targetDesktopPath);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to delete desktop file: {Path}", targetDesktopPath);
                        // Continue with other operations
                    }
                }

                // Remove mime type file
                var targetMimePath = Path.Combine(packagesDir, "ss14-mime-type.xml");
                if (File.Exists(targetMimePath))
                {
                    try
                    {
                        File.Delete(targetMimePath);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to delete MIME type file: {Path}", targetMimePath);
                        // Continue with other operations
                    }
                }

                // Update databases
                RunLinuxProcess("update-mime-database", localShareDir + "/mime");
                RunLinuxProcess("update-desktop-database", localShareDir + "/applications");

                // Attempt to reset protocol handlers to remove our defaults
                RunLinuxProcess("xdg-mime", "default x-scheme-handler/ss14");
                RunLinuxProcess("xdg-mime", "default x-scheme-handler/ss14s");
                RunLinuxProcess("xdg-mime", "default application/rtbundle");
                RunLinuxProcess("xdg-mime", "default application/rtreplay");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to unregister protocol, and we did not catch it");
            return ProtocolsResultCode.ErrorUnknown;
        }

        Log.Information("Successfully unregistered protocol");
        return ProtocolsResultCode.Success;
    }

    private static void RunLinuxProcess(string fileName, string arguments)
    {
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                }
            };
            proc.Start();

            var error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                Log.Warning("Process {FileName} exited with code {ExitCode}: {Error}",
                    fileName, proc.ExitCode, error);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to run Linux process {FileName} with arguments {Arguments}",
                fileName, arguments);
        }
    }

    // UI popup stuff
    public static async Task OptionsManualPopup(MainWindow control)
    {
        var existing = CheckExisting() != ProtocolsCheckResultCode.Exists;

        // Not using ConfirmDialogBuilder because I am not sure how to make it support variables or whatever they are named
        var dialog = new ConfirmDialog
        {
            Title = LocalizationManager.Instance.GetString("protocols-dialog-title"),
            DialogContent = LocalizationManager.Instance.GetString("protocols-dialog-content-action-question",
                ("action", existing ? LocalizationManager.Instance.GetString("protocols-dialog-action-register")
                    : LocalizationManager.Instance.GetString("protocols-dialog-action-unregister"))),
            ConfirmButtonText = LocalizationManager.Instance.GetString("protocols-dialog-continue"),
            CancelButtonText = LocalizationManager.Instance.GetString("protocols-dialog-back"),
        };

        var question = await dialog.ShowDialog<bool>(control);

        if (question)
        {
            await HandleResult(control);
        }
    }

    public static async Task ProtocolSignupPopup(MainWindow control, DataManager cfg)
    {
        if (CheckExisting() == ProtocolsCheckResultCode.NeedsUpdate)
        {
            await ProtocolUpdatePopup(control);
            return;
        }

        if (!IsCandidateForProtocols(cfg))
            return;

        var answer = await Helpers.ConfirmDialogBuilder(control,
            "protocols-dialog-title",
            "protocols-dialog-content",
            "protocols-dialog-confirm",
            "protocols-dialog-deny");

        if (answer)
        {
            await HandleResult(control);
        }

        cfg.SetCVar(CVars.HasSeenProtocolsDialog, true);
    }

    private static async Task ProtocolUpdatePopup(MainWindow control)
    {
        var answer = await Helpers.ConfirmDialogBuilder(control,
            "protocols-dialog-title",
            "protocols-dialog-content-update",
            "protocols-dialog-confirm",
            "protocols-dialog-deny");

        if (answer)
        {
            await HandleResult(control);
        }
    }

    private static async Task HandleResult(MainWindow control)
    {
        // Lord, spare me for I have sinned.
        // The goto is evil, yet the alternative is worse (in my opinion).
        // Judge me not for the sin, but for the necessity. amen.
        retryPoint:

        var action = CheckExisting() == ProtocolsCheckResultCode.Exists ? await UnregisterProtocol() : await RegisterProtocol();

        switch (action)
        {
            case ProtocolsResultCode.Success:
                await Helpers.OkDialogBuilder(control,
                    "protocols-dialog-title",
                    "protocols-dialog-content-success",
                    "protocols-dialog-ok");
                break;
            case ProtocolsResultCode.ErrorWindowsUac:
                var retryUac = await Helpers.ConfirmDialogBuilder(control,
                    "protocols-dialog-error-title",
                    "protocols-dialog-error-windows-uac",
                    "protocols-dialog-error-again",
                    "protocols-dialog-deny");
                if (retryUac)
                    goto retryPoint;
                break;
            case ProtocolsResultCode.ErrorMacOSTranslocation:
                await Helpers.OkDialogBuilder(control,
                    "protocols-dialog-error-title",
                    "protocols-dialog-error-macos-translocation",
                    "protocols-dialog-error-ok");
                break;
            case ProtocolsResultCode.ErrorUnknown:
                var retryUnknown = await Helpers.ConfirmDialogBuilder(control,
                    "protocols-dialog-error-title",
                    "protocols-dialog-error-generic",
                    "protocols-dialog-error-again",
                    "protocols-dialog-deny");
                if (retryUnknown)
                    goto retryPoint;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static bool IsCandidateForProtocols(DataManager cfg)
    {
        // They have been shown this dialog before, don't bother.
        if (cfg.GetCVar(CVars.HasSeenProtocolsDialog))
            return false;

        // It already exists. Either cause of a reset config file or already installed by steam.
        // Let's also set the cvar.
        if (CheckExisting() == ProtocolsCheckResultCode.Exists)
        {
            cfg.SetCVar(CVars.HasSeenProtocolsDialog, true);

            return false;
        }

        // Check if the OS is compatible... im sorry freebsd users
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return false;

        // We (hopefully) are ready!
        return true;
    }

    public enum ProtocolsResultCode : byte
    {
        Success =  0,
        ErrorWindowsUac,
        ErrorMacOSTranslocation,
        ErrorUnknown
    }

    public enum ProtocolsCheckResultCode : byte
    {
        Exists =  0,
        NeedsUpdate,
        NonExistent
    }
}
