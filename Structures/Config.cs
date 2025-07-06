using Newtonsoft.Json;

namespace DiscordReborn.Structures
{
    public class Config
    {
        [JsonProperty]
        public string Token { get; set; }

        [JsonProperty]
        public ulong[] Owners { get; set; }

        [JsonProperty]
        public string LavalinkBaseURL { get; set; }

        [JsonProperty]
        public ushort LavalinkPort { get; set; }
    }
}
