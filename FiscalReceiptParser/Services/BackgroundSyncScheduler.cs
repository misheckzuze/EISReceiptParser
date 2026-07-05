using System;
using System.Threading;
using System.Threading.Tasks;

namespace FiscalReceiptParser.Services
{
    /// <summary>
    /// Ported from Java's App.main scheduler: retries pending (State = 0) invoices
    /// every 2 minutes, starting immediately. Guards against overlapping runs, since
    /// Java's single-thread ScheduledExecutorService naturally serializes ticks but
    /// System.Threading.Timer does not.
    /// </summary>
    public static class BackgroundSyncScheduler
    {
        private static System.Threading.Timer? _timer;
        private static int _isRunning; // 0 = idle, 1 = running (guards re-entrancy)

        public static void Start()
        {
            if (_timer != null) return; // already started

            _timer = new System.Threading.Timer(
                callback: _ => _ = TickAsync(),
                state: null,
                dueTime: TimeSpan.Zero,
                period: TimeSpan.FromMinutes(2));
        }

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private static async Task TickAsync()
        {
            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            {
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("🔄 Running auto-resend for pending transactions...");

                string? token = ConfigHelper.GetToken();
                await TransactionService.RetryPendingTransactionsAsync(token ?? "");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Background sync error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isRunning, 0);
            }
        }
    }
}