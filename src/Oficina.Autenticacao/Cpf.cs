namespace Oficina.Autenticacao;

public static class Cpf
{
    // Aceita somente 11 digitos ASCII ou a mascara 000.000.000-00.
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var input = value.Trim();
        if (input.Length == 14)
        {
            if (input[3] != '.' || input[7] != '.' || input[11] != '-')
                return false;
            input = input[..3] + input.Substring(4, 3) + input.Substring(8, 3) + input[12..];
        }

        if (input.Length != 11 || input.Any(c => c < '0' || c > '9'))
            return false;
        if (input.All(c => c == input[0]))
            return false;
        if (Digit(input.AsSpan(0, 9)) != input[9] - '0'
            || Digit(input.AsSpan(0, 10)) != input[10] - '0')
            return false;

        normalized = input;
        return true;
    }

    private static int Digit(ReadOnlySpan<char> digits)
    {
        var sum = 0;
        for (var index = 0; index < digits.Length; index++)
            sum += (digits[index] - '0') * (digits.Length + 1 - index);
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
