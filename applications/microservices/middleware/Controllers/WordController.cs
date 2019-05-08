using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Http;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using Microsoft.Extensions.Caching.Memory;

namespace microservices.middleware
{
    // https://docs.microsoft.com/en-us/aspnet/core/tutorials/first-web-api?view=aspnetcore-2.2&tabs=visual-studio-code#overview

    [Route("v1/[controller]")]
    [ApiController]
    public class WordController : ControllerBase
    {
        readonly HttpClient client;
        IMemoryCache _cache;

        public WordController(IMemoryCache memoryCache)
        {
            client = new HttpClient();
            _cache = memoryCache;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Word>>> GetWordItems()
        {
            // get top words from cache
            var topWords = TextAnalyzer.GetTopWords(_cache);

            // in case there is nothing in the cache, generate top words now
            // and update the cache
            if (topWords == null)
            {
                var postStream = await client.GetStreamAsync("http://backend/v1/post");
                var serializer = new DataContractJsonSerializer(typeof(List<Post>));
                var posts = serializer.ReadObject(postStream) as List<Post>;
                topWords = TextAnalyzer.AnalyseTextAndUpdateCache(posts, _cache);
            }

            // prepare result objects
            var result = new List<Word>(topWords.Count);
            for (int i = 0; i < topWords.Count; i++)
            {
                result.Add(new Word
                {
                    Name = topWords[i].Key,
                    Size = topWords[i].Value,
                });
            }

            return result;
        }
    }
}
