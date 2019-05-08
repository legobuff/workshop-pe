using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.IO;
using microservices.frontend;
using System.Net.Http;
using System.Runtime.Serialization.Json;

namespace microservices.frontend.Pages
{
    public class PostModel : PageModel
    {
        HttpClient client;

        public PostModel()
        {
            client = new HttpClient();
        }

        [BindProperty]
        public Post Post { get; set; }


        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var serializer = new DataContractJsonSerializer(typeof(List<Post>));

            // send new post to backend
            MemoryStream s = new MemoryStream();
            serializer.WriteObject(s, Post);
            string stringData = System.Text.Encoding.UTF8.GetString(s.ToArray());
            var contentData = new StringContent(stringData, System.Text.Encoding.UTF8, "application/json");
            await client.PostAsync("http://middleware/v1/post", contentData);

            return RedirectToPage("Index");
        }
    }
}
