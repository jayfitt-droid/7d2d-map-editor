using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MapEditor.Core.GameData;
using MapEditor.Core.Ops;

namespace MapEditor.App;

public partial class NewWorldDialog : Window
{
    private static readonly int[] Sizes = [2048, 3072, 4096, 6144, 8192, 10240, 12288, 14336, 16384];

    public NewWorldDialog(BiomeTable biomes)
    {
        InitializeComponent();
        SizeBox.ItemsSource = Sizes.Select(s => $"{s} ({s / 1024.0:0.#}k)").ToList();
        SizeBox.SelectedIndex = Array.IndexOf(Sizes, 6144);
        SeedBox.Text = Random.Shared.Next(1, 100000).ToString(CultureInfo.InvariantCulture);

        var choices = biomes.Biomes.Where(b => !b.Name.Contains("underwater", StringComparison.OrdinalIgnoreCase)).ToList();
        BiomeBox.ItemsSource = choices;
        BiomeBox.SelectedItem = choices.FirstOrDefault(b => b.Name.Contains("forest") && !b.Name.Contains("burnt")) ?? choices.FirstOrDefault();

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

        Options = new NewWorldOptions(name, Sizes[SizeBox.SelectedIndex], (float)baseHeight, (float)amplitude, (float)scale, seed,
            (BiomeBox.SelectedItem as BiomeDefinition)?.Color ?? 0x004000,
            Edges.Select(b => (EdgeStyle)b.SelectedItem!).ToArray());
        DialogResult = true;
    }

    private static bool TryNumber(TextBox box, double min, double max, out double value) =>
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value >= min && value <= max;
}
