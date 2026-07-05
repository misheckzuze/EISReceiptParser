using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace FiscalReceiptParser.Services
{
    public static class EisServiceController
    {
        private static string SERVICE_NAME => EisServiceConstants.SERVICE_NAME;

        public static void InstallService(string serviceExePath)
        {
            if (!File.Exists(serviceExePath))
                throw new FileNotFoundException(
                    "Service executable not found. Build the project first.", serviceExePath);

            if (IsInstalled())
                UninstallService();

            RunSc($"create \"{SERVICE_NAME}\" " +
                  $"binPath= \"{serviceExePath}\" " +
                  $"DisplayName= \"{EisServiceConstants.SERVICE_DISPLAY_NAME}\" " +
                  $"start= auto");

            RunSc($"description \"{SERVICE_NAME}\" \"{EisServiceConstants.SERVICE_DESCRIPTION}\"");
        }

        public static void UninstallService()
        {
            if (!IsInstalled()) return;

            if (IsRunning()) StopService();

            RunSc($"delete \"{SERVICE_NAME}\"");
        }

        // ── Start ─────────────────────────────────────────────────────────────
        // Goes through sc.exe (RunSc) rather than ServiceController.Start() so
        // the same UAC "runas" elevation prompt covers this too — otherwise this
        // throws UnauthorizedAccessException unless the calling app is already
        // running elevated.
        public static void StartService()
        {
            if (GetStatus() == ServiceControllerStatus.Running) return;

            RunSc($"start \"{SERVICE_NAME}\"");
            WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }

        // ── Stop ──────────────────────────────────────────────────────────────
        public static void StopService()
        {
            if (GetStatus() == ServiceControllerStatus.Stopped) return;

            RunSc($"stop \"{SERVICE_NAME}\"");
            WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }

        // ── Restart ───────────────────────────────────────────────────────────
        public static void RestartService()
        {
            if (IsRunning()) StopService();
            Thread.Sleep(1000);
            StartService();
        }

        // ── State checks (read-only, no elevation needed) ────────────────────
        public static bool IsInstalled()
        {
            return ServiceController.GetServices()
                .Any(s => s.ServiceName.Equals(SERVICE_NAME,
                     StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsRunning()
        {
            return GetStatus() == ServiceControllerStatus.Running;
        }

        public static ServiceControllerStatus GetStatus()
        {
            if (!IsInstalled()) return ServiceControllerStatus.Stopped;

            try
            {
                using (var sc = new ServiceController(SERVICE_NAME))
                    return sc.Status;
            }
            catch
            {
                return ServiceControllerStatus.Stopped;
            }
        }

        private static void WaitForStatus(ServiceControllerStatus target, TimeSpan timeout)
        {
            try
            {
                using (var sc = new ServiceController(SERVICE_NAME))
                    sc.WaitForStatus(target, timeout);
            }
            catch
            {
                // sc.exe already returned success/failure above — a slow SCM
                // transition here isn't fatal, the UI will just refresh shortly after.
            }
        }

        // ── Private: run sc.exe and throw on failure ──────────────────────────
        private static void RunSc(string arguments)
        {
            var psi = new ProcessStartInfo("sc.exe", arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Verb = "runas"   // requires elevation
            };

            using (var p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();

                if (p.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"sc.exe failed (exit={p.ExitCode}): {stdout} {stderr}");
            }
        }
    }

    // ── Constants shared between service exe and WinForms UI ─────────────────
    public static class EisServiceConstants
    {
        public const string SERVICE_NAME = "EISFiscalizationService";
        public const string SERVICE_DISPLAY_NAME = "EIS Fiscalisation Service";
        public const string SERVICE_DESCRIPTION =
            "Monitors a folder for incoming pdfs to be fiscalised to the MRA EIS fiscalisation API.";
    }
}