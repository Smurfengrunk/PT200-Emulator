using PT200Emulator.Infrastructure.Logging;

namespace PT200Emulator.Core.Parser
{
    public class ModeManager
    {
        private readonly Dictionary<int, ModeDefinition> definitions;
        private readonly HashSet<int> activeModes = new();
        private readonly LocalizationProvider loc;

        public ModeManager(LocalizationProvider localization)
        {
            loc = localization;
            definitions = new Dictionary<int, ModeDefinition>
        {
            { 1,  new ModeDefinition(1,  "Auto Line Feed", false) },
            { 2,  new ModeDefinition(2,  "Character/Block", false) },
            { 3,  new ModeDefinition(3,  "Logical Attributes", false) },
            { 4,  new ModeDefinition(4,  "Page/Line", false) },
            { 5,  new ModeDefinition(5,  "Hard/Soft Scroll", false) },
            { 6,  new ModeDefinition(6,  "Unprotected/Modified", true) },
            { 7,  new ModeDefinition(7,  "Null/Space", true) },
            { 8,  new ModeDefinition(8,  "Screen Wrap", false) },
            { 9,  new ModeDefinition(9,  "Line Truncate", false) },
            { 10, new ModeDefinition(10, "Numeric/Function Keypad", true) },
            { 11, new ModeDefinition(11, "One/Two Page Boundary", false) },
            { 12, new ModeDefinition(12, "Visual Attribute Lock", false) },
            { 13, new ModeDefinition(13, "Local Cursor Action", true) },
            { 14, new ModeDefinition(14, "Selective Data Trap", false) },
            { 15, new ModeDefinition(15, "Transparent Data Mode", false) },
            { 16, new ModeDefinition(16, "Host Notification", false) },
            { 17, new ModeDefinition(17, "Send Tabs", true) },
            { 18, new ModeDefinition(18, "Function Termination", false) },
            { 19, new ModeDefinition(19, "Soft Lock Option", false) },
            { 20, new ModeDefinition(20, "DSC/Normal", false) },
            { 21, new ModeDefinition(21, "E2 Mode", false) },
            { 22, new ModeDefinition(22, "Dead Keys Enable", false) }
        };

            // Initiera med default states
            foreach (var def in definitions.Values)
            {
                if (def.DefaultSet)
                    activeModes.Add(def.Id);
            }
        }

        public bool IsSet(int mode) => activeModes.Contains(mode);

        public void Set(int mode)
        {
            activeModes.Add(mode);
            Log(mode, true);
        }

        public void Reset(int mode)
        {
            activeModes.Remove(mode);
            Log(mode, false);
        }

        private void Log(int mode, bool enabled)
        {
            if (definitions.TryGetValue(mode, out var def))
                this.LogDebug(loc.Get(enabled ? "mode.set" : "mode.reset", mode, def.Name));
            else
                this.LogDebug(loc.Get(enabled ? "mode.set" : "mode.reset", mode, "Okänt läge"));
        }

        public void Dump()
        {
            this.LogDebug(loc.Get("mode.dump.header"));
            foreach (var def in definitions.Values.OrderBy(d => d.Id))
            {
                var status = IsSet(def.Id) ? loc.Get("mode.status.on") : loc.Get("mode.status.off");
                this.LogDebug(loc.Get("mode.dump.line", def.Id, status, def.Name));
            }
        }

    }

    public class ModeDefinition
    {
        public int Id { get; }
        public string Name { get; }
        public bool DefaultSet { get; }

        public ModeDefinition(int id, string name, bool defaultSet)
        {
            Id = id;
            Name = name;
            DefaultSet = defaultSet;
        }
    }
}