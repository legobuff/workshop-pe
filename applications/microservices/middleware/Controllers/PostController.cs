using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Http;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.IO;
using Microsoft.Extensions.Caching.Memory;

namespace microservices.middleware
{
    // https://docs.microsoft.com/en-us/aspnet/core/tutorials/first-web-api?view=aspnetcore-2.2&tabs=visual-studio-code#overview

    [Route("v1/[controller]")]
    [ApiController]
    public class PostController : ControllerBase
    {
        readonly HttpClient client;
        IMemoryCache _cache;

        public PostController(IMemoryCache memoryCache)
        {
            client = new HttpClient();
            _cache = memoryCache;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Post>>> GetPostItems()
        {
            var postStream = await client.GetStreamAsync("http://backend/v1/post");
            var serializer = new DataContractJsonSerializer(typeof(List<Post>));
            return serializer.ReadObject(postStream) as List<Post>;
        }

        [HttpPost]
        public async Task<ActionResult<int>> PostPostItem(Post post)
        {
            var serializer = new DataContractJsonSerializer(typeof(List<Post>));

            // send new post to backend
            MemoryStream s = new MemoryStream();
            serializer.WriteObject(s, post);
            string stringData = System.Text.Encoding.UTF8.GetString(s.ToArray());
            var contentData = new StringContent(stringData, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync("http://backend/v1/post", contentData);

            // update top words
            var postStream = await client.GetStreamAsync("http://backend/v1/post");
            var posts = serializer.ReadObject(postStream) as List<Post>;
            TextAnalyzer.AnalyseTextAndUpdateCache(posts, _cache);

            return 0;
        }

        [HttpDelete("{postID}")]
        public async Task DeletePostItem(int postID)
        {
            await client.DeleteAsync("http://backend/v1/post/"+postID.ToString());

            // update top words
            var serializer = new DataContractJsonSerializer(typeof(List<Post>));
            var postStream = await client.GetStreamAsync("http://backend/v1/post");
            var posts = serializer.ReadObject(postStream) as List<Post>;
            TextAnalyzer.AnalyseTextAndUpdateCache(posts, _cache);
        }

    }
}
