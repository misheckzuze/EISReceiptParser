using FiscalReceiptParser.Data;
using FiscalReceiptParser.Services;
using System.Text;

namespace FiscalReceiptParser
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ApplicationConfiguration.Initialize();

            Database.InitializeDatabase();

            BackgroundSyncScheduler.Start();

            bool isActivated = TerminalActivationStatus.IsActivatedAsync().GetAwaiter().GetResult();

            if (!isActivated)
            {
                using var activationForm = new ActivationForm();
                var result = activationForm.ShowDialog();

                if (result != DialogResult.OK)
                {
                    // User closed the activation dialog without activating.
                    // Don't let the POS run unactivated — exit cleanly instead.

                    BackgroundSyncScheduler.Stop();
                    return;
                }
            }

            Application.Run(new Form1());

            BackgroundSyncScheduler.Stop();
        }
    }
}