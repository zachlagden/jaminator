using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jaminator.Services
{
    /// <summary>
    /// Blocks startup until the manifest URL is reachable. Polls every 10s and
    /// reports each transition (offline→online, online→offline) via callback so
    /// the UI can flip an overlay.
    /// </summary>
    public sealed class InternetGate
    {
        private static readonly HttpClient Probe = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private readonly string _probeUrl;
        private readonly Logger _log;

        public InternetGate(string probeUrl, Logger log)
        {
            _probeUrl = probeUrl;
            _log = log;
        }

        /// <summary>
        /// Polls until the probe URL responds OK. Calls <paramref name="onStateChange"/>
        /// with true=online, false=offline whenever the state changes.
        /// </summary>
        public async Task WaitUntilOnlineAsync(Action<bool> onStateChange, CancellationToken ct = default)
        {
            var wasOnline = (bool?)null;
            var attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                attempt++;
                var ok = await ProbeOnceAsync().ConfigureAwait(false);
                if (ok != wasOnline)
                {
                    wasOnline = ok;
                    onStateChange(ok);
                    _log.Info(ok
                        ? "Network: online"
                        : "Network: offline — retrying every 10s");
                }
                if (ok) return;

                try { await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false); }
                catch (TaskCanceledException) { return; }
            }
        }

        private async Task<bool> ProbeOnceAsync()
        {
            try
            {
                using var resp = await Probe
                    .GetAsync(_probeUrl, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }
}
