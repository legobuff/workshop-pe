using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace monolith.Pages
{
    public class PostModel : PageModel
    {
        [BindProperty]
        public Post Post { get; set; }

        private IMemoryCache cache;

        public PostModel(IMemoryCache memoryCache)
        {
            cache = memoryCache;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            using (var db = new BloggingContext())
            {
                await db.Posts.AddAsync(Post);
                await db.SaveChangesAsync();
            }

            TextAnalyzer.AnalyseTextAndUpdateCache(cache);

            return RedirectToPage("Index");
        }
    }
}
