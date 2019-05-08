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
using microservices.frontend;
using System.Net.Http;
using System.Runtime.Serialization.Json;

namespace microservices.frontend.Pages
{
    public class IndexModel : PageModel
    {
        HttpClient client;

        public IndexModel()
        {
            client = new HttpClient();
        }

        public IEnumerable<Post> Posts { get; private set; }
        public List<Word> TopWords { get; private set; }

        public async Task OnGetAsync()
        {
            var postStream = await client.GetStreamAsync("http://middleware/v1/post");
            var postSerializer = new DataContractJsonSerializer(typeof(List<Post>));
            Posts = postSerializer.ReadObject(postStream) as List<Post>;

            var wordStream = await client.GetStreamAsync("http://middleware/v1/word");
            var wordSerializer = new DataContractJsonSerializer(typeof(List<Word>));
            TopWords = wordSerializer.ReadObject(wordStream) as List<Word>;
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            await client.DeleteAsync("http://middleware/v1/post/"+id.ToString());

            return RedirectToPage();
        }
    }

}
