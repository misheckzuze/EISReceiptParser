using FiscalReceiptParser.Data;
using FiscalReceiptParser.Services;
<<<<<<< HEAD
using System.Text;
=======
using System.ServiceProcess;
>>>>>>> 23be223ca687b321fd946dc3865ab1bf737a47c3

namespace FiscalReceiptParser
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
<<<<<<< HEAD
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ApplicationConfiguration.Initialize();

            Database.InitializeDatabase();

            BackgroundSyncScheduler.Start();

            bool isActivated = TerminalActivationStatus.IsActivatedAsync().GetAwaiter().GetResult();

            if (!isActivated)
=======
            if (!Environment.UserInteractive)
>>>>>>> 23be223ca687b321fd946dc3865ab1bf737a47c3
            {
                ServiceBase.Run(new EisFiscalisationWindowsService());

<<<<<<< HEAD
                if (result != DialogResult.OK)
                {
                    // User closed the activation dialog without activating.
                    // Don't let the POS run unactivated — exit cleanly instead.

                    BackgroundSyncScheduler.Stop();
                    return;
                }
=======
>>>>>>> 23be223ca687b321fd946dc3865ab1bf737a47c3
            }
            else
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

<<<<<<< HEAD
            Application.Run(new Form1());

            BackgroundSyncScheduler.Stop();
=======
                Database.InitializeDatabase();

                bool isActivated = TerminalActivationStatus.IsActivatedAsync().GetAwaiter().GetResult();

                if (!isActivated)
                {
                    using var activationForm = new ActivationForm();
                    var result = activationForm.ShowDialog();

                    if (result != DialogResult.OK)
                    {
                        return;
                    }
                }

                Application.Run(new Form1());
            }
>>>>>>> 23be223ca687b321fd946dc3865ab1bf737a47c3
        }
    }
}