namespace LMMs.Api.ViewModels
{
    public class ChatRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public IFormFile? File { get; set; }

        /// <summary>
        /// When false (default), no tools are registered unless a file is uploaded — then only file reading is offered.
        /// This is the reliable way to stop the model from calling calculator/time/search on every turn.
        /// </summary>
        public bool EnableTools { get; set; }

        /// <summary>
        /// When EnableTools is true: if false (default), web search is not offered.
        /// </summary>
        public bool EnableWebSearch { get; set; }
    }
}
