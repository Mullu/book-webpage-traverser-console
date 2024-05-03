using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace BookWebPageTraverser
{
    class Program
    {
        private const string IndexFileName = "index.html";
        private const string ImgNodeName = "img";
        private const string LinkNodeName = "link";
        private const string AnchorNodeName = "a";
        private const string HrefAttributeName = "href";
        private const string SrcAttributeName = "src";

        static async Task Main(string[] args)
        {
            const string baseUrl = "http://books.toscrape.com/";
            const string outputPath = "downloaded_books_files";
            const string booksFolderName = "Books";
            var urlToLocalPathMap = new Dictionary<string, string>();

            try
            {
                string booksFolder = Path.Combine(outputPath, booksFolderName);
                CreateDirectory(booksFolder);

                var htmlDocument = await FetchHtmlDocument(baseUrl);

                SaveHtmlPage(htmlDocument.DocumentNode.OuterHtml, booksFolder, IndexFileName);

                const string xpathExpression = "//ul[@class='nav nav-list']/li/ul/li/a";
                var bookCategoryPages = htmlDocument.DocumentNode.SelectNodes(xpathExpression);

                if (bookCategoryPages != null)
                {
                    foreach (var bookCategoryPage in bookCategoryPages)
                    {
                        string categoryUrl = baseUrl + bookCategoryPage.Attributes[HrefAttributeName].Value;
                        var categoryHtmlDocument = await FetchHtmlDocument(categoryUrl);

                        string categoryFolder = Path.Combine(booksFolder, bookCategoryPage.InnerText.Trim());
                        CreateDirectory(categoryFolder);
                        SaveHtmlPage(categoryHtmlDocument.DocumentNode.OuterHtml, categoryFolder, IndexFileName);

                        string bookLinksXPath = "//h3/a";
                        var bookLinks = categoryHtmlDocument.DocumentNode.SelectNodes(bookLinksXPath);

                        if (bookLinks != null)
                        {
                            foreach (var bookLink in bookLinks)
                            {
                                string bookUrl = GetBookUrl(baseUrl, bookLink.Attributes[HrefAttributeName].Value);
                                var bookHtmlDocument = await FetchHtmlDocument(bookUrl);
                                string bookTitle = GetValidFileName(bookHtmlDocument.DocumentNode.SelectSingleNode("//h1").InnerText.Trim());
                                string bookFolder = Path.Combine(booksFolder, bookCategoryPage.InnerText.Trim(), bookTitle);
                                CreateDirectory(bookFolder);

                                SaveHtmlPage(bookHtmlDocument.DocumentNode.OuterHtml, bookFolder, IndexFileName);

                                ExtractAndDownloadResources(bookHtmlDocument, bookUrl, bookFolder, urlToLocalPathMap);
                            }
                        }
                    }
                }

                UpdateHtmlLinks(urlToLocalPathMap, outputPath);
                Console.WriteLine("Download completed. You can view the original page and its resources in:");
                Console.WriteLine($"Directory: {Path.GetFullPath(outputPath)}");
                Console.WriteLine($"Original HTML page: {Path.Combine(outputPath, IndexFileName)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        private static void CreateDirectory(string directoryPath)
        {
            Directory.CreateDirectory(directoryPath);
        }

        private static async Task<HtmlDocument> FetchHtmlDocument(string url)
        {
            var httpClient = new HttpClient();
            var html = await httpClient.GetStringAsync(url);
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html);
            return htmlDocument;
        }

        private static void SaveHtmlPage(string htmlContent, string folderPath, string fileName)
        {
            File.WriteAllText(Path.Combine(folderPath, fileName), htmlContent);
        }

        private static string GetBookUrl(string baseUrl, string bookHref)
        {
            return baseUrl + "catalogue" + bookHref.Substring(8);
        }

        private static string GetValidFileName(string fileName)
        {
            string invalidChars = new string(Path.GetInvalidFileNameChars());
            string invalidReStr = Regex.Escape(invalidChars);
            return Regex.Replace(fileName, "[" + invalidReStr + "]", "_");
        }

        private static void ExtractAndDownloadResources(HtmlDocument htmlDocument, string baseUrl, string bookFolder, Dictionary<string, string> urlToLocalPathMap)
        {
            var nodes = htmlDocument.DocumentNode.SelectNodes($"//{ImgNodeName}|//{LinkNodeName}|//{AnchorNodeName}[@{HrefAttributeName}]");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    string src = null;
                    if (node.Name == ImgNodeName)
                    {
                        src = node.Attributes[SrcAttributeName]?.Value;
                    }
                    else if (node.Name == LinkNodeName || node.Name == AnchorNodeName)
                    {
                        src = node.Attributes[HrefAttributeName]?.Value;
                    }

                    if (src != null && !urlToLocalPathMap.ContainsKey(src))
                    {
                        Uri uri;
                        if (!Uri.TryCreate(src, UriKind.Absolute, out uri))
                        {
                            uri = new Uri(new Uri(baseUrl), src);
                        }

                        var fileName = Path.Combine(bookFolder, Path.GetFileName(uri.LocalPath));

                        DownloadFile(uri, fileName).Wait();

                        urlToLocalPathMap.Add(src, fileName);
                    }
                }
            }
        }


        private static async Task DownloadFile(Uri uri, string outputPath)
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

        private static void UpdateHtmlLinks(Dictionary<string, string> urlToLocalPathMap, string outputPath)
        {
            string updatedHtml = File.ReadAllText(Path.Combine(outputPath, IndexFileName));
            foreach (var kvp in urlToLocalPathMap)
            {
                updatedHtml = updatedHtml.Replace(kvp.Key, Path.GetFileName(kvp.Value));
            }
            File.WriteAllText(Path.Combine(outputPath, IndexFileName), updatedHtml);
        }
    }
}
