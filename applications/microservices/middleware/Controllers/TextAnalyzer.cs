using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace microservices.middleware
{
    public static class TextAnalyzer
    {
        //public static Dictionary<string,int>

        const string CacheKeyNameTopWords = "topWords";

        public static List<KeyValuePair<string, float>> AnalyseTextAndUpdateCache(IEnumerable<Post> posts, IMemoryCache memoryCache)
        {
            // calculate word statistics
            var stats = new Dictionary<string, int>();
            foreach (var p in posts)
            {
                CalculateWordStats(p, stats);
            }

            // get top 10 words
            var sorted = stats.ToList();
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            var top = sorted.Take(10).ToArray();
            var normalized = new List<KeyValuePair<string, float>>(top.Length);
            if (top.Length > 0)
            {
                var max = (float)top[0].Value;
                for (int i = 0; i < top.Length; i++)
                {
                    var p = top[i];
                    var n = new KeyValuePair<string, float>(p.Key, (((float)p.Value) / max));
                    normalized.Add(n);
                }
            }
            normalized.Sort((a, b) => b.Key.CompareTo(a.Key));

            // save to memory cache
            memoryCache.Set(CacheKeyNameTopWords, normalized);

            System.Threading.Thread.Sleep(System.TimeSpan.FromSeconds(3));

            return normalized;
        }

        static void CalculateWordStats(Post p, Dictionary<string, int> stats)
        {
            var text = p.Title + " " + p.Content;
            foreach (var w in text.Split(" "))
            {
                var word = w.Trim('.', ',', '!', ':', '+', '-').ToLower();
                if (word.Length < 3) continue;
                if (StopWords.Exists(word)) continue;
                word = word.Substring(0, 1).ToUpper() + word.Substring(1);
                int v;
                if (!stats.TryGetValue(word, out v))
                {
                    v = 0;
                }
                v += 1;
                stats[word] = v;
            }
        }

        public static List<KeyValuePair<string, float>> GetTopWords(IMemoryCache memoryCache)
        {
            // get top words from cache
            var topWords = memoryCache.Get<List<KeyValuePair<string, float>>>(CacheKeyNameTopWords);
            return topWords;
        }


    }
}


