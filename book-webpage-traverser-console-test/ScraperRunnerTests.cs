using BookWebPageScraper.src.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace BookWebPageScraper.Tests
{
    public class ScraperRunnerTests
    {
        [Fact]
        public async Task ScrapeBookPages_Run_Success()
        {
            // Arrange
            var mockScraperHandler = new Mock<IScraperHandlerService>();
            mockScraperHandler.Setup(x => x.ScrapeBookPages(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var serviceProvider = new ServiceCollection()
                .AddTransient<IScraperHandlerService>(_ => mockScraperHandler.Object)
                .BuildServiceProvider();

            var scraperRunner = new ScraperRunner(serviceProvider.GetService<IScraperHandlerService>());

            // Act
            await scraperRunner.Run();

            // Assert
            Assert.DoesNotContain("Test exception", FakeConsole.Output);
            mockScraperHandler.Verify(x => x.ScrapeBookPages(It.IsAny<string>()), Times.Once);
        }


        [Fact]
        public async Task ScrapeBookPages_Run_Exception()
        {
            // Arrange
            FakeConsole.StartCapture();
            var exceptionMessage = "Test exception";
            var scraperHandlerMock = new Mock<IScraperHandlerService>();
            scraperHandlerMock.Setup(x => x.ScrapeBookPages(It.IsAny<string>()))
                              .Throws(new Exception(exceptionMessage));

            var scraperRunner = new ScraperRunner(scraperHandlerMock.Object);

            // Act
            await scraperRunner.Run();
            FakeConsole.StopCapture();

            // Assert
            Assert.Contains(exceptionMessage, FakeConsole.Output);
        }
    }
}
