using BookWebPageScraper.src.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BookWebPageScraper
{
    public class Startup
    {
        public static ServiceProvider Configure()
        {
            var serviceProvider = new ServiceCollection()
                .AddTransient<IScraperHandlerService, ScraperHandlerService>()
                .BuildServiceProvider();

            return serviceProvider;
        }
    }
}
