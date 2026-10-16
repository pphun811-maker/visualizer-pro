namespace VisualizerPro.Core;

public static class Fourier
{
    public static void Transform(double[] real, double[] imaginary)
    {
        int n = real.Length;
        if (n != imaginary.Length || n < 2 || (n & (n - 1)) != 0) throw new ArgumentException("FFT length must be a power of two.");
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (real[i], real[j]) = (real[j], real[i]); (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]); }
        }
        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2 * Math.PI / length, wr = Math.Cos(angle), wi = Math.Sin(angle);
            for (int start = 0; start < n; start += length)
            {
                double ur = 1, ui = 0;
                for (int j = 0; j < length / 2; j++)
                {
                    int a = start + j, b = a + length / 2;
                    double vr = real[b] * ur - imaginary[b] * ui, vi = real[b] * ui + imaginary[b] * ur;
                    real[b] = real[a] - vr; imaginary[b] = imaginary[a] - vi;
                    real[a] += vr; imaginary[a] += vi;
                    (ur, ui) = (ur * wr - ui * wi, ur * wi + ui * wr);
                }
            }
        }
    }

    public static double[] Window(int count, WindowKind kind)
    {
        var result = new double[count];
        for (int i = 0; i < count; i++)
        {
            double x = 2 * Math.PI * i / (count - 1);
            result[i] = kind switch
            {
                WindowKind.BlackmanHarris => .35875 - .48829 * Math.Cos(x) + .14128 * Math.Cos(2 * x) - .01168 * Math.Cos(3 * x),
                WindowKind.FlatTop => .21557895 - .41663158 * Math.Cos(x) + .277263158 * Math.Cos(2 * x) - .083578947 * Math.Cos(3 * x) + .006947368 * Math.Cos(4 * x),
                _ => .5 - .5 * Math.Cos(x)
            };
        }
        return result;
    }
}
