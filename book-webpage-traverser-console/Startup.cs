using BookWebPageScraper.src.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BookWebPageScraper
{
    public class Startup
    {
        public static IServiceProvider Configure()
        {
            var serviceProvider = new ServiceCollection()
                .AddTransient<IScraperHandlerService, ScraperHandlerService>()
                .AddHttpClient()
                .BuildServiceProvider();

            return serviceProvider;
        }
    }
}
