using System.Numerics;
using System.Text.RegularExpressions;

namespace LanaSoft.Brain.Mcp.Safety;

/// <summary>
/// Refuse l'écriture d'un secret dans la mémoire. Règle 7 du brief : jamais de mot de passe, clé,
/// jeton, IBAN ou numéro de carte. Les détections vérifiables (IBAN, carte) sont confirmées par leur
/// clé de contrôle, pour ne pas bloquer un simple numéro.
/// </summary>
public static partial class SecretDetector
{
    /// <summary>Renvoie la nature du premier secret trouvé, ou null.</summary>
    public static string? Find(string text)
    {
        if (PrivateKey().IsMatch(text)) return "clé privée";
        if (Jwt().IsMatch(text)) return "jeton JWT";
        if (KnownApiKey().IsMatch(text)) return "clé d'API";
        if (Assignment().IsMatch(text)) return "mot de passe ou clé en clair";
        if (Iban().Matches(text).Any(m => IsValidIban(m.Value))) return "IBAN";
        if (CardNumber().Matches(text).Any(m => PassesLuhn(m.Value))) return "numéro de carte bancaire";
        return null;
    }

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")]
    private static partial Regex Jwt();

    // Anthropic, Mem0, OpenAI, Google, GitHub, GitLab, Slack, AWS, Azure DevOps (PAT 52 car.).
    [GeneratedRegex(@"\b(sk-ant-[A-Za-z0-9_-]{20,}|m0sk_[A-Za-z0-9_-]{16,}|sk-[A-Za-z0-9]{32,}|AIza[0-9A-Za-z_-]{35}|gh[pousr]_[A-Za-z0-9]{36,}|glpat-[A-Za-z0-9_-]{20,}|xox[abprs]-[A-Za-z0-9-]{10,}|AKIA[0-9A-Z]{16}|[a-z0-9]{52})\b")]
    private static partial Regex KnownApiKey();

    // « password: xxx », « mot de passe = xxx », « api_key=xxx », « secret : xxx »...
    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|mot de passe|mdp|api[_ -]?key|secret|token|jeton)\s*[:=]\s*\S{6,}")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]{4}){2,7}(?:[ ]?[A-Z0-9]{1,4})?\b")]
    private static partial Regex Iban();

    [GeneratedRegex(@"\b(?:\d[ -]?){13,19}\b")]
    private static partial Regex CardNumber();

    internal static bool IsValidIban(string candidate)
    {
        var iban = candidate.Replace(" ", "", StringComparison.Ordinal);
        if (iban.Length is < 15 or > 34) return false;
        var rearranged = iban[4..] + iban[..4];
        var digits = string.Concat(rearranged.Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));
        return BigInteger.Parse(digits) % 97 == 1;
    }

    internal static bool PassesLuhn(string candidate)
    {
        var digits = candidate.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19) return false;
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i];
            if (i % 2 == 1) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return sum % 10 == 0;
    }
}
