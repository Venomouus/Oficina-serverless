using Oficina.Autenticacao;
using Xunit;

namespace Oficina.Serverless.Tests;

public class CpfTests
{
    [Theory]
    [InlineData("52998224725", "52998224725")]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData(" 529.982.247-25 ", "52998224725")]
    [InlineData("01234567890", "01234567890")]
    public void AceitaCpfComDigitosValidos(string input, string expected)
    {
        Assert.True(Cpf.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00000000000")]
    [InlineData("11111111111")]
    [InlineData("52998224724")]
    [InlineData("52998224735")]
    [InlineData("5299822472")]
    [InlineData("529982247250")]
    [InlineData("04.252.011/0001-10")]
    [InlineData("abc52998224725")]
    [InlineData("529-982-247.25")]
    [InlineData("５２９９８２２４７２５")]
    public void RecusaEntradaInvalidaSemNormalizacaoParcial(string? input)
    {
        Assert.False(Cpf.TryNormalize(input, out var normalized));
        Assert.Empty(normalized);
    }
}
