using System.Windows;
using System.Windows.Media;

namespace AlgorithmsLab.Desktop;

internal static class Theme
{
    public static bool IsDark { get; private set; }

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var resources = ApplicationResources();
        var palette = dark ? Dark : Light;

        foreach (var (key, hex) in palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            resources[key] = brush;
        }
    }

    private static ResourceDictionary ApplicationResources() => System.Windows.Application.Current.Resources;

    private static readonly Dictionary<string, string> Dark = new()
    {
        ["Page"] = "#101611", ["Sidebar"] = "#131B15", ["Card"] = "#19221B", ["Field"] = "#141C16",
        ["Hover"] = "#243328", ["Border"] = "#344438", ["Text"] = "#EBF2E9", ["Muted"] = "#A4B5A6",
        ["Subtle"] = "#829786", ["Accent"] = "#A0E39F", ["AccentHover"] = "#B5EEB5",
        ["OnAccent"] = "#142715", ["AccentPanel"] = "#233C29", ["Bar"] = "#518463",
        ["DimBar"] = "#2B4032", ["Compare"] = "#F2C885", ["Change"] = "#94B6EF", ["Danger"] = "#FF9F98",
        ["Pivot"] = "#D094F5", ["PivotPanel"] = "#31223E", ["ComparePanel"] = "#3B311F",
        ["ChangePanel"] = "#1F2E44", ["RunBg"] = "#1D2820"
    };

    private static readonly Dictionary<string, string> Light = new()
    {
        ["Page"] = "#F2F6F1", ["Sidebar"] = "#E8F0E7", ["Card"] = "#FCFEFC", ["Field"] = "#F4F7F3",
        ["Hover"] = "#E3EDE2", ["Border"] = "#CEDCCF", ["Text"] = "#18251A", ["Muted"] = "#465E4B",
        ["Subtle"] = "#58705C", ["Accent"] = "#285C38", ["AccentHover"] = "#1F4C2D",
        ["OnAccent"] = "#FFFFFF", ["AccentPanel"] = "#DDEFE0", ["Bar"] = "#669477",
        ["DimBar"] = "#CDDCD0", ["Compare"] = "#A86611", ["Change"] = "#3566A9", ["Danger"] = "#9E322B",
        ["Pivot"] = "#7E3AF2", ["PivotPanel"] = "#EFE5FD", ["ComparePanel"] = "#FBF0DC",
        ["ChangePanel"] = "#E2ECFA", ["RunBg"] = "#EAF2EA"
    };
}
