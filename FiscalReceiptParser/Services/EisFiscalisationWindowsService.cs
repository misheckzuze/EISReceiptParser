using System;
using System.ServiceProcess;
using FiscalReceiptParser.Data;

namespace FiscalReceiptParser.Services
{
    /// <summary>
    /// The service that keeps invoice fiscalisation running with no dashboard open.
    /// It owns only one job: make sure the SQLite schema exists, then hand off to
    /// PdfFolderWatcher, which watches the Palladium PDF export folder and
    /// fiscalises everything that lands in it.
    /// </summary>
    public class EisFiscalisationWindowsService : ServiceBase
    {
        // Sourced from EisServiceConstants so the name registered with the SCM
        // (via EisServiceController.InstallService) always matches the name this
        // process reports at runtime. A mismatch here is why a service can show
        // "installed" but fail to start with no useful error.
        public const string SERVICE_NAME = EisServiceConstants.SERVICE_NAME;
        public const string SERVICE_DISPLAY_NAME = EisServiceConstants.SERVICE_DISPLAY_NAME;
        public const string SERVICE_DESCRIPTION = EisServiceConstants.SERVICE_DESCRIPTION;

        private PdfFolderWatcher _pdfWatcher;

        public EisFiscalisationWindowsService()
        {
            ServiceName = SERVICE_NAME;
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                // Safe to call every time — CREATE TABLE IF NOT EXISTS is a no-op
                // once the schema is already there. Guarantees the DB (and folder
                // settings table) exist before PdfFolderWatcher tries to read them.
                Database.InitializeDatabase();

                _pdfWatcher = new PdfFolderWatcher();
                _pdfWatcher.Start();

                EisServiceLog.Info("EIS Fiscalisation service started — watching for Palladium PDF invoices.");
            }
            catch (Exception ex)
            {
                EisServiceLog.Error($"Service failed to start: {ex.Message}");
                System.Diagnostics.EventLog.WriteEntry(
                    SERVICE_NAME, ex.ToString(),
                    System.Diagnostics.EventLogEntryType.Error);
                throw;
            }
        }

        protected override void OnStop()
        {
            try
            {
                _pdfWatcher?.Stop();
                _pdfWatcher?.Dispose();

                EisServiceLog.Info("EIS Fiscalisation service stopped.");
            }
            catch (Exception ex)
            {
                EisServiceLog.Error($"Error stopping service: {ex.Message}");
            }
        }
    }
}