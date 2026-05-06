using System;
using System.Collections.Generic;
using System.IO;
using Jaminator.Models;

namespace Jaminator.Services
{
    public sealed class FolderManager
    {
        private readonly Logger _log;
        public FolderManager(Logger log) { _log = log; }

        public void EnsureFolders(IEnumerable<FolderEntry> folders)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var f in folders)
            {
                if (!f.CreateIfMissing) continue;
                var rel = f.Path.Replace('/', Path.DirectorySeparatorChar);
                var abs = Path.Combine(profile, rel);
                if (Directory.Exists(abs))
                {
                    _log.Info($"Folder exists: {abs}");
                }
                else
                {
                    Directory.CreateDirectory(abs);
                    _log.Info($"Folder created: {abs}");
                }
            }
        }
    }
}
