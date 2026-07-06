using System;
using System.Windows.Forms;
using FiscalReceiptParser.Models;

namespace FiscalReceiptParser.Services
{
    /// <summary>
    /// Ported from Java's continueAfterValidation offline-limit check. Pure logic,
    /// no UI — safe to call from the folder-watcher pipeline as well as any
    /// button-driven flow.
    ///
    /// NOTE: matches Java's comparison exactly, including its edge case — if
    /// LimitHours is 0 (OfflineLimit not yet configured/synced) and there are no
    /// pending invoices (HoursElapsed also 0), this evaluates 0 >= 0 = true and
    /// blocks every transaction. That's inherited from the Java source as-is,
    /// not something added here — worth deciding if that's the desired behavior.
    /// </summary>
    public static class OfflineLimitChecker
    {
        public static OfflineLimitCheckResult Check()
        {
            int limitHours = InvoiceRepository.GetOfflineTransactionLimitHours();
            var earliestPending = InvoiceRepository.GetEarliestPendingInvoiceDateTime();

            double hoursElapsed = earliestPending.HasValue
                ? (DateTime.Now - earliestPending.Value).TotalHours
                : 0;

            bool allowed = !(hoursElapsed >= limitHours);

            return new OfflineLimitCheckResult
            {
                IsAllowed = allowed,
                HoursElapsed = hoursElapsed,
                LimitHours = limitHours
            };
        }

        /// <summary>
        /// UI convenience wrapper for button-driven flows that want a popup —
        /// not used by the folder-watcher pipeline, which logs instead.
        /// </summary>
        public static bool CheckWithPrompt(IWin32Window? owner = null)
        {
            var result = Check();

            if (!result.IsAllowed)
            {
                MessageBox.Show(owner,
                    "⚠️ Offline transaction time limit reached. Please sync before continuing.",
                    "Offline Limit Reached", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return result.IsAllowed;
        }
    }
}