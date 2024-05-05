using BookWebPageScraper.src.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BookWebPageScraper
{
    public class ScraperRunner
    {
        private readonly IScraperHandlerService _scraperHandlerService;

        public ScraperRunner(IScraperHandlerService scraperHandlerService)
        {
            _scraperHandlerService = scraperHandlerService;
        }

        public static async Task Main(string[] args)
        {
            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();

            var serviceProvider = Startup.Configure();
            var scraperHandlerService = serviceProvider.GetService<IScraperHandlerService>();
            var scraperRunner = new ScraperRunner(scraperHandlerService);

            await scraperRunner.Run();

            stopwatch.Stop();
            Console.WriteLine($"Total execution time: {stopwatch.Elapsed}");
        }

        public async Task Run()
        {
            const string baseUrl = "http://books.toscrape.com/";
            const string outputPath = "downloaded_books_files";

            try
            {
                await _scraperHandlerService.ScrapeBookPages(baseUrl, outputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }
    }
}
