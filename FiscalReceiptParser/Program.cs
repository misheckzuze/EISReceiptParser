using FiscalReceiptParser.Data;
using FiscalReceiptParser.Services;

namespace FiscalReceiptParser
{
    internal static class Program
    {
        [STAThread]
        static void Main()
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