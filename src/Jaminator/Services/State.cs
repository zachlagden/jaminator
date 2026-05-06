using System;
using System.IO;
using Newtonsoft.Json;

namespace Jaminator.Services
{
    /// <summary>
    /// Tiny key/value state file at ProgramData\Jaminator\state.json. Used for
    /// first-run welcome flag and last-login-run summary so the UI can surface
    /// "what happened during the silent logon run".
    /// </summary>
    public sealed class State
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Jaminator");
        private static readonly string File =
            Path.Combine(Dir, "state.json");

        public static StateData Load()
        {
            try
            {
                if (System.IO.File.Exists(File))
                {
                    var json = System.IO.File.ReadAllText(File);
                    return JsonConvert.DeserializeObject<StateData>(json) ?? new StateData();
                }
            }
            catch { /* corrupt - fall through to default */ }
            return new StateData();
        }

        public static void Save(StateData data)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                System.IO.File.WriteAllText(File, json);
            }
            catch { /* don't let state-write failure crash anything */ }
        }
    }

    public sealed class StateData
    {
        [JsonProperty("welcomeSeen")] public bool WelcomeSeen { get; set; }
        [JsonProperty("lastLoginRunUtc")] public DateTime? LastLoginRunUtc { get; set; }
        [JsonProperty("lastLoginRunOk")] public bool? LastLoginRunOk { get; set; }
        [JsonProperty("lastFullRunUtc")] public DateTime? LastFullRunUtc { get; set; }
    }
}
