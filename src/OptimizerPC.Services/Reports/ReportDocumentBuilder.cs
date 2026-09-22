using System.Globalization;
using System.Reflection;
using OptimizerPC.Core;
using OptimizerPC.Core.Abstractions;
using OptimizerPC.Core.Formatting;
using OptimizerPC.Core.Models;

namespace OptimizerPC.Services.Reports;

/// <summary>
/// Monta o conteudo do relatorio a partir das fontes locais. Toda a leitura acontece
/// aqui: os renderizadores recebem apenas texto final. O numero de linhas e limitado
/// para que um relatorio gerado anos depois nao cresca sem controle.
/// </summary>
public sealed class ReportDocumentBuilder
{
    private const int MaxHistoryRows = 120;

    private const int MaxDiagnosisRows = 200;

    private readonly IDashboardService _dashboard;
    private readonly IDiagnosticService _diagnostics;
    private readonly IHistoryService _history;
    private readonly IRestoreService _restore;
    private readonly ILocalizer _localizer;
    private readonly IAppLogger _logger;

    public ReportDocumentBuilder(
        IDashboardService dashboard,
        IDiagnosticService diagnostics,
        IHistoryService history,
        IRestoreService restore,
        ILocalizer localizer,
        IAppLogger logger)
    {
        _dashboard = dashboard;
        _diagnostics = diagnostics;
        _history = history;
        _restore = restore;
        _localizer = localizer;
        _logger = logger;
    }

