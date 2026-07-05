using FiscalReceiptParser.Data;
using FiscalReceiptParser.Services;
using System.ServiceProcess;

namespace FiscalReceiptParser
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            if (!Environment.UserInteractive)
            {
                ServiceBase.Run(new EisFiscalisationWindowsService());

            }
            else
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

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
        }
    }
}