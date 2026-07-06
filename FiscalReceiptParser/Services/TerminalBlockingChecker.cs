using System.Threading.Tasks;
using System.Windows.Forms;
using FiscalReceiptParser.Models;

namespace FiscalReceiptParser.Services
{

    public static class TerminalBlockingChecker
    {
        /// <summary>
        /// Pure logic version — no UI. Ported from Java's
        /// Helper.checkAndHandleTerminalBlocking, minus the Alert popups, so it's
        /// safe to call from an unattended background pipeline (the folder watcher)
        /// as well as from a foreground button click.
        /// </summary>
        public static async Task<BlockingCheckResult> CheckAsync()
        {
            string? terminalId = ConfigHelper.GetTerminalId();
            string? bearerToken = ConfigHelper.GetToken();

            if (string.IsNullOrEmpty(terminalId) || string.IsNullOrEmpty(bearerToken))
            {
                System.Diagnostics.Debug.WriteLine("⚠️ Missing terminalId/token for blocking check — allowing by default.");
                return new BlockingCheckResult { IsAllowed = true };
            }

            using var httpClient = new System.Net.Http.HttpClient();
            var service = new MraApiService(httpClient);

            var checkResult = await service.CheckIfTerminalIsBlockedAsync(terminalId, bearerToken);

            bool isUnblocked = checkResult == null
                ? TerminalBlockingRepository.GetBlockingReason(terminalId) == null
                : checkResult.IsUnblocked;

            if (isUnblocked)
            {
                TerminalBlockingRepository.DeleteBlockingReason(terminalId);
                return new BlockingCheckResult { IsAllowed = true };
            }

            string? existingReason = TerminalBlockingRepository.GetBlockingReason(terminalId);

            if (existingReason != null)
            {
                return new BlockingCheckResult { IsAllowed = false, Reason = existingReason };
            }

            var blockingInfo = await service.FetchBlockingMessageAsync(terminalId, bearerToken);

            if (blockingInfo == null)
            {
                return new BlockingCheckResult
                {
                    IsAllowed = false,
                    Reason = "Unable to verify terminal status. Please check your connection and try again."
                };
            }

            if (blockingInfo.IsBlocked)
            {
                string reason = !string.IsNullOrEmpty(blockingInfo.BlockingReason)
                    ? blockingInfo.BlockingReason
                    : "No reason provided by server.";

                TerminalBlockingRepository.SaveBlockingReason(terminalId, reason, false);
                return new BlockingCheckResult { IsAllowed = false, Reason = reason };
            }

            // Server says NOT blocked, overriding an incorrect isUnblocked==false
            TerminalBlockingRepository.DeleteBlockingReason(terminalId);
            return new BlockingCheckResult { IsAllowed = true };
        }

        /// <summary>
        /// UI convenience wrapper — for any button-click-driven flow that still wants
        /// a popup. Not used by the folder-watcher pipeline (see ProcessFileAsync),
        /// which logs to the activity list instead of showing modal dialogs.
        /// </summary>
        public static async Task<bool> CheckAndHandleTerminalBlockingAsync(IWin32Window? owner = null)
        {
            var result = await CheckAsync();

            if (!result.IsAllowed)
            {
                bool isConnectionError = result.Reason?.StartsWith("Unable to verify") == true;
                MessageBox.Show(owner, $"Terminal is blocked. Reason: {result.Reason}",
                    isConnectionError ? "Connection Error" : "Terminal Blocked",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return result.IsAllowed;
        }
    }
}