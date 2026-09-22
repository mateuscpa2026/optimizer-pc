using OptimizerPC.Core.Abstractions;
using OptimizerPC.Services.Restore;
using Xunit;

namespace OptimizerPC.Tests.Restore;

public sealed class RestorePayloadTests
{
    [Fact]
    public void ForRegistryInt_PreservaValorExistenteEAusente()
    {
        var withValue = RestorePayload.ReadRegistryValue(RestorePayload.ForRegistryInt("HKCU", "Software\\OptimizerPC", "Enabled", 1));
        var withoutValue = RestorePayload.ReadRegistryValue(RestorePayload.ForRegistryInt("HKCU", "Software\\OptimizerPC", "Enabled", null));

        Assert.NotNull(withValue);
        Assert.True(withValue.HadValue);
        Assert.Equal(1, withValue.PreviousInt);
        Assert.Null(withValue.PreviousString);
        Assert.NotNull(withoutValue);
        Assert.False(withoutValue.HadValue);
        Assert.Null(withoutValue.PreviousInt);
        Assert.Null(withoutValue.PreviousString);
    }

    [Fact]
    public void ForServiceStartMode_PreservaAmbosOsValoresDoServico()
    {
        var payload = RestorePayload.ReadServiceStartMode(
            RestorePayload.ForServiceStartMode("HKLM", "System\\CurrentControlSet\\Services\\OptimizerPC", 2, null));

        Assert.NotNull(payload);
        Assert.True(payload.HadStart);
        Assert.Equal(2, payload.PreviousStart);
        Assert.False(payload.HadDelayed);
        Assert.Equal(0, payload.PreviousDelayed);
    }

    [Fact]
    public void ForPowerPlan_PreservaGuidNoFormatoCanonico()
    {
        var scheme = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var payload = RestorePayload.ReadPowerPlan(RestorePayload.ForPowerPlan(scheme, "Equilibrado"));

        Assert.NotNull(payload);
        Assert.Equal(scheme.ToString("D"), payload.SchemeGuid);
        Assert.Equal("Equilibrado", payload.SchemeName);
    }

    [Fact]
    public void ForRegistryBackup_RestauraTodosOsFormatosSuportados()
    {
        var previous = new RegistryValueData(
            "CurrentUser",
            "Software\\OptimizerPC",
            "Items",
            "MultiString",
            null,
            null,
            new[] { "one", "two" });
        var payload = RestorePayload.ReadRegistryBackup(
            RestorePayload.ForRegistryBackup(RegistryHiveKind.CurrentUser, "Software\\OptimizerPC", "Items", previous));

        Assert.NotNull(payload);
        Assert.True(payload.HadValue);
        Assert.Equal("CurrentUser", payload.Hive);
        Assert.Equal("MultiString", payload.Kind);
        var restored = payload.ToValueData();
        Assert.NotNull(restored);
        Assert.Equal(previous.Hive, restored.Hive);
        Assert.Equal(previous.SubKey, restored.SubKey);
        Assert.Equal(previous.Name, restored.Name);
        Assert.Equal(previous.Kind, restored.Kind);
        Assert.Equal(previous.StringValue, restored.StringValue);
        Assert.Equal(previous.IntValue, restored.IntValue);
        Assert.Equal(previous.MultiStringValue, restored.MultiStringValue);
    }

    [Fact]
    public void ForToggle_CodificaOValorAnteriorSemExecutarNada()
    {
        var payload = RestorePayload.ReadToggle(RestorePayload.ForToggle("HKCU", "Software\\OptimizerPC", "Startup", new byte[] { 0, 10, 255 }, true));

        Assert.NotNull(payload);
        Assert.True(payload.HadValue);
        Assert.Equal("AAr/", payload.Previous);
        Assert.True(payload.Applied);
    }

    [Fact]
    public void ForVisualEffects_PreservaCadaValorLido()
    {
        var payload = RestorePayload.ReadVisualEffects(RestorePayload.ForVisualEffects(false, new[]
        {
            new VisualEffectValue("Control Panel\\Desktop", "DragFullWindows", true, "String", "1", null),
            new VisualEffectValue("Control Panel\\Desktop", "MenuShowDelay", false, null, null, null)
        }));

        Assert.NotNull(payload);
        Assert.False(payload.OptimizedForPerformance);
        Assert.Equal(2, payload.Values.Count);
        Assert.True(payload.Values[0].HadValue);
        Assert.False(payload.Values[1].HadValue);
    }

    [Theory]
    [InlineData("{ nao e json }")]
    [InlineData("[]")]
    public void LeituraInvalidaOuFormatoDiferenteNaoLancaExcecao(string json)
    {
        Assert.Null(RestorePayload.ReadRegistryValue(json));
        Assert.False(RestorePayload.HasProperty(json, "hive"));
    }

    [Fact]
    public void HasProperty_IdentificaSomentePropriedadesDoObjetoSerializado()
    {
        var payload = RestorePayload.ForRegistryInt("HKCU", "Software\\OptimizerPC", "Enabled", 1);

        Assert.True(RestorePayload.HasProperty(payload, "hive"));
        Assert.True(RestorePayload.HasProperty(payload, "previousInt"));
        Assert.False(RestorePayload.HasProperty(payload, "Hive"));
        Assert.False(RestorePayload.HasProperty(payload, "ausente"));
    }
}
