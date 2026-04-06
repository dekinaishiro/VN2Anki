using System;

namespace VN2Anki.Helpers
{
    public static class TextHelper
    {
        public static double CalculateSimilarity(string s, string t)
        {
            if (string.IsNullOrEmpty(s) && string.IsNullOrEmpty(t)) return 1.0;
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t)) return 0.0;

            s = s.Trim();
            t = t.Trim();

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) ;
            for (int j = 0; j <= m; d[0, j] = j++) ;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            return 1.0 - ((double)d[n, m] / Math.Max(s.Length, t.Length));
        }
    }
}