    /// <summary>
    /// Monta o documento do relatorio. O retorno e interno: apenas os renderizadores
    /// desta biblioteca consomem o documento; a interface publica devolve o arquivo.
    /// </summary>
    internal async Task<ReportDocument> BuildAsync(ReportRequest request, CancellationToken cancellationToken)
    {
        var summary = await _dashboard
            .BuildAsync(request.IncludeRecommendations, null, cancellationToken)
            .ConfigureAwait(false);

        var sections = new List<ReportSection>
        {
            BuildSummarySection(await CountRestorableAsync(cancellationToken).ConfigureAwait(false), summary)
        };

        if (request.IncludeSystemInfo)
        {
            sections.Add(BuildSystemSection(summary));
        }

        sections.Add(BuildHealthSection(summary));

        if (request.IncludeCleanupSummary)
        {
            sections.Add(BuildCleanupSection(summary));
        }

        if (request.IncludeDiagnosis)
        {
            sections.Add(await BuildDiagnosisSectionAsync(cancellationToken).ConfigureAwait(false));
        }

        if (request.IncludeRecommendations && summary.Recommendations.Count > 0)
        {
            sections.Add(BuildRecommendationSection(summary));
        }

        if (request.IncludeHistory)
        {
            sections.Add(await BuildHistorySectionAsync(cancellationToken).ConfigureAwait(false));
        }

        return new ReportDocument
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? _localizer["Report.Title.Default"] : request.Title!,
            GeneratedAtUtc = DateTime.UtcNow,
            GeneratedAtLabel = _localizer["Report.Field.GeneratedAt"],
            LanguageCode = Humanize.LanguageCode(_localizer.Current),
            Introduction = _localizer["Report.Introduction"],
            Footer = _localizer["Report.Footer"],
            Summary = _localizer.Format(
                "Report.Summary.Index",
                summary.Score.TotalText,
                summary.ProblemCount,
                summary.RecommendationCount),
            Sections = sections
        };
    }

    private ReportSection BuildSummarySection(int restorableCount, DashboardSummary summary)
    {
        var snapshot = summary.System;
        var fields = new List<ReportField>
        {
            Field("Report.Field.Computer", snapshot?.Os.ComputerName),
            Field("Report.Field.User", snapshot?.Os.UserName),
            Field("Report.Field.Windows", BuildWindowsText(snapshot)),
            Field("Report.Field.AppVersion", ReadAppVersion()),
            Field("Report.Field.Elevated", YesNo(snapshot?.Os.IsElevated ?? false)),
            Field("Report.Field.Uptime", snapshot is null ? null : Humanize.Uptime(snapshot.Os.Uptime, _localizer["Common.Duration.SubSecond"])),
            Field("Report.Field.Score", summary.Score.TotalText + " (" + _localizer["Score.Category." + summary.Score.Category] + ")"),
            Field("Report.Field.RestoreAvailable", restorableCount.ToString(CultureInfo.CurrentCulture))
        };

        return new ReportSection
        {
            Title = _localizer["Report.Section.Summary"],
            Fields = fields,
            Note = _localizer["Report.Note.Local"]
        };
    }

    /// <summary>
    /// Quantidade de alteracoes reversiveis registradas pelo aplicativo. Uma falha de
    /// leitura nao invalida o relatorio: o pior caso e a contagem sair zerada.
    /// </summary>
    private async Task<int> CountRestorableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var records = await _restore.GetRecordsAsync(cancellationToken).ConfigureAwait(false);
            return records.Count(record => record.CanRestore);
        }
        catch (Exception exception)
        {
            _logger.Warning("Report", "Nao foi possivel ler os registros de restauracao para o relatorio.", exception);
            return 0;
        }
    }

    private ReportSection BuildSystemSection(DashboardSummary summary)
    {
        var snapshot = summary.System;
        if (snapshot is null)
        {
            return new ReportSection
            {
                Title = _localizer["Report.Section.System"],
                Note = _localizer["Report.Note.SystemUnavailable"]
            };
        }

        var fields = new List<ReportField>
        {
            Field("Report.Field.CpuName", string.IsNullOrWhiteSpace(snapshot.Cpu.Name) ? _localizer["Common.Cpu.Unknown"] : snapshot.Cpu.Name),
            Field("Report.Field.CpuCores", snapshot.Cpu.PhysicalCores + snapshot.Cpu.LogicalProcessors > 0
                ? Humanize.Cores(snapshot.Cpu.PhysicalCores, snapshot.Cpu.LogicalProcessors, _localizer)
                : null),
            Field("Report.Field.CpuClock", snapshot.Cpu.MaxClockMhz > 0 ? snapshot.Cpu.MaxClockMhz + " MHz" : null),
            Field("Report.Field.Motherboard", Join(snapshot.Motherboard.Manufacturer, snapshot.Motherboard.Product)),
            Field("Report.Field.Bios", Join(snapshot.Motherboard.BiosVendor, snapshot.Motherboard.BiosVersion)),
            Field("Report.Field.MemoryTotal", Humanize.Bytes(snapshot.Memory.TotalBytes)),
            Field("Report.Field.MemoryUsed", Humanize.Bytes(snapshot.Memory.UsedBytes) + " (" + Humanize.Percent(snapshot.Memory.UsedPercent) + ")"),
            Field("Report.Field.MemoryAvailable", Humanize.Bytes(snapshot.Memory.AvailableBytes))
        };

        var tables = new List<ReportTable>
        {
            new()
            {
                Title = _localizer["Report.Table.Modules"],
                Headers = new[] { _localizer["Report.Column.Slot"], _localizer["Report.Column.Capacity"], _localizer["Report.Column.Speed"], _localizer["Report.Column.Type"] },
                Rows = snapshot.Memory.Modules
                    .Select(module => (IReadOnlyList<string>)new[]
                    {
                        module.Slot,
                        Humanize.Bytes(module.CapacityBytes),
                        module.SpeedMhz > 0 ? module.SpeedMhz + " MHz" : "—",
                        Join(module.MemoryType, module.FormFactor)
                    })
                    .ToList(),
                EmptyText = _localizer["Report.Empty.None"]
            },
            new()
            {
                Title = _localizer["Report.Table.Gpus"],
                Headers = new[] { _localizer["Report.Column.Name"], _localizer["Report.Column.Vendor"], _localizer["Report.Column.Driver"], _localizer["Report.Column.Memory"], _localizer["Report.Column.Resolution"] },
                Rows = snapshot.Gpus
                    .Select(gpu => (IReadOnlyList<string>)new[]
                    {
                        gpu.Name,
                        gpu.Vendor,
                        gpu.DriverVersion,
                        gpu.AdapterMemoryBytes > 0 ? Humanize.Bytes(gpu.AdapterMemoryBytes) : "—",
                        gpu.Resolution
                    })
                    .ToList(),
                EmptyText = _localizer["Report.Empty.None"]
            },
            new()
            {
                Title = _localizer["Report.Table.Devices"],
                Headers = new[] { _localizer["Report.Column.Model"], _localizer["Report.Column.Media"], _localizer["Report.Column.Bus"], _localizer["Report.Column.Capacity"], _localizer["Report.Column.Health"] },
                Rows = snapshot.StorageDevices
                    .Select(device => (IReadOnlyList<string>)new[]
                    {
                        device.Model,
                        Humanize.MediaType(device.MediaType, _localizer),
                        Humanize.BusType(device.BusType, _localizer),
                        Humanize.Bytes(device.SizeBytes),
                        HealthText(device)
                    })
                    .ToList(),
                EmptyText = _localizer["Report.Empty.None"]
            },
            new()
            {
                Title = _localizer["Report.Table.Volumes"],
                Headers = new[] { _localizer["Report.Column.Volume"], _localizer["Report.Column.Label"], _localizer["Report.Column.FileSystem"], _localizer["Report.Column.Total"], _localizer["Report.Column.Free"], _localizer["Report.Column.FreePercent"] },
                Rows = snapshot.Volumes
                    .Select(volume => (IReadOnlyList<string>)new[]
                    {
                        volume.DriveLetter,
                        volume.Label,
                        volume.FileSystem,
                        Humanize.Bytes(volume.TotalBytes),
                        Humanize.Bytes(volume.FreeBytes),
                        Humanize.Percent(volume.FreePercent)
                    })
                    .ToList(),
                EmptyText = _localizer["Report.Empty.None"]
            }
        };

        return new ReportSection
        {
            Title = _localizer["Report.Section.System"],
            Fields = fields,
            Tables = tables,
            Note = _localizer["Report.Note.SystemPrivacy"]
        };
    }

    private ReportSection BuildHealthSection(DashboardSummary summary)
    {
        var score = summary.Score;
        var fields = new List<ReportField>
        {
            Field("Report.Field.Score", score.TotalText),
            Field("Report.Field.ScoreCategory", _localizer["Score.Category." + score.Category]),
            Field("Report.Field.Problems", summary.ProblemCount.ToString(CultureInfo.CurrentCulture)),
            Field("Report.Field.CpuUsage", Humanize.Percent(summary.Metrics.CpuPercent)),
            Field("Report.Field.MemoryUsage", Humanize.Percent(summary.Metrics.MemoryPercent)),
            Field("Report.Field.SystemDriveFree", Humanize.Bytes(summary.SystemDriveFreeBytes) + " (" + Humanize.Percent(summary.SystemDriveFreePercent) + ")")
        };

        var table = new ReportTable
        {
            Title = _localizer["Report.Table.Factors"],
            Headers = new[] { _localizer["Report.Column.Factor"], _localizer["Report.Column.Score"], _localizer["Report.Column.Weight"], _localizer["Report.Column.Detail"] },
            Rows = score.Factors
                .Select(factor => (IReadOnlyList<string>)new[]
                {
                    _localizer[factor.TitleKey],
                    factor.ScoreText,
                    factor.Weight.ToString("0", CultureInfo.CurrentCulture),
                    factor.DetailValue ?? (string.IsNullOrWhiteSpace(factor.DetailKey) ? "—" : _localizer[factor.DetailKey])
                })
                .ToList(),
            EmptyText = _localizer["Report.Empty.None"]
        };

        return new ReportSection
        {
            Title = _localizer["Report.Section.Health"],
            Fields = fields,
            Tables = new[] { table },
            Note = _localizer["Score.Disclaimer"]
        };
    }

    private ReportSection BuildCleanupSection(DashboardSummary summary)
    {
        var targets = summary.CleanupTargets
            .Where(target => target.IsAvailable)
            .OrderByDescending(target => target.TotalBytes)
            .ToList();

        var fields = new List<ReportField>
        {
            Field("Report.Field.TemporaryBytes", Humanize.Bytes(summary.TemporaryBytes)),
            Field("Report.Field.RecycleBinBytes", Humanize.Bytes(summary.RecycleBinBytes)),
            Field("Report.Field.RecoverableBytes", Humanize.Bytes(summary.TemporaryBytes + summary.RecycleBinBytes))
        };

        var table = new ReportTable
        {
            Title = _localizer["Report.Table.CleanupTargets"],
            Headers = new[]
            {
                _localizer["Report.Column.Location"],
                _localizer["Report.Column.Category"],
                _localizer["Report.Column.PotentialSize"],
                _localizer["Report.Column.Files"],
                _localizer["Report.Column.Risk"]
            },
            Rows = targets
                .Select(target => (IReadOnlyList<string>)new[]
                {
                    target.Paths.Count > 0 ? target.Paths[0] : _localizer[target.TitleKey],
                    _localizer[target.TitleKey],
                    Humanize.Bytes(target.TotalBytes),
                    target.FileCount.ToString(CultureInfo.CurrentCulture),
                    _localizer["Risk." + target.Risk]
                })
                .ToList(),
            EmptyText = _localizer["Report.Empty.None"]
        };

        return new ReportSection
        {
            Title = _localizer["Report.Section.Cleanup"],
            Fields = fields,
            Tables = new[] { table },
            Note = _localizer["Report.Note.Cleanup"]
        };
    }

    private async Task<ReportSection> BuildDiagnosisSectionAsync(CancellationToken cancellationToken)
    {
        DiagnosisResult? diagnosis;
        try
        {
            diagnosis = await _diagnostics.GetLastResultAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warning("Report", "Nao foi possivel ler o ultimo diagnostico para o relatorio.", exception);
            diagnosis = null;
        }

        if (diagnosis is null || diagnosis.Checks.Count == 0)
        {
            return new ReportSection
            {
                Title = _localizer["Report.Section.Diagnosis"],
                Note = _localizer["Report.Note.NoDiagnosis"]
            };
        }

        var fields = new List<ReportField>
        {
            Field("Report.Field.DiagnosisDate", diagnosis.CompletedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)),
            Field("Report.Field.DiagnosisDuration", Humanize.Duration(diagnosis.Duration, _localizer["Common.Duration.SubSecond"])),
            Field("Report.Field.Problems", diagnosis.ProblemCount.ToString(CultureInfo.CurrentCulture)),
            Field(
                "Report.Field.DiagnosisErrors",
                diagnosis.Checks.Count(check => check.Succeeded is false).ToString(CultureInfo.CurrentCulture))
        };

        var tables = diagnosis.Checks
            .Take(MaxDiagnosisRows)
            .GroupBy(check => check.CategoryKey)
            .Select(group => new ReportTable
            {
                Title = _localizer[group.Key],
                Headers = new[] { _localizer["Report.Column.Check"], _localizer["Report.Column.Status"], _localizer["Report.Column.Detail"] },
                Rows = group
                    .Select(check => (IReadOnlyList<string>)new[]
                    {
                        _localizer[check.TitleKey],
                        StatusText(check),
                        BuildCheckDetail(check)
                    })
                    .ToList(),
                EmptyText = _localizer["Report.Empty.None"]
            })
            .ToList();

        var note = diagnosis.ProblemCount == 0
            ? _localizer["Report.Note.DiagnosisClean"]
            : _localizer.Format("Report.Note.DiagnosisProblems", diagnosis.ProblemCount);

        return new ReportSection
        {
            Title = _localizer["Report.Section.Diagnosis"],
            Fields = fields,
            Tables = tables,
            Note = note
        };
    }

    private ReportSection BuildRecommendationSection(DashboardSummary summary)
    {
        var table = new ReportTable
        {
            Title = _localizer["Report.Table.Recommendations"],
            Headers = new[]
            {
                _localizer["Report.Column.Recommendation"],
                _localizer["Report.Column.Impact"],
                _localizer["Report.Column.Risk"],
                _localizer["Report.Column.EstimatedGain"]
            },
            Rows = summary.Recommendations
                .Select(recommendation => (IReadOnlyList<string>)new[]
                {
                    _localizer[recommendation.TitleKey],
                    _localizer["Impact." + recommendation.Impact],
                    _localizer["Risk." + recommendation.Risk],
                    recommendation.EstimatedGainBytes > 0
                        ? Humanize.Bytes(recommendation.EstimatedGainBytes)
                        : "—"
                })
                .ToList(),
            EmptyText = _localizer["Report.Empty.None"]
        };

        return new ReportSection
        {
            Title = _localizer["Report.Section.Recommendations"],
            Tables = new[] { table },
            Note = _localizer["Report.Note.Recommendations"]
        };
    }

    private async Task<ReportSection> BuildHistorySectionAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<HistoryEntry> entries;
        try
        {
            entries = await _history.QueryAsync(MaxHistoryRows, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warning("Report", "Nao foi possivel ler o historico para o relatorio.", exception);
            entries = Array.Empty<HistoryEntry>();
        }

        var table = new ReportTable
        {
            Title = _localizer["Report.Table.History"],
            Headers = new[]
            {
                _localizer["History.Column.Timestamp"],
                _localizer["History.Column.Category"],
                _localizer["History.Column.Action"],
                _localizer["History.Column.Description"],
                _localizer["History.Column.Result"]
            },
            Rows = entries
                .Select(entry => (IReadOnlyList<string>)new[]
                {
                    entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                    entry.Category,
                    entry.Action,
                    entry.Description,
                    entry.Result
                })
                .ToList(),
            EmptyText = _localizer["Report.Empty.None"]
        };

        return new ReportSection
        {
            Title = _localizer["Report.Section.History"],
            Tables = new[] { table },
            Note = _localizer.Format("Report.Note.HistoryLimit", MaxHistoryRows)
        };
    }

    private string BuildCheckDetail(DiagnosticCheck check)
    {
        if (check.Succeeded is false)
        {
            return string.IsNullOrWhiteSpace(check.ErrorDetail)
                ? _localizer["Report.Status.NotVerified"]
                : check.ErrorDetail!;
        }

        if (string.IsNullOrWhiteSpace(check.Detail) is false)
        {
            return check.Detail!;
        }

        return string.IsNullOrWhiteSpace(check.AdviceKey) ? "—" : _localizer[check.AdviceKey];
    }

    private string StatusText(DiagnosticCheck check) => check.Succeeded is false
        ? _localizer["Report.Status.NotVerified"]
        : _localizer["Severity." + check.Severity];

    private string HealthText(StorageDeviceInfo device)
    {
        if (device.SmartSupported is false)
        {
            return _localizer["Common.NotAvailable"];
        }

        return _localizer["Health.Status." + device.Health];
    }

    private ReportField Field(string labelKey, string? value) =>
        new() { Label = _localizer[labelKey], Value = string.IsNullOrWhiteSpace(value) ? _localizer["Common.NotAvailable"] : value! };

    private string YesNo(bool value) => value ? _localizer["Common.Yes"] : _localizer["Common.No"];

    private static string Join(string? first, string? second)
    {
        var parts = new[] { first, second }
            .Where(part => string.IsNullOrWhiteSpace(part) is false)
            .Select(part => part!.Trim());

        return string.Join(" · ", parts);
    }

    private static string BuildWindowsText(SystemSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return string.Empty;
        }

        var os = snapshot.Os;
        var text = os.FullVersionText;
        if (string.IsNullOrWhiteSpace(os.Architecture) is false)
        {
            text += " · " + os.Architecture;
        }

        return text;
    }

    private static string ReadAppVersion()
    {
        try
        {
            var assembly = typeof(ReportDocumentBuilder).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(informational) is false)
            {
                var plus = informational!.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString() ?? "1.0";
        }
        catch (Exception)
        {
            return "1.0";
        }
    }
}
