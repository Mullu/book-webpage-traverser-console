namespace BookWebPageScraper.src.Services
{
    public interface IScraperHandlerService
    {
        Task ScrapeBookPages(string outputPath);
    }
}
