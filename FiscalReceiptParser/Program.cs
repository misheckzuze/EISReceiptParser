using FiscalReceiptParser.Data;
using FiscalReceiptParser.Services;
using System.ServiceProcess;
using System.Text;

namespace FiscalReceiptParser
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // See https://aka.ms/applicationconfiguration.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            //ApplicationConfiguration.Initialize();

            Database.InitializeDatabase();

            // Run as Windows Service
            if (!Environment.UserInteractive)
            {
                ServiceBase.Run(new EisFiscalisationWindowsService());
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Run as desktop application
            BackgroundSyncScheduler.Start();

            bool isActivated = TerminalActivationStatus
                .IsActivatedAsync()
                .GetAwaiter()
                .GetResult();

            if (!isActivated)
            {
                using var activationForm = new ActivationForm();
                var result = activationForm.ShowDialog();

                if (result != DialogResult.OK)
                {
                    BackgroundSyncScheduler.Stop();
                    return;
                }
            }

            Application.Run(new Form1());

            BackgroundSyncScheduler.Stop();
        }
    }
}