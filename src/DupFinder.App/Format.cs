namespace DupFinder.App;

public static class Format
{
    public static string Bytes(long bytes)
    {
        var units = Strings.ByteUnits.Split('|');
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} {units[0]}" : $"{value:0.#} {units[unit]}";
    }

    /// <summary>
    /// Число со словом в нужной форме: <c>Count(5, Strings.FileForms)</c> → «5 files» / «5 файлов».
    /// Формы в ресурсе разделены «|»: две — правило английского (1 / прочие),
    /// три — правило русского (1, 21… / 2–4, 22–24… / остальные).
    /// </summary>
    public static string Count(long n, string forms) => $"{n:N0} {Pick(n, forms.Split('|'))}";

    private static string Pick(long n, string[] forms)
    {
        if (forms.Length != 3)
            return n == 1 ? forms[0] : forms[^1];
        var mod100 = n % 100;
        var mod10 = n % 10;
        if (mod100 is >= 11 and <= 14) return forms[2];
        return mod10 switch { 1 => forms[0], >= 2 and <= 4 => forms[1], _ => forms[2] };
    }
}
