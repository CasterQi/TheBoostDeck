using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using TheBoostDeck.Core;
using TheBoostDeck.Infrastructure;

namespace TheBoostDeck.App;

/// <summary>
/// Runs the officially signed PawnIO installer that ships next to the app when
/// the driver is missing. The installer is still raised through Windows UAC;
/// this class never attempts to bypass the elevation prompt.
/// </summary>
internal static class PawnIoAutoInstaller
{
    internal const string InstallerFileName = "PawnIO_setup.exe";
    internal const string ExpectedSha256 = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032";
    internal const string SilentArguments = "-install -silent";
    private const int InstallTimeoutMilliseconds = 120_000;

    internal static PawnIoInstallResult TryInstallIfMissing(IAppLogger logger, Action? onInstallerStarted = null)
    {
        var before = PawnIoDependency.Detect();
        if (before.Status != "missing")
            return new PawnIoInstallResult(false, before.Ready, before.Status, null);

        var applicationDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var installerPath = Path.Combine(applicationDirectory, InstallerFileName);
        if (!File.Exists(installerPath))
        {
            const string error = "The bundled PawnIO installer is missing from the application directory.";
            logger.Error(error);
            return new PawnIoInstallResult(true, false, before.Status, error);
        }

        if (!HasExpectedHash(installerPath, out var hashError))
        {
            logger.Error($"The bundled PawnIO installer failed its integrity check. {hashError}");
            return new PawnIoInstallResult(true, false, before.Status, hashError);
        }

        try
        {
            logger.Info("PawnIO is missing; launching the bundled official installer silently. Windows may request elevation.");
            onInstallerStarted?.Invoke();
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = SilentArguments,
                WorkingDirectory = applicationDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                const string error = "Windows did not start the bundled PawnIO installer.";
                logger.Error(error);
                return new PawnIoInstallResult(true, false, before.Status, error);
            }

            if (!process.WaitForExit(InstallTimeoutMilliseconds))
            {
                const string error = "The bundled PawnIO installer did not finish within two minutes.";
                logger.Error(error);
                return new PawnIoInstallResult(true, false, before.Status, error);
            }

            if (process.ExitCode != 0)
            {
                var error = $"The bundled PawnIO installer exited with code {process.ExitCode}.";
                logger.Error(error);
                return new PawnIoInstallResult(true, false, before.Status, error);
            }

            var after = WaitForReady();
            if (!after.Ready)
            {
                const string error = "The PawnIO installer completed, but the driver is not ready yet. A restart may be required.";
                logger.Error(error);
                return new PawnIoInstallResult(true, false, after.Status, error);
            }

            logger.Info($"PawnIO {after.Version ?? "2.2.0"} was installed from the bundled official installer.");
            return new PawnIoInstallResult(true, true, after.Status, null);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            const string error = "The PawnIO elevation prompt was cancelled; the dashboard will keep the degraded CPU metrics.";
            logger.Error(error);
            return new PawnIoInstallResult(true, false, before.Status, error);
        }
        catch (Exception exception)
        {
            var error = $"The bundled PawnIO installer could not be started: {exception.Message}";
            logger.Error(error, exception);
            return new PawnIoInstallResult(true, false, before.Status, error);
        }
    }

    private static SensorDriverInfo WaitForReady()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var state = PawnIoDependency.Detect();
            if (state.Ready)
                return state;

            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        return PawnIoDependency.Detect();
    }

    private static bool HasExpectedHash(string installerPath, out string? error)
    {
        try
        {
            using var stream = File.OpenRead(installerPath);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (actual == ExpectedSha256)
            {
                error = null;
                return true;
            }

            error = $"SHA-256 mismatch: expected {ExpectedSha256}, got {actual}.";
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"Could not read the bundled installer: {exception.Message}";
            return false;
        }
    }
}

internal readonly record struct PawnIoInstallResult(
    bool Attempted,
    bool Ready,
    string Status,
    string? Error);
