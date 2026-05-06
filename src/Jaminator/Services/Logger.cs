using System;
using System.IO;

namespace Jaminator.Services
{
    public sealed class Logger
    {
        private readonly string _logFile;
        private readonly object _fileLock = new object();

        public event Action<string>? OnMessage;

        public Logger()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                   "Jaminator", "logs");
            Directory.CreateDirectory(dir);
            _logFile = Path.Combine(dir, $"jaminator-{DateTime.Now:yyyyMMdd}.log");
        }

        public void Info(string msg) => Write("INFO", msg);
        public void Warn(string msg) => Write("WARN", msg);
        public void Error(string msg) => Write("ERROR", msg);
        public void Error(string msg, Exception ex) => Write("ERROR", msg + " — " + ex.Message);

        private void Write(string level, string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {msg}";
            try
            {
                lock (_fileLock)
                {
                    File.AppendAllText(_logFile, line + Environment.NewLine);
                }
            }
            catch { /* never let logging failure crash a run */ }
            OnMessage?.Invoke(line);
        }
    }
}
