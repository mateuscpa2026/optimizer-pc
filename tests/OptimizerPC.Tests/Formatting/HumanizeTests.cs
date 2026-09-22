using System.Globalization;
using OptimizerPC.Core;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Tests.Fakes;
using Xunit;

namespace OptimizerPC.Tests.Formatting;

public sealed class HumanizeTests
{
    private readonly FakeLocalizer _localizer = new();

    [Theory]
    [InlineData(-1, "0 B")]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    public void Bytes_FormataLimitesDeUnidade(long bytes, string expected)
    {
        Assert.Equal(expected, Humanize.Bytes(bytes, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Bytes_UsaACulturaSolicitadaParaCasasDecimais()
    {
        Assert.Equal("1,5 MB", Humanize.Bytes(1536L * 1024, CultureInfo.GetCultureInfo("pt-BR")));
        Assert.Equal("1.5 MB", Humanize.Bytes(1536L * 1024, CultureInfo.GetCultureInfo("en-US")));
    }

    [Theory]
    [InlineData(12.8, 0, "13%")]
    [InlineData(12.8, 1, "12.8%")]
    [InlineData(-4.2, 0, "-4%")]
    public void Percent_FormataComPrecisaoSolicitada(double value, int decimals, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(expected, Humanize.Percent(value, decimals));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Speed_NaoPermiteValorNegativo()
    {
        Assert.Equal("0 B/s", Humanize.Speed(-100));
        Assert.Equal("2 KB/s", Humanize.Speed(2048));
    }

    [Theory]
    [InlineData(0.5, "menos de um segundo")]
    [InlineData(1, "1 s")]
    [InlineData(59, "59 s")]
    [InlineData(60, "1 min 0 s")]
    [InlineData(3661, "1 h 1 min")]
    [InlineData(90061, "1 d 1 h")]
    public void Duration_FormataCadaFaixaDeTempo(double seconds, string expected)
    {
        Assert.Equal(expected, Humanize.Duration(TimeSpan.FromSeconds(seconds), "menos de um segundo"));
    }

    [Fact]
    public void Uptime_IncluiMinutosQuandoDuraPeloMenosUmDia()
    {
        Assert.Equal("2 d 3 h 4 min", Humanize.Uptime(new TimeSpan(2, 3, 4, 0), "menos de um segundo"));
        Assert.Equal("2 h 3 min", Humanize.Uptime(new TimeSpan(2, 3, 0), "menos de um segundo"));
    }

    [Theory]
    [InlineData(StorageMediaType.Ssd, "SSD")]
    [InlineData(StorageMediaType.Hdd, "HDD")]
    [InlineData(StorageMediaType.ScmOrNvme, "NVMe")]
    public void MediaType_ConservaSiglasDeMercado(StorageMediaType type, string expected)
    {
        Assert.Equal(expected, Humanize.MediaType(type));
    }

    [Fact]
    public void MediaType_EBusType_TraduzemSomenteOsValoresNecessarios()
    {
        Assert.Equal("Common.NotIdentified", Humanize.MediaType(StorageMediaType.Unknown, _localizer));
        Assert.Equal("Storage.Bus.SdCard", Humanize.BusType(StorageBusType.Sd, _localizer));
        Assert.Equal("Storage.Bus.MemoryCard", Humanize.BusType(StorageBusType.Mmc, _localizer));
        Assert.Equal("SATA", Humanize.BusType(StorageBusType.Sata, _localizer));
    }

    [Fact]
    public void CoresEIdioma_UsamOContratoDeLocalizacao()
    {
        Assert.Equal("Common.Cpu.CoresThreads(4, 8)", Humanize.Cores(4, 8, _localizer));
        Assert.Equal("Common.Cpu.Cores(8)", Humanize.Cores(0, 8, _localizer));
        Assert.Equal("pt-BR", Humanize.LanguageCode(AppLanguage.PtBr));
        Assert.Equal("en-US", Humanize.LanguageCode(AppLanguage.EnUs));
        Assert.Equal("es", Humanize.LanguageCode(AppLanguage.Es));
    }
}
