using System.Globalization;

namespace HaulingTracker;

/// <summary>
/// A quantum drive, from the Star Citizen Wiki API (items, type QuantumDrive, "standard jump"). All speeds are in metres per second and
/// the accelerations in metres per second squared. Size 1 to 4, grade A to D.
/// </summary>
public sealed record QtDrive(string Name, string Maker, int Size, string Grade, double Speed, double Accel1, double Accel2, double Engage)
{
    public string Key => Name + " S" + Size;
    public string Label => Key + " (" + (Speed / 1e6).ToString("0", CultureInfo.InvariantCulture) + ")";

    /// <summary>
    /// Seconds for one jump of this many Gm (the time it shows for a distance in the star map, without the spool time).
    /// The drive speeds up with the stage 1 acceleration until it reaches the switch speed, then with the stage 2 acceleration up to the
    /// top speed, flies at the top speed and slows down the same way. A jump that is too short to reach the top speed
    /// speeds up and slows down halfway each. Checked against SPViewer with a TS-2 (S3, 12 jumps from 1 to 150 Gm, within 2 seconds),
    /// an XL-1 (S2) and a VK-00 (S1) (1, 15 and 100 Gm, within 1 second). The size 4 drives are only approximate (an Allegro is up to 10 seconds off).
    /// </summary>
    public double Seconds(double gm)
    {
        var dist = gm * 1e9;
        if (dist <= 0) return 0;
        var (rampTime, rampDist) = Ramp(Speed);
        if (2 * rampDist <= dist) return 2 * rampTime + (dist - 2 * rampDist) / Speed;
        // too short to reach the top speed: find the speed where the way up and the way down together use the whole distance
        double lo = 0, hi = Speed;
        for (var i = 0; i < 80; i++)
        {
            var mid = (lo + hi) / 2;
            if (2 * Ramp(mid).Dist > dist) hi = mid;
            else lo = mid;
        }
        return 2 * Ramp(lo).Time;
    }

    /// <summary>
    /// The speed where the drive goes from the stage 1 to the stage 2 acceleration. For size 2 and 3 it is the engage speed of the data.
    /// The data has 1500 m/s for size 1, but SPViewer and the other size 1 numbers fit about 48 Mm/s; size 4 fits about 90 Mm/s (roughly).
    /// </summary>
    public double SwitchSpeed => Size switch { 1 => 48e6, 4 => 90e6, _ => Engage };

    /// <summary>Time and distance of speeding up from standstill to a speed.</summary>
    (double Time, double Dist) Ramp(double to)
    {
        var s1 = Math.Min(to, SwitchSpeed);
        var t = s1 / Accel1;
        var d = s1 * s1 / (2 * Accel1);
        if (to > s1)
        {
            var t2 = (to - s1) / Accel2;
            t += t2;
            d += (s1 + to) / 2 * t2;
        }
        return (t, d);
    }
}

/// <summary>Every quantum drive in the game data, for the drive each worker has in his ship (the Party table) and the flying time estimates.</summary>
public static class QuantumDrives
{
    /// <summary>Used for a worker without a drive set, so the estimate still has something to work with.</summary>
    public static QtDrive Assumed => Find("Odyssey S2")!;

