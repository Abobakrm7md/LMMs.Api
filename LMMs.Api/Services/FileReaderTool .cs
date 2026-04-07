using DocumentFormat.OpenXml.Office2016.Excel;
using DocumentFormat.OpenXml.Packaging;
using LMMs.Api.Interfaces;
using System.ComponentModel;
using UglyToad.PdfPig;

namespace LMMs.Api.Services
{

    // ✅ object مشترك بين الـ Controller والـ Tool في نفس الـ request
    public class FileContext
    {
        public string? FileName { get; set; }
    }
    public class FileReaderTool : IAgentTool
    {
        private readonly string _filesFolder;
        private readonly FileContext _fileContext; // ✅ مشترك مع الـ Controller

        public FileReaderTool(FileContext fileContext)
        {
            _fileContext = fileContext;
            _filesFolder = Path.Combine(Directory.GetCurrentDirectory(), "Files");
        }
        public string Name => "read_file";

        // ✅ بيتسيت قبل كل request من الـ Controller

        public Delegate GetFunction() => ReadFile;

        [Description("Read the content of the attached file. Supports .txt, .csv, .json, .pdf, .docx")]
        private async Task<string> ReadFile(
        [Description("Pass 'attached' to read the uploaded file")] string source = "attached")
        {
            if (_fileContext.FileName is null)
                return "No file attached.";

            var fullPath = Path.Combine(_filesFolder, _fileContext.FileName);

            if (!File.Exists(fullPath))
                return $"File not found: {_fileContext.FileName}";

            var extension = Path.GetExtension(_fileContext.FileName).ToLower();

            var content = extension switch
            {
                ".txt" or ".json" or ".xml" or ".md"
                    => await File.ReadAllTextAsync(fullPath),

                ".csv"
                    => await ReadCsvAsync(fullPath),

                ".pdf"
                    => ReadPdf(fullPath),

                ".docx"
                    => ReadDocx(fullPath),

                _ => $"Unsupported file type: {extension}"
            };

            if (content.Length > 4000)
                content = content[..4000] + "\n... (truncated)";

            return $"File: {_fileContext.FileName}\n\n{content}";
        }

        private static async Task<string> ReadCsvAsync(string path)
        {
            var lines = await File.ReadAllLinesAsync(path);
            return string.Join("\n", lines.Take(20));
        }

        private static string ReadPdf(string path)
        {
            using var doc = PdfDocument.Open(path);
            return string.Join("\n", doc.GetPages().Take(5).Select(p => p.Text));
        }

        private static string ReadDocx(string path)
        {
            using var doc = WordprocessingDocument.Open(path, false);
            return doc.MainDocumentPart?.Document.Body?.InnerText
                   ?? "Could not read file.";
        }

        public async Task<string> ExecuteAsync(
            IReadOnlyDictionary<string, object?> args, CancellationToken ct)
        {
            return await ReadFile();
        }
    }
}
