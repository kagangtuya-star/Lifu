using Dalamud.Configuration;

namespace Lifu
{
    public class Configuration : IPluginConfiguration{
        public int Version { get; set; } = 0;
        public void Initialize() { }
        public void Save() => DalamudApi.PluginInterface.SavePluginConfig(this);

        public int LeveQuestId { get; set; } = 1635;
        public int LeveItemMagic { get; set; } = 2005;
        public int TargetDelay { get; set; } = 1500;

        public string LeveNpc1 { get; set; } = "格里格";
        public string LeveNpc2 { get; set; } = "阿尔德伊恩";
    }
}
