/// <summary>Runtime synthetic Saudi IBANs for tests.
/// Release source may only embed the two allow-listed samples in <c>scripts/release_source.py</c>.</summary>
static class SyntheticIban
{
    /// <summary>Valid IBAN from a 2-digit bank clearing code and an 18-digit account body.</summary>
    public static string FromAccount(string bankCode2, string account18)
    {
        if (bankCode2.Length != 2 || !bankCode2.All(char.IsAsciiDigit))
            throw new ArgumentException("bankCode2 must be 2 digits", nameof(bankCode2));
        if (account18.Length != 18 || !account18.All(char.IsAsciiDigit))
            throw new ArgumentException("account18 must be 18 digits", nameof(account18));
        string body = bankCode2 + account18;
        int rem = 0;
        foreach (char ch in body + "SA00")
            foreach (char d in char.IsAsciiLetter(ch) ? ((int)ch - 'A' + 10).ToString() : ch.ToString())
                rem = (rem * 10 + (d - '0')) % 97;
        return $"SA{98 - rem:D2}{body}";
    }

    /// <summary>Valid IBAN from a synthetic account sequence (padded to 18 digits).</summary>
    public static string FromSequence(int sequence, string bankCode2 = "80") =>
        FromAccount(bankCode2, sequence.ToString().PadLeft(18, '0'));
}
