using BookWebPageScraper.src.Services;
using HtmlAgilityPack;
using System.Collections.Concurrent;
using File = System.IO.File;

namespace BookWebPageScraper
{
    public class ScraperHandlerService : IScraperHandlerService
    {
        private const string IndexFileName = "index.html";
        private const string ImgNodeName = "img";
        private const string HrefAttributeName = "href";
        private const string SrcAttributeName = "src";
        private static readonly SemaphoreSlim semaphoreSlim = new SemaphoreSlim(4);
        private static readonly object locker = new();

        public async Task ScrapeBookPages(string baseUrl, string outputPath)
        {
            var urlToLocalPathMap = new ConcurrentDictionary<string, string>();
            string currentPagetUrl = baseUrl;

            try
            {
                CreateDirectory(outputPath);

                var htmlDocument = await FetchHtmlDocument(baseUrl);
                SaveHtmlPage(currentPagetUrl, htmlDocument.DocumentNode.OuterHtml, outputPath, IndexFileName);  //Home page
                await ExtractAndDownloadBookCategoryPages(htmlDocument, baseUrl);

                string catalogueFolder = Path.Combine(outputPath, "catalogue");
                CreateDirectory(catalogueFolder);

                int totalPages = 50;
                foreach (int pageNumber in Enumerable.Range(1, totalPages))
                {
                    Console.WriteLine($"Processed page: {currentPagetUrl} : started");
                    var currentPageHtmlDocument = await FetchHtmlDocument(currentPagetUrl);
                    await ScrapeBookPage(currentPagetUrl, pageNumber, currentPageHtmlDocument, catalogueFolder, urlToLocalPathMap);

                    Console.WriteLine($"Processed page: {currentPagetUrl} : finished");

                    currentPagetUrl = GetNextPageUrl(currentPageHtmlDocument, currentPagetUrl);
                }

                Console.WriteLine("Scraping completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        private void CreateDirectory(string directoryPath)
        {
            Directory.CreateDirectory(directoryPath);
        }

        private async Task<HtmlDocument> FetchHtmlDocument(string url)
        {
            using var httpClient = new HttpClient();
            var html = await httpClient.GetStringAsync(url);
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html);

            return htmlDocument;
        }

        private void SaveHtmlPage(string pageUrl, string htmlContent, string folderPath, int pageNumber)
        {
            string pageFileName;
            pageFileName = $"page-{pageNumber}.html";

            lock (locker)
            {
                File.WriteAllText(Path.Combine(folderPath, pageFileName), htmlContent);
            }

            Console.WriteLine($"Successfully downloaded and saved {pageUrl}");
        }

        private void SaveHtmlPage(string pageUrl, string htmlContent, string folderPath, string pageFileName)
        {
            lock (locker)
            {
                File.WriteAllText(Path.Combine(folderPath, pageFileName), htmlContent);
            }

            Console.WriteLine($"Successfully downloaded and saved {pageUrl}");
        }

        private async Task ScrapeBookPage(
            string pageUrl,
            int pageNumber,
            HtmlDocument htmlDocument,
            string catalogueFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            string bookLinksXPath = "//h3/a";
            var bookLinks = htmlDocument.DocumentNode.SelectNodes(bookLinksXPath);

            SaveHtmlPage(pageUrl, htmlDocument.DocumentNode.OuterHtml, catalogueFolder, pageNumber);

            if (bookLinks != null)
            {
                var imageScrapingTasks = new List<Task>();

                foreach (var bookLink in bookLinks)
                {
                    imageScrapingTasks.Add(ScrapeBookPageImageFiles(pageUrl, bookLink, catalogueFolder, urlToLocalPathMap));
                }

                await Task.WhenAll(imageScrapingTasks);
            }
        }

        private async Task ScrapeBookPageImageFiles(
            string pageUrl,
            HtmlNode bookLink,
            string booksFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            await AcquireSemaphore();

            try
            {
                string bookUrl = GetBookUrl(pageUrl, bookLink.Attributes[HrefAttributeName].Value);
                var bookHtmlDocument = await FetchHtmlDocument(bookUrl);
                string bookTitle = GetValidFileName(bookHtmlDocument.DocumentNode.SelectSingleNode("//h1").InnerText.Trim());
                int indexHtmlIndex = bookLink.Attributes[HrefAttributeName].Value.IndexOf("index.html");
                string bookPath = bookLink.Attributes[HrefAttributeName].Value.Substring(0, indexHtmlIndex);
                string bookFolder = Path.Combine(booksFolder, bookPath);
                bookFolder = bookFolder.Replace("/", "\\");
                if (!bookFolder.EndsWith("\\"))
                {
                    bookFolder += "\\";
                }

                CreateDirectory(bookFolder);

                var imageNodes = GetImageNodes(bookHtmlDocument);
                if (imageNodes != null)
                {
                    var imageDownloadTasks = new List<Task>();

                    foreach (var node in imageNodes)
                    {
                        string src = node.Attributes[SrcAttributeName]?.Value;
                        if (src != null && !urlToLocalPathMap.ContainsKey(src))
                        {
                            Uri uri = GetAbsoluteUri(bookUrl, src);
                            string fileName = GetLocalFilePath(bookFolder, uri.LocalPath);

                            imageDownloadTasks.Add(DownloadAndSaveFile(uri, fileName)
                                .ContinueWith(task =>
                                {
                                    if (task.Exception != null)
                                    {
                                        Console.WriteLine($"Failed to download {uri}: {task.Exception.InnerException.Message}");
                                    }
                                    else
                                    {
                                        urlToLocalPathMap.TryAdd(src, fileName);
                                    }
                                }));
                        }
                    }

                    await Task.WhenAll(imageDownloadTasks);
                }
            }
            finally
            {
                ReleaseSemaphore();
            }
        }

        private async Task ExtractAndDownloadBookCategoryPages(HtmlDocument htmlDocument, string baseUrl)
        {
            var pageLinks = htmlDocument.DocumentNode.SelectNodes("//a[contains(@href,'index.html')]");
            if (pageLinks != null)
            {
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 20
                };

                Parallel.ForEach(pageLinks, options, async (pageLink) =>
                {
                    await AcquireSemaphore();
                    try
                    {
                        string pageUrl = GetBookUrl(baseUrl, pageLink.Attributes["href"].Value);
                        if (pageUrl.StartsWith("http://books.toscrape.com/catalogue/category"))
                        {
                            if (pageUrl.StartsWith("http://books.toscrape.com/catalogue/category/books_1"))
                            {
                                await ProcessBooks1Page(pageUrl);
                            }
                            else
                            {
                                await ProcessBookCategoryPage(pageUrl);
                            }
                        }
                    }
                    finally
                    {
                        ReleaseSemaphore();
                    }
                });
            }
        }

        private async Task ProcessBooks1Page(string pageUrl)
        {
            var pageHtmlDocument = await FetchHtmlDocument(pageUrl);

            string pageFileName = GetPageFileName(pageUrl);

            string contentPath = Path.Combine("downloaded_books_files", "catalogue", "category", "books_1");
            Directory.CreateDirectory(contentPath);
            contentPath += "\\";

            lock (locker)
            {
                SaveHtmlPage(pageUrl, pageHtmlDocument.DocumentNode.OuterHtml, contentPath, pageFileName);
            }
        }

        private async Task ProcessBookCategoryPage(string pageUrl)
        {
            string currentUrl = pageUrl;

            while (!string.IsNullOrEmpty(currentUrl))
            {
                var pageHtmlDocument = await FetchHtmlDocument(currentUrl);

                string pageFileName = GetPageFileName(currentUrl);

                string[] parts = currentUrl.Split('/');
                string middlePart = parts[parts.Length - 2];

                string contentPath = Path.Combine("downloaded_books_files", "catalogue", "category", "books", middlePart);
                Directory.CreateDirectory(contentPath);
                contentPath += "\\";

                await SaveHtmlBookCategoryPage(currentUrl, pageHtmlDocument.DocumentNode.OuterHtml, contentPath, pageFileName);

                currentUrl = GetNextPageUrl(pageHtmlDocument, currentUrl);
            }
        }

        private async Task SaveHtmlBookCategoryPage(string pageUrl, string htmlContent, string folderPath, string pageFileName)
        {
            lock (locker)
            {
                File.WriteAllText(Path.Combine(folderPath, pageFileName), htmlContent);
            }

            Console.WriteLine($"Successfully downloaded and saved book category {pageUrl}");
        }

        private string GetPageFileName(string url)
        {
            string[] parts = url.Split('/');
            return parts[parts.Length - 1];
        }

        private HtmlNodeCollection GetImageNodes(HtmlDocument htmlDocument)
        {
            return htmlDocument.DocumentNode.SelectNodes($"//{ImgNodeName}");
        }

        private async Task DownloadAndSaveFile(Uri uri, string outputPath)
        {
            using (var httpClient = new HttpClient())
            {
                using (var response = await httpClient.GetAsync(uri))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        using (var fileStream = new FileStream(outputPath, FileMode.Create))
                        {
                            await response.Content.CopyToAsync(fileStream);
                            Console.WriteLine($"Successfully downloaded and saved {uri}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Failed to download {uri}. Status code: {response.StatusCode}");
                    }
                }
            }
        }

