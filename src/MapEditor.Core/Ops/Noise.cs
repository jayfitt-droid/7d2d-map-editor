namespace MapEditor.Core.Ops;

/// <summary>Seeded 2D gradient (Perlin-style) noise with fractal octaves. Output is roughly -1..1.</summary>
public sealed class Noise
{
    private readonly int[] _perm = new int[512];

    public Noise(int seed)
    {
        var p = Enumerable.Range(0, 256).ToArray();
        new Random(seed).Shuffle(p);
        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    public float Sample(float x, float y)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float xf = x - xi, yf = y - yi;
        xi &= 255; yi &= 255;
        float u = Fade(xf), v = Fade(yf);
        int aa = _perm[_perm[xi] + yi], ab = _perm[_perm[xi] + yi + 1];
        int ba = _perm[_perm[xi + 1] + yi], bb = _perm[_perm[xi + 1] + yi + 1];
        float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
        float x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
        return Lerp(x1, x2, v) * 1.414f;
    }

    /// <summary>Fractal Brownian motion: <paramref name="octaves"/> layers, each double the frequency and half the amplitude.</summary>
    public float Fbm(float x, float y, int octaves = 5)
    {
        float sum = 0, amp = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += Sample(x, y) * amp;
            norm += amp;
            x *= 2; y *= 2; amp *= 0.5f;
        }
        return sum / norm;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Grad(int hash, float x, float y) => (hash & 3) switch
    {
        0 => x + y,
        1 => -x + y,
        2 => x - y,
        _ => -x - y,
    };
}
