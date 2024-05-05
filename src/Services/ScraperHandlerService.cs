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

                int totalPages = 50;
                foreach (int pageNumber in Enumerable.Range(1, totalPages))
                {
                    Console.WriteLine($"Processed page: {currentPagetUrl} : started");
                    var htmlDocument = await FetchHtmlDocument(currentPagetUrl);

                    string catalogueFolder = pageNumber == 1 ? outputPath : Path.Combine(outputPath, "catalogue");
                    CreateDirectory(catalogueFolder);

                    SaveHtmlPage(htmlDocument.DocumentNode.OuterHtml, catalogueFolder, pageNumber);

                    await ScrapeBookPage(currentPagetUrl, catalogueFolder, urlToLocalPathMap);

                    UpdateHtmlLinks(urlToLocalPathMap, catalogueFolder);
                    Console.WriteLine($"Processed page: {currentPagetUrl} : finished");

                    currentPagetUrl = GetNextPageUrl(htmlDocument, currentPagetUrl);
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

        private void SaveHtmlPage(string htmlContent, string folderPath, int pageNumber)
        {
            string pageFileName;
            if (pageNumber == 1)
            {
                pageFileName = $"index.html";
            }
            else
            {
                pageFileName = $"page-{pageNumber}.html";
            }

            lock (locker)
            {
                File.WriteAllText(Path.Combine(folderPath, pageFileName), htmlContent);
            }
        }

        private void SaveHtmlPage(string htmlContent, string folderPath, string pageFileName)
        {
            lock (locker)
            {
                File.WriteAllText(Path.Combine(folderPath, pageFileName), htmlContent);
            }
        }

        private async Task ScrapeBookPage(
            string baseUrl,
            string booksFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            var htmlDocument = await FetchHtmlDocument(baseUrl);

            string bookLinksXPath = "//h3/a";
            var bookLinks = htmlDocument.DocumentNode.SelectNodes(bookLinksXPath);

            if (bookLinks != null)
            {
                await Task.WhenAll(bookLinks.Select(bookLink =>
                    ScrapeBooPageFiles(baseUrl, bookLink, booksFolder, urlToLocalPathMap)));
            }
        }

        private async Task ScrapeBooPageFiles(
            string baseUrl,
            HtmlNode bookLink,
            string booksFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            await AcquireSemaphore();

            try
            {
                string bookUrl = GetBookUrl(baseUrl, bookLink.Attributes[HrefAttributeName].Value);
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

                lock (locker)
                {
                    SaveHtmlPage(bookHtmlDocument.DocumentNode.OuterHtml, bookFolder, IndexFileName);
                }

                await ExtractAndDownloadContent(bookHtmlDocument, bookUrl, bookFolder, urlToLocalPathMap);
            }
            finally
            {
                ReleaseSemaphore();
            }
        }

        private async Task ExtractAndDownloadContent(
            HtmlDocument htmlDocument,
            string baseUrl,
            string bookFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            await ExtractAndDownloadImages(htmlDocument, baseUrl, bookFolder, urlToLocalPathMap);
            await ExtractAndDownloadPages(htmlDocument, baseUrl, bookFolder, urlToLocalPathMap);
        }

        private async Task ExtractAndDownloadPages(
            HtmlDocument htmlDocument,
            string baseUrl,
            string catalogueFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            var pageLinks = htmlDocument.DocumentNode.SelectNodes("//a[contains(@href,'index.html')]");
            if (pageLinks != null)
            {
                foreach (var pageLink in pageLinks)
                {
                    string pageUrl = GetBookUrl(baseUrl, pageLink.Attributes["href"].Value);
                    {
                        if (pageUrl.StartsWith("http://books.toscrape.com/catalogue/category/"))
                        {
                            var pageHtmlDocument = await FetchHtmlDocument(pageUrl);

                            string pageFileName = GetPageFileName(pageLink.Attributes["href"].Value);

                            string[] parts = pageUrl.Split('/');
                            string middlePart = parts[parts.Length - 2];

                            string contentPath;
                            if (middlePart == "books.toscrape.com")
                            {
                                contentPath = Path.Combine("downloaded_books_files", "catalogue", "category", "books_1");
                            }
                            else
                            {
                                contentPath = Path.Combine("downloaded_books_files", "catalogue", "category", "books", middlePart);
                            }

                            Directory.CreateDirectory(contentPath);
                            contentPath += "\\";

                            lock (locker)
                            {
                                SaveHtmlPage(pageHtmlDocument.DocumentNode.OuterHtml, contentPath, pageFileName);
                            }
                        }
                    }
                }
            }
        }

        private string GetPageFileName(string url)
        {
            string[] parts = url.Split('/');
            return parts[parts.Length - 1];
        }

        private async Task ExtractAndDownloadImages(
            HtmlDocument htmlDocument,
            string baseUrl,
            string bookFolder,
            ConcurrentDictionary<string, string> urlToLocalPathMap)
        {
            var imageNodes = GetImageNodes(htmlDocument);
            if (imageNodes != null)
            {
                foreach (var node in imageNodes)
                {
                    string src = node.Attributes[SrcAttributeName]?.Value;
                    if (src != null && !urlToLocalPathMap.ContainsKey(src))
                    {
                        Uri uri = GetAbsoluteUri(baseUrl, src);
                        string fileName = GetLocalFilePath(bookFolder, uri.LocalPath);

                        await DownloadAndSaveFile(uri, fileName);

                        urlToLocalPathMap.TryAdd(src, fileName);
                    }
                }
            }
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
                            Console.WriteLine($"Successfully downloaded and saved {uri}. Status code: {response.StatusCode}");
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

        private void UpdateHtmlLinks(ConcurrentDictionary<string, string> urlToLocalPathMap, string outputPath)
        {
            string[] htmlFiles = Directory.GetFiles(outputPath, "*.html", SearchOption.AllDirectories);
            foreach (var file in htmlFiles)
            {
                string content = File.ReadAllText(file);
                foreach (var keyValuePair in urlToLocalPathMap)
                {
                    content = content.Replace(keyValuePair.Key, keyValuePair.Value.Replace('\\', '/'));
                }
                File.WriteAllText(file, content);
            }
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
