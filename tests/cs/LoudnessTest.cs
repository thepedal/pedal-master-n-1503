// Feeds block energies (one per line) into LoudnessMeter; prints I and LRA.
using System; using System.IO; using WDE.PedalMasterN;
static class LoudnessTest {
    static void Main(string[] a) {
        var m = new LoudnessMeter();
        foreach (var l in File.ReadAllLines(a[0])) m.Add(double.Parse(l, System.Globalization.CultureInfo.InvariantCulture));
        m.UpdateRange();
        Console.WriteLine("{0:F2} {1:F2}", m.Integrated, m.Range);
    }
}
