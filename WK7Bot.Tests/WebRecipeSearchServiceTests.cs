using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Core.Exceptions;
using WK7Bot.Models;
using WK7Bot.Services;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class WebRecipeSearchServiceTests
{
    private const string SearchResultsHtml = """
    <html><body><div class="results">
    <a rel="nofollow" class="result__a" href="https://duckduckgo.com/y.js?ad_domain=edeka.de&amp;u3=https%3A%2F%2Fwww.edeka.de">Anzeige: Edeka</a>
    <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.chefkoch.de%2Frezepte%2F123%2Fpfannkuchen.html&amp;rut=abc">Der perfekte Pfannkuchen</a>
    <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.kochbar.de%2Frezept%2F9.html&amp;rut=def">Pfannkuchen bei Kochbar</a>
    </div></body></html>
    """;

    private const string SearchResultsWithoutRecipeHtml = """
    <html><body><div class="no-results">Keine Ergebnisse gefunden.</div></body></html>
    """;

    private const string SearchResultsThreeHtml = """
    <html><body><div class="results">
    <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Frezept%2Fa.html&amp;rut=1">Erstes</a>
    <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Frezept%2Fb.html&amp;rut=2">Zweites</a>
    <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Frezept%2Fc.html&amp;rut=3">Drittes</a>
    </div></body></html>
    """;

    private const string RecipePageHtml = """
    <!DOCTYPE html><html><head><title>Pfannkuchen</title>
    <script type="application/ld+json">
    {"@context":"https://schema.org","@graph":[{"@type":"https://schema.org/Recipe","name":"Der perfekte Pfannkuchen","description":"&lt;b&gt;Klassiker&lt;/b&gt; aus der Pfanne","prepTime":"PT20M","cookTime":"PT1H5M","recipeYield":"4 Portionen","recipeIngredient":["200 g Mehl","300 ml Milch"],"recipeInstructions":[{"@type":"HowToStep","text":"Teig r\u00fchren"},{"@type":"HowToSection","name":"Braten","itemListElement":[{"@type":"HowToStep","text":"Goldbraun backen"}]}],"image":"http://insecure.example.com/old.jpg"}]}
    </script></head><body>Rezeptseite</body></html>
    """;

    private const string RecipePageWithImageHtml = """
    <!DOCTYPE html><html><head><title>Bild</title>
    <script type="application/ld+json">
    [{"@type":"Recipe","name":"K\u00e4sesp\u00e4tzle","prepTime":"PT15M","recipeYield":["5","5 Portionen"],"recipeIngredient":["Sp\u00e4tzle","K\u00e4se"],"recipeInstructions":"Alles vermengen und im Ofen \u00fcberbacken.","image":{"@type":"ImageObject","url":"https://images.example.com/kaesespaetzle.jpg"}}]
    </script></head><body>Rezeptseite</body></html>
    """;

    private const string MalformedRecipePageHtml = """
    <!DOCTYPE html><html><head><title>Kaputt</title>
    <script type="application/ld+json">{ this is not json</script>
    </head><body>x</body></html>
    """;

    private const string SearchResultsWithListingHtml = """
    <html><body><div class="results">
    <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.chefkoch.de%2Frs%2Fs0%2Fzucchini%2FRezepte.html&amp;rut=a1">Zucchini Rezepte - Kochbuch</a>
    <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.org%2Frezept%2Fzucchini-kuchen.html&amp;rut=a2">Zucchini Kuchen</a>
    <a rel="nofollow" class="result__a" href="https://www.youtube.com/watch?v=dQw4w9WgXcQ">Video</a>
    </div></body></html>
    """;

    private const string BotChallengeHtml = """
    <html><body>Unfortunately, bots use DuckDuckGo too. Please complete the following challenge to confirm this search was made by a human.</body></html>
    """;

    private const string ChefkochListingHtml = """
    <html><head>
    <script type="application/ld+json">
    {"@context":"https://schema.org","@type":"ItemList","itemListElement":[
      {"@type":"ListItem","position":1,"url":"https://www.chefkoch.de/rezepte/111111/erstes-rezept.html","name":"Erstes"},
      {"@type":"ListItem","position":2,"url":"https://www.chefkoch.de/rezepte/222222/zweites-rezept.html","name":"Zweites"}]}
    </script></head><body>
    <a href="/rezepte/333333/drittes-rezept.html">Drittes Rezept</a>
    <a href="/rezepte/was-koche-ich-heute/">Navigationspunkt ohne Rezeptide</a>
    </body></html>
    """;

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private readonly object _sync = new();

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int CallCount { get; private set; }
        public List<string> RequestUrls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                CallCount++;
                RequestUrls.Add(request.RequestUri!.ToString());
            }

            return Task.FromResult(_responder(request));
        }
    }

    private static WebRecipeSearchService CreateService(
        HttpMessageHandler handler,
        IRandomSource? randomSource = null,
        IMemoryCache? recentRecipeCache = null)
        => new(
            new HttpClient(handler),
            NullLogger<WebRecipeSearchService>.Instance,
            randomSource ?? new StubRandomSource(),
            recentRecipeCache);

    private static HttpResponseMessage Html(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode) { Content = new StringContent(content, Encoding.UTF8, "text/html") };

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new WebRecipeSearchService(null!, NullLogger<WebRecipeSearchService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new WebRecipeSearchService(new HttpClient(), null!));
    }

    [Fact]
    public async Task SearchWebRecipeAsync_ParsesStructuredRecipe_AndKeepsSourceUrl()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("duckduckgo.com"))
            {
                return Html(SearchResultsHtml);
            }

            if (url.Contains("chefkoch.de"))
            {
                return Html(RecipePageHtml);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var recipe = await service.SearchWebRecipeAsync("Pfannkuchen");

        Assert.NotNull(recipe);
        Assert.Equal("Der perfekte Pfannkuchen", recipe!.Title);
        Assert.Equal("Klassiker aus der Pfanne", recipe.Description);
        Assert.Equal("20 Min.", recipe.PrepTime);
        Assert.Equal("1 Std. 5 Min.", recipe.CookTime);
        Assert.Equal(4, recipe.Servings);
        Assert.Equal(new[] { "200 g Mehl", "300 ml Milch" }, recipe.Ingredients);
        Assert.Equal(new[] { "Teig rühren", "Goldbraun backen" }, recipe.Instructions);
        Assert.Equal("https://www.chefkoch.de/rezepte/123/pfannkuchen.html", recipe.SourceUrl);
        Assert.Empty(recipe.DietTags);
        Assert.Equal(string.Empty, recipe.TransplantSafetyNotes);
        Assert.Equal(string.Empty, recipe.ImageUrl);

        // Search page plus both candidate pages (the next candidate is prefetched in parallel).
        Assert.Equal(3, handler.CallCount);
        Assert.DoesNotContain(handler.RequestUrls, u => u.Contains("y.js"));
        Assert.Contains("Pfannkuchen", handler.RequestUrls[0]);
        Assert.Contains("html.duckduckgo.com", handler.RequestUrls[0]);
    }

    [Fact]
    public async Task SearchWebRecipeAsync_SkipsCandidatesWithoutRecipeData()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("duckduckgo.com"))
            {
                return Html(SearchResultsHtml);
            }

            if (url.Contains("chefkoch.de"))
            {
                return Html(MalformedRecipePageHtml);
            }

            if (url.Contains("kochbar.de"))
            {
                return Html(RecipePageWithImageHtml);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var recipe = await service.SearchWebRecipeAsync("Käsespätzle");

        Assert.NotNull(recipe);
        Assert.Equal("Käsespätzle", recipe!.Title);
        Assert.Equal(new[] { "Spätzle", "Käse" }, recipe.Ingredients);
        Assert.Equal(5, recipe.Servings);
        Assert.Contains("überbacken", recipe.Instructions.Single());
        Assert.Equal("https://images.example.com/kaesespaetzle.jpg", recipe.ImageUrl);
        Assert.Equal("https://www.kochbar.de/rezept/9.html", recipe.SourceUrl);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task SearchWebRecipeAsync_ReturnsNull_WhenNeitherProviderHasResults()
    {
        var handler = new StubHttpMessageHandler(_ => Html(SearchResultsWithoutRecipeHtml));
        var service = CreateService(handler);

        var recipe = await service.SearchWebRecipeAsync("gibbnichts");

        Assert.Null(recipe);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains(handler.RequestUrls, u => u.Contains("chefkoch.de"));
    }

    [Fact]
    public async Task SearchWebRecipeAsync_ReturnsNull_WhenNoCandidateExposesRecipeData()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.ToString().Contains("duckduckgo.com")
                ? Html(SearchResultsHtml)
                : Html("<html><body>kein Rezept</body></html>"));

        var service = CreateService(handler);

        var recipe = await service.SearchWebRecipeAsync("Pfannkuchen");

        Assert.Null(recipe);
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task SearchWebRecipeAsync_SkipsRequest_WhenQueryIsBlank()
    {
        var handler = new StubHttpMessageHandler(_ => Html(SearchResultsHtml));
        var service = CreateService(handler);

        var recipe = await service.SearchWebRecipeAsync("   ");

        Assert.Null(recipe);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchWebRecipeAsync_Throws_WhenNeitherProviderResponds()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<RecipeSearchUnavailableException>(
            () => service.SearchWebRecipeAsync("Pfannkuchen"));

        Assert.Contains("Pfannkuchen", exception.Message);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task SearchWebRecipeAsync_FallsBackToChefkoch_WhenSearchServesBotChallenge()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("duckduckgo.com"))
            {
                return Html(BotChallengeHtml);
            }

            if (url.Contains("chefkoch.de/rs/"))
            {
                return Html(ChefkochListingHtml);
            }

            if (url.Contains("chefkoch.de/rezepte/111111/"))
            {
                return Html(RecipePageWithImageHtml);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var recipe = await service.SearchWebRecipeAsync("Zucchini Kuchen");

        Assert.NotNull(recipe);
        Assert.Equal("Käsespätzle", recipe!.Title);
        Assert.Equal("https://www.chefkoch.de/rezepte/111111/erstes-rezept.html", recipe.SourceUrl);

        // Search page + Chefkoch listing + all three listing candidates (prefetched in parallel).
        Assert.Equal(5, handler.CallCount);
    }

    [Fact]
    public async Task SearchWebRecipeAsync_FallsBackToChefkoch_WhenSearchHasNoParseableCandidate()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("duckduckgo.com"))
            {
                return Html(SearchResultsWithoutRecipeHtml);
            }

            if (url.Contains("chefkoch.de/rs/"))
            {
                return Html(ChefkochListingHtml);
            }

            if (url.Contains("chefkoch.de/rezepte/333333/"))
            {
                return Html(RecipePageHtml);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var recipe = await service.SearchWebRecipeAsync("Zucchini Aprikosen");

        Assert.NotNull(recipe);
        Assert.Equal("Der perfekte Pfannkuchen", recipe!.Title);
        Assert.Equal("https://www.chefkoch.de/rezepte/333333/drittes-rezept.html", recipe.SourceUrl);
        Assert.Equal(5, handler.CallCount);
        Assert.DoesNotContain(handler.RequestUrls, u => u.Contains("/rezepte/was-koche-ich-heute/"));
    }

    [Fact]
    public async Task SearchWebRecipeAsync_TriesRecipePagesBeforeListings_AndSkipsBlockedHosts()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("duckduckgo.com"))
            {
                return Html(SearchResultsWithListingHtml);
            }

            if (url.Contains("example.org"))
            {
                return Html(RecipePageWithImageHtml);
            }

            if (url.Contains("chefkoch.de"))
            {
                return Html("<html><body>Suchergebnisliste ohne Rezept</body></html>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var recipe = await service.SearchWebRecipeAsync("Zucchini Kuchen");

        Assert.NotNull(recipe);
        Assert.Equal("Käsespätzle", recipe!.Title);

        // Search page plus both candidates (the listing page is prefetched alongside the recipe page).
        Assert.Equal(3, handler.CallCount);
        Assert.Contains(handler.RequestUrls, u => u.Contains("example.org"));
        Assert.Contains(handler.RequestUrls, u => u.Contains("chefkoch.de/rs/"));
        Assert.DoesNotContain(handler.RequestUrls, u => u.Contains("youtube.com"));
    }

    [Fact]
    public async Task SearchWebRecipeAsync_StartsAtRandomisedCandidate_WhenRandomSelectsALaterStart()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("duckduckgo.com"))
            {
                return Html(SearchResultsThreeHtml);
            }

            if (url.Contains("example.com/rezept/c.html"))
            {
                return Html(RecipePageWithImageHtml);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        // Window of three candidates weights 3/2/1 (total 6); roll 5 selects the third candidate as start.
        var service = CreateService(handler, new StubRandomSource(5));

        var recipe = await service.SearchWebRecipeAsync("Pfannkuchen");

        Assert.NotNull(recipe);
        Assert.Equal("https://example.com/rezept/c.html", recipe!.SourceUrl);

        // Search page plus all three candidates (downloaded in parallel, evaluated from the rolled start).
        Assert.Equal(4, handler.CallCount);
        Assert.Contains(handler.RequestUrls, u => u.EndsWith("/c.html"));
    }

    [Fact]
    public async Task SearchWebRecipeAsync_PrefersCandidatesThatWereNotRecentlyServed()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("duckduckgo.com"))
            {
                return Html(SearchResultsHtml);
            }

            if (url.Contains("chefkoch.de/rezepte/123/"))
            {
                return Html(RecipePageHtml);
            }

            if (url.Contains("kochbar.de"))
            {
                return Html(RecipePageWithImageHtml);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(handler, recentRecipeCache: cache);

        var first = await service.SearchWebRecipeAsync("Pfannkuchen");
        Assert.Equal("https://www.chefkoch.de/rezepte/123/pfannkuchen.html", first!.SourceUrl);
        Assert.Equal("Der perfekte Pfannkuchen", first.Title);

        var second = await service.SearchWebRecipeAsync("Pfannkuchen");
        Assert.Equal("https://www.kochbar.de/rezept/9.html", second!.SourceUrl);
        Assert.Equal("Käsespätzle", second.Title);
    }

    [Fact]
    public async Task SearchWebRecipeAsync_KeepsWorking_WhenEveryCandidateWasRecentlyServed()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.ToString().Contains("duckduckgo.com")
                ? Html(SearchResultsHtml)
                : Html(RecipePageHtml));

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(handler, recentRecipeCache: cache);

        var first = await service.SearchWebRecipeAsync("Pfannkuchen");
        var second = await service.SearchWebRecipeAsync("Pfannkuchen");
        var third = await service.SearchWebRecipeAsync("Pfannkuchen");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);

        // Three requests per call (search page + both candidate pages), even once every candidate has been served.
        Assert.Equal(9, handler.CallCount);
    }
}
