using FiscalReceiptParser.Data;
using FiscalReceiptParser.Services;

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
            ApplicationConfiguration.Initialize();

            Database.InitializeDatabase();

            bool isActivated = TerminalActivationStatus.IsActivatedAsync().GetAwaiter().GetResult();

            if (!isActivated)
            {
                using var activationForm = new ActivationForm();
                var result = activationForm.ShowDialog();

                if (result != DialogResult.OK)
                {
                    // User closed the activation dialog without activating.
                    // Don't let the POS run unactivated — exit cleanly instead.
                    return;
                }
            }

            Application.Run(new Form1());
        }
    }
}