        private string GetNextPageUrl(HtmlDocument htmlDocument, string currentUrl)
        {
            var nextPageLink = htmlDocument.DocumentNode.SelectSingleNode("//li[@class='next']/a");
            if (nextPageLink != null)
            {
                string nextPagePath = nextPageLink.Attributes[HrefAttributeName].Value;
                Uri currentUri = new Uri(currentUrl);
                Uri nextPageUri = new Uri(currentUri, nextPagePath);
                return nextPageUri.AbsoluteUri;
            }
            else
            {
                return null;
            }
        }

        private string GetBookUrl(string baseUrl, string relativeUrl)
        {
            if (!Uri.TryCreate(relativeUrl, UriKind.Absolute, out Uri result))
            {
                return new Uri(new Uri(baseUrl), relativeUrl).AbsoluteUri;
            }
            return result.AbsoluteUri;
        }

        private string GetValidFileName(string input)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                input = input.Replace(c, '_');
            }

            input = input.Replace("...", "_");

            input = input.TrimEnd('.');

            return input;
        }

        private string GetLocalFilePath(string folderPath, string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            return Path.Combine(folderPath, fileName);
        }

        private Uri GetAbsoluteUri(string baseUrl, string relativeUrl)
        {
            return new Uri(new Uri(baseUrl), relativeUrl);
        }

        private async Task AcquireSemaphore()
        {
            await semaphoreSlim.WaitAsync();
        }

        private void ReleaseSemaphore()
        {
            semaphoreSlim.Release();
        }
    }
}
