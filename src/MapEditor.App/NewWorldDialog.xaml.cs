using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using MapEditor.Core.GameData;
using MapEditor.Core.Ops;

namespace MapEditor.App;

public partial class NewWorldDialog : Window
{
    private static readonly int[] Sizes = [2048, 3072, 4096, 6144, 8192, 10240, 12288, 14336, 16384];

    private static readonly LayoutChoice[] Layouts =
    [
        new(BiomeLayoutKind.CenterForest, "Forest in the middle"),
        new(BiomeLayoutKind.CenterWasteland, "Wasteland in the middle"),
        new(BiomeLayoutKind.Circle, "Ring"),
        new(BiomeLayoutKind.Circle2, "Two rings"),
        new(BiomeLayoutKind.Line, "Line"),
    ];

    private readonly List<ShareRow> _shares;

    public NewWorldDialog(BiomeTable biomes)
    {
        InitializeComponent();
        SizeBox.ItemsSource = Sizes.Select(s => $"{s} ({s / 1024.0:0.#}k)").ToList();
        SizeBox.SelectedIndex = Array.IndexOf(Sizes, 6144);
        SeedBox.Text = Random.Shared.Next(1, 100000).ToString(CultureInfo.InvariantCulture);

        var choices = biomes.Biomes.Where(b => !b.Name.Contains("underwater", StringComparison.OrdinalIgnoreCase)).ToList();
        BiomeBox.ItemsSource = choices;
        BiomeBox.SelectedItem = choices.FirstOrDefault(b => b.Name.Contains("forest") && !b.Name.Contains("burnt")) ?? choices.FirstOrDefault();

        LayoutBox.ItemsSource = Layouts;
        _shares = BiomeLayout.DefaultShares(biomes).Select(s => new ShareRow(s.Name, s.Color, s.Weight)).ToList();
        foreach (var row in _shares)
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ShareRow.Weight)) UpdatePercents();
            };
        SharesList.ItemsSource = _shares;
        SetBiomeDefaults();
        BiomeModeBox.SelectedIndex = 0;

        foreach (var box in Edges)
        {
            box.ItemsSource = Enum.GetValues<EdgeStyle>();
            box.SelectedItem = EdgeStyle.Land;
        }
    }

    public NewWorldOptions? Options { get; private set; }

    private ComboBox[] Edges => [EdgeN, EdgeE, EdgeS, EdgeW];

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ErrorText.Text = "Enter a name that can be used as a folder name.";
            return;
        }
        if (!TryNumber(BaseHeightBox, 2, 250, out double baseHeight) ||
            !TryNumber(NoiseAmpBox, 0, 120, out double amplitude) ||
            !TryNumber(NoiseScaleBox, 20, 10000, out double scale) ||
            !int.TryParse(SeedBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
        {
            ErrorText.Text = "Check the numbers: height 2-250, roughness 0-120, feature size 20-10000, and a whole-number seed.";
            return;
        }

        BiomePlan? plan = null;
        if (GeneratesBiomes)
        {
            if (_shares.All(s => s.Weight <= 0))
            {
                ErrorText.Text = "Give at least one biome a share of the map.";
                return;
            }
            plan = new BiomePlan(((LayoutChoice)LayoutBox.SelectedItem).Kind,
                _shares.Select(s => new BiomeShare(s.Name, s.Color, s.Weight)).ToList());
        }

        Options = new NewWorldOptions(name, Sizes[SizeBox.SelectedIndex], (float)baseHeight, (float)amplitude, (float)scale, seed,
            (BiomeBox.SelectedItem as BiomeDefinition)?.Color ?? 0x004000,
            Edges.Select(b => (EdgeStyle)b.SelectedItem!).ToArray(), plan);
        DialogResult = true;
    }

    private bool GeneratesBiomes => BiomeModeBox.SelectedIndex == 1;

    private void BiomeMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (BiomeBox is null || LayoutPanel is null) return;
        BiomeBox.Visibility = GeneratesBiomes ? Visibility.Collapsed : Visibility.Visible;
        LayoutPanel.Visibility = GeneratesBiomes ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BiomeDefaults_Click(object sender, RoutedEventArgs e) => SetBiomeDefaults();

    private void SetBiomeDefaults()
    {
        LayoutBox.SelectedIndex = 0;
        foreach (var row in _shares) row.Weight = BiomeLayout.DefaultWeight(row.Color);
        UpdatePercents();
    }

    /// <summary>Shows each biome's weight as a percentage of them all.</summary>
    private void UpdatePercents()
    {
        int total = _shares.Sum(s => s.Weight);
        foreach (var row in _shares) row.PercentText = total == 0 ? "0%" : $"{100.0 * row.Weight / total:0}%";
    }

    private sealed record LayoutChoice(BiomeLayoutKind Kind, string Label);

    /// <summary>One biome's slider in the generated layout.</summary>
    private sealed class ShareRow(string name, uint color, int weight) : ObservableObject
    {
        public string Name { get; } = name;
        public uint Color { get; } = color;

        private int _weight = weight;

        public int Weight
        {
            get => _weight;
            set => SetProperty(ref _weight, value);
        }

        private string _percentText = "";

        public string PercentText
        {
            get => _percentText;
            set => SetProperty(ref _percentText, value);
        }
    }

    private static bool TryNumber(TextBox box, double min, double max, out double value) =>
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value >= min && value <= max;
}
