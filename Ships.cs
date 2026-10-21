namespace HaulingTracker;

/// <summary>
/// A ship that can haul, from the Star Citizen Wiki API (vehicles, flight-ready ships with 30 SCU of cargo or more): the name and how many SCU of cargo it holds.
/// </summary>
public sealed record Ship(string Name, int Scu)
{
    public string Label => Name + " (" + Scu + " SCU)";
}

/// <summary>The ships of the game data, for the ship each worker flies (the Party table).</summary>
public static class Ships
{
    public static readonly Ship[] All =
    {
        new("400i", 42),
        new("600i", 44),
        new("600i 2951 BIS", 44),
        new("890 Jump", 388),
        new("A2 Hercules Starlifter", 216),
        new("Apollo Medivac", 32),
        new("Apollo Triage", 32),
        new("Asgard", 180),
        new("C1 Spirit", 64),
        new("C2 Hercules Starlifter", 696),
        new("Carrack", 456),
        new("Carrack Expedition", 456),
        new("Caterpillar", 576),
        new("Caterpillar Pirate", 576),
        new("Constellation Mk IV Andromeda", 96),
        new("Constellation Mk IV Aquila", 96),
        new("Constellation Mk IV Phoenix", 80),
        new("Constellation Mk IV Phoenix Emerald", 80),
        new("Constellation Mk IV Taurus", 174),
        new("Corsair", 72),
        new("Cutlass Black", 46),
        new("Golem OX", 64),
        new("Hammerhead", 40),
        new("Hermes", 288),
        new("Idris-M", 1326),
        new("Idris-P", 1374),
        new("Ironclad", 2200),
        new("Ironclad Assault", 1440),
        new("M2 Hercules Starlifter", 522),
        new("Mercury Star Runner", 114),
        new("MISC Freelancer", 66),
        new("MISC Freelancer DUR", 36),
        new("MISC Freelancer MAX", 120),
        new("MISC Freelancer MIS", 36),
        new("MISC Hull A", 64),
        new("MISC Hull B", 512),
        new("MISC Hull C", 4608),
        new("MISC Starfarer", 291),
        new("MISC Starfarer Gemini", 291),
        new("MISC Starlancer MAX", 224),
        new("MISC Starlancer TAC", 96),
        new("MOLE", 32),
        new("MOTH", 224),
        new("Perseus", 96),
        new("Polaris", 576),
        new("Prowler Utility", 32),
        new("RAFT", 192),
        new("Railen", 640),
        new("Reclaimer", 420),
        new("Reclaimer 2949 Best In Show Edition", 420),
        new("Shiv", 32),
        new("Tiburon", 64),
        new("Tyilui", 96),
        new("Valkyrie", 90),
        new("Zeus Mk II CL", 128),
        new("Zeus Mk II ES", 32),
    };

    /// <summary>The ship with this label (or name), or null.</summary>
    public static Ship? Find(string? labelOrName) =>
        string.IsNullOrWhiteSpace(labelOrName) ? null : All.FirstOrDefault(s => string.Equals(s.Label, labelOrName, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Name, labelOrName, StringComparison.OrdinalIgnoreCase));
}
