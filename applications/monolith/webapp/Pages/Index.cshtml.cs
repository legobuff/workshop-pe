using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;

namespace monolith.Pages
{
    public class IndexModel : PageModel
    {
        public IEnumerable<Post> Posts { get; private set; }
        public List<KeyValuePair<string, float>> TopWords { get; private set; }
        private IMemoryCache cache;

        public IndexModel(IMemoryCache memoryCache)
        {
            cache = memoryCache;
        }

        public async Task OnGetAsync()
        {
            using (var db = new BloggingContext())
            {
                Posts = await db.Posts.OrderBy(e => -e.PostId).ToListAsync();
            }
            TopWords = TextAnalyzer.GetTopWords(cache);
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            using (var db = new BloggingContext())
            {
                var p = db.Posts.Find(id);
                if (p != null)
                {
                    db.Posts.Remove(p);
                    await db.SaveChangesAsync();
                }
            }

            TextAnalyzer.AnalyseTextAndUpdateCache(cache);

            return RedirectToPage();
        }
    }

}
