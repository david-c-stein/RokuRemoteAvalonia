using System.Collections.Generic;

namespace RokuRemoteAvalonia.Models
{
    public enum ActionType
    {
        KeyPress,
        AppLaunch,
        Wait
    }

    public class QuickLaunchAction
    {
        public ActionType Type { get; set; }
        public string Command { get; set; } = "";
        public string Parameter { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int DelayMs { get; set; } = 100;
    }

    public class QuickLaunchSequence
    {
        public string Name { get; set; } = "";
        public List<QuickLaunchAction> Actions { get; set; } = new();
        
        public string GetDisplaySummary()
        {
            if (Actions.Count == 0) return "No actions recorded";
            if (Actions.Count == 1) return Actions[0].DisplayName;
            return $"{Actions.Count} actions: {Actions[0].DisplayName}...";
        }
    }
}