    public static readonly QtDrive[] All =
    {
        new("Atlas", "Roberts Space Industries", 1, "A", 259054500, 9990736, 26053770, 1500),
        new("Beacon", "Wei-Tek", 1, "C", 161410900, 9393000, 22045500, 1500),
        new("Burst", "ArcCorp", 1, "B", 230160000, 9393000, 23270250, 1500),
        new("Colossus", "Juno Starwerk", 1, "B", 241120000, 11271600, 26944500, 1500),
        new("Drift", "RAMP Corporation", 1, "C", 167389100, 7172836, 17814550, 1500),
        new("Eos", "Roberts Space Industries", 1, "C", 199272700, 7685182, 20041360, 1500),
        new("Expedition", "Tarsus", 1, "C", 189309100, 8112136, 21154770, 1500),
        new("Flood", "ArcCorp", 1, "D", 188312700, 7685182, 19039300, 1500),
        new("FoxFire", "Ace Astrogation", 1, "B", 263040000, 10848920, 28291720, 1500),
        new("Goliath", "Juno Starwerk", 1, "C", 219200000, 10246910, 24495000, 1500),
        new("Hyperion", "Roberts Space Industries", 1, "B", 219200000, 8453700, 22045500, 1500),
        new("LightFire", "Ace Astrogation", 1, "C", 239127300, 9862650, 25719750, 1500),
        new("Rush", "ArcCorp", 1, "C", 209236400, 8539091, 21154770, 1500),
        new("Siren", "Wei-Tek", 1, "B", 177552000, 10332300, 24250050, 1500),
        new("Spectre", "RAMP Corporation", 1, "A", 217605800, 9324687, 23158910, 1500),
        new("VK-00", "Wei-Tek", 1, "A", 209834200, 12210900, 28659150, 1500),
        new("Voyage", "Tarsus", 1, "B", 208240000, 8923350, 23270250, 1500),
        new("Vulcan", "Juno Starwerk", 1, "D", 197280000, 9222218, 22045500, 1500),
        new("Wayfare", "Tarsus", 1, "D", 170378200, 7300923, 19039300, 1500),
        new("Zephyr", "RAMP Corporation", 1, "B", 184128000, 7890120, 19596000, 1500),
        new("Aither", "Roberts Space Industries", 2, "B", 275942800, 5945657, 8994857, 84000000),
        new("Bolon", "Juno Starwerk", 2, "C", 275942800, 7206857, 9994285, 84000000),
        new("Bolt", "RAMP Corporation", 2, "B", 231792000, 5549280, 7995428, 84000000),
        new("Cascade", "ArcCorp", 2, "D", 237060000, 5405143, 7768285, 84000000),
        new("Crossfield", "Wei-Tek", 2, "C", 203194300, 6606285, 8994857, 84000000),
        new("Flash", "ArcCorp", 2, "B", 289740000, 6606285, 9494571, 84000000),
        new("Hemera", "Roberts Space Industries", 2, "A", 326114300, 7026685, 10630280, 84000000),
        new("Huracan", "Juno Starwerk", 2, "B", 303537200, 7927542, 10993710, 84000000),
        new("Khaos", "Roberts Space Industries", 2, "C", 250857100, 5405143, 8177143, 84000000),
        new("Nova", "RAMP Corporation", 2, "C", 210720000, 5044800, 7268571, 84000000),
        new("Odyssey", "Tarsus", 2, "C", 238314300, 5705428, 8631428, 84000000),
        new("Quest", "Tarsus", 2, "D", 214482900, 5134885, 7768285, 84000000),
        new("Sojourn", "Tarsus", 2, "B", 262145700, 6275971, 9494571, 84000000),
        new("SparkFire", "Ace Astrogation", 2, "C", 301028600, 6936600, 10494000, 84000000),
        new("Spicule", "RAMP Corporation", 2, "A", 273936000, 6558240, 9449143, 84000000),
        new("SunFire", "Ace Astrogation", 2, "B", 331131400, 7630260, 11543400, 84000000),
        new("Torrent", "ArcCorp", 2, "C", 263400000, 6005714, 8631428, 84000000),
        new("XL-1", "Wei-Tek", 2, "A", 264152600, 8588171, 11693310, 84000000),
        new("Yaluk", "Juno Starwerk", 2, "D", 248348600, 6486171, 8994857, 84000000),
        new("Yeager", "Wei-Tek", 2, "B", 223513700, 7266914, 9894343, 84000000),
        new("Agni", "Juno Starwerk", 3, "B", 585715600, 4685175, 5969838, 190000000),
        new("Balandin", "Wei-Tek", 3, "B", 431299700, 4294744, 5372854, 190000000),
        new("Drifter", "Tarsus", 3, "D", 413873400, 3034716, 4218356, 190000000),
        new("Echo", "ArcCorp", 3, "D", 457439100, 3194438, 4218356, 190000000),
        new("Erebos", "Roberts Space Industries", 3, "A", 629281300, 4152769, 5772488, 190000000),
        new("Fissure", "ArcCorp", 3, "C", 508265600, 3549375, 4687063, 190000000),
        new("Impulse", "ArcCorp", 3, "B", 559092200, 3904313, 5155769, 190000000),
        new("Kama", "Juno Starwerk", 3, "C", 532468700, 4259250, 5427125, 190000000),
        new("Metis", "Roberts Space Industries", 3, "C", 484062500, 3194438, 4440375, 190000000),
        new("Pontes", "Wei-Tek", 3, "C", 392090600, 3904313, 4884413, 190000000),
        new("Ranger", "Tarsus", 3, "B", 505845300, 3709097, 5155769, 190000000),
        new("TS-2", "Wei-Tek", 3, "A", 509717800, 5075606, 6349736, 190000000),
        new("Tyche", "Roberts Space Industries", 3, "B", 532468700, 3513881, 4884413, 190000000),
        new("Vesta", "Juno Starwerk", 3, "D", 479221900, 3833325, 4884413, 190000000),
        new("Wanderer", "Tarsus", 3, "C", 459859400, 3371906, 4687063, 190000000),
        new("Allegro", "Unknown", 4, "A", 1262910000, 2404215, 21945000, 200000000),
        new("Mauler", "Unknown", 4, "A", 1142633000, 2610290, 21840500, 200000000),
    };

    public static QtDrive? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase) || string.Equals(d.Label, key, StringComparison.OrdinalIgnoreCase));
}
