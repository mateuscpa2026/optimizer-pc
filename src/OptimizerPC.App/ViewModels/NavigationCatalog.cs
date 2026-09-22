namespace OptimizerPC.App.ViewModels;

/// <summary>
/// Agrupamento da navegacao lateral.
/// </summary>
public sealed record NavigationSection(string TitleKey, IReadOnlyList<NavigationEntry> Entries);

/// <summary>
/// Entrada da navegacao lateral: liga a tela ao titulo, ao icone e ao ViewModel.
/// </summary>
public sealed record NavigationEntry(Screen Screen, string TitleKey, string IconKey, Type ViewModelType);

/// <summary>
/// Mapa fixo das telas. Acrescentar uma tela exige registrar aqui o ViewModel
/// correspondente: a navegacao resolve a tela pelo identificador e nunca por
/// caminho informado externamente.
/// </summary>
public static class NavigationCatalog
{
    private static readonly Lazy<IReadOnlyList<NavigationSection>> LazySections = new(Build);

    public static IReadOnlyList<NavigationSection> Sections => LazySections.Value;

    public static NavigationEntry? Find(Screen screen)
    {
        foreach (var section in Sections)
        {
            foreach (var entry in section.Entries)
            {
                if (entry.Screen == screen)
                {
                    return entry;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<NavigationSection> Build()
    {
        return new[]
        {
            new NavigationSection("Nav.Section.Overview", new[]
            {
                new NavigationEntry(Screen.Dashboard, "Nav.Dashboard", "Icon.Dashboard", typeof(DashboardViewModel)),
                new NavigationEntry(Screen.Diagnosis, "Nav.Diagnosis", "Icon.Diagnosis", typeof(DiagnosisViewModel))
            }),

            new NavigationSection("Nav.Section.Optimization", new[]
            {
                new NavigationEntry(Screen.Optimization, "Nav.Optimization", "Icon.Optimization", typeof(OptimizationViewModel)),
                new NavigationEntry(Screen.Cleaning, "Nav.Cleaning", "Icon.Cleaning", typeof(CleaningViewModel)),
                new NavigationEntry(Screen.Startup, "Nav.Startup", "Icon.Startup", typeof(StartupViewModel))
            }),

            new NavigationSection("Nav.Section.Monitoring", new[]
            {
                new NavigationEntry(Screen.Processes, "Nav.Processes", "Icon.Processes", typeof(ProcessesViewModel)),
                new NavigationEntry(Screen.Services, "Nav.Services", "Icon.Services", typeof(ServicesViewModel)),
                new NavigationEntry(Screen.Performance, "Nav.Performance", "Icon.Performance", typeof(PerformanceViewModel)),
                new NavigationEntry(Screen.Storage, "Nav.Storage", "Icon.Storage", typeof(StorageViewModel))
            }),

            new NavigationSection("Nav.Section.Profiles", new[]
            {
                new NavigationEntry(Screen.LowEnd, "Nav.LowEnd", "Icon.LowEnd", typeof(LowEndViewModel)),
                new NavigationEntry(Screen.Gamer, "Nav.Gamer", "Icon.Gamer", typeof(GamerViewModel))
            }),

            new NavigationSection("Nav.Section.Tools", new[]
            {
                new NavigationEntry(Screen.Tools, "Nav.Tools", "Icon.Tools", typeof(ToolsViewModel)),
                new NavigationEntry(Screen.Restore, "Nav.Restore", "Icon.Restore", typeof(RestoreViewModel)),
                new NavigationEntry(Screen.Reports, "Nav.Reports", "Icon.Reports", typeof(ReportsViewModel)),
                new NavigationEntry(Screen.History, "Nav.History", "Icon.History", typeof(HistoryViewModel))
            }),

            new NavigationSection("Nav.Section.System", new[]
            {
                new NavigationEntry(Screen.Settings, "Nav.Settings", "Icon.Settings", typeof(SettingsViewModel))
            })
        };
    }
}
