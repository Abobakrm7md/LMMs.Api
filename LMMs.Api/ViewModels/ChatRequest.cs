namespace LMMs.Api.ViewModels
{
    public class ChatRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public IFormFile? File { get; set; }
    }
}
