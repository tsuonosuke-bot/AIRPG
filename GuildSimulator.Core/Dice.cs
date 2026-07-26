using System.Globalization;

namespace GuildSimulator.Core;

// "XdY" / "XdY+Z" / "XdY-Z" 形式のダイス表記（例: 1d6, 2d4+1）。武器のダメージダイスなどに使う。
public readonly struct Dice
{
    public readonly int count;
    public readonly int sides;
    public readonly int modifier;

    public Dice(int count, int sides, int modifier = 0)
    {
        this.count = Math.Max(1, count);
        this.sides = Math.Max(1, sides);
        this.modifier = modifier;
    }

    public int Min => count + modifier;
    public int Max => count * sides + modifier;

    public int Roll()
    {
        int total = modifier;
        for (int i = 0; i < count; i++)
            total += GameRandom.Range(1, sides + 1);
        return total;
    }

    public static int Roll(string? notation) => Parse(notation).Roll();

    public static Dice Parse(string? notation)
    {
        if (string.IsNullOrWhiteSpace(notation)) return new Dice(1, 4);

        var s = notation.Trim().ToLowerInvariant();
        int dIndex = s.IndexOf('d');
        if (dIndex < 0) return new Dice(1, 4);

        int count = int.TryParse(s[..dIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 1;

        string rest = s[(dIndex + 1)..];
        int signIndex = -1;
        for (int i = 1; i < rest.Length; i++)
        {
            if (rest[i] == '+' || rest[i] == '-') { signIndex = i; break; }
        }

        int sides;
        int modifier = 0;
        if (signIndex >= 0)
        {
            sides = int.TryParse(rest[..signIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sd) ? sd : 4;
            modifier = int.TryParse(rest[signIndex..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : 0;
        }
        else
        {
            sides = int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sd) ? sd : 4;
        }
        return new Dice(count, sides, modifier);
    }

    public override string ToString() => modifier switch
    {
        > 0 => $"{count}d{sides}+{modifier}",
        < 0 => $"{count}d{sides}{modifier}",
        _ => $"{count}d{sides}",
    };
}